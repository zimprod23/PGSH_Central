# Operations — the live base, safe points, transactions, and rebuilding

> Read before any bulk act against the live base, before wrapping a handler in a transaction, and before rebuilding from `Medecine.mdb`.
>
> Split out of `CLAUDE.md` on 2026-09-06 — the text is unchanged. See [`CLAUDE.md`](../CLAUDE.md) for the always-on rules and the map of these documents.

## §0 — Repartir de zéro : reconstruire l'application à partir d'une sauvegarde

> **La marche à suivre quand il ne reste que le dépôt et un fichier `.dump`.** Écrite le
> 17/09/2026, après l'avoir faite pour de vrai : Docker Desktop réinitialisé, `.vhdx` supprimé,
> **tous** les volumes perdus.

### Ce qu'il faut avoir

| | Où | Si ça manque |
|---|---|---|
| le dépôt | git | rien à faire sans lui |
| un point de sauvegarde (`.dump` **+** `.manifest.json`) | `%LOCALAPPDATA%\PGSH\backups` | → §1, reconstruire depuis `Medecine.mdb` ; c'est une autre procédure, bien plus longue |
| Docker qui répond | `docker info` | démarrer Docker Desktop ; si la distro WSL ne monte pas, `wsl --shutdown` puis relancer |

⚠ **Le realm Keycloak n'est pas dans la liste, et c'est voulu** : il est écrit dans
`keycloak/pgsh-realm.json` et se reconstruit tout seul. Rien à restaurer de ce côté.
→ [`../keycloak/README.md`](../keycloak/README.md)

### Les étapes

**1. Lancer l'AppHost une première fois.** Il crée le conteneur Postgres et son volume, et Keycloak
importe le realm.

⚠ **Deux ressources vont échouer, et c'est le comportement correct** — ne cherchez pas de défaut :

| Ressource | Ce qu'elle dit | Pourquoi |
|---|---|---|
| `migrations` | « Cette base n'a jamais été migrée… restaurez un point de sauvegarde » | `Worker.RefuseToBuildAnEmptyBaseAsync` |
| `pgsh-api` | un bandeau `L'API NE DÉMARRE PAS` sur la sortie d'erreur | `SchemaPresenceCheck` |

**2. Arrêter l'AppHost. Laisser Docker tourner** — le conteneur Postgres est
`ContainerLifetime.Persistent` et survit à l'arrêt ; c'est lui dont la restauration a besoin.

**3. Choisir le point :**

```powershell
.\scripts\pgsh-restore.ps1 -List
```

**4. Restaurer :**

```powershell
.\scripts\pgsh-restore.ps1 -Id 20260915-102338-3med-safe
```

Il recopie le dump dans le conteneur, lance `pg_restore`, puis **recompte les douze effectifs du
manifeste** et refuse en nommant les tables qui divergent.

⚠ **Le code de sortie de `pg_restore` n'est un verdict dans aucun des deux sens** : `--clean
--if-exists` signale des objets absents sur une base vide et rend non-zéro en ayant parfaitement
restauré, et il rend 0 sur des erreurs qui ont laissé des tables vides. **C'est le recomptage qui
tranche**, pas lui.

**5. Relancer l'AppHost.** Cette fois : `migrations` ne trouve rien à appliquer et **sème les comptes
statiques absents** ; `pgsh-api` démarre.

**6. Se connecter** — `admin.pgsh@um5.ac.ma` / `123`.

⚠ **La première connexion de chaque personne re-rattache son dossier**, sans rien à faire : le realm
refait émet un `sub` neuf, la ligne restaurée porte celui du realm détruit, et `SyncAsync` retombe sur
l'e-mail puis appelle `User.RelinkIdentity`, qui garde l'ancien sujet dans son événement.

**7. Vérifier à l'écran** : l'effectif des étudiants, la grille de la promotion planifiée, la page
Sauvegardes.

**8. ⚠ Reprendre un point tout de suite :**

```powershell
.\scripts\pgsh-snapshot.ps1 -Label "après restauration"
```

La base restaurée recommence à diverger du point d'où elle vient dès la première saisie, et le
prochain incident se mesurera à partir d'ici.

### ⚠ Les quatre malentendus qui ont coûté le plus, ce jour-là

- **« J'ai des sauvegardes, pourquoi ça plante ? »** — rien ne les applique. Le volume est le stockage
  vivant ; le point est une copie qu'il faut **repousser dedans**. Il n'y a délibérément **aucun
  bouton** qui restaure : un processus ne remplace pas la base qu'il est en train de servir.
- **« On devrait pouvoir repartir à vide. »** — non. Trois migrations sont des migrations de
  **données** et refusent une base vide par conception. **Restaurer, jamais « migrer puis importer ».**
- **`dotnet restore` ≠ restauration de la base.** L'un restaure des paquets NuGet, l'autre des données.
  Aucun rapport.
- **Un volume n'est pas une sauvegarde.** `%LOCALAPPDATA%\PGSH\backups` a survécu *parce qu'il est en
  dehors* du volume. Le volume Keycloak, lui, n'avait aucune copie — d'où le realm en fichier.

### Ce qui n'est pas récupéré

Tout ce qui est entré **après** l'horodatage du point. Le manifeste le dit (`TakenAtUtc`), et c'est le
seul chiffre à regarder pour savoir ce qu'il faut ressaisir.

---

## ⚠ The base is live — take a `pg_dump -Fc` before every bulk act
Since the 2026-09-01 rebuild the development base *is* the faculty's data: 10 203 students, 43 605
registrations, 105 626 périodes, 87 092 évaluations, plus the 2026-2027 réinscription applied through
the UI on 2026-09-02. `pgsh-postgres-data` is a **volume, not a backup** — it survives a restart and
not a bad bulk apply.

- **The dangerous acts are not delete buttons, they are bulk applies**, and each *names* what it
  costs without being able to *undo* it: `ApplyDeliberationCommand` (the file is not stored, so a
  half-corrected promotion cannot be reconstructed), the réinscription roll, `ApplyRotationCycleCommand`,
  `UnpublishCohortScheduleCommand(Force: true)`, `DeleteAllCohortsCommand`, `DeleteAcademicYearCommand`,
  and `ApplyBulkDelocalizationCommand` — which deletes the planned rotations of every student it names.
  ⚠ That last one is the **only** bulk act with a per-row undo (`CancelDelocalizationCommand`), and the
  undo does not restore the périodes: it returns the students to the répartition, which then has to be
  re-published. See [`delocalization.md`](delocalization.md).
- ⚠ **`IAuditableCommand` records the criteria, the author and the date — never the previous values.**
  The trail says what was asked for, not what it replaced.
- ⚠ **Do not pipe `pg_dump`** — write with `-f` inside the container, then `docker cp`. A piped dump
  has already been corrupted once here.
- **The mechanism exists for both halves now.** Taking: the Sauvegardes page and a daily timer.
  Restoring: **`scripts/pgsh-restore.ps1`**, because the API deliberately cannot — a process does not
  replace the database it is serving from. `PHASES.md` §18.2.

## ⚠ Le 17/09/2026 : le volume est parti, et ce qui a décidé de la suite

Docker Desktop réinitialisé, `.vhdx` supprimé, **tous** les volumes avec. Ce qui a été perdu et ce qui
ne l'a pas été dit tout ce qu'il faut savoir sur où ranger une sauvegarde :

| | |
|---|---|
| `pgsh-postgres-data` | perdu — c'est un volume, **pas** une sauvegarde |
| `%LOCALAPPDATA%\PGSH\backups` | **intact** — hors du volume, par construction ; le plus récent avait deux jours |
| le volume Keycloak | perdu, et **sans aucune copie** — realm, comptes et client à refaire de mémoire |

- ⚠ **Une base vide ne se reconstruit pas en lançant les migrations.** Trois d'entre elles sont des
  migrations **de données** (`Cnpn1650Med3Stages`, `Cnpn1650ImmersionStages`,
  `Cnpn1650Med3CatalogueAlignment`) qui lisent le catalogue que l'import hérité écrit ; sur une base
  vide elles font `RAISE EXCEPTION` — *« Aucun niveau « 3ᵉ année Médecine » : le catalogue des niveaux
  doit exister avant les stages. »* C'est voulu, et la migration le dit : « Refusing costs an apply; a
  wrong requirement set costs a promotion planned against stages it does not owe. » C'est aussi
  pourquoi le palier Testcontainers passe par `EnsureCreated` et non par la chaîne de migrations.
  **Donc : restaurer, jamais « migrer puis importer ».**
- ⚠ **Avoir des sauvegardes n'empêche rien** : rien ne les applique au démarrage. Le volume est le
  stockage vivant, le point de sauvegarde est une copie qu'il faut **repousser dedans**. C'est
  exactement ce qu'on a cru l'inverse ce jour-là.
- ✅ **Ce que l'incident a changé** : le realm Keycloak est désormais un fichier versionné
  (`keycloak/pgsh-realm.json`, importé au démarrage), donc perdre son volume n'est plus un incident.
  → [`../keycloak/README.md`](../keycloak/README.md)

## A safe point is a dump plus a manifest, and the manifest is the half that matters
`Domain/Backups/` (pure) · `Application/Backups/` (the port and the handlers) ·
`Infrastructure/Backups/` (the `docker exec pg_dump` adapter and the timer). Built 2026-09-03.

- ⚠ **The register is the directory, not a table.** A registry kept in the base would be rolled back
  by the very restore it describes: every point taken after the restored one vanishes from the list
  while its file sits on disk, and the operator reads a list that disagrees with the directory. It is
  also why this shipped against a live base with **no migration**.
- ⚠ **`SchemaFingerprint` treats *unknown* as « cannot certify », never as agreement.** A dump taken
  before a migration and restored under code expecting the new schema gives a base the running app
  cannot read, and nothing about the file says so. Only the migration is compared — the same schema
  built from two shas is the same schema, and comparing the sha would refuse every restore taken
  before the last commit, i.e. all of them.
- ⚠ **`SafePointState` keeps `Unavailable` and `None` apart, and that is the whole design.** « The
  runner cannot be reached » and « there is no backup » call for opposite acts — fix Docker, versus
  take a point — and one blank covering both is the same defect as an omitted year read as « toutes
  les années ». `SchemaChanged` outranks `Stale`: an hour-old dump under the wrong migration is a
  restore that *refuses*; a three-day-old one under the right migration works and costs three days.
- ⚠ **`DatabaseCensus` is name → count, and a missing key is `null`, never 0.** A manifest is a
  document read back months later, possibly by a build that knows tables the writer did not. « Ce
  point n'en dit rien » and « rien n'a changé » are different answers and only one is a reason to
  proceed.
- **`SafePointTaker` owns what a point records**, because there are two callers — the command a human
  or a dialog sends, and the timer — and a manifest written by one that the other cannot compare
  against is a point with no undo attached. The timer goes through the taker rather than through
  MediatR: it has no `HttpContext`, so `ExecutionAuthorizer` would have nobody to judge, and the
  guard exists for HTTP callers.
- ⚠ **Never piped.** `ProcessRunner` uses an argument list with `UseShellExecute = false`, and the
  dump is written with `-f` **inside** the container and then `docker cp`-ed out — the procedure that
  has worked three times here, against the piped one that corrupted an archive once.
- ⚠ **The container is discovered from `docker ps`, not configured.** Aspire names it itself
  (`postgres-…`) and a name written into settings goes stale on the next `dotnet run`; pgAdmin is
  excluded by image, since its name contains « postgres » too. **Several matches is a refusal, not a
  choice** — a developer machine routinely runs other projects' databases, and a dump of the *wrong*
  base, filed and labelled as this one's safe point, is exactly the silent failure the phase removes.
- ⚠ **The printed restore command carries `PGPASSWORD=<mot de passe>` as a placeholder, plus the line
  that obtains it.** Measured 2026-09-03 against the running container, the image's local socket is
  `scram-sha-256`, **not** `trust` — a command without it fails with « no password supplied », which
  reads as a broken instruction. The value itself never reaches the page: a credential on a screen is
  a credential in a screenshot.
- **The chain was exercised against the real container** (`pg_dump -Fc -f` → `docker cp` →
  `pg_restore -l`, schema-only). ⚠ To repeat it by hand from Git Bash, export `MSYS_NO_PATHCONV=1`,
  or MSYS rewrites `/tmp/x` into `C:/Users/…/Temp/x` and the check fails for a reason that has nothing
  to do with the product. Production is unaffected — `ArgumentList` passes through no shell.
- ⚠ **The cadence and the freshness window are one decision, not two.** The timer is
  **daily** (`IntervalMinutes` 1440, since 2026-09-04) and `SafePointEvaluator.DefaultFreshFor`
  is **48 h** — one interval plus the run that closes it. Freshness means « the timer has not
  missed a run », not « the dump is recent », so lengthening one without widening the other
  makes every point read `Stale` between runs on a healthy system: a warning that fires
  whatever the data says, i.e. noise, i.e. dismissed. Pinned by
  `A_point_one_whole_scheduled_interval_old_is_still_fresh`, which restates the interval
  rather than referencing it so the test is a check and not a tautology.
- **Retention prunes `Scheduled` points only.** A point taken by hand, or by a dialog before a
  déliberation, is the only record of a state that has no other undo.
- ⚠ **Nothing restores from the API, and that is not a gap to close later.** A process cannot replace
  the database it is serving from — the restore drops and recreates objects the API holds open.
  `GetRestorePlanQuery` returns the cost in both directions and the command to run with the stack
  stopped. **A schema mismatch does not fail that read**: the refusal has to be able to name the
  `dotnet ef database update` that makes the point usable, and a query that failed could not.
- **Roles split where the risk does.** *Taking* is `Roles.Administrative` — scolarité applies the
  bulk acts, so a gate it could not pass would put the button out of reach of the only person who
  needs it. *Deleting* is `SuperUser`, and the **newest** point is refused to everybody: it is the
  one every confirmation dialog reads.
- ⚠ **The banner does not block.** With no usable undo the act asks for a ticked box, exactly like
  `ConfirmedDefaultCount` — confirm what cannot be undone. Blocking outright would mean that the day
  Docker is down, nobody can close a year.
- ⚠ **`Backups:KeycloakRealmCovered` is `false`, and the page says so.** The realm is a second volume;
  restoring the base without it leaves `SyncUserMiddleware` matching a `sub` against `User` rows that
  are gone, and its fallback is the e-mail address — i.e. somebody else's account.

## A multi-step write is one transaction, or a closed tab is a half-built plan
`IApplicationDbContext.ExecuteAtomicallyAsync` — used by `GenerateMacroPlanCommandHandler`, which
writes cohortes, then affectations and cells **stage by stage**, committing after each.

- ⚠ **The failure is the browser's, not the database's.** Closing the tab or losing the connection
  cancels the request; EF abandons the remaining steps and everything already committed stays. What is
  left is a plan built for the first three stages and nothing for the rest — not obviously broken,
  simply wrong, and indistinguishable from a plan somebody meant that way.
- **A `Result` failure rolls back too.** A refusal returned halfway through leaves exactly the same
  partial state as a dropped connection.
- ⚠ **`ChangeTracker.Clear()` runs on retries only — clearing on the way in ate the audit entry.**
  `AuditLogPipelineBehavior` stages the act's journal row **before** the handler; that is exactly what
  makes a refused act write nothing and a successful one record itself in the same unit of work. A
  helper that opened by clearing the tracker destroyed it: the act committed and its trail vanished,
  silently, on the one table whose whole purpose is to be read back later. **Found by driving the real
  screen on 2026-09-05** — a service reorder wrote its ranks and produced no `STAGE_SERVICE_ORDER_SET`.
  Entities staged before the transaction are snapshotted and re-staged after a clear, so a retry
  drops the failed attempt's rows without losing the journal entry.
  - ⚠ **Any `IAuditableCommand` whose handler wraps its work here had the same hole.** Today only the
    service-order command does; `GenerateMacroPlanCommand` is not auditable. But bulk acts are
    precisely the ones that want both a transaction and a trail, so the next one would have hit it.
  - Pinned by `The_act_records_itself_even_though_the_write_opens_its_own_transaction`, which
    reproduces the live symptom exactly — verified by restoring the original two lines and watching
    it fail.
- ⚠ **Through `Database.CreateExecutionStrategy()`, never straight to `BeginTransaction`.** Aspire's
  `AddNpgsqlDbContext` enables retry-on-failure, and a retrying strategy refuses a user-initiated
  transaction outright — which would turn every wrapped handler into a 500. `ChangeTracker.Clear()`
  opens each attempt, or a retry inserts the failed attempt's entities twice.
- ⚠ **Domain events still publish from each inner `SaveChangesAsync`, i.e. before the outer commit.**
  Nothing on the macro-plan path raises one (the entities are built with object initialisers), but a
  handler wrapped here must raise no event whose handler assumes the write is durable.
- ⚠ **The in-memory provider has no transactions**, so `TestHarness` and `ApiFactory` ignore
  `InMemoryEventId.TransactionIgnoredWarning` and the call becomes a no-op. That is the honest reading:
  **this suite cannot prove atomicity**, exactly as it cannot prove a FK or an `OnDelete`. It proves
  the steps inside the unit of work.

## ⚠ Rebuilding the base from the Access file is not « migrate, then import »

Three things break a naive `drop → dotnet ef database update → import`, and two of them break
**silently**. Measured 2026-09-01.

**1 · The CNPN data migrations refuse to run against an empty base.** `Cnpn1650Med3Stages`,
`Cnpn1650ImmersionStages` and `Cnpn1650Med3CatalogueAlignment` open with
`RAISE EXCEPTION 'Aucun niveau « 3ᵉ année Médecine »…'` — they need the `Levels` and `Stages` the
*import* creates. So the chain is:

```
1. dotnet ef database update 20260830143914_PriorEnrolment   # the last schema-only migration
2. PGSH.LegacyImport --source Medecine.mdb --connection <cs> --apply
3. PGSH.LegacyImport --seed-curricula --connection <cs> --apply
4. dotnet ef database update                                  # the remaining CNPN migrations
5. PGSH.LegacyImport --stamp-cnpn --connection <cs> --apply
```

**2 · Step 5 is the one nothing else does — `CnpnHistoryAttributor`.** The student attribution was a
single `UPDATE` inside `CnpnVersioning` and the registration backfill another inside
`RegistrationCnpnAndLevelEffectivity`. Both were written to run *over* data already present. Replayed
in the order above they run before the import, stamp nobody, and are then marked applied — so nothing
runs them again, and the base ends with 10 200 students and 49 500 registrations carrying a null
text. ⚠ **Every reader falls back on null gracefully, so nothing complains**: the déliberation stops
knowing whose year might be his last, the final-year gate stands aside for everyone, and
`CohortProvisioner` plans against requirement sets nobody is bound by. The pass reuses
`EntryYearDeduction` and `CnpnAssignment` rather than restating them, never moves a *confirmed* stamp,
and never overwrites a registration's own.

**3 · The CNPN texts lose the intake year they are selected by — and that is silent too.**
`CnpnVersioning` reads them out of `AcademicYears`
(`(SELECT "Id" FROM "AcademicYears" ORDER BY "StartDate" LIMIT 1)` for 2174.18 and PHARM-LEGACY, the
row labelled `2024-2025` for 1650.25). Running before the import, that table is empty and all four
texts are stored with **no intake year at all**.

⚠ **A text with no intake year is not malformed — it is *citation-only***, which arrêté 2175.22
legitimately is. So nothing throws and nothing refuses; `CnpnAssignment.SelectVersionAsync` simply
finds no candidate for anybody. Measured on the first 2026-09-01 rebuild: **10 185 of 10 185 students
unresolved, 0 stamped**, reported as a count by a pass that returned success. Closed twice over —
`CnpnIntakeYearsBackfill` fills the three that are meant to have one (never 2175.22), and
`CnpnHistoryAttributor` now **refuses** when it can place nobody at all, because one unplaceable
student is a fact and the whole population is a broken catalogue.

**4 · The import cannot restore what was authored here.** Nothing in `Medecine.mdb` expresses it, and
it is not test residue. Measured on the live base before the 2026-09-01 rebuild:

| | count | why it cannot be regenerated |
|---|---|---|
| `Holidays` | 24 | Aïd, Moharram and Mawlid follow the Hijri calendar, turn on observation of the crescent, and are announced by decree — they **cannot be generated, only entered** |
| `StageAllowedServices` | 146 | authored per stage; the source has no such column |
| `CnpnLevelEffectivities` | 3 | « ce texte régit ce niveau à partir de cette année » — authored, and it decides the text of every registration created there afterwards |
| `ServiceChefAssignment` | 2 | the only dated chef evidence; 140 of 148 services carry only an undated legacy note |
| `AcademicYears` → 2026-2027 | 1 | the Access base stops at 2025/2026 |

Dump those **before** dropping anything. What is *not* preserved is planning the rebuild makes
meaningless anyway — partitions, `StageSlot`s, cells, and the verdicts of a year about to be
re-imported.

⚠ **Natural keys where they are unique, ids where they are not — and the difference is measured, not
assumed.** The obvious rule (« never key a restore on ids, the import regenerates them ») is right in
general and produced a wrong restore here: **service names are not unique.** 25 are shared across
hospitals — « Pharmacie » exists in 9 — and « Urologie » appears twice inside one hospital, so a
`JOIN Services ON Name = …` fanned **146 `StageAllowedServices` out into 178**. `Service` carries no
external identifier at all: the importer keys it on the Access `CodeS` and does not persist it.

What makes ids usable is that the import is **deterministic**, and that was checked rather than
believed: the pre-rebuild dump restored beside the rebuilt base and joined on `Id` gives 148/148
services identical, 0 stages and 0 levels differing. The restore therefore **asserts its own counts
in SQL** — `RAISE EXCEPTION` on a mismatch, so psql exits non-zero and the rebuild stops. That
assertion is what a silent fan-out needs, and what its absence cost.

⚠ **The two seeded employees have to be restored too.** `PGSH.MigrationService` creates them at
Aspire startup, which a rebuild never runs — so the chef tenures pointed at users that did not exist
and restored **0 of 2**, without an error.

⚠ **The 2026-2027 year has to be restored before the effectivity rules**, one of which takes effect
from it — and `IX_AcademicYear_IsCurrent` is unique and filtered, so demote and promote are **two
statements in that order**, never one `UPDATE`.

## ✅ Les trois actes destructeurs sont atomiques (2026-09-12)
`DeleteAllGroupsCommand`, `DeleteAllCohortsCommand` et `EmptyAllYearGroupsCommand` n'écrivent que par
`ExecuteDelete` / `ExecuteUpdate`, **hors du change tracker**. Chaque instruction atterrissait pour de
bon dès qu'elle passait, et rien ne les liait : six pour « Supprimer les groupes », cinq pour
« Réinitialiser les cohortes », une plus la ligne du registre pour « Vider les groupes ».

**Ce que laissait une interruption.** Les six vont dans cet ordre — périodes, historique de cohorte,
affectations, cellules, cohortes, groupes — puis la ligne de journal. Une requête coupée après la
troisième laissait les groupes, leurs cohortes et **toute la grille** en place, avec plus personne
dedans : un plan complet et confiant pour zéro étudiant, que rien à l'écran ne distingue d'un plan
voulu. Et le registre se taisait, sa ligne étant la septième instruction — donc des périodes détruites
sans une trace, sur l'acte le plus destructeur de la campagne.

⚠ **Ce n'est pas un scénario rare.** ASP.NET annule la requête dès que la connexion tombe, et un
onglet fermé *est* une connexion tombée. Sur une promotion de 925 étudiants, l'acte dure des secondes
visibles.

**Corrigé** : chacun des trois est enveloppé dans `IAuditTrail.RunAtomicallyAsync` — tout atterrit, ou
rien. ⚠ Par la **piste** et non par le contexte : l'acte est audité, une nouvelle tentative vide le
change tracker, et seule la piste sait quelle version de l'entrée est la bonne. Voir
[`docs/audit-calendar.md`](audit-calendar.md).

⚠ **La sauvegarde reste la règle** : `pg_dump -Fc` avant tout acte de masse. L'atomicité protège d'une
destruction *à moitié faite*, jamais d'une destruction complète qu'on regrette.

## Le canevas des affectations (13/09/2026)

C'est un acte en masse sur la base vivante, donc la règle ordinaire s'applique sans changement :
**`pg_dump -Fc` avant**, et c'est l'utilisateur qui clique.

Ce qui le distingue des autres actes en masse :

- il **détruit** des périodes, et le nombre détruit est confirmé **séparément** de ce qui est écrit ;
- un seul refus, n'importe où dans le fichier, refuse le **fichier entier** — il n'y a pas
  d'application partielle à rattraper ;
- **il n'a pas d'annulation en masse.** Renvoyer le fichier corrigé remplace ce qu'il décrit, ce qui
  couvre la faute de frappe ; rien ne retire une affectation qui n'aurait jamais dû exister. Le point
  de sauvegarde est donc le seul retour en arrière pour ce cas-là, et c'est une raison de plus de le
  prendre.

⚠ **Sur une promotion entière, la transaction est rapide et l'acte a l'air de traîner ensuite** : les
événements se publient après le commit, un par un, chacun écrivant une ligne d'historique. Le compte
d'`Histories` qui monte est un avancement, pas un blocage — même forme que la déliberation et le
rouleau de réinscription.

Détail complet dans [`affectation-sheet.md`](affectation-sheet.md).

## Annuler un téléversement d'affectations (13/09/2026)

Le manque signalé dans la section précédente est comblé : `POST affectations/imports/{id}/reversal`,
précédé de son aperçu. **Le point de sauvegarde reste la bonne habitude** — l'annulation supprime des
affectations — mais il n'est plus le *seul* retour en arrière.

Ce qu'il faut savoir avant de s'en servir :

- elle est **totale** là où elle s'applique : les périodes remplacées reviennent avec leurs drapeaux et
  leur cellule de grille, pas seulement avec leur service et leurs dates ;
- elle est **tout ou rien** : une affectation évaluée, pointée ou replanifiée depuis l'import refuse
  toute l'annulation, parce que la défaire imposerait un état ancien par-dessus un travail plus récent ;
- `confirmedCount` porte les affectations **supprimées** — celles que l'import avait créées. Remettre
  une rotation se refait en renvoyant le fichier ; une affectation supprimée, rien ne la remet ;
- un import déjà annulé refuse, et **reste au registre**.

⚠ **La migration `AffectationImportJournal` doit être appliquée** avant que l'import puisse être défait.
Elle est purement additive (trois tables neuves) et ne touche rien d'existant — mais un import appliqué
**avant** elle n'aura laissé aucun registre, donc rien à défaire.
