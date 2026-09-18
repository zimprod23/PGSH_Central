# Planning — rosters, partitions, and taking a plan apart

> Read before touching `AcademicGroup`, partition labels, pauses, or any act that unpublishes, clears or deletes part of a plan.
>
> Split out of `CLAUDE.md` on 2026-09-06 — the text is unchanged. See [`CLAUDE.md`](../CLAUDE.md) for the always-on rules and the map of these documents.

## Undoing a publication: `unpublish → clear cells → delete slot`
The chain is right and each link is guarded, but ⚠ **deleting a `ServicePeriod` is not bookkeeping**:
`ServiceEvaluation`, `AttendanceRecord`, `PeriodPause` and `Delocalization` all **cascade** from it.

- `UnpublishCohortScheduleCommand` refuses once anything has started (`ScheduleUnderway`) and the refusal
  **names what would be lost** — periods started, marks entered, attendance days. `Force: true` is the
  caller having read that sentence; the UI shows it and asks a second time. Bulk unpublish never forces.
- Removal goes through the aggregate (`InternshipAssignment.RemovePublishedPeriods`), which recomputes
  the note and the status from what is left. Deleting the rows underneath it is what left assignments
  reading *Validated, 14.5* with nothing behind them. `RecomputeStatusFromPeriods` deliberately does
  **not** preserve terminal states the way `SyncStatusAfterReschedule` does — a verdict pronounced over
  evaluations that no longer exist is exactly what has to be walked back.
- ⚠ **Ad-hoc periods are never touched.** A period with no cell behind it is imported history, a
  délocalisation or a revalidation; none came from a répartition and none can be recreated by publishing
  one. Reported as `AdHocPeriodsKept`.

## Taking a plan apart: what each act is allowed to take with it
Four buttons undo planning, and they used to disagree about what "undo" means — one of them silently,
one of them not at all. `AffectationToll` / `AffectationTollReader` (`Stages/Planning/`) is the single
answer to "what is this act about to destroy", read the same way by all of them so two refusals cannot
describe the same rows differently.

⚠ **An affectation does not hang off the roster pointer.** `InternshipAssignment` is
(inscription × **cohorte**) and `ServicePeriod` hangs off the affectation, so clearing
`Registration.AcademicGroupId` leaves every one of them exactly where it was.

⚠ **Every « quel autre groupe ? » picker is scoped to the student's own promotion, server-side.**
The transfer, the changement de groupe, the échange and the mass délocalisation all ask
`/groups?levelId=` and all drop the level-less bucket. A roster of another promotion runs stages this
student does not owe — the act is refused on arrival — so offering it is offering a refusal.

- ⚠ **The promotion is read off `GroupDetailResponse.LevelId`, and it was not there before
  2026-09-06.** The three screens derived it by looking the current roster up *in the options list*,
  which asks for 200 of the 1 003 rosters: past that page the promotion came back `null`, the guard
  fell through, and every group of the year was offered. Same class as « never compute a count from a
  page » — a *scope* derived from a page is a scope that silently widens.
- ⚠ **`?levelId=` is deliberately wider than « rosters of this promotion »**: it also matches a
  level-less roster holding a registration of that level, so « Non réparti » stays findable on the
  screen scolarité assigns from. A picker therefore drops `LevelId == null` itself — that bucket has
  no cohorte on any stage, so every student in it comes back « pas de cohorte ».

⚠ **A roster délocalisé en masse is a fifth act, and it is deliberately none of these four.** It
deletes the students' périodes and leaves the roster, its cohortes and every cell of the grid exactly
as they were — which is what makes it the one undoable act of the family. The cells simply stop
counting the students who left. See [`delocalization.md`](delocalization.md).

- **« Vider le groupe » cleared the pointer and nothing else.** The result was not an empty roster, it
  was a roster that *reads* empty: the affectations stayed in its cohortes, their périodes stayed on
  the chefs' worklists and in `ServiceOccupancyCalculator`'s counts, and the printed répartition still
  named them — against a page showing 0 étudiants, with nothing on either side saying so.
- ⚠ **…and putting the students back does not undo it.** A re-découpage sends them to *other* rosters,
  and `StudentAffectationService` dedupes on **(inscription, cohorte)** — the new cohortes are not the
  old ones, so each student comes back with a **second** affectation for the same stage. Double in the
  dossier, double against the service quota, two rows for one rotation.
- **The rule now:** nothing planned → empties silently · affectations merely planned → refused, and the
  refusal names the count (`RosterHasAffectations`); `DropAffectations: true` is the caller having read
  that sentence · anything underway → refused outright (`RosterAffectationsUnderway`).
- ⚠ **That last one is deliberately not forceable.** The act that destroys marks and attendance is
  « Dépublier », which names its cost and asks twice. A roster-side button must never become the way
  round it — same reason `AllowOverCapacity` had to stop waiving admissibility.
- **`EmptyAllYearGroupsCommand` has no `DropAffectations` at all.** A roster's affectations are a
  handful of rows an admin can be shown a number for; a promotion's are its whole planning and a
  year's are the faculty's, and destroying them is not what anybody means by « retirer les étudiants
  des groupes ». It refuses while any exist and points at the per-stage reset, where the cost is
  announced stage by stage.
- ⚠ **…and until 2026-09-07 it had no scope between one roster and the whole year, which made a
  re-découpage impossible.** Every other act on rosters is per promotion — the cut
  (`AssignRotationGroupsCommand`), the arrangement (`AutoArrangeGroupsCommand`), the rotation block —
  and `AutoArrangeGroupsCommand` only picks up registrations whose `AcademicGroupId` is **null**, so
  redoing a promotion's groups *requires* emptying its rosters first. Emptying was the one act that
  jumped straight to the year. Measured on the live base that day: 4ᵉ année Médecine 2026-2027 held
  116 rosters, 925 students, **0 cohortes and 0 affectations** — its block had just been deleted — and
  « Vider » was refused over **11 916 affectations / 11 407 périodes** belonging to 3ᵉ MED, 5ᵉ MED and
  5ᵉ Pharmacie, three promotions nobody was touching and whose planning is published. The operator had
  filtered the page to 4ᵉ MED; the button beside that filter ignored it.
- **`EmptyAllYearGroupsCommand.LevelId` is now optional and narrows the act.** Given, the toll is read
  through `AffectationTollReader.ForPromotionRostersAsync` and the refusal names the promotion
  (`PromotionRostersHaveAffectations`); the audit code is `PROMOTION_GROUPS_EMPTIED` rather than
  `YEAR_GROUPS_EMPTIED`, because a register that calls both acts the same cannot say which was run.
  Omitted, the year-wide act is unchanged. ⚠ **The level narrows the year, it never replaces it** — a
  level-only read would count every year that promotion ever ran.
- ⚠ **A refusal must be scoped to the act.** This is the general shape, not a one-off: a guard read
  wider than what the button does refuses over rows the operator has no way to reach, and « réinitialisez
  les cohortes des stages concernés » is unactionable advice when the stages concerned belong to
  someone else's promotion.

⚠ **`DeleteCohortCommand` had no guard whatsoever**, while its bulk twin `DeleteAllCohortsCommand`
refused as soon as one affectation left `Planned`. So the *safe* act was the one touching a hundred
cohortes and the unguarded one was the button beside each line: deleting a cohorte mid-stage removed
every `ServicePeriod` — « plan-generated **and** ad-hoc » — and `ServiceEvaluation`,
`AttendanceRecord`, `PeriodPause` and `Delocalization` all cascade from those. A chef's marks and a
term of attendance, gone on one click, answered with a 204 and no number. Both now share the guard and
both return what they removed.

- ⚠ **`DeleteAllCohortsCommand`'s year was optional and unresolved**, i.e. null meant "every year this
  stage ever ran" — on the one command in this area that deletes rows, against a stage that keeps 563
  cohortes across six years. It resolves through `AcademicYearResolver` like everything else.
- **`Engaged` is read from the affectation's status as well as from the périodes.** A terminal verdict
  (`Validated`, `Rejected`) can stand over périodes since removed, and that is still not something a
  structural act may delete sideways.
- **Two flat queries, not one.** The period counts fold an aggregate over a collection navigation;
  nesting that inside a second aggregate over the affectations is the shape Npgsql refuses — the
  family that killed the macro plan. `SqlTranslationTests` pins both.

**The rotation block was already right, and for the right reason.** `RotationCycleContext` counts
*published cells* through `PublishedCells` (the coverage table, not the FK), so apply and delete are
both refused while anything on the axis is published — and a started rotation is published by
construction. Nothing was added there. ⚠ The one thing it cannot see is a cohorte served only by
**ad-hoc** périodes (imported history, délocalisations, revalidations): those hang off no cell, so they
neither block the removal nor are destroyed by it — removing slots cascades cells, never périodes.

### Repartir de zéro sur une promotion — l'ordre, et ce que chaque acte emporte

Chaque étape refuse tant que la précédente n'est pas faite, et chacune dit pourquoi en nommant des
nombres. ⚠ **La portée de chaque acte n'est pas la même**, et c'est ce qui se lit mal : deux d'entre
eux sont par **stage**, deux par **promotion**, un par **année**.

| # | acte | portée | ce qu'il supprime | ce qu'il laisse |
|---|---|---|---|---|
| 1 | **Dépublier** (par cohorte, ou « toutes » d'un stage) | stage + année | les périodes issues de la grille, et la couverture | l'affectation, la cohorte, la cellule ; les périodes **ad hoc** (histoire importée, délocalisations, revalidations) |
| 2 | **Réinitialiser les cohortes** | stage + année | cohortes, affectations, périodes, adhésions **et cellules** de ce stage | les créneaux (l'axe), les rosters, les inscriptions |
| 3 | **Vider les groupes** de la promotion | promotion + année | rien — seulement `Registration.AcademicGroupId` | tout le reste, y compris les rosters eux-mêmes |
| 4 | **Supprimer le bloc de rotation** | promotion + année | les créneaux du bloc, et les cellules qui y pendaient | les affectations et les périodes, qui pendent à la **cohorte** |
| 5 | **Supprimer les groupes** (facultatif) | promotion + année | les rosters et leurs cohortes | les inscriptions ; « Non réparti », qui n'est d'aucune promotion |

- ⚠ **L'étape 1 n'est nécessaire que si une rotation a *démarré*.** Tant que tout est `Planned`,
  l'étape 2 supprime les périodes elle-même. Dès qu'une période est démarrée, évaluée, ou porte des
  présences, **tout le reste refuse** et « Dépublier » par cohorte est le seul acte qui puisse y
  toucher — celui qui nomme ce que la cohorte perdrait et demande deux fois.
- ⚠ **L'étape 2 se fait stage par stage, et c'est délibéré** : c'est là que le coût est annoncé, une
  fois par stage. Il n'existe pas d'équivalent « pour toute la promotion », et il ne doit pas en
  exister — ce serait la suppression d'une planification entière derrière un seul bouton.
- **Après l'étape 2, l'étape 4 passe** : « publié » se lit sur `ServicePeriodSlotCoverage`, qui
  disparaît avec les périodes, donc `PublishedCells` retombe à 0.
- ⚠ **L'étape 4 ne sert qu'à changer l'axe.** Pour simplement re-découper les groupes et replanifier
  sur le même bloc, on s'arrête à l'étape 3 — le bloc est réutilisable tel quel.
- ⚠ **L'étape 5 exige que l'étape 3 soit faite, et c'est tout ce qu'elle exige.** Un roster habité
  refuse la suppression : la détacher silencieusement de ses étudiants n'est pas un acte. Supprimer
  les rosters n'est de toute façon nécessaire que pour changer leur **nombre** ou leur
  **numérotation** — vidés, ils se re-remplissent — donc l'étape est facultative dans le cas courant.
- ⚠ **L'étape 5 est passée par promotion le 07/09/2026, et c'était le dernier acte de roster à
  sauter directement à l'année.** Signalé ainsi : « supprimer les groupes d'une promotion répond
  *One or more groups in this year have students assigned*, mais quand j'ai vidé les groupes de
  **toutes** les promotions ça a marché ». La garde lisait l'année entière alors que l'acte visait une
  promotion, donc elle refusait sur les étudiants **des autres**, et la seule issue était de vider
  l'année. `DeleteAllGroupsCommand.LevelId` (optionnel, narrows), refus nommant la promotion et
  **comptant** les étudiants restants (`AcademicGroups.HasStudents`,
  `AcademicGroups.PromotionRostersUnderway`), codes d'audit distincts **`YEAR_GROUPS_DELETED`** /
  **`PROMOTION_GROUPS_DELETED`** — l'acte n'écrivait rien au registre jusque-là.
  `DELETE /groups/all?academicYearId=&levelId=`.
- ⚠ **« Non réparti » porte un `LevelId` nul** — il rassemble les inscriptions non réparties de
  **toutes** les promotions — donc un acte scopé sur une promotion l'enjambe et seul l'acte annuel
  l'atteint. C'est la bonne coupure : ce roster n'appartient pas à la promotion nommée.
- ⚠ **Un niveau inconnu refuse** (`Levels.NotFound`) au lieu de retomber sur « aucun niveau nommé ».

#### ⚠ Ce que les cinq actes ne touchent pas — et il faut le défaire à la main

Les cinq étapes démontent la **planification d'une année**. Trois choses vivent ailleurs et
survivent donc intactes ; deux sont voulues, la troisième surprend.

| ce qui survit | où il vit | à défaire ? |
|---|---|---|
| `StageAllowedService.PlacementMode = Reserved` | la ligne d'autorisation, **invariante à l'année** | ⚠ **oui, à la main** |
| `StageAllowedService.Rank` (l'ordre de rotation) | idem | non — c'est le catalogue |
| les périodes **ad hoc** des années passées | l'affectation, hors grille | non — c'est l'histoire |
| les lignes de **dossier** (`Histories`) | l'étudiant, jamais l'année | ⚠ **oui, et il n'y a pas d'écran** |
| les entrées de **registre** (`AuditLogs`) | l'acte administratif | non — c'est sa raison d'être |

⚠ **Le dossier survit à tout, et il n'a pas d'année.** `Histories` porte
`Id, HistoryData, CreatedAt, StudentId, Metadata` et **aucun `AcademicYearId`** : démonter une année
n'en efface aucune ligne, et rien dans l'application ne permet d'en supprimer une. Une remise à blanc
qui doit aussi valoir pour le dossier passe donc par du SQL, et **le scoper par étudiant ou par date
est faux** — un 4ᵉ année traîne cinq promotions derrière lui, et l'import Access a écrit ses lignes
en août 2026. Ce qui scope, mesuré le 10/09/2026, c'est le **type** : les `StatusChange` sont les
verdicts de déliberation (toutes `"academicYear": "2025-2026"` à cette date), tandis que
`Delocalization`, `DelocalizationCancelled`, `GroupTransfer` et `CohortTransfer` naissent du travail
de planification de l'année en cours. Vérifier qu'aucune clé étrangère ne pointe vers `Histories`
(aucune ne le fait), exporter les lignes avant, et garder le compte comme garde. → `NOTES.md`,
session 57.

⚠ **Et le registre, lui, ne se vide pas avec.** Le dossier est le récit de ce qui est arrivé à un
étudiant ; le registre est la trace de ce que l'administration a fait. Les vider ensemble effacerait
la preuve que l'acte a eu lieu. → [`audit-calendar.md`](audit-calendar.md)

⚠ **Un service réservé le reste après une remise à zéro complète**, et c'est la seule des trois qui
peut se lire comme un défaut : la promotion est repartie de rien, la répartition automatique refuse
de le pourvoir, et **rien à l'écran de la planification ne dit pourquoi** — le mode est sur la fiche
du stage, pas sur la grille. La bascule « Réservé » de `Admin → Stages → services autorisés` est ce
qui le rend à la rotation. C'est le pendant exact de la règle du `Rank` : ce sont des entrées de
planification portées par le **catalogue**, et le catalogue ne se remet pas à zéro avec une année.

⚠ **`AcademicGroup.Purpose` et `CohortSlotAssignment.Source`, eux, disparaissent normalement** — le
motif avec son roster à l'étape 5, l'épinglage avec sa cellule aux étapes 2 et 4. Rien à défaire.
  L'élargissement-sur-absence est ici le plus cher de tous : il supprimerait les rosters de **toutes**
  les promotions après un contrôle qui n'en a regardé qu'une.
- **Le prédicat est écrit une fois** (`RosterScope.Query`), partagé par « Vider » et « Supprimer ».
  Les deux posaient la même question et y répondaient séparément, ce qui est exactement comment
  l'une a gagné la portée par promotion et l'autre non.

**Puis on reconstruit**, dans l'ordre inverse : découper en groupes (« Répartir automatiquement »,
qui ne ramasse que les inscriptions sans groupe) → découper en partitions → poser le bloc → répartir
→ publier.

⚠ **Deleting the rotation block is not one of the steps that clears affectations.** It removes the
`StageSlot`s and cascades the *cells* planned on them; an `InternshipAssignment` hangs off the
**cohorte**, so it survives untouched — which is why « j'ai supprimé le bloc » leaves « Vider » still
refusing. Only « Réinitialiser les cohortes » deletes affectations, and it is per (stage, année).

## ⚠ ~~A pause is stage-scoped, compensates in calendar days, and does not move the grid~~ — retired 18/09/2026
`StagePauseRunner` (`Stages/Planning/`) + `InternshipAssignment.PausePeriod` / `ResumePeriod` **no
longer exist**, nor do `POST stages/{id}/schedule/pause` and `.../resume`, nor the two buttons on
« Suivi des affectations ». The section is kept because every bullet below is why, and because the
same mistake is available to anyone writing the cascade: **the four faults are one fault, which is
writing dates at pause time.**

⚠ **The earlier verdict here was « the stage-scoped pause stays ».** It was reversed, not forgotten —
the reasoning and the honest statement of what was *lost* are in `PHASES.md` §17.2.

- **Scope is one stage, one academic year** (both mandatory — pausing "Chirurgie" must not reach a
  promotion that sat those exams six years ago), then optionally cohortes, a partition label, or
  period numbers. It sets `IsPaused` on every `Underway` période and opens a `PeriodPause` **dated
  today**; resume closes it, adds the elapsed days to that période's `EndDate`, and pushes every
  later période of the same assignment forward by the same amount. Closed and interrupted périodes
  never move, which is correct — a closed rotation is what actually happened.
- ⚠ **An exam week is a fact about a promotion, not a stage.** Two promotions rotate through the same
  services on the same morning and only one of them is composing. Suspending a promotion is therefore
  one call per stage, and **nothing records that they were one event** — no row to correct, none to
  revoke, and the calls can diverge.
- ⚠ **The shift is in *calendar* days.** `WorkingDayCalendar` is not consulted on this path at all,
  so a pause spanning a weekend costs two days nobody was going to serve.
- ⚠ **Only the `ServicePeriod`s move; the `StageSlot`s and cells stay.** After a resume the grid and
  the périodes published from it disagree, silently.
- ⚠ **`ResumePeriod` accumulates** — it adds days on every run. Anything declarative built here must
  *derive* the shift from a window rather than add to it, or re-applying moves the dates twice.
- ⚠ **Nothing can be declared in advance and a forgotten resume is silent** — rotations frozen with
  no end date, no compensation, and nothing on screen that reads as wrong.

The replacement is a **promotion-scoped calendar**, not a second date-pushing mechanism:
`WorkingDayProvider.ForPromotionAsync(yearId, levelId, ct)` = faculty holidays ∪ that promotion's
declared pauses, so every existing reader compensates in worked days and the grid and the périodes
stay laid from one calendar. Plan and guards: `PHASES.md` §17.

⚠ **And it is now the *only* act**, since 18/09/2026: the promotion pause turned out to be a
replacement after all, not a second act. A window is **declared** — no date written, revocable,
correctable — and the columns it cuts are moved one at a time by
`InternshipAssignment.Reschedule`, which writes **absolute** dates and therefore does nothing when
replayed with the same window. Declaration and displacement are two acts on purpose: the first is a
decision, the second a consequence, and the retired pause was both at once.

⚠ **What this withdrew, said plainly:** suspending a single cohorte's rotation is something PGSH can
no longer do. Nothing replaces it. When it is asked for, it is built on the declarative shape above.

### …and a published period cannot be moved mid-flight
« On est en P3, peut-on changer P7 ? » — no, and one of the three obstacles is a trap rather than a
refusal:

- ⚠ **`UpdateStageSlotCommandHandler` has no published-guard at all**, unlike `DeleteStageSlot`
  (`SlotHasPublishedCellAsync`). It rewrites the créneau's dates and never touches the `ServicePeriod`s
  published from it. Editing P7 is *permitted* and splits the grid from the périodes without saying
  so, which is worse than a refusal.
- `SetCohortSlotAssignmentCommandHandler` refuses when the **cohorte** holds any grid-linked période,
  not when *this cell* is published — `PublishedCells.IsCellPublishedAsync` answers the narrower
  question and is not used here.
- `UnpublishCohortScheduleCommand(int CohortId, bool Force)` has no period scope: undoing P7 undoes
  P1-P10 for that cohorte, and `Force` takes the marks and the attendance of the périodes already
  served.

⚠ **`SingleService` complicates all three** — the *kₛ* cells fold into one `ServicePeriod`, so editing
a column mid-run splits a stay rather than editing a row. `PHASES.md` §17.1.

## A roster belongs to one promotion, and its number counts within that promotion
`AcademicGroup` is keyed `(AcademicYearId, LevelId, GroupNumber)` — `IX_AcademicGroup_Year_Level_Number`,
`NULLS NOT DISTINCT`. The faculty numbers its groups per promotion and runs them at the same time: the
3rd year 1-80, the 5th year 1-60, the 6th year 1-100. A number without its promotion identifies nothing.

- ⚠ **This was the largest data defect in the base, and it emptied a répartition.** `LegacyImportPlanner`
  keyed rosters on `(ANNEE_UNIV, GROUPE_STG)` alone, folding all three numberings into one set of rows:
  measured 2026-08-13, **80 of the 100 numbered rosters of 2025-2026 carried registrations from four or
  five promotions at once**, and `LevelId` was null on all 1,003 rows. `GroupScheduleConflictGuard`
  forbids a roster from being in two services at once — correctly, on the premise that a roster is one
  set of students — so the 3rd year's April–July placements *were* the 5th year's, seven of the 5th
  year's nine columns were refused, and the printed document came out with two.
  `SplitAcademicGroupsPerLevel` splits them; the importer now keys on the promotion too.
- **« Non réparti » (`GroupNumber = 0`) is the one roster with no promotion**, by definition: it holds
  every promotion's unassigned registrations — 4,725 of them in 2025-2026 — and carries no cohorts.
  `NULLS NOT DISTINCT` is what keeps a year to one of them.
- ⚠ **Reach a promotion's rosters by `LevelId`, never by "has a registration at that level".** That
  fallback existed for legacy rows without a level; it also reaches the bucket, so cutting one level
  handed a partition label to 4,725 people. Planning paths (`AssignRotationGroups`,
  `ClearRotationGroups`, `PreviewRotationCycleQuery`) match on `LevelId` alone. `GetAcademicGroupsQuery`
  deliberately keeps the wider reach — it is the screen scolarité assigns *from*, and hiding the bucket
  behind a level filter hides the students it exists to surface.
- **Numbering restarts at 1 per promotion** (`AutoArrangeGroups`, `CreateGroup`). It used to continue
  from the year's highest number, which is why a 5th year would have printed as groups 81-140.
- **The label is keyed the same way** — `IX_AcademicGroup_Year_Level_Label`, `NULLS NOT DISTINCT`
  (`GroupLabelPerPromotion`). Held to (year, label), « Groupe 1 » — the obvious name for the 4th
  year's first roster — was already taken by the 3rd year's, so a promotion could not be *named* the
  way it is numbered and printed. A label distinguishes two rosters of one promotion and nothing more.
- ⚠ **An index makes rosters distinguishable; it cannot stop them being mixed.** Two writes point a
  row at a roster by plain FK and neither is guarded by anything downstream — every later check is
  keyed on the roster the row *claims*. Both are now refused (`AcademicGroupErrors`,
  `StageErrors.CohortPromotionMismatch`):
  - `TransferStudentCommand` — a target roster in another **year** or another **promotion**. Otherwise
    the student is affected to that roster's cohorts, i.e. stages he does not owe, and counted against
    the other promotion's service quota. This is the write that could recreate by hand what
    `SplitAcademicGroupsPerLevel` had to repair across 1,003 rows.
  - `CreateCohortCommand` — a roster paired with a stage of another promotion. `CohortProvisioner`
    always checked this on the bulk path; the hand-built path had no equivalent.
- ⚠ **The bucket is asked for by its own name — `AcademicGroup.AsUnassignedBucket(yearId, label)`.**
  Since 14/09/2026 the type is closed: the three keys are `private set` and there is no constructor,
  so a promotion's roster goes through `ForPromotion(yearId, levelId, number, label)`, which demands
  all three. **Two factories rather than one with a nullable `levelId`**, because under a single one
  *forgetting* the promotion and *meaning* the bucket are the same call — and that is the shape the
  4 725-student incident came from. The bucket factory also takes no `rotationGroup` at all, so the
  next rule is not only refused at runtime, it has no parameter to pass through. The same treatment
  closed `Cohort` — `(StageId, AcademicGroupId)`, `Cohort.For(...)` — and `StageSlot` before it.
  → `PGSH.Tests/Application/PlanningIdentityTests.cs`
- ⚠ **« Non réparti » must never acquire a partition label or a cohorte.** Either turns the bucket
  into a roster and moves every promotion in it as one body. `AssignRotationGroups` can no longer
  reach it, but `CreateGroup`/`UpdateGroup` write `RotationGroup` directly and are refused
  (`UnassignedRosterCannotBePartitioned`), which is what lets `CohortProvisioner` match on `LevelId`
  alone — its old "or has a registration at that level" fallback matched the bucket for *every* level
  in a plan. The 12 bucket cohorts in the base are legacy-import history and are left alone; the guard
  is on creation.
- ⚠ **Only `AssignRotationGroupsCommand` cuts a promotion.** `RotationArranger` used to fall back to
  `services.Count` — the *stage's* service count — whenever no count was given and no group carried a
  label. That is not a statement about how a promotion divides, and it is sticky: Santé Publique has
  one service, so arranging it first cut the whole promotion one-way and every later stage inherited
  it, because `BuildLabels` lets an existing cut win over any requested count. The arranger now fills
  gaps in an existing cut, cuts only on an explicit count, and reports `PromotionNotPartitioned` when
  a partition is targeted on a promotion nobody has divided — rather than writing 0 cells, which
  reads as "nothing to do".
- ⚠ **…and the cut it fills gaps in is the *promotion's*, never the stage's cohorts**
  (`PromotionPartitioning`). `BuildLabels` takes "the existing partition count" from the labels it is
  shown, and a stage routinely reaches only part of its promotion — `CohortProvisioner` skips what a
  text does not require, and cohorts are provisioned stage by stage. Shown one stage's cohorts, a
  promotion cut into ten read as cut into two, and the gap-fill wrote those two onto real rosters
  permanently: measured on Med6 (2026-08-13), **A = 42, B = 42, C–J = 2 each** on a promotion re-cut
  into ten clean partitions the session before. The balance is wrong for the same reason — "fill the
  smallest partition" measured over a subset is not the promotion's smallest. The mirror case is worse
  because it is silent: a stage whose own cohorts carry no label made `alreadyCut` false, so a
  legitimate partition target was refused as *not partitioned*.
  - An arrange labels only the rosters **it is actually placing**. The count and the balance come from
    the whole promotion; the write does not, because partitioning a roster this arrange never touches
    is `AssignRotationGroupsCommand`'s act, with its own guards and its own audit entry.
- ⚠ **`LevelId` is required on both the cut and the clear** — it is the guard, not a filter. Optional,
  a year-wide call cut *every* promotion of the year in one act (each with its own partition count,
  resolved by `BuildLabels` into a single one for all of them) and reached « Non réparti », the roster
  that belongs to no promotion. The type says so now, so the compiler refuses the year-wide call and
  `?levelId=` is a required query parameter.
- **A count is not read off a page** — `GetPromotionPartitioningQuery` (`GET groups/partitioning`).
  The Plan macro tab derived its partitions, their sizes and « N groupes sans partition » from
  `GET /groups` at `pageSize: 200`; a promotion adds ~100 rosters a year, so past 200 every number on
  that tab reads low — including the one whose whole job is to say a gap-fill is owed. Raising the
  page size moves the cliff. The aggregate is computed where the rows are.

## ✅ Composing a roster from a list, in one act (2026-09-07)

`AcademicGroups/BulkAssignment/` — `PreviewBulkRosterAssignmentQuery` /
`ApplyBulkRosterAssignmentCommand`, both running **one** `BulkRosterAssignmentPlanner`.
`POST groups/assign/bulk/preview` and `POST groups/assign/bulk`, journalised
`STUDENTS_ASSIGNED_TO_ROSTER`.

**Why.** A partner hospital takes the students who volunteered for it, and a nominative placement
request is answered by a **roster** ([`planning-rotation.md`](planning-rotation.md)). Composing that
roster was one dialog per student, so a list of a hundred was a hundred dialogs — which is how a real
list stops being used at all. Everything downstream already worked.

- ⚠ **Two verbs, decided per student, and frozen in the plan.** In no roster → *attached*
  (`StudentAffectationService` + `LateArrivalScheduler`, what « affecter à un groupe » does); already
  in a roster → *moved* (`StudentGroupRelocator`, « changement de groupe », no trace). Deciding at
  apply time by re-reading the row would let the answer change between what the operator confirmed
  and what runs.
- **The guards are the single acts' own, read the same way.** Engagement is
  `AffectationToll.IsUnderway` **narrowed to the source roster**, exactly as the relocator narrows it
  — a revalidation placed by hand into another cohorte is not the roster's doing and has no business
  refusing this. ⚠ But read **in one batch**: asked per student it is one round trip each, on the act
  whose whole reason for existing is that a hundred of anything is too many.
- **Nine row states**: `WillJoin`, `WillMove`, `AlreadyThere`, `Underway` (→ use a transfer, which
  carries the running rotation across and keeps the trace), `TargetMissingStage`, `WrongPromotion`,
  `CursusEnded`, `NotFound`, `WrongYear`. ⚠ **`AlreadyThere` is neither applicable nor a refusal**:
  re-sending a corrected list is the normal way the act is used, so most of a second run lands there,
  and counting it as a refusal would read as a run that failed.
- ⚠ **`WrongPromotion` is its own answer.** A roster is keyed (année, niveau, numéro), so a 4ᵉ année
  on a 5ᵉ année list is refused and named, never folded in — and it is distinct from `WrongYear`,
  because a student on the wrong promotion's list and one on the wrong year's are two different
  corrections.
- ⚠ **« Non réparti » is never a destination.** The bucket belongs to no promotion and carries no
  cohorte, so affectations would have nowhere to land while the file says the students are somewhere.
- **It moves students and places nobody.** Which service the roster goes to is the grid's answer — a
  pinned cell on a reserved service — with its own guards, its own audit entry and its own
  published-cells refusal.
- Refusals first, 200 rows at most, **every count measured before the cap**, `ConfirmedCount` at the
  apply. Who is named comes from the shared `StudentSelectionResolver`.

⚠ **`AcademicGroup.Purpose` is what makes the roster still legible next year.** « Volontaires Kénitra
(GST), formulaire du 12/09 ». Nothing else records why a roster exists: the only evidence that roster
102 was the military one is the pattern of its cells, and a re-découpage dissolves it without
anything saying what was lost.

⚠ **Number the volunteer rosters contiguously.** `GroupNumberRanges` folds *roster numbers* into
runs, so thirteen rosters numbered 48-60 print « 48-60 » and the same thirteen scattered through the
promotion print as a spray of singletons.

## ✅ Qui va dans quel groupe est **tiré au sort** (12/09/2026)

Demandé par la faculté : « quand on découpe les étudiants en groupes, je crois que vous les triez par
nom de famille ; nous voulons que ce soit aléatoire ». C'était exact, et ce n'était pas un choix.

`AutoArrangeGroupsCommandHandler` lisait ses candidats `OrderBy(r => r.Student.LastName)` et les
déposait dans cet ordre, donc les rosters se formaient par **tranches de l'alphabet** : une promotion
coupée en 100 groupes donnait Groupe 1 = les dix premiers noms de la liste, Groupe 2 = les dix suivants,
et ainsi de suite. ⚠ **Le rang alphabétique d'un étudiant décidait donc son année entière** — son roster
décide sa partition, sa partition décide ses créneaux, ses créneaux décident ses services et ses chefs.
Et les porteurs d'un même nom se suivaient par construction, donc ils partaient dans le même groupe.
(Propriété du code, lisible sans mesure : c'est le `OrderBy` suivi d'un `Skip`/`Take` par roster.)

**Le tirage vit dans `RosterDraw`** (`Application/AcademicGroups/Manage/`), à côté de `RosterCut` et
séparé de lui pour une raison :

- ⚠ **`RosterCut` dit la *forme* de la coupe, `RosterDraw` dit l'*ordre* dans lequel on y dépose les
  gens.** Le tirage ne change ni le nombre de rosters ni leur taille — ni, par conséquent, l'équilibre
  des colonnes que **②ter** ci-dessous a gagné. C'est vérifié sur le handler (`RosterDrawTests`), parce
  que c'est exactement le genre de propriété qu'un mélange emporte sans le dire.
- ⚠ **Un tirage est une permutation.** Perdre ou dupliquer une inscription se lirait comme une
  promotion d'une autre taille, sans que rien ne le signale : Fisher–Yates, la liste d'entrée n'est pas
  touchée, et le test le pose comme la propriété première.
- ⚠ **Reproductible, donc explicable.** Un acte qui mélange sans rien dire rend « pourquoi cet étudiant
  dans le groupe 41 ? » définitivement sans réponse. Le numéro du tirage est déposé au registre par le
  handler — `IAuditTrail.RecordOutcome(("drawSeed", …))`, **avant** son premier `SaveChanges`, donc
  avant que l'entrée ne soit validée — et les candidats sont lus dans un ordre **total** (nom, puis
  identifiant) pour que ce numéro désigne encore quelque chose. Ce n'est pas une promesse de rejouer la
  coupe : les candidats d'un jour ne sont pas ceux du lendemain.
- **Les signalements passent avant le tirage, pas après** : une inscription gelée reste écartée
  nommément, avec l'évidence sur laquelle le drapeau a été levé (`RosterCutByCountTests`).
- **Et c'est dit à l'écran** — « composition tirée au sort », sous *Groupes → Arrangement automatique*.
  Une propriété de l'acte que l'opérateur ne peut pas deviner et qu'il ne peut pas vérifier à l'œil sur
  100 rosters.

⚠ **Ce que cela ne touche pas** : `PartitionAllocator`, qui distribue les *rosters* entre partitions.
Là l'ordre est **choisi** (`Contiguous`, et la faculté ne s'en sert pas autrement) — les blocs de
numéros sont ce que la répartition imprime, et les rendre aléatoires rendrait le tableau illisible.
Le hasard porte sur la composition d'un groupe, jamais sur la place d'un groupe dans le tableau.

## ✅ Deux façons de nommer une découpe, et une seule forme correcte (11/09/2026)

`RosterCut` (`Application/AcademicGroups/Manage/`) — arithmétique pure, sans magasin ni horloge, donc
les cas tordus se parcourent exhaustivement au lieu de se discuter. Même forme et même raison que
`RotationTiling`. `PHASES.md` §27.2 ; l'item **0ba** (le canevas) reste ouvert.

**① La coupe se demande dans *une* unité, et le validateur refuse les deux et aucune.**
`AutoArrangeGroupsCommand` porte `GroupSize?` **ou** `GroupCount?` — « des groupes de 20 » et « la
5ᵉ MED en 100 groupes » sont deux façons de dire la même coupe, elles diffèrent par ce qui est tenu
fixe. ⚠ **Un défaut silencieux ici découperait une promotion que personne n'a dimensionnée**, donc la
règle est dite plutôt que défaultée — et elle vit dans le validateur, donc elle est couverte dans
`PGSH.Tests/Integration/` (`AutoArrangeUnitEndpointTests`) ou elle ne l'est pas.

**② « Également » veut dire plus grand reste, jamais `Skip`/`Take`.** L'ancienne boucle prenait
`GroupSize` à la fois, donc le dernier groupe portait le reste : **mesuré à l'écran le 10/09/2026**,
les 232 inscriptions de la 4ᵉ Pharmacie en taille 20 donnaient **onze groupes de 20 et un de 12**.
Ce groupe-là partait ensuite en rotation comme une cohorte entière — il occupe la place d'un service
pour 60 % d'un groupe, et la colonne imprimée ne s'équilibre pas.

- ⚠ **Le chemin « par taille » est corrigé lui aussi, et le nombre de groupes ne bouge pas** : 232 en
  taille 20 fait toujours ⌈232 ÷ 20⌉ = 12 rosters, mais 4 × 20 + 8 × 19 au lieu de 11 × 20 + 1 × 12.
  L'écran nomme ce champ « nombre **maximum** d'étudiants par groupe », et une coupe équilibrée
  respecte ce maximum tout aussi bien.
- ⚠ **Jamais de groupe vide.** Plus de groupes demandés que d'étudiants : un par étudiant. Un roster
  que personne n'habite n'est pas un roster plus petit — c'est une ligne que la répartition porte à
  travers tous les stages de l'année pour rien. Le handler **refuse** avant d'en arriver là, en
  nommant **les deux nombres** : un refus qui n'en nommerait qu'un renvoie l'opérateur deviner lequel
  il a mal lu.

**②ter ⚠ …et l'*ordre* des tailles décide de l'équilibre des colonnes — corrigé le 11/09/2026.**
L'équilibre gagné **dans** un roster était rendu **entre** les partitions, parce que deux actes
corrects se composaient mal.

- `RosterCut` rendait les grands rosters **en tête** (« les gros d'abord, pour que `Skip`/`Take`
  parcoure la promotion dans l'ordre des numéros ») — raison qui ne tenait pas : le handler avance
  avec un décalage courant, donc **toute** permutation place les mêmes étudiants.
- `PartitionAllocator.Contiguous` donne à la partition A le **premier bloc de numéros**, à B le
  suivant, etc. C'est la convention de la faculté — et la seule stratégie que le registre montre
  choisie, **8 fois sur 8**.
- Mis bout à bout, tous les rosters surdimensionnés atterrissaient dans les premières partitions.
  ⚠ **Mesuré sur la base vivante le 11/09/2026** : la 3ᵉ MED — 933 inscriptions, 100 rosters (33 de
  10 puis 67 de 9), 10 partitions — est sortie en
  **100, 100, 100, 93, 90, 90, 90, 90, 90, 90**. Écart de **10 étudiants** entre colonnes.

⚠ **Et une partition est une *colonne*, c'est-à-dire ce qu'un service tient à un instant.** C'est ce
qui a fait passer **Santé Publique** et **Simulation Médicale** d'une marge annoncée de **+6**
(100 places pour une colonne moyenne de 94) à exactement **0** sur trois colonnes sur dix : les deux
stages étaient assis sur leur plafond sans que rien à l'écran ne le dise. Une seule arrivée tardive,
ou un seul changement de groupe vers A, B ou C, les faisait dépasser.

- **Le correctif est un ordre, pas une taille.** `(i · larger) mod count < larger` vaut pour
  exactement `larger` des `count` positions, quel que soit leur PGCD, et les espace aussi
  régulièrement que les entiers le permettent. Mêmes tailles, même nombre, même convention de blocs
  contigus : 232 en taille 20 se lit désormais `20, 19, 19, 20, 19, 19…` et la même coupe de la
  3ᵉ MED donnerait **94, 94, 94, 93 ×7**.
- ⚠ **Rien n'est rejoué sur la 3ᵉ MED, qui est publiée.** Le correctif est de l'arithmétique : il ne
  vaut que pour les coupes **à venir**. Les colonnes de la 3ᵉ MED restent 100/100/100/93/90×6 jusqu'à
  ce que quelqu'un décide de la redécouper — ce que `AssignRotationGroupsCommandHandler` refuse tant
  qu'une cellule est publiée, et c'est bien ainsi.
- La morsure est vérifiée : ordre d'origine rétabli → **11 tests tombent** (`RosterCutTests`), dont le
  balayage de propriété sur les neuf promotions de 2026-2027 × sept comptes de partitions × quatre
  tailles de bloc.

**②bis ⚠ La coupe est *une* transaction — corrigé le 11/09/2026.** Les rosters d'un texte sont
enregistrés dès qu'ils sont créés (c'est leur clé générée par le magasin que les inscriptions
reçoivent ensuite), et les inscriptions ne sont écrites qu'au tout dernier `SaveChanges`. Entre les
deux, une requête annulée — l'onglet fermé, la connexion tombée : ASP.NET annule le jeton — laissait
la promotion porteuse de **rosters vides**, toutes ses inscriptions encore détachées.

- ⚠ **Et rejouer l'acte ne répare pas** : la numérotation reprend au plus haut `GroupNumber`
  existant, donc la seconde tentative construit un **second** jeu à côté des orphelins. Rien à
  l'écran ne distingue cet état d'une coupe voulue.
- L'enveloppe est `auditTrail.RunAtomicallyAsync`, la même que le plan macro et que les trois actes
  destructeurs. ⚠ **Elle passe par la piste et non par le contexte** parce que l'acte est audité :
  une nouvelle tentative vide le change tracker, et l'entrée du journal est à la piste de la remettre
  (voir [`docs/audit-calendar.md`](audit-calendar.md)). Ce que cet acte a fait est de toute façon
  déjà dans `AuditMetadata` — l'unité demandée et son chiffre — mais l'enveloppe est la même dans les
  deux cas. ⚠ Le fournisseur *in-memory* n'honore aucune transaction : cette suite prouve les étapes,
  jamais l'atomicité, qui se vérifie sur SQLite (`AtomicUnitOfWorkTests`).

**③ Les paniers CNPN cassent la division, et c'est le cœur.** Les groupes ne mélangent jamais deux
textes, donc chaque texte prend des rosters **entiers** : « N » s'apporte entre eux *avant* qu'on ne
coupe quoi que ce soit (`RosterCut.Apportion`, plus grand reste sur les parties fractionnaires).

- ⚠ **Un texte qui porte des étudiants reçoit toujours au moins un roster** — sinon ses inscrits n'ont
  aucun groupe, ce qu'aucun arrondi ne doit pouvoir produire — **et jamais plus de rosters qu'il n'a
  d'étudiants**. L'une ou l'autre borne écarte le total de ce qui a été demandé, et c'est pourquoi
  l'écran affiche la **forme obtenue** (« 4 × 20, 8 × 19 ») à côté du nombre : « 12 groupes » est vrai
  des deux répartitions, et seule l'une est celle qu'on a demandée.
- ⚠ **« 100 » veut dire 100 *de plus*** tant que la numérotation continue depuis le plus haut numéro
  existant (voir « Numbering restarts at 1 per promotion » ci-dessus). Non traité : la promotion est
  découpée sur une année vierge dans la campagne, et le dire à l'écran reste à faire.

**② Le canevas de découpage.** Un `.xlsx` d'une ligne par inscription d'une (année, niveau) avec une
colonne « Groupe » à remplir, puis l'import : les groupes manquants sont créés, chacun est rattaché là
où la feuille le dit. Les deux moitiés du patron existent déjà (`Get*TemplateQuery` pour la descente,
`ApplyReinscriptionSheetCommand` pour la remontée ; ClosedXML est référencé par `PGSH.Infrastructure`).

- ⚠ **Ce n'est pas l'acte de masse ci-dessus.** `ApplyBulkRosterAssignmentCommand` vise **un** roster
  avec une liste nommée ; la feuille les nomme **tous**. Ce qui se réutilise est son vocabulaire —
  `BulkRosterAssignmentRowStatus` — et ses **deux verbes décidés par étudiant**, qui restent la règle.
- ⚠ **Aperçu et `ConfirmedCount`, plus l'annulation à côté** : la feuille tombe sur des lignes que
  personne n'a tapées une par une, exactement comme la délocalisation de masse (item 0aw).
- ⚠ **Apparier sur `Appogee` *et* CNE**, le CNE étant facultatif.
- ⚠ **Un tableur peut faire naître des rosters.** C'est ce qui rend la fonction utile, et c'est aussi
  ce qui demande que « groupes à créer » se compte **à part** et voyage dans la confirmation.
- ⚠ **Deux textes CNPN dans un même groupe est un refus par ligne, nommé** — la feuille ne doit pas
  pouvoir contourner en silence ce que le découpage automatique s'interdit par construction.

## « Retrait » is a status wearing a level's clothes — `Level.IsPromotion`
The Access base used `CODE_N = 'MED00'` to mark a **withdrawal** rather than a year of study, and
`LegacyImport.LevelMapper` deliberately kept it as a `Level` with `Year = 0` so the registration — and
the rotations already served that year — survived the import instead of being dropped.

**The data is coherent and is not to be "repaired".** All 12 registrations read `Status = Withdrawn`,
the parcours run 1ère → 2ème → 3ème → **Retrait**, 8 of the 12 carry real périodes, and two of those
students came back (Retrait 2023-24 → 5ème année 2025-26). The real year they withdrew from is
**unrecoverable**: MED00 *replaced* it in the source.

- ⚠ **What it costs is that a marker is offered wherever a promotion is.** It has no stage, no cohorte
  and nobody to rotate, but it is a `Level`, so it appeared in every picker beside « Troisième Année ».
  One of its rosters ended up carrying partition **E** — not a deliberate cut but
  `SplitAcademicGroupsPerLevel` copying the folded roster's label onto each shard.
  `CnpnTargetPlanner` had already had to special-case year 0 by hand (« année ≤ 2 » must not sweep up
  the withdrawn); `Level.IsPromotion` exists so the third such exception is not written by hand too.
- **Refused:** `AssignRotationGroupsCommand` and `AutoArrangeGroupsCommand` (`Levels.NotAPromotion`).
- ⚠ **`ClearRotationGroupsCommand` is deliberately *not* refused.** A label already on a marker's
  roster is exactly what has to come off, and refusing the undo because the state should not exist
  leaves no way to reach it but SQL. Same shape as the bucket cohorts: **the guard is on creation**.
- **Reads split on intent, not on the row.** `GetLevelsQuery.PromotionsOnly` is *off* by default: the
  student dossier, the parcours and the level catalogue all have to name a withdrawn registration's
  level. It is the screens asking « which promotion am I planning? » that pass `true`
  (`getPromotionLevels` on the frontend). A browse filter over *existing* rosters keeps the full list.

## A partition's shape is a choice, and it shows up in the published table
`PartitionAllocator` cuts a promotion into rotation partitions (`AcademicGroup.RotationGroup`). Two
strategies, both producing equal-sized partitions, and the arranger cannot tell them apart:

| `PartitionStrategy` | 8 groups, 2 partitions | printed cell |
|---|---|---|
| `Interleaved` (default) | A = 1,3,5,7 · B = 2,4,6,8 | `1, 3, 5, 7, 9…` |
| `Contiguous` | A = 1-4 · B = 5-8 | `1-40` |

- ⚠ **A partition is a fact about a *cell*, never about a row of the répartition.** `RotationGroup`
  lives on `RepartitionCell`. It sat on `RepartitionRow` — meaning "the partition its first period
  belongs to" — which is not a property of the row at all: over the year the row visits every
  partition, because that is exactly what the crossover is. The failure mode is the dangerous kind,
  plausible and self-consistent: with two partitions every Médecine row opens on A and every
  Chirurgie row on B, so the document printed **one colour per stage** under a legend reading
  « Partition A / Partition B ». A cell whose cohorts disagree carries `null`, not the first label
  found.
- ⚠ **…and the published répartition does not print it at all.** A partition is scolarité's internal
  division for building the rotation; the reader of that page is a student looking for his own group,
  to whom "Partition G" explains nothing he can act on. The document colours by **stage** — which is
  what he navigates by, is true of a whole row, and is already written in the first column, so the
  five-tint palette may safely cycle. `RepartitionCell.rotationGroup` is still sent (it is a real
  fact, and it is what explains *why* a cell holds the numbers it does) and is still shown where it
  is actionable: `ScheduleGridModal`, `AssignmentsPage`.
  - The partition palette could **not** cycle, which is how the collision was found: it wrapped at 6
    while the 5th year has 9 partitions and the 6th has 10, so A and G printed identically under a
    legend giving each its own swatch.

- ⚠ **The stripe was never designed — it falls out of balancing.** "Fill the smallest partition,
  walking groups in number order" alternates on every group, so each partition steps by the partition
  count. The step is `partitionCount`, not always 2, and `RotationArranger` defaults it to
  **`partitionCount ?? services.Count`** when nobody says otherwise.
- **Contiguity in the cell comes from contiguity in the partition.** A cell's service is
  `serviceQueue[(ci + offset) % n]` where `ci` is the cohort's index *within the partition*, and the
  queue repeats each service in a run — so consecutive `ci` share a service. Interleaved partitions
  make those consecutive indices non-consecutive group numbers, and `GroupNumberRanges` correctly
  refuses to merge across a hole. Nothing is broken; there is simply nothing to collapse.
  - ⚠ **…and it is what the printed cell costs.** A stage with few services and a whole promotion to
    place puts every group of a partition in one cell: Santé Publique in the 5th year is *one*
    service taking 6-7 groups per période. Cut contiguously that prints « 21-27 » — five characters,
    and exactly what `MED05.png` prints. Cut interleaved it cannot collapse at all and prints
    « 3, 12, 21, 30, 39, 48, 57 » — twenty-five. Both are correct plans; only the second needs three
    lines of a column. Worth saying when an admin asks why the document got hard to read.
- ⚠ **`Contiguous` interacts with the CNPN split.** `AutoArrangeGroupsCommandHandler` buckets by
  (year, level, `CnpnVersionId`) and numbers sequentially per bucket, so each text's groups occupy a
  contiguous run — a contiguous partition can therefore land entirely inside one text where an
  interleaved one mixes them. Since `CohortProvisioner` skips stages a text does not require, that
  changes which rows exist in each partition's half of the matrix.
- **A gap-fill never re-cuts.** `AssignUnlabelled` only fills `null` labels, and `BuildLabels` lets the
  *existing* partition count win over the requested one — so a re-run cannot reshuffle a plan already
  built on the current partitioning.
  - ⚠ **It needs its own control, or the only reachable path is the destructive one.** The UI showed
    the assign form *only while no label existed*, so a promotion that later grew 20 unlabelled groups
    could be repaired only by « Redécouper » — a full re-cut — with « Supprimer les partitions » next
    to it. That is how a level got cleared by a stray click. A safe act hidden behind a destructive
    one is a defect in the same way an unguarded destructive act is. Changing the cut is `Reassign: true` (`ReassignAll`), which is
  **refused outright while any cell of the promotion is published**: students have been sent there.
  Merely-planned cells are counted (`PlannedCellsAffected`) so the caller knows an arrange is owed.
- ⚠ **A wrong count can only be undone by clearing** — `ClearRotationGroupsCommand`. That
  `BuildLabels` rule means a promotion mistakenly cut into two stays two-way for every later assign
  whatever count is asked for, so "unset the partitions" is a distinct act, not a flag on assign. It
  is refused while published (`CannotClearPublished`) for the same reason a re-cut is: the printed
  répartition names the partition students were sent as, and a label nobody holds cannot reproduce it.
- **Clearing destroys nothing else, and that is provable rather than hopeful.** Nothing points at a
  label — cohorts hang off groups, cells off cohorts and slots, periods off cells — so the command
  removes no row and breaks no FK. What it costs is that the planned cells no longer describe any
  partition, which is `PlannedCellsAffected` (an arrange is owed), not data loss.

## ⚠ An unplanned promotion is a state, not a defect — and a past year has no grid at all
Two facts about the live base that a sweep will otherwise re-report as findings every session.

**Planning is ordered, not inferred.** A promotion of the current year holding registrations but no
rosters, no cohortes and no cellules is **normal**: the faculty issues the order to plan it, and
until that order arrives there is nothing to plan. Stated by the user 2026-09-04, after a sweep
flagged MED1 (44), MED2 (1 027), MED6 (701), MED7 (1 347) and every Pharmacie level of 2026-2027.
The same goes for a stage carrying no `AllowedServices` on such a promotion — authoring that list is
part of the planning order, not a missing prerequisite. Report it as state; never "fix" it, because
the fix writes cohortes, affectations and cells for a whole year nobody asked for.

**Every year before 2026-2027 has périodes and no grid.** Measured 2026-09-04:

| | périodes | créneaux | cellules |
|---|---|---|---|
| 2017-18 → 2025-26 | **105 626** | **0** | **0** |
| 2026-2027 | 12 340, all grid-linked | 137 | 2 873 |

The Access import carried the rotations that were *served*; the source had no planning grid to carry.
So the périodes are right, nothing is dangling (0 périodes point at a deleted cell), and there is
nothing to repair.

- **What it cost was on screen, and it is closed** (2026-09-05): opening the planning grid on a past
  year showed an **empty table** while every dossier showed its périodes — the reported symptom « il y
  a des périodes mais elles n'apparaissent pas dans la grille ». `StageScheduleSummary` now carries
  `DeclaredSlotCount`, `ServedPeriodCount` and `EmptyGridNote` (`StageScheduleNotes`), and the grid
  prints the sentence. Same rule as `RepartitionSummary.DeclaredSlotCount` and `ExportNotes`.
  - ⚠ **It is *three* causes, not two.** « Cette année n'a jamais été planifiée ici » (no créneau,
    périodes served — imported history, and laying an axis over it reconstitutes nothing) · « aucun
    axe n'est posé » (no créneau, nothing served) · « l'axe est posé, personne n'y est réparti ».
    A fourth is separated inside the last: an axis over a promotion holding **no cohorte** is told to
    provision, not to arrange — the placements panel's lesson, where a promotion with no roster at
    all was told « rien n'est réparti », which points at the second gesture instead of the first.
  - ⚠ **`ServedPeriodCount` is null, never 0, when the question was not put** — every grid that has
    an axis. « Aucune période » is an answer and « on n'a pas regardé » is not, and each count is
    read only when it will be printed, so the ordinary request pays nothing for the note.
  - ⚠ **The note describes the stage and the year, never the filtered selection.** Under a partition
    filter an empty answer is the filter's doing; told « aucune cohorte » there, an admin goes and
    undoes a cut that is correct. Read from `PartitionSlotUseQuery`, which is already stage-wide.
- **And the grid marks publication per cell as well as per cohorte** — `SlotCellResponse.IsPublished`,
  read through **`PublishedCells`** and never through the FK, which names only a run's **first** cell.
  Measured on Gynécologie Obstétrique 2026-2027: **363 cells, 121 named by the FK, 363 covered** — a
  per-cell flag built on the FK would read 242 published cells as free. The row flag stays as it is
  and stays correct: one période with a non-null FK suffices to make « cette cohorte est publiée »
  true, which is a strictly weaker claim than the cell's.
  - The marker is a **marker**, not a new guard: editing is still disabled per *row*, because
    `SetCohortSlotAssignmentCommandHandler` refuses on « the cohorte holds a grid-linked période ».
    What the cell flag does add is the pre-flight refusal on **clearing** a published cell, which
    `ClearCohortSlotAssignmentCommandHandler` was already refusing server-side.

## Deux canevas, dans cet ordre (13/09/2026)

Le découpage vient **avant** les affectations, et le code le dit plutôt que de le supposer : une ligne
du canevas des affectations dont l'étudiant n'est dans aucun roster est refusée (`NoRoster`), parce
qu'il n'y a pas de cohorte où accrocher l'affectation.

- **« qui est dans quel groupe »** → le canevas de découpage (file d'attente, item 0ba) ;
- **« où va chacun et quand »** → le canevas des affectations,
  [`affectation-sheet.md`](affectation-sheet.md).

⚠ Le second **crée les cohortes** dont il a besoin — une par (roster, stage) — et les compte à part sur
l'aperçu, pour la même raison que le premier compte « groupes à créer » à part : un tableur qui fait
naître des lignes que personne n'a autorisées mérite sa propre ligne sur la confirmation. La cohorte
créée porte le libellé de son roster, exactement comme `CohortProvisioner` l'écrit — une cohorte née
d'un fichier ne doit pas être reconnaissable dans les listes.
