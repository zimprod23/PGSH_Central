<#
.SYNOPSIS
    Prend un point de sauvegarde PGSH, avec son manifeste.

.DESCRIPTION
    La moitié « prise » de la phase 18 existe déjà dans l'application (page Sauvegardes, plus un timer
    quotidien). Ce script est la même chose **hors d'une API qui tourne** — le cas où on en a le plus
    besoin : avant un acte en masse, la pile arrêtée, ou l'API en panne.

    ⚠ **Un point est un dump ET un manifeste.** Le manifeste est la moitié qui compte : sans les
    effectifs, `pgsh-restore` ne peut rien affirmer et la restauration redevient une supposition.

    ⚠ **Jamais par un tube.** Le dump est écrit avec `-f` **dans** le conteneur puis sorti par
    `docker cp` — la procédure qui a marché ici, contre la version canalisée qui a corrompu une
    archive une fois (docs/operations.md).

.PARAMETER Label
    Ce que ce point est. Obligatoire : « 20260917-142233 » ne dit pas pourquoi on l'a pris.

.EXAMPLE
    ./scripts/pgsh-snapshot.ps1 -Label "avant canevas 4 PHAR"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Label,
    [string] $Container,
    [string] $BackupDirectory,
    [string] $TakenBy = $env:USERNAME
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'pgsh-common.ps1')

$containerName = Get-PgshContainer -Explicit $Container
$connection    = Get-PgshConnection -Container $containerName
$directory     = Get-PgshBackupDirectory -Explicit $BackupDirectory

New-Item -ItemType Directory -Force -Path $directory | Out-Null

$slug = ($Label.ToLowerInvariant() -replace '[^a-z0-9]+', '-').Trim('-')
if ([string]::IsNullOrWhiteSpace($slug)) { $slug = 'point' }

$id        = "{0}-{1}" -f (Get-Date -Format 'yyyyMMdd-HHmmss'), $slug
$localFile = Join-Path $directory "$id.dump"
$remote    = "/tmp/$id.dump"

Write-Host "Conteneur : $containerName"
Write-Host "Base      : $($connection.Database)"
Write-Host "Point     : $id"
Write-Host ""

# ⚠ Les effectifs sont relevés AVANT le dump, dans le même état que lui : les relever après
# laisserait un acte joué entre les deux faire mentir le manifeste sur ce que le fichier contient.
Write-Host "Relevé des effectifs…"
$census = [ordered]@{
    Students              = [int64](Invoke-PgshPsql $connection 'select count(*) from public."Users" where "UserType" = ''Student''')
    Registrations         = [int64](Invoke-PgshPsql $connection 'select count(*) from public."Registrations"')
    InternshipAssignments = [int64](Invoke-PgshPsql $connection 'select count(*) from public."InternshipAssignments"')
    ServicePeriods        = [int64](Invoke-PgshPsql $connection 'select count(*) from public."ServicePeriods"')
    ServiceEvaluations    = [int64](Invoke-PgshPsql $connection 'select count(*) from public."ServiceEvaluation"')
    AcademicGroups        = [int64](Invoke-PgshPsql $connection 'select count(*) from public."AcademicGroups"')
    Cohorts               = [int64](Invoke-PgshPsql $connection 'select count(*) from public."Cohorts"')
    StageSlots            = [int64](Invoke-PgshPsql $connection 'select count(*) from public."StageSlots"')
    CohortSlotAssignments = [int64](Invoke-PgshPsql $connection 'select count(*) from public."CohortSlotAssignments"')
    RegistrationHolds     = [int64](Invoke-PgshPsql $connection 'select count(*) from public."RegistrationHolds"')
    Holidays              = [int64](Invoke-PgshPsql $connection 'select count(*) from public."Holidays"')
    AuditLogs             = [int64](Invoke-PgshPsql $connection 'select count(*) from public."AuditLogs"')
}

$lastMigration = Invoke-PgshPsql $connection `
    'select "MigrationId" from public."__EFMigrationsHistory" order by "MigrationId" desc limit 1'

try {
    Write-Host "pg_dump…"
    docker exec -e "PGPASSWORD=$($connection.Password)" $containerName `
        pg_dump -U $connection.User -d $connection.Database -Fc -f $remote
    if ($LASTEXITCODE -ne 0) { throw "pg_dump a échoué (code $LASTEXITCODE)." }

    docker cp "${containerName}:$remote" $localFile
    if ($LASTEXITCODE -ne 0) { throw "docker cp a échoué." }
}
finally {
    docker exec $containerName rm -f $remote 2>&1 | Out-Null
}

$size = (Get-Item $localFile).Length

# ⚠ `GitSha` est relevé pour que SchemaFingerprint puisse dire « je ne peux pas certifier » plutôt
# que de supposer l'accord : seule la migration est comparée, le sha est le contexte.
$gitSha = (git -C (Split-Path $PSScriptRoot -Parent) rev-parse HEAD 2>$null)

@{
    Id            = $id
    Label         = $Label
    Kind          = 'Named'
    TakenAtUtc    = (Get-Date).ToUniversalTime().ToString('o')
    SizeBytes     = $size
    LastMigration = $lastMigration
    GitSha        = $gitSha
    Census        = $census
    Note          = 'Pris par scripts/pgsh-snapshot.ps1'
    TakenBy       = $TakenBy
    Verification  = 'Never'
    VerifiedAtUtc = $null
} | ConvertTo-Json -Depth 5 | Set-Content -Path ($localFile -replace '\.dump$', '.manifest.json') -Encoding utf8

Write-Host ""
Write-Host ("Point pris : {0} ({1:N1} Mo), migration {2}" -f $id, ($size / 1MB), $lastMigration) -ForegroundColor Green
$census.GetEnumerator() | ForEach-Object { "  {0,-24} {1,10:N0}" -f $_.Key, $_.Value }
Write-Host ""
Write-Host "Restaurer :  ./scripts/pgsh-restore.ps1 -Id $id" -ForegroundColor Cyan
