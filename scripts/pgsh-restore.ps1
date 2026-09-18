<#
.SYNOPSIS
    Restaure un point de sauvegarde PGSH, et **vérifie** qu'il est arrivé.

.DESCRIPTION
    La moitié « restauration » de la phase 18 — celle que l'API ne peut délibérément pas faire :
    un processus ne remplace pas la base qu'il est en train de servir, la restauration supprimant et
    recréant des objets que l'API tient ouverts. L'application *imprime* la commande ; ceci l'exécute.

    ⚠ **La vérification est le point, pas la commodité.** `pg_restore` rend 0 en ayant tout de même
    laissé passer des erreurs (`--clean --if-exists` en produit sur une base vide), donc « la commande
    n'a pas échoué » ne veut pas dire « la base est celle du manifeste ». Le script recompte ce que le
    manifeste annonce et **refuse** sur un écart. C'est la forme établie par la reconstruction du
    01/09/2026 : on affirme le résultat, on ne le suppose pas.

    ⚠ **Ce qu'il ne restaure pas** : le realm Keycloak, qui n'est pas dans le dump et n'a pas à y être
    — il est écrit dans `keycloak/pgsh-realm.json` et se reconstruit au démarrage. Voir keycloak/README.md.

.PARAMETER Id
    L'identifiant du point (le nom de fichier sans .dump). À défaut, -Latest.

.PARAMETER Latest
    Prend le point le plus récent du dossier.

.PARAMETER List
    N'affiche que les points disponibles et sort.

.PARAMETER Force
    Passe la confirmation. ⚠ La restauration écrase la base entière et ne se défait pas.

.EXAMPLE
    ./scripts/pgsh-restore.ps1 -List

.EXAMPLE
    ./scripts/pgsh-restore.ps1 -Id 20260915-102338-3med-safe
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [string] $Id,
    [switch] $Latest,
    [switch] $List,
    [string] $Container,
    [string] $BackupDirectory,
    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'pgsh-common.ps1')

$directory = Get-PgshBackupDirectory -Explicit $BackupDirectory
if (-not (Test-Path $directory)) {
    throw "Aucun dossier de sauvegardes : $directory"
}

$points = @(
    Get-ChildItem -Path $directory -Filter '*.dump' -File |
    Sort-Object LastWriteTime -Descending |
    ForEach-Object {
        $manifestPath = $_.FullName -replace '\.dump$', '.manifest.json'

        $manifest = $null
        if (Test-Path $manifestPath) {
            $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
        }

        [pscustomobject]@{
            Id       = $_.BaseName
            File     = $_.FullName
            SizeMb   = [math]::Round($_.Length / 1MB, 1)
            TakenAt  = $_.LastWriteTime
            Manifest = $manifest
        }
    }
)

if ($points.Count -eq 0) { throw "Aucun point de sauvegarde dans $directory." }

if ($List) {
    $points | ForEach-Object {
        $label = '(sans manifeste)'
        $mig   = '?'
        if ($_.Manifest) {
            $label = $_.Manifest.Label
            $mig   = $_.Manifest.LastMigration
        }
        "{0,-42} {1,7} Mo  {2:yyyy-MM-dd HH:mm}  {3}  [{4}]" -f $_.Id, $_.SizeMb, $_.TakenAt, $label, $mig
    }
    return
}

$point = if ($Id) {
    $points | Where-Object Id -eq $Id | Select-Object -First 1
} elseif ($Latest) {
    $points[0]
} else {
    throw "Indiquez -Id <point>, ou -Latest. `-List` montre ce qui existe."
}

if (-not $point) { throw "Point introuvable : $Id. `-List` montre ce qui existe." }

if (-not $point.Manifest) {
    # ⚠ Sans manifeste il n'y a rien à vérifier après coup — et une restauration non vérifiée est
    # précisément ce que ce script existe pour ne plus faire. Permis, mais dit.
    Write-Warning "Ce point n'a pas de manifeste : les effectifs ne pourront pas être vérifiés après la restauration."
}

$containerName = Get-PgshContainer -Explicit $Container
$connection    = Get-PgshConnection -Container $containerName

Write-Host ""
Write-Host "Point      : $($point.Id)" -ForegroundColor Cyan
if ($point.Manifest) {
    Write-Host "Libellé    : $($point.Manifest.Label)"
    Write-Host "Pris le    : $($point.Manifest.TakenAtUtc) UTC par $($point.Manifest.TakenBy)"
    Write-Host "Migration  : $($point.Manifest.LastMigration)"
}
Write-Host "Conteneur  : $containerName"
Write-Host "Base       : $($connection.Database)"
Write-Host ""
Write-Host "⚠ La base entière est remplacée. Ce qui y est entré depuis ce point est perdu." -ForegroundColor Yellow
Write-Host "⚠ Arrêtez l'AppHost d'abord : l'API tient des objets que la restauration supprime." -ForegroundColor Yellow
Write-Host ""

if (-not $Force -and -not $PSCmdlet.ShouldProcess($connection.Database, "Restaurer $($point.Id)")) {
    Write-Host "Annulé." -ForegroundColor Yellow
    return
}

$remote = "/tmp/$($point.Id).dump"

try {
    Write-Host "Copie du dump dans le conteneur…"
    # ⚠ Jamais par un tube : un dump canalisé a déjà corrompu une archive ici (docs/operations.md).
    docker cp $point.File "${containerName}:$remote"
    if ($LASTEXITCODE -ne 0) { throw "docker cp a échoué." }

    Write-Host "pg_restore…"
    $output = docker exec -e "PGPASSWORD=$($connection.Password)" $containerName `
        pg_restore -U $connection.User -d $connection.Database `
        --clean --if-exists --no-owner $remote 2>&1

    # ⚠ Le code de sortie n'est PAS le verdict. `--clean --if-exists` signale des objets absents sur
    # une base vide et rend non-zéro en ayant parfaitement restauré ; à l'inverse il rend 0 sur des
    # erreurs qui ont laissé des tables vides. Le verdict est le recomptage, plus bas.
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "pg_restore a signalé des erreurs (code $LASTEXITCODE). Ce n'est pas concluant — les effectifs le seront."
        $output | Select-Object -Last 15 | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }
    }
}
finally {
    docker exec $containerName rm -f $remote 2>&1 | Out-Null
}

if (-not $point.Manifest) {
    Write-Host ""
    Write-Host "Restauré, non vérifié (pas de manifeste)." -ForegroundColor Yellow
    return
}

# ─────────────────────────────────────────────────────────────────────────────
# La vérification — §18.2 : « Assert the restore in SQL, on a row-count mismatch
# against the manifest's census ».
# ⚠ Les requêtes reflètent DatabaseCensusReader une par une. « Students » n'est pas
# une table : Users est en TPH, discriminée par UserType.
# ─────────────────────────────────────────────────────────────────────────────
$censusSql = [ordered]@{
    'Students'              = 'select count(*) from public."Users" where "UserType" = ''Student'''
    'Registrations'         = 'select count(*) from public."Registrations"'
    'InternshipAssignments' = 'select count(*) from public."InternshipAssignments"'
    'ServicePeriods'        = 'select count(*) from public."ServicePeriods"'
    'ServiceEvaluations'    = 'select count(*) from public."ServiceEvaluation"'
    'AcademicGroups'        = 'select count(*) from public."AcademicGroups"'
    'Cohorts'               = 'select count(*) from public."Cohorts"'
    'StageSlots'            = 'select count(*) from public."StageSlots"'
    'CohortSlotAssignments' = 'select count(*) from public."CohortSlotAssignments"'
    'RegistrationHolds'     = 'select count(*) from public."RegistrationHolds"'
    'Holidays'              = 'select count(*) from public."Holidays"'
    'AuditLogs'             = 'select count(*) from public."AuditLogs"'
}

Write-Host ""
Write-Host "Vérification des effectifs annoncés par le manifeste :" -ForegroundColor Cyan

$mismatches = @()
$unchecked  = @()

foreach ($key in $censusSql.Keys) {
    $expected = $point.Manifest.Census.$key

    # ⚠ « Ce point n'en dit rien » et « rien n'a changé » sont deux réponses, et une seule autorise à
    # continuer — la même règle que DatabaseCensus, où une clé absente vaut null et jamais 0.
    if ($null -eq $expected) { $unchecked += $key; continue }

    $actual = [int64](Invoke-PgshPsql -Connection $connection -Sql $censusSql[$key])

    if ($actual -eq [int64]$expected) {
        "  {0,-24} {1,10:N0}  ✓" -f $key, $actual | Write-Host -ForegroundColor Green
    } else {
        "  {0,-24} {1,10:N0}  ✗ attendu {2:N0}" -f $key, $actual, $expected | Write-Host -ForegroundColor Red
        $mismatches += $key
    }
}

foreach ($key in $unchecked) {
    "  {0,-24} {1,>10}" -f $key, '(le manifeste n''en dit rien)' | Write-Host -ForegroundColor DarkGray
}

Write-Host ""

if ($mismatches.Count -gt 0) {
    throw ("Restauration NON conforme au manifeste : " + ($mismatches -join ', ') +
           ". La base est dans un état intermédiaire — ne la servez pas, et reprenez depuis un autre point.")
}

Write-Host "Restauration vérifiée : la base porte exactement les effectifs du point $($point.Id)." -ForegroundColor Green
Write-Host ""
Write-Host "Ensuite :" -ForegroundColor Cyan
Write-Host "  1. Relancer l'AppHost."
Write-Host "  2. ⚠ Le realm Keycloak n'est pas dans ce dump — il est importé de keycloak/pgsh-realm.json."
Write-Host "  3. Marquer ce point comme restauré sur la page Sauvegardes (BackupVerification.Restored)."
