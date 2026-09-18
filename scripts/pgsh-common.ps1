<#
.SYNOPSIS
    Ce que pgsh-snapshot et pgsh-restore ont en commun : trouver le conteneur, lire la connexion,
    lire un manifeste.

.DESCRIPTION
    ⚠ Les règles ici sont celles de `PgDumpBackupArchive`, délibérément, et pas par goût de la
    symétrie : un script qui découvrirait le conteneur autrement que l'application prendrait un jour
    un autre conteneur qu'elle, et un dump de la mauvaise base rangé sous le nom de celle-ci est
    exactement la panne silencieuse que la phase 18 existe pour retirer.

    Les trois règles reprises :
      1. le conteneur vient de `docker ps`, jamais d'un réglage — Aspire le renomme à chaque `dotnet run` ;
      2. pgAdmin est exclu par image, son nom contenant « postgres » lui aussi ;
      3. **plusieurs correspondances est un refus, pas un choix** — une machine de développement fait
         tourner les bases d'autres projets.
#>

Set-StrictMode -Version Latest

function Get-PgshContainer {
    <#
        .SYNOPSIS Le conteneur Postgres de PGSH, ou une erreur qui dit pourquoi il n'y en a pas un seul.
    #>
    [CmdletBinding()]
    param([string] $Explicit)

    if ($Explicit) { return $Explicit }

    $rows = @(docker ps --format "{{.Names}}`t{{.Image}}" 2>$null)
    if ($LASTEXITCODE -ne 0) {
        throw "Docker ne répond pas. Le moteur est-il démarré ? (`docker info` pour vérifier.)"
    }

    $matches = @(
        $rows |
        ForEach-Object {
            $name, $image = $_ -split "`t", 2
            [pscustomobject]@{ Name = $name; Image = $image }
        } |
        # ⚠ Par l'IMAGE, pas par le nom : le conteneur pgAdmin s'appelle lui aussi « postgres-… ».
        Where-Object { $_.Image -match '^(docker\.io/)?(library/)?postgres:' }
    )

    if ($matches.Count -eq 0) {
        throw "Aucun conteneur Postgres en cours d'exécution. Démarrez l'AppHost d'abord (le conteneur survit à son arrêt)."
    }

    if ($matches.Count -gt 1) {
        $names = ($matches | ForEach-Object { $_.Name }) -join ', '
        throw "Plusieurs conteneurs Postgres tournent ($names). Nommez celui voulu avec -Container : se tromper de base est irréparable."
    }

    return $matches[0].Name
}

function Get-PgshConnection {
    <#
        .SYNOPSIS  L'utilisateur, la base et le mot de passe, lus du conteneur lui-même.
        .DESCRIPTION
            ⚠ Le mot de passe se lit dans le conteneur (`printenv`) et **n'est ni affiché ni
            journalisé** : Aspire en tire un neuf à chaque démarrage, donc l'écrire dans un fichier
            serait à la fois faux le lendemain et un secret de plus à faire fuir. Mesuré le
            03/09/2026 : la socket locale de cette image est en `scram-sha-256`, pas en `trust` —
            une commande sans mot de passe échoue sur « no password supplied ».
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Container)

    $password = docker exec $Container printenv POSTGRES_PASSWORD 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($password)) {
        throw "Impossible de lire POSTGRES_PASSWORD dans $Container."
    }

    $user = docker exec $Container printenv POSTGRES_USER 2>$null
    if ([string]::IsNullOrWhiteSpace($user)) { $user = 'postgres' }

    [pscustomobject]@{
        Container = $Container
        User      = $user.Trim()
        Database  = 'TodoDatabase'
        Password  = $password.Trim()
    }
}

function Get-PgshBackupDirectory {
    param([string] $Explicit)

    if ($Explicit) { return $Explicit }
    return Join-Path $env:LOCALAPPDATA 'PGSH\backups'
}

function Invoke-PgshPsql {
    <#
        .SYNOPSIS Une requête scalaire, sans afficher le mot de passe.

        .DESCRIPTION
            ⚠ **Le SQL passe par l'entrée standard, jamais par `-c`.** PowerShell retire les guillemets
            doubles des arguments qu'il passe à un exécutable natif : `... from public."Users"` arrive
            à psql en `... from public.users`, PostgreSQL replie l'identifiant non quoté en minuscules,
            et la requête échoue sur « relation "public.users" does not exist » — sur une base où la
            table existe parfaitement. Mesuré le 17/09/2026, sur la vérification d'une restauration qui
            avait réussi : les tables PascalCase de ce schéma rendent le piège systématique.

            Par `stdin` il n'y a plus d'argument à citer, donc plus rien à retirer.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $Connection,
        [Parameter(Mandatory)] [string] $Sql
    )

    $out = $Sql | docker exec -i -e "PGPASSWORD=$($Connection.Password)" $Connection.Container `
        psql -U $Connection.User -d $Connection.Database -t -A -v ON_ERROR_STOP=1 2>&1

    if ($LASTEXITCODE -ne 0) { throw "psql a échoué : $out" }

    $lines = @($out | Where-Object { "$_".Trim() -ne '' })
    if ($lines.Count -eq 0) { throw "psql n'a rien renvoyé pour : $Sql" }

    return "$($lines[-1])".Trim()
}
