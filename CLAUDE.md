# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Project Is

**PGSH** — *Plateforme de Gestion des Stages Hospitaliers* — manages hospital internships for medical and pharmacy students (Medecine, Pharmacie, Master, Doctorat programs). It covers the full lifecycle: hospital structure, academic groups, student registrations, rotation planning, execution, attendance, and evaluation.

The documents that carry the rest are mapped just below.

## ▶ The map — read the document before you touch the area

`CLAUDE.md` holds what is true of **every** change: the architecture, the testing contract, and the
traps that bite any handler. Everything area-specific lives in `docs/`, in the same words it was
written in — the file is not a summary of the rule, it *is* the rule.

⚠ **These are not background reading.** Each one records defects that shipped, were measured on the
live base, and are invisible to the test suite. Open the document named for the area **before**
planning a change to it, not after a review finds the same defect again.

| Touching this | Read | Because |
|---|---|---|
| a requirement set, a CNPN stamp, `Curriculum`, the Stages catalogue figures | [`docs/cnpn.md`](docs/cnpn.md) | what a student owes is a fact about a **registration**, not about the student — and the read order is always `r.CnpnVersionId ?? r.Student.CnpnVersionId` |
| the rotation cycle, the macro plan, `RotationArranger`, `SchedulePublisher`, the planning grid, a stage's allowed services | [`docs/planning-rotation.md`](docs/planning-rotation.md) | the axis is `T = Σkₛ`, the balance is per **column**, an unscoped auto-arrange is a fill that silently decides the year — and the axis is laid on the **promotion's** calendar, exam weeks included |
| `AcademicGroup`, partition labels, pauses, unpublishing, clearing or deleting part of a plan, **how a promotion is cut** | [`docs/planning-rosters.md`](docs/planning-rosters.md) | a roster is keyed **(year, level, number)**, an affectation does not hang off the roster pointer — so « vider le groupe » leaves every one of them where it was — and **who lands in which roster is drawn, not sorted** |
| `Registration.Status`, a bulk canvas or roll, `RegistrationHold` | [`docs/year-closing.md`](docs/year-closing.md) | PGSH cannot know who passed — the faculty declares it, silence means opposite things on the two documents, and a refused row loses the faculty's statement |
| what a student owes, who may enter a final year, re-opening a failed stage | [`docs/progression.md`](docs/progression.md) | « entrer » means *begin*, not *be registered in* — reading it the other way refused a quarter of a promotion the faculty had named |
| creating, correcting or deleting an `AcademicYear`, or moving the current-year flag | [`docs/academic-year.md`](docs/academic-year.md) | `IX_AcademicYear_IsCurrent` is unique and filtered, so demote and promote are **two statements in that order** |
| capacity, admissibility, occupancy maths, who leads a service, the chef's worklist | [`docs/services.md`](docs/services.md) | quotas **replace** `Service.Capacity` rather than sitting under it, no rows means *open*, and a load over a window is the **peak inside it**, never the sum |
| a stage served outside the faculty, `Service.IsExternal`, the mass délocalisation, the dates it is recorded under, how many students *stand* in a service | [`docs/delocalization.md`](docs/delocalization.md) | a délocalisé stays in his cohorte and must stop occupying the service he left — counted per membership, sending sixty students away relieved the grid by **nothing** — and the window is the **cohorte's** passage, never the stage's whole axis |
| an export sheet, column, or a second export | [`docs/exports.md`](docs/exports.md) | an export is the one read deliberately exempt from pagination, and a column blank on every row reads as a column the export forgot |
| a bulk act on the live base, a transaction, rebuilding from `Medecine.mdb` | [`docs/operations.md`](docs/operations.md) | the base **is** the faculty's data; the rebuild is not « migrate then import », and it fails silently |
| **everything is lost and you are starting again from a backup** | [`docs/operations.md` §0](docs/operations.md) | the step-by-step, written after doing it for real on 17/09/2026 — **two resources are *supposed* to fail** on the first boot, `dotnet restore` is not a database restore, and a volume is not a backup |
| téléverser des affectations, le canevas des affectations, `DeclareRotation`, une période « hors grille » | [`docs/affectation-sheet.md`](docs/affectation-sheet.md) | c'est l'acte le plus destructeur de l'application — un seul refus refuse le fichier entier, **deux** nombres sont confirmés séparément (ce qui s'écrit et ce qui se détruit), et ce qu'il écrit **n'est pas dans la grille** |
| an audited act or act code, anything measured in worked days, a « suspension d'examens » | [`docs/audit-calendar.md`](docs/audit-calendar.md) | a refused act must write nothing, an empty holiday calendar quietly means "minus weekends", there are **two** calendars — the faculty's and each promotion's — so a reader that knows its (année, niveau) must ask for that one, and the calendar answers **two questions** (« compte dans la durée » / « peut borner une fenêtre »), never one |

### The other documents at the repo root

| File | What it is | When to open it |
|---|---|---|
| [`SCHEMA.md`](SCHEMA.md) | the database schema, delete behaviours, enum reference | before a migration, or whenever an `OnDelete` decides whether a guard is needed |
| [`PHASES.md`](PHASES.md) | the development roadmap, phase by phase | to find out whether a gap is deferred work or an oversight |
| [`PLANNING.md`](PLANNING.md) | the crossover arithmetic, and the end-to-end procedure for a répartition annuelle | when you need the *procedure* rather than the rule |
| [`NOTES.md`](NOTES.md) | accumulated domain knowledge and how each finding was measured | when you need the evidence behind a rule, or the story of a defect |
| [`HANDOFF.md`](HANDOFF.md) | the live queue, and the last few sessions | at the start of a session, to find what is actually waiting |
| [`SMOKE-TEST.md`](SMOKE-TEST.md) | the manual verification pass over the current sessions | before handing work back for the user to check |

Superseded history lives in [`HANDOFF-ARCHIVE.md`](HANDOFF-ARCHIVE.md) and
[`SMOKE-TEST-ARCHIVE.md`](SMOKE-TEST-ARCHIVE.md). Nothing there is current; it is kept because it
records *why* a decision was taken.

## ⚠ The rules that bite on any handler

The detail is in the documents above. These are the ones that have shipped a defect in more than one
area, so they are worth carrying in your head on **every** change:

- **An omitted academic year means the current one, never all of them.** Widening on absence is the
  defect, not the fallback. Resolve through `AcademicYearResolver`; a read that genuinely spans years
  says so some other way. → « Shared helpers » below
- **Filtering by level *and* year is one `Any`, never two.** 2 635 students here have repeated, so
  each predicate lands on a different registration: « 5ᵉ année, 2026-2027 » is 833 students asked as
  one `Any` and **2 127** asked as two. `RegistrationStatus` joins the same `Any`.
- **Paginate by what the response *contains*, not by the handler's return type.** A single-object
  response hides unbounded collections from any `List<T>` grep — that is what shipped 4 725 students
  in one object. To show a count, ask for `pageSize: 1` and read `TotalCount`. → « Shared helpers »
- **A collection subquery in a projection is the shape Npgsql refuses.** Reach for a flat, top-level
  query keyed on the parent id and fold in memory. Half of this is checkable without a database —
  add a case to `SqlTranslationTests`. → the Testing section below
- **`AsNoTracking()` on a reusable subquery reaches its host**, so a composed query mutates detached
  objects and `SaveChanges` writes nothing. A shared query states no tracking behaviour; each caller
  states its own.
- **An un-Included collection is indistinguishable from an empty one** — and the in-memory provider
  fixes navigations up from the change tracker, so this suite *cannot see* the mistake. Ask the store
  for the fact and let the aggregate decide what to do about it.
  - ⚠ **It shipped, and the loud half was not where the damage was done.** Measured 23/09/2026:
    neither chef handler Included `Service.ChefHistory`, which the aggregate **modifies**.
    `RemoveChef` therefore nulled the pointer, closed nothing, and **saved successfully** — leaving a
    service with no chef and an open tenure. The next assign added a second open row and hit
    `IX_ServiceChefAssignment_ServiceId` (`UNIQUE … WHERE "EndDate" IS NULL`) → **23505** → « Une
    erreur serveur est survenue », and Pédiatrie1/Pédiatrie2 could never be given a chef again.
    **The rule to carry: if the aggregate writes to a collection, the handler Includes it** —
    reading it is not the only reason.
  - ⚠ **And only the Postgres tier can test it.** In-memory rebuilds the navigation and passes on the
    broken code; SQLite has no *filtered* indexes so it refuses two tenures even correctly closed and
    fails on the right code. → `PGSH.Tests/Postgres/ServiceChefTenureTests.cs`
  - ⚠ **A repair that self-heals hides its own cause.** The first test walked assign → remove →
    assign and passed with `RemoveChef` still broken, because the *fixed* assign closes whatever open
    tenure it finds and repairs the mess on the way past. Each Include needs the assertion that looks
    at the state **immediately after its own act**, or one handler's correctness masks the other's.
- **Say what a blank means.** One number standing for two states is the recurring defect in this
  codebase: « aucune période » and « rien n'est encore réparti » call for opposite acts. A warning
  that fires whatever the data says is noise, and noise is dismissed — which puts the real one out of
  sight.
  - ⚠ **And « empty on every row » is not the same question as « empty on most rows ».** An export
    column blank everywhere announced itself; the same column blank on 86% of rows said nothing, and
    read as a broken export. That is `ExportNotes.ColumnFill` — asked for the one column whose blank
    has two opposite remedies, never as a blanket « partial column » note, because half a roll's
    columns are legitimately partial. → [`docs/exports.md`](docs/exports.md)
  - ⚠ **Et le défaut vit aussi dans le *schéma*, pas seulement dans un écran.** Un enum dont le zéro
    est une vraie valeur transforme « jamais renseigné » en une réponse : `BacSeries` n'ayant pas de
    membre « non renseigné », `LegacyImportPlanner` — qui n'écrivait pas ce champ du tout — a laissé
    **10 203** étudiants se lire « Bac Français », et `InscriptionPlanner` écrivait `SVT` pour chaque
    canevas à colonne vide : pas le zéro, une supposition **choisie**, trois lignes après le
    `Gender.None` du même fichier dont le commentaire dit « None is the honest answer; it is not a
    guess ». Un enum qui peut manquer porte le membre qui le dit, **ajouté en dernier** (la colonne est
    un `integer` sans conversion, donc réordonner reclasserait la base).
- **Do not copy onto a child a fact its parent already states.** `Hospital.City` exists and a service
  belongs to exactly one hospital, so « add a city to Service » is a driftable duplicate — the same
  objection that kept `AcademicYearId` off `Cohort`, and stronger here because there is not even a
  join to save: the reader already traverses `Service.Hospital` for the name. Filter through the
  navigation instead. → [`docs/services.md`](docs/services.md)
- **A name typed by a human is matched exactly and *suggested* loosely.** `NameSuggestions` names the
  two or three nearest catalogue entries in the refusal; it never resolves one. This catalogue holds
  « Médecine A » and « Médecine B », so a one-character tolerance in the *match* would send a cohorte
  to another hospital in silence. The tolerance belongs in the report.
  → [`docs/affectation-sheet.md`](docs/affectation-sheet.md)
- **A cell a human chose is not the arranger's to rewrite.** `CohortSlotAssignment.Source`
  (`Arranged` / `Pinned`) is read as a lock exactly like publication, and the count of what was left
  alone travels in the result (`PinnedCellsKept`). Before it, an auto-arrange destroyed every
  nominative placement in its reach reporting a perfectly normal `Assigned = N`. Any new act writing
  or deleting cells has to make the same distinction. → [`docs/planning-rotation.md`](docs/planning-rotation.md)
  - ⚠ **And the same rule one level up: `StageSlot.Source` (`Laid` / `MovedByHand`), added
    18/09/2026.** `CellSource` stopped the arranger rewriting a *cell* a human chose; nothing stopped
    an axis recompute rewriting the *dates* a human chose — the same fault reached from the other
    end, and a column is usually moved for a reason the grid does not know (a service shut that
    week, a jury rescheduled). `StartDate`/`EndDate` are now `private set`: a column moves through
    **`MoveTo`**, which marks it in the same gesture, or **`RelayTo`**, which is the axis writing and
    refuses a hand-moved column outright. Two statements — write the dates, then set the flag — is
    one occasion to write the first without the second, and the missing half is silent until the
    recompute that overwrites it. The change named its own offenders: the handler plus two fixtures.
  - ⚠ **A hand-moved column *anchors*; it is not merely skipped.** That is where it differs from a
    pinned cell: an axis is **ordered**, so leaving a cell alone disturbs no neighbour while leaving
    a column alone constrains its own. A recompute resumes the cascade *after* it, and an overlap
    that results is a named refusal rather than a silently broken order.
    → `PGSH.Tests/Domain/SlotSourceTests.cs`
- **A service can be held out of the rotation** — `StageAllowedService.PlacementMode = Reserved`.
  ⚠ Its capacity leaves `TotalCapacity` with it, so whatever withholds places must **say how many**.
  And note what does *not* enforce this: `RotationArranger` computes `saturatedServices` **after**
  `SaveChangesAsync`, as a report — the placement weights by capacity and never reads live occupancy,
  so a service cannot be reserved by filling it first. → [`docs/services.md`](docs/services.md)
- **Une fenêtre se *déclare*, une date se *déplace* — jamais le même acte.** Déclarer (« la 3ᵉ MED
  compose du 10/03 au 30/04 ») est une décision ; déplacer les colonnes que cela coupe en est la
  conséquence. Confondre les deux est ce qui a coûté la pause par étape, **retirée le 18/09/2026**
  plutôt que réparée : elle écrivait des dates au moment de la pause, et les sept défauts en
  découlaient tous — allongement en **jours calendaires**, grille laissée derrière (donc
  `ServiceOccupancyCalculator` lisant l'ancienne fenêtre et le dossier la nouvelle), `UtcNow` aux
  deux bouts (donc impossible à programmer), aucune garde `Movable` (donc par-dessus des présences
  pointées), aucun événement, rien au registre, et **accumulation au rejeu**. La forme qui survit est
  `PromotionPause` (aucune date écrite, révocable, corrigeable) + `InternshipAssignment.Reschedule`
  (dates **absolues**, donc rejouable sans dériver). ⚠ **Le critère est la rejouabilité** : un acte
  qui ajoute à ce qui est stocké ne peut être ni corrigé ni annulé, un acte qui dérive d'une fenêtre
  déclarée le peut toujours. → [`docs/planning-rosters.md`](docs/planning-rosters.md), `PHASES.md` §17.2
  - ⚠ **Un fait déclaré qui n'agit sur rien doit se *voir*, ou il passe pour n'avoir pas pris.**
    Déclarer une fenêtre n'écrit aucune date — c'est la propriété qui la rend révocable — donc le seul
    signe qu'elle existe est ce que les écrans en disent. Le 18/09/2026 une fenêtre posée sur une
    promotion entièrement publiée coupait **10 colonnes et 1 535 rotations** et l'opérateur a rapporté
    « aucun impact » : le chiffre n'existait que dans l'**aperçu**, derrière un bouton à presser
    *avant* d'enregistrer, et vidé à la frappe suivante. La ligne de la liste portait ce que la fenêtre
    *coûte* (« 29 ouvrables perdus ») et rien de ce qu'elle *coupe*. ⚠ **Coût et portée sont deux
    faits** : le premier est de l'arithmétique de calendrier, le second dit qu'un plan déjà posé est
    entaillé.
  - ⚠ **Et « rien ne traverse » a deux lectures opposées, dont une n'est pas une bonne nouvelle.**
    Soit la promotion a déclaré ses semaines d'examens et la sélection les évite, soit **personne n'a
    rien déclaré** — et sur cette base c'est l'état ordinaire (2026-2027 n'a porté qu'une seule fenêtre
    déclarée). Un compte qui ne sépare pas les deux fait lire l'ignorance comme un feu vert, alors la
    réponse transporte aussi le nombre de fenêtres déclarées (`WindowsDeclaredForPromotion`). ⚠ Le
    piège se referme à l'intérieur même du correctif : tirer la promotion de la sélection seule fait
    répondre « aucune fenêtre » dès que la sélection est vide, donc la promotion du stage entre dans
    l'union. → `GetStagePauseCrossingsQuery`, `PauseVisibilityTests`
  - ⚠ **Et un fait déclaré qui n'écrit rien se *dérive* à la lecture — il ne se rattrape pas par un
    drapeau.** C'est la moitié qui manquait au retrait de la pause par étape : une fenêtre déclarée ne
    touchant aucune rotation, **472 rotations de la 4ᵉ MED se lisaient « En cours »** un matin
    d'examens (mesuré 18/09/2026), c'est-à-dire « cet étudiant est dans son service », pendant que la
    faculté avait écrit le contraire. La réponse est `PromotionSuspensionLookup` : une requête par
    page, indexée par (année, niveau), pliée en mémoire, et le motif **remplace** le statut à l'écran
    plutôt que de s'y ajouter — « En cours » est vrai du cycle de vie et faux de l'endroit où
    l'étudiant est ce matin. ⚠ **La propriété qui justifie la forme est la révocation** : révoquer la
    fenêtre éteint l'état pour toute la promotion à la lecture suivante, sans un écrit et sans rien à
    défaire, là où « reprendre » devait repasser sur chaque période et pouvait être oublié. ⚠ Et la
    date vient de `IDateTimeProvider`, jamais d'un `DateTime.UtcNow` au fond de la classe — c'est ce
    qui rendait l'ancienne pause impossible à programmer *et* impossible à tester.
    ⚠ **Et « ouverte » n'est pas « en cours aujourd'hui ».** `InternshipAssignment.Start()` est un
    *whole-student start* : il pose `IsStarted` sur **toutes** les périodes d'un coup, donc un séjour
    de mai le porte dès septembre. Une suspension posée sur le seul critère du cycle de vie marque donc
    « En examens » un stage qui ne commence pas avant deux mois — il faut **en plus** que la fenêtre du
    séjour contienne le jour. La ligne d'*affectation* garde le critère large (l'étudiant compose,
    quelles que soient ses dates) ; les trois lectures au niveau *période* portent les deux conditions.
    → `PromotionSuspensionDisplayTests`
  - ⚠ **Un état dérivé doit atteindre *tous* les portails, ou il devient une règle à deux réponses
    selon qui regarde.** Livré d'abord côté administration seulement, il laissait le chef — qui décide
    de pointer une absence — et l'étudiant lui-même sans rien. Les chemins sont distincts et aucun
    n'est optionnel : `GetInternshipAssignmentsQuery` (admin), `GetServicePeriodsQuery`,
    `GetMyServicePeriodsQuery` (chef, arrivées de transfert comprises) et
    `GetInternshipAssignmentByIdQuery` — **ce dernier est celui que lit le portail étudiant**, et il
    n'a pas de type partagé avec les autres côté client : trois fichiers TypeScript déclarent leur
    propre ligne, donc trois à modifier.
  - ⚠ **Et un acte retiré se dit, avec ce qu'il emporte.** « Suspendre la rotation d'une seule
    cohorte » n'est plus possible et rien ne le remplace — c'est un retrait, pas une substitution.
    L'argument qui l'avait sauvé deux fois (« un service qui ferme une semaine, c'est vraiment par
    stage ») était juste comme énoncé de domaine et comparait un acte idéal à celui du dépôt, lequel
    n'avait **jamais servi** : 0 période suspendue en base le jour du retrait.
- **A closure is two facts about a day, not one.** `WorkingDayCalendar` answers
  `CountsTowardDuration` (is somebody expected in a service) and `CanBoundAWindow` (may a période begin
  or end here) as **separate** methods; there is no `IsWorkingDay` any more, and reaching for one
  predicate is how a férié became impossible to *cross* — marking it worked would also have let a stage
  end on it. The two are nested: every bounding day counts, not every counted day bounds. The flag is
  `ICalendarClosure.CountsAsWorkingDay`, on the **interface** — `PromotionPause` answers `false` without
  a column, because an exam week can never be worked. → [`docs/audit-calendar.md`](docs/audit-calendar.md)
- **A date derived from a stage is not a date about a cohorte.** An axis holds one column per
  partition, so `min`/`max` over *all* a stage's créneaux is as many times too long as there are
  partitions — the délocalisation window wrote **14/09/2026 → 25/03/2027** into twelve dossiers for a
  stage those students serve in one month, and would have overlapped every other stage of their year
  the moment it was published. Anything derived per (stage, année) and then applied to a group has to
  be re-asked at the level it is actually true at, and a bulk act resolves it **after** it knows who
  it is acting on, never once before the loop. → [`docs/delocalization.md`](docs/delocalization.md)
- **Un acte en masse réversible enregistre ce qu'il a *détruit*, pas ce qu'il a écrit.** Ce qu'il a
  écrit est encore là ; ce qu'il a remplacé n'existe plus nulle part la seconde d'après. C'est ce qui
  sépare une vraie annulation d'un « on retire nos lignes » qui laisse l'étudiant plus mal qu'avant
  l'acte. → `AffectationImport` / `ReplacedPeriod`, [`docs/affectation-sheet.md`](docs/affectation-sheet.md)
  - ⚠ **Et l'annulation n'est totale que parce que l'acte refuse de détruire ce qu'il ne saurait
    remettre.** Deux choses ici : une **note** et une **journée de présence**. `AttendanceRecord`
    cascade depuis `ServicePeriod`, donc tout acte qui supprime une période supprime les présences avec
    elle, **en silence** — une note s'annonce sur tous les écrans, une présence est invisible jusqu'au
    jour où on en a besoin. Relâcher l'un des deux refus rend le registre menteur, et une annulation qui
    remet moins qu'elle n'a enlevé est pire que pas d'annulation, parce que quelqu'un s'y fie.
  - ⚠ **« Est-ce encore ce que j'ai écrit ? » se décide sur un chiffre enregistré, jamais recompté.**
    Compter aujourd'hui les périodes d'une affectation et les comparer à ce qu'on trouve revient à les
    comparer à elles-mêmes : la garde passe alors sur une affectation que quelqu'un a replanifiée, et
    l'annulation écrase un état plus récent. Le nombre écrit par l'acte est stocké avec lui.
  - ⚠ **Un acte annulé se garde, il ne s'efface pas.** « Cet étudiant a-t-il été planifié puis
    dé-planifié ? » est une question du dossier, et une ligne qui disparaît en se défaisant répond
    « il ne s'est rien passé ».
- **Confirm what cannot be undone.** A bulk act that writes onto rows nobody named carries the count
  the operator was shown and refuses on a mismatch — a boolean cannot do it, because a row created
  between the preview and the apply is the whole risk.
- **An audited act has to reach a `SaveChanges`, and its entry has to say *how much*.**
  `AuditLogPipelineBehavior` stages the row **before** the handler and the handler's `SaveChanges`
  commits it — which is what makes a refused act write nothing, and what makes an act that never
  saves write nothing *either*. ⚠ So a handler writing only through `ExecuteDelete`/`ExecuteUpdate`
  ends with an explicit `SaveChangesAsync`. And the code alone is not the record: « Réinitialiser les
  cohortes » on a virgin promotion and on a published one are the same code and unrelated events, so
  the handler deposits what it actually destroyed through **`IAuditTrail.RecordOutcome`**, before it
  saves. → `docs/audit-calendar.md`
  - ⚠ **And the `SaveChanges` is *unconditional*, which is the half the first sweep missed.** A save
    guarded by `if (count > 0)` writes no journal entry on exactly the run where the act had no effect —
    and that run is **the ordinary one**: re-cutting a promotion already cut, re-seeding a calendar
    already seeded, re-cloning a text already cloned, setting a placement mode to the one it holds.
    Six such exits were found on 14/09/2026 (`AssignRotationGroups` ×2, `ClearRotationGroups` ×2,
    `SeedNationalHolidays`, `CloneCnpnCurricula`, plus `SetAllowedServicePlacementMode`), all in acts
    whose refusals were tested and whose no-ops were not. **« Personne n'a joué cet acte » and
    « quelqu'un l'a joué sans effet » are the two states the register exists to separate**, so the
    zero path records its zeros and saves — the shape `DeleteAllGroupsCommandHandler` established.
    Pinned by `PGSH.Tests/Integration/NoEffectAuditEndpointTests.cs`, whose witness is that a *refused*
    act still writes nothing.
- **A validator describes what a *save* must satisfy, not what a good record looks like.** It is
  applied to rows that already exist; a rule the imported data fails makes those rows read-only.
  → its own section below
- **A delete asks the schema first, or the constraint answers for it.** A `RESTRICT` FK reached
  without a guard surfaces as `DbUpdateException` → a **500** whose only content is the name of a
  PostgreSQL constraint; a `CASCADE` one takes its children **silently**. `SCHEMA.md` says which is
  which, and the count has to be asked *before* the delete — afterwards there is nothing left to
  count. Count every reason **together** rather than short-circuiting on the first: a user who clears
  one and is then told about the next has been sent round the loop twice, and the second trip looks
  like the first fix having failed. `DeleteAcademicYearCommand` is the shape; `DeleteStageCommand` and
  `DeleteServiceCommand` were the two that had no guard at all. ⚠ And a `CASCADE` is not automatically
  the right bargain: it is, when the children are meaningless without the parent (a créneau of a
  deleted stage), and it is **not** when the join row hangs off a *second* aggregate that survives
  amputated — `StageAllowedService` carries a `Rank` and a `PlacementMode`, so deleting a service
  refuses on the stages authorising it rather than punching a hole in an order
  `ServiceRotationOrder` holds contiguous from 1. → [`docs/services.md`](docs/services.md)
- **`ErrorType.Problem` means a *fault*, and nothing else may use it.** `CustomResults` maps it to
  **500**, and the client discards `detail` above 500 (`errorMiddleware` shows the fixed « Une erreur
  serveur est survenue »), so a business refusal typed `Problem` loses the one sentence that explains
  it. Thirteen did. The worst was `AcademicYearResolver`'s `NoCurrentAcademicYear` — the fallback of
  *every* handler that omits a year, i.e. a base with no current year made **every screen** 500 with
  nothing on any of them saying to pick a year. A refusal is `Conflict` (the request meets the state),
  `Validation` (the request is malformed), `NotFound` or `Forbidden`; `Problem` is for an unreachable
  archive or a `pg_dump` out of disk. → `PGSH.Tests/Integration/ErrorStatusMappingEndpointTests.cs`
  - ⚠ **…with exactly one *kind* of exception above 500, and it is deliberate: `503`.** A dependency
    being down is not a fault of the request, and `errorMiddleware` shows a 503's `detail` rather than
    the fixed sentence — a 500 may carry anything internal, a 503 is written to be read.
    - **Two things claim it, and the bar is stated rather than stretched**: the request wrote
      nothing, the fault is in a **dependency** rather than in the request, and the remedy is an
      operator action on that dependency. `DatabaseOutage` is the first.
      `IncompleteIdentityTokenException` is the second — a token the identity provider issued with no
      subject (measured 17/09/2026: a rebuilt Keycloak client missing the `basic` scope, so every
      token was valid, signed and named nobody). ⚠ **401 is the tempting status there and it is
      wrong**: the client turns a 401 into `keycloak.logout()`, so a realm issuing subject-less tokens
      puts the user in a **login loop** with nothing naming the cause.
  - ⚠ **`DomainException.Detail` is opt-in, and was `null` for everybody until 17/09/2026** — so no
    domain exception's sentence ever reached a screen, only its `Title`. A subclass with something
    worth showing overrides it; `Message` is not used automatically, because a message written for a
    log is not always the sentence written for a reader.
- **A database that cannot be reached must not look like a bug** — `DatabaseOutage` +
  `GlobalExceptionHandler` (`PGSH.API/Infrastructure/`). Every non-`DomainException` used to land in
  `_ => 500, "Server failure"`, so on 13/09/2026 — WSL upgraded itself, the Docker distro stopped,
  PostgreSQL with it — **every screen** answered 500 with a stack trace from `SyncUserMiddleware`,
  and an infrastructure outage read exactly like a defect. It is now **503** plus a sentence saying
  the request wrote nothing and that the thing to act on is the database server.
  - ⚠ **The classification is deliberately narrow, because the opposite mistake hides real bugs.**
    Only a `DbException` in the chain may declare an outage, and only when it is `IsTransient` or
    carries a socket/IO/timeout failure under it. A `PostgresException` the server actually answered
    with — a violated constraint, a missing column — stays a **500**: filed as « service
    indisponible » it would become an operations incident nobody ever fixes. A bare `IOException`
    (an export that cannot write its file) is not an outage either. → `PGSH.Tests/Api/DatabaseOutageTests.cs`
  - **A probe and the work it guards do not share a timeout.** `BackupOptions.TimeoutSeconds` (600)
    is a `pg_dump`; `ProbeTimeoutSeconds` (10) is `docker version`, which answers in a second or
    never. Sharing one number left the backup screen waiting ten minutes on a dying engine instead
    of saying so, and `ProcessRunner.Execution.TimedOut` is what lets « it refused » and « it never
    answered » be two different sentences. → `PGSH.Tests/Api/BackupProbeTimeoutTests.cs`
- **A multi-step write is one transaction, or it is a half-written state somebody will read as
  deliberate.** ASP.NET cancels the token whenever the tab closes or the connection drops, so "the
  request stopped between two statements" is the ordinary case, not the exotic one. The roster cut
  committed its rosters and their members separately — a promotion left carrying **empty rosters**,
  and re-running builds a *second* set beside them because the numbering continues. « Supprimer les
  groupes » fires **six** `ExecuteDelete` in a row, and stopping after the third left the rosters,
  the cohortes and the whole grid standing with **nobody in them**.
  - ⚠ **An audited act wraps through `IAuditTrail.RunAtomicallyAsync`, never through
    `IApplicationDbContext.ExecuteAtomicallyAsync` directly.** A retry clears the change tracker, so
    the journal entry the pipeline staged before the handler has to be put back — and only the trail
    knows which version of it is current, because `RecordOutcome` **replaces** the pending entry
    rather than mutating it. The context re-staged a photograph taken on the way in, which is how the
    two mechanisms used to be incompatible; it now puts nothing back on its own. The context helper
    remains correct for an act that writes no journal entry. → `IAuditTrail`, `AtomicUnitOfWorkTests`
- **Une période hors grille n'est pas une période absente.** `ServiceOccupancyCalculator` lit les
  **cellules** (`CohortSlotAssignments`), pas les périodes, si bien qu'une rotation écrite autrement que
  par une publication — une délocalisation, un stage importé, le canevas des affectations — est visible
  dans le dossier, sur la page du service et dans l'export, et **invisible dans la grille et dans la
  charge que la grille affiche**. Ce n'est pas un défaut : c'est la grille qui répond à « qu'a-t-on
  planifié », pas à « qui est là ». Ce qui serait un défaut est de ne pas le **dire** — tout acte qui
  écrit des périodes sans cellules porte la phrase dans son rapport. ⚠ Et « dépublier » ne les reprend
  pas : `RemovePublishedPeriods` est l'inverse de publier et ne touche que ce que publier a fait.
  → [`docs/affectation-sheet.md`](docs/affectation-sheet.md)
- **Confirmer un acte destructeur, c'est confirmer ce qu'il *détruit*, séparément de ce qu'il écrit.**
  Les deux nombres bougent pour des raisons différentes — une période évaluée entre l'aperçu et
  l'application change ce qui est détruit sans rien changer à ce qui est écrit — et c'est la destruction
  qui est définitive. `ApplyAffectationSheetCommand` porte `ConfirmedCount` **et**
  `ConfirmedDroppedPeriods` ; un seul nombre aurait laissé passer exactement le cas qui compte.
- **The base is live.** Take a `pg_dump -Fc` before every bulk act, and never write to the base to
  verify something — `scripts/pgsh-snapshot.ps1` does it in one line, and `pgsh-restore.ps1` puts it
  back **and recounts the manifest's census** rather than trusting an exit code.
  → [`docs/operations.md`](docs/operations.md)
  - ⚠ **A volume is not a backup, and having backups prevents nothing on its own** — nothing applies
    them at startup. On 17/09/2026 a Docker factory reset took the `.vhdx` and every volume with it;
    `%LOCALAPPDATA%\PGSH\backups` survived *because it is outside the volume*, and the Keycloak realm
    did not survive at all, because it lived only in its volume.
  - ⚠ **An empty base cannot be rebuilt by running the migrations.** Three are **data** migrations
    that read the catalogue the legacy import writes, and they `RAISE EXCEPTION` on an empty base by
    design — « Aucun niveau « 3ᵉ année Médecine » … ». So the answer to a lost volume is **restore**,
    never « migrate then import ». It is also why the Testcontainers tier uses `EnsureCreated`.
  - ⚠ **The Keycloak realm is a versioned file** — `keycloak/pgsh-realm.json`, imported by
    `.WithRealmImport`. A realm written down needs no backup; it rebuilds. **Adding a user there
    without a matching `User` row gives a 403 « Profile Not Found » that names nothing actionable**,
    so its accounts and `Seeder.SeedStaticUsersOnlyAsync` are one list.
    → [`keycloak/README.md`](keycloak/README.md)

## Build & Run Commands

```bash
# Build the entire solution
dotnet build PGSH.sln

# Run the full stack (API + PostgreSQL + Keycloak + Redis + frontend via Aspire)
dotnet run --project PGSH.AppHost

# Frontend only
cd PGSH.Frontend
npm install
npm run dev       # Vite dev server, port 5173
npm run build
npm run lint

# Add a new EF Core migration (run from repo root)
dotnet ef migrations add MigrationName --project PGSH.Infrastructure --startup-project PGSH.API

# Apply migrations manually (MigrationService also runs them on Aspire startup)
dotnet ef database update --project PGSH.Infrastructure --startup-project PGSH.API
```

## Solution Structure

```
PGSH.sln
├── PGSH.AppHost/          # .NET Aspire orchestration (PostgreSQL, Keycloak, Redis, API, frontend)
├── PGSH.MigrationService/ # EF Core migration worker + Bogus data seeder; runs at Aspire startup
├── PGSH.ServiceDefaults/  # Shared Aspire config: telemetry, resilience, health checks
├── PGSH.API/              # ASP.NET Core 9 minimal API (Endpoints/, Extensions/, Middleware/)
├── PGSH.Application/      # CQRS commands & queries via MediatR
├── PGSH.Domain/           # Domain entities, value objects, enums
├── PGSH.Infrastructure/   # EF Core DbContext, Keycloak auth, authorization, migrations
├── PGSH.SharedKernel/     # Base types: Entity, Result<T>, Error, DomainEvent
└── PGSH.Frontend/         # React 19 + TypeScript + Vite + Mantine UI + Redux + Keycloak
```

## Architecture

**Clean Architecture** — Domain → SharedKernel ← Application ← Infrastructure ← API.

### CQRS / MediatR
All business logic lives in `PGSH.Application/` as commands (`*Command`) and queries (`*Query`). API endpoints dispatch them through MediatR `ISender`. Pipeline behaviors in registration order: `RequestLoggingPipelineBehavior` → `ValidationPipelineBehavior` (FluentValidation, runs validators in parallel).

### Minimal Endpoints
Every endpoint implements `IEndpoint` (defined in `PGSH.API/Endpoints/IEndpoint.cs`). `EndpointExtensions` auto-discovers all implementations via reflection. To add an endpoint, create a class implementing `IEndpoint` in `PGSH.API/Endpoints/<domain>/`.

### Result Pattern
All handlers return `Result<T>`. Endpoints map failures to HTTP problem responses via `CustomResults.Problem(result)`. Never throw exceptions for expected business failures — use `Result.Failure(Error.NotFound(...))` etc.

⚠ **The error's `ErrorType` is what picks the status code**, in `CustomResults.GetStatusCode`:
`Validation` → 400, `NotFound` → 404, `Conflict` → 409, `Forbidden` → 403, and `Failure`/`Problem`
→ 500. `Failure` additionally *masks* its own message (« An unexpected error occurred »). So typing a
business refusal `Problem` is how a carefully written sentence becomes « Une erreur serveur est
survenue » on screen — see the rule above.

⚠ **`Result<T>` cannot carry a null success value.** Its implicit operator is
`value is not null ? Success(value) : Failure(Error.NullValue)`, so returning `null` from a method
declared `Result<T?>` silently produces a *failure*, not an empty success. Never model an optional
outcome that way — resolve the value only when it is actually wanted, and keep `Result<T>` non-nullable.

### Domain Events
Entities inheriting `Entity` raise events via `entity.Raise(new SomeEvent(...))`. `ApplicationDbContext.SaveChangesAsync` publishes them **after** the transaction commits (eventual consistency). Event handlers live in `PGSH.Application/<domain>/`.

⚠ **Un agrégat qui fait confiance à son appelant n'a pas d'invariant, il a une convention.** Déplacer
une rotation publiée (phase 17.1) écrivait les dates sur des `ServicePeriod` chargées à plat sur le
contexte : la règle « une rotation commencée, notée ou pointée ne se déplace pas » reposait
entièrement sur `PublishedPeriodShifter.PlanAsync`, et l'acte ne levait **aucun** événement alors
qu'il réécrit des milliers de fenêtres d'un coup. C'est `InternshipAssignment.Reschedule` qui porte
les deux, et l'événement transporte **les deux fenêtres** — sans l'ancienne, personne ne peut dire de
combien la rotation a bougé ni dans quel sens.
- ⚠ **Un événement par changement *réel*, jamais par ligne touchée.** Sous
  `StageRotationMode.SingleService` une période couvre une suite de colonnes : déplacer celle du
  *milieu* ne bouge pas le séjour, et lever un événement par période couverte ferait lire « des
  milliers de rotations déplacées » là où aucune ne l'est. C'est la même distinction que
  `PeriodsShifted` / `PeriodsCovered` porte dans le résultat.

### Database
- **PostgreSQL** via EF Core 9 + Npgsql. Column and table names are **PascalCase** (snake_case naming is not enabled).
- `ApplicationDbContext` is in `PGSH.Infrastructure/Database/`.
- Entity configurations (`IEntityTypeConfiguration<T>`) are organized by domain folder: `PGSH.Infrastructure/Users/`, `PGSH.Infrastructure/Hospitals/`, `PGSH.Infrastructure/Stages/`, `PGSH.Infrastructure/Registrations/`.
- The Aspire connection name is `"TodoDatabase"` (legacy name from project scaffolding — unrelated to functionality). Standalone dev reads from `appsettings.Development.json`.

### Authentication & Authorization
- **Keycloak** (realm `pgsh`, port 8082) issues JWT tokens validated by `Aspire.Keycloak.Authentication`.
- `KeycloakRoleTransformer` maps `realm_access.roles` from the JWT to standard `ClaimTypes.Role`.
- `SyncUserMiddleware` calls `UserContext.SyncAsync` on every authenticated request — links Keycloak `sub` to the local `User` record by `IdentityProviderId`, falls back to email matching. The local `User` record must exist before a login works.
- `HasPermission` attribute on endpoints uses `PermissionAuthorizationHandler`. **Current state:** role-based check only — granular per-user permissions via `PermissionProvider` are a Phase 8 stub.

### Enum Serialization
`JsonStringEnumConverter` is registered globally in `AddPresentation()`. All enums serialize/deserialize as strings in JSON (e.g., `"Pending"` not `0`).

### API Documentation
Scalar UI at `/scalar/v1`, Swagger UI at `/swagger`. Both are configured with Keycloak OAuth2 PKCE for authenticated requests in development.

## Testing — mandatory, not optional

`PGSH.Tests` (xUnit + FluentAssertions + NSubstitute + EF InMemory) is part of the definition of done.

- **Every new feature and every bug fix ships with tests, in the same change.** Treat "implement X" as
  implicitly including "and cover it". Run them green before handing back.
- Cover the happy path **plus each guard**: every `Result.Failure` a handler can return is a test case.
- Shared seeding lives in `PGSH.Tests/TestHarness.cs` (`SeedCatalog`, `SeedService`, `SeedChef`, `SeedCohort`,
  `SeedRegistration`, `SeedAssignment`, `SeedPeriod`, `SeedSlot`, `SeedSlotAssignment`, `SeedObjective`) —
  extend it rather than re-rolling in-memory boilerplate per file.
- **Drive the real lifecycle in setup.** Seeding a period as pre-closed leaves the assignment `Planned`, so it
  never reaches `Evaluated`; go through `Start()` → `CompletePeriod()` → `SubmitEvaluation()`.
- **Never encode a known bug as expected behaviour.** If a test would cement an unresolved asymmetry, leave the
  case uncovered with a comment saying why, and raise it.
- ⚠ **Known blind spot:** `UseInMemoryDatabase` ignores FK constraints, unique indexes, `OnDelete` behaviour and
  SQL translatability — constraint and query-translation defects remain invisible in the bulk of the
  suite; do not read a green suite as proof that a query runs on PostgreSQL.
  - ✅ **Testcontainers exists since 13/09/2026** — `PGSH.Tests/Postgres/`, one `postgres:17-alpine`
    per run. It is the only tier that can answer « what does the *server* do »: filtered unique
    indexes, `RESTRICT`/`CASCADE`, and the rows a query actually returns. Write a case here whenever
    the answer depends on the schema rather than on the code.
    - **`[PostgresFact]` / `[PostgresTheory]`**, never a bare `[Fact]`: they **skip with a sentence**
      when Docker is absent, so a machine without it gets skipped tests rather than green ones —
      one result standing for two states is the defect this repo names everywhere else.
    - ⚠ **The schema comes from `EnsureCreated`, not from the migration chain**, because three CNPN
      *data* migrations `RAISE EXCEPTION` on an empty base by design (`docs/operations.md` §1). So it
      proves the schema the **model** describes, and still proves nothing about migration drift.
    - ⚠ **Ask the server from a context that has not seen the answer.** EF resolves a `RESTRICT`
      itself when the dependents are tracked — severing the association client-side before any SQL is
      sent — so seeding and deleting through one context tests the change tracker. Seed through one,
      then `database.Connect()` a second to perform the act. That mistake made the first
      delete-behaviour test pass for the wrong reason.
    - Each test gets its own database, cloned from a template with `CREATE DATABASE … TEMPLATE …`
      (milliseconds), so unlike `ApiFactory` nothing leaks between tests.
  - ⚠ **And it *refuses* `ExecuteDelete` / `ExecuteUpdate` outright** — « not supported by the current
    database provider ». A handler that writes only through them therefore has a **success path no
    test in this repository can reach by any route**: the handler tests can assert its refusals and
    nothing else, and an endpoint test 500s before it touches the act. Measured 2026-09-10, and it is
    how `DeleteAllGroupsCommand` and `EmptyAllYearGroupsCommand` carried `IAuditableCommand` from
    Phase 20 onward while **writing no journal entry at all** — every write went through
    `ExecuteDelete`, so nothing ever called `SaveChanges` to commit the pending row.
  - **`TestHarness.NewSqliteContext(connection)` is the way in** (`OpenSqlite()` holds the
    connection open — an SQLite in-memory database dies with it). Relational, so it runs both, and it
    enforces foreign keys and unique indexes as a bonus — that is what exposed every fixture building
    a `Hospital` with `CenterId = 0`. ⚠ **It is not PostgreSQL**: no filtered indexes, no
    `NULLS NOT DISTINCT`, different type affinities. It answers « does this act run and write », never
    anything about the real schema. `ExecuteDeleteAuditTests` is the pattern.
  - ⚠ **It bit for real on 2026-08-26.** `CohortProvisioner` projected
    `g.Registrations.Select(r => r.CnpnVersionId ?? r.Student.CnpnVersionId).Distinct().ToList()`
    *inside* a `Select(g => new { … })`. The subquery's element is a computed value carrying no key,
    so Npgsql cannot correlate it — « Unable to translate a collection subquery in a projection… » —
    and the macro plan died on the first real request with the whole suite green. Reach for a **flat,
    top-level query** keyed on the parent id and fold in memory.
  - **Half of that hole closes without a database** — `SqlTranslationTests` +
    `TestHarness.NewNpgsqlContext()`. Translation happens when a query is *compiled*, before any
    connection opens, so a context on the Npgsql provider pointing at nothing answers
    "does this become SQL?" via `ToQueryString()`. It proves nothing about the *rows*; it does stop a
    500. Add a case whenever a query takes a shape a provider might refuse (collection subquery in a
    projection, `Distinct`/`GroupBy` over a computed element, a client-side call in a predicate).
    - **The whole macro-plan path is swept** (2026-08-26): `CohortProvisioner` →
      `StudentAffectationService` → `RotationArranger` (+ `GroupScheduleConflictGuard`,
      `ServiceOccupancyCalculator`) → `SchedulePublisher`. All twelve compile; the sweep found no
      second defect. ⚠ **`SchedulePublisher` had never executed against PostgreSQL at all** — the
      Med6 rehearsal ran `publish: false` and the base held 0 grid-linked périodes, so the first real
      publication would have been its first run. ✅ **It has now run, on 13/09/2026**: the faculty
      planned and published the 3ᵉ MED of 2026-2027 — 933 students, 100 rosters in 10 partitions,
      8 stages, 80 créneaux, 1 000 cellules, **7 464 périodes liées à la grille**. The sentence above
      is kept because it is why that class is swept; it is no longer a description of the base. Every query on that class is named, the per-cohort
      publish included: it shares nothing with the stage-wide one but the class, so sweeping only the
      path the macro plan takes would have left the human's own button uncovered.
    - **The CNPN area is swept too** (2026-09-01): the stamper's four reads, the effectivity
      planner's scope and detail queries, the targeting selector, and the two read screens'
      correlated `Count` projections. It had **no case at all** before, on the strength of its
      queries looking flat — which is what was believed about `CohortProvisioner`. It is also the
      least forgiving place for the mistake: the stamper runs inside the réinscription, which
      creates a whole promotion's registrations in one act.
    - **A query is testable here only if it is *named*.** Each one is an
      `internal static IQueryable<T> …Query(IApplicationDbContext, …)` beside its caller, which the
      handler then executes — the shape `CohortProvisioner.GroupTextsQuery` established. A query
      buried in a private async method cannot be compiled without running it.
    - ⚠ **A projection is not a predicate.** A client-side call in the final `Select` does **not**
      fail here — EF evaluates the top-level projection on the client by design and `ToQueryString()`
      returns SQL for it. The same call in a `Where` throws. This file catches what the provider
      *refuses*; a projection that quietly client-evaluates is a performance question and belongs to
      the half that needs a real database.

### `PGSH.Tests/Integration/` — the half of an endpoint that is not the handler
`ApiFactory` (`WebApplicationFactory<Program>`) hosts the **real** `Program.cs` in-process, so a test
reaches a route the way a browser does: routing, the required-ness of a query parameter, model
binding, authentication, `SyncUserMiddleware`, the exception handler and the `Result.Failure` →
problem-details mapping. A handler test sees none of that.

- ⚠ **A guard ordered *after* the write returns the same `Result.Failure` and passes the handler
  test.** Only the store tells the two apart, so a refusal test asserts the refusal **and** that
  nothing was written. That is the case this suite exists for.
- ⚠ **Write the control too.** A route that 400s on everything — a typo in the path, a binding failure
  — satisfies every refusal assertion and proves nothing. Pair each refusal with the request that must
  still succeed.
- Authentication is header-driven (`TestAuthHandler`: `X-Test-User`, `X-Test-Roles`), and **sending no
  header leaves the request anonymous** — a handler that always authenticates cannot tell "allowed"
  from "not checked". Roles are emitted as Keycloak's `realm_access` JSON so `KeycloakRoleTransformer`
  is exercised rather than bypassed.
- `ResetAsync()` per test, via `IAsyncLifetime`. The host and its store are shared across a class, and
  a test that writes leaves its rows behind: rows one test wrote made three unrelated tests fail when
  a guard was removed to check the suite bites, which hides which assertion actually broke.
- ⚠ **The tests now depend on `PGSH.API`, so `dotnet test` fails with MSB3021/MSB3027 while the Aspire
  stack is running** — the API holds its own `bin`. Build somewhere else instead:
  `dotnet test PGSH.Tests/PGSH.Tests.csproj -p:BaseOutputPath=<tmp>/`. Do **not** also set
  `BaseIntermediateOutputPath` — one shared `obj` across projects gives MSB4006 (circular dependency).
- **Prove a new guard test bites**: break the guard, confirm the test fails, restore it. A pipeline
  test has many ways to pass for the wrong reason.
  - ⚠ **Restore from a scratch copy, never with `git checkout <file>`.** Whole sessions of work live
    in this repo's working tree — 223 files were uncommitted on 18/09/2026 — so `git checkout` is not
    an undo, it is a delete, and it takes every *other* unstaged change in that file with it. It cost
    ~130 lines of session 73 that day (`InternshipAssignment.Reschedule`, plus the removal of
    `PausePeriod`/`ResumePeriod`), recovered only because the method had been read verbatim earlier
    and a pre-incident build artifact survived in another project's `bin` to check the result against.
    `cp <file> <scratchpad>/` before breaking it, `cp` back after.

## Application Layer Conventions

⚠ **`AsNoTracking()` on a reusable subquery reaches its host.** Tracking is a property of the whole
compiled query, so composing a marked selector into another query makes *that* query no-tracking
too. `CnpnTargetPlanner.MatchedStudentIdsQuery` is composed into the read that loads the students
the apply then mutates: marked, the apply stamped detached objects and `SaveChanges` wrote nothing —
preview right, apply reporting success, not one student moved. **A shared query states no tracking
behaviour; each caller states its own.**

### Shared helpers — always use these, never inline
- **Pagination** — `QueryableExtensions.ToPaginatedResponseAsync(pageNumber, pageSize, selector, ct)` in `Application/Extensions/`. Apply after filtering and `OrderBy`. Never manually write `CountAsync + Skip + Take + ToListAsync + new PaginatedResponse`.
  - ⚠ **One ceiling, and it *clamps* — `QueryableExtensions.MaxPageSize` (200).** A larger page is
    served short and the response still carries the true `TotalCount`, so nothing is hidden. Every
    query validator states it through `PaginationRules.IsAPageSize()` / `.IsAPageNumber()`, never by
    hand. Four validators had spelled their own **stricter** 100 and *refused*, so a request the
    pipeline would have served never reached it: `GET /stages?levelId=3&pageSize=200` — the CNPN
    editor's « which stages may this text require » read — 400'd on every open, the stage list came
    back **empty**, and the picker rendered disabled saying « Tous les stages du niveau sont listés ».
    Two stages the faculty had just created could therefore not be required by any text, and the
    refusal read on screen as a broken control rather than as a rule. The client names the same
    number once (`common/constants/pagination.ts`). Pinned by
    `PGSH.Tests/Integration/PaginationBoundsEndpointTests.cs`.
  - ⚠ **Paginate by what the response *contains*, not by the handler's return type.** A single-object
    response hides unbounded collections from any `List<T>` grep: `GetGroupByIdQuery` returned one
    `GroupDetailResponse` carrying 4,725 students — one per registration in the "Non réparti" group —
    each with two correlated sub-queries, and that is what crashed the browser.
  - ⚠ **Anything scoped per academic year must filter on it server-side.** Cohorts exist per
    (stage, group) and groups per year, so an unscoped stage query returns every year it ever ran
    (681 rows for "Chirurgie"). Never fetch all years and filter in the client.
  - ⚠ **To show a count, ask for `pageSize: 1` and read `TotalCount`** — never fetch the rows.
- **Academic year** — `AcademicYearResolver` in `Application/AcademicYears/`. Any handler whose result
  depends on the year resolves it through this, and takes `int? AcademicYearId` on its command/query.
  - ⚠ **An omitted year means "the current one", never "all of them".** Widening on absence is the
    defect, not the fallback: it is what made the évaluation-import canvas list 3,553 students across
    six promotions where 688 were wanted, and what let *publish / auto-arrange / start / close / pause
    / resume* reach into past years whenever they were scoped by partition label instead of by
    explicit cohort ids. A read that genuinely spans years says so some other way.
  - ⚠ **Year-stamped tables:** `AcademicGroup`, `Curriculum` and `StageSlot`. A `StageSlot` is keyed
    `(StageId, AcademicYearId, PeriodNumber)` — the same P1 exists once per promotion with its own
    dates — and `SlotOverlapGuard`'s no-two-periods-at-once rule is level-**and-year**-wide, since two
    promotions never share a student.
  - ⚠ **Do not denormalise `AcademicYearId` onto `Cohort`** to shorten
    `a.Cohort.AcademicGroup.AcademicYearId`. Measured 2026-08-08 on the worst stage (CHIRURGIE, 563
    cohorts over 6 years): the whole two-hop join is **49 of 910 shared buffers — ~5%**; the other 861
    are the nested loop into `InternshipAssignments`, which denormalising `Cohort` does not touch. The
    join was never the cost — the *missing predicate* was (3,553 rows fetched where 688 were wanted).
    Drift is not the objection (a composite FK to `AcademicGroup(Id, AcademicYearId)` would make the
    copy non-driftable); the objection is that it optimises the cheap half.
- **Localization mapping** — `LocalizationMapper.FromCoordinates(x, y, z)` in `Application/Hospitals/`. Use for any Center, Hospital, or Service handler that maps GPS coordinates.
- **Who leads a service** — `ServiceChefDirectory` / `ServiceChefProvider` in
  `Application/Hospitals/Chefs/`. Tenure open on the date → sitting chef → legacy note, asked
  **as of a date** the caller names. Never re-derive it: the répartition and the stage export both
  read this one, and `FromSourceNote` must travel with the name.
- **Per-period mark / verdict** — `StageScoring.PeriodMark` / `IsPeriodValidated` in `Domain/Stages/`. The single source of truth shared by the domain roll-up and every read handler (student record, fiche). Never recompute a mark inline.
- **Where a rotation stands** — `ServicePeriodLifecycle` / `ServicePeriodState` in `Domain/Stages/`
  (`Planned` → `Underway` → `AwaitingEvaluation` → `Settled`). Same rule as `StageScoring`, for the
  same reason: `IsStarted && !IsComplete && !IsInterrupted` had been written out in four separate
  files and its not-started twin in two, i.e. six chances to disagree about what « en cours » means
  with nothing to catch it. Never restate the triple — `Where(ServicePeriodLifecycle.Underway)` in a
  query, `IsUnderway(p)` in memory, `StateOf(...)` in a projection.
  - ⚠ **The expressions are the authority and the delegates are compiled from them.** Two hand-written
    copies, one for EF and one for memory, is the drift the class removes. EF needs an `Expression`
    (a method call in a `Where` is refused by the provider), so that is what is written.
  - ⚠ **`Settled` is defined as the *complement* of the other three, not as "closed and marked".**
    That is what makes the four a partition: a row complete-but-never-started is a state the
    lifecycle cannot produce and the store can hold, and under a positive definition it would belong
    to no state — invisible in every list and counted in none. `ServicePeriodLifecycleTests` walks
    all 16 flag combinations.
  - ⚠ **`AwaitingEvaluation` / `Settled` read the `Evaluation` navigation**; in memory it must be
    loaded. `Planned` / `Underway` touch flags only and are always safe.
  - ⚠ **« Peut-on encore la déplacer ? » is a *fifth* rule in the same class — `Movable` — and it is
    deliberately not derived from the four states.** It reads `IsStarted`, `IsComplete`, `Evaluation`
    **and `Attendance`**, and that last one bears on no state at all: a rotation that is `Planned` and
    carries a journée de présence is a row the lifecycle cannot produce and the store can hold, and
    moving it would leave those days on dates nobody served. It was written out in
    `InternshipAssignment.Reschedule` and again in `PublishedPeriodShifter.PlanAsync`, and the pause
    report wanted it a third time — **a guard that refuses and a report that promises must read one
    rule**, or a screen offers a move the aggregate then declines.
    - **A store-side caller composes it rather than restating it** — `ExpressionComposition.Through`
      substitutes the path (`coverage => coverage.ServicePeriod`) for the predicate's parameter, and
      `.Not()` inverts the tree. `Invoke` is what EF refuses; pinned by `SqlTranslationTests`.
  - ⚠ **And « déplacer le début » and « repousser la fin » are *two* questions — `Movable` and
    `Extendable`, split 18/09/2026.** Conflating them is what made the case that matters
    unrepairable: a window declared mid-year lands on rotations that have **started**, which
    `Movable` refuses — rightly, something happened on that start date — so the only rule available
    answered « rien n'est rattrapable » for exactly the promotions a late window hurts. Pushing the
    *end* rewrites nothing that took place, so a started rotation extends.
    - ⚠ **Attendance blocks a move and does not block an extension**, and that asymmetry is the
      whole of the split: a pointed day lives between the start and the old end, and a window that
      only grows still contains every one of them. The guard that is therefore *missing* from
      `Extendable` — pulling the end back — lives in the **name of the act**:
      `InternshipAssignment.ExtendTo` only ever pushes. Refusing by a state a caller could
      mis-read is how it would have come back.
    - ⚠ **The two are nested — `Movable` ⊂ `Extendable` — exactly as `CountsTowardDuration` and
      `CanBoundAWindow` are, and for the same reason.** Two independent predicates end up
      disagreeing about one row and a repair act picks the wrong one. Making it a *theorem* rather
      than a coincidence is why `IsInterrupted` was added to `Movable`: a rotation cut short by a
      transfer has a window recording what was actually served, so **both** its ends are facts. In
      practice an interruption implies a start, so nothing the lifecycle produces changes — what it
      closes is a row the store can hold. Pinned over all 32 flag combinations by
      `ServicePeriodLifecycleTests`.
    - ⚠ **And a third rule, dated: `MovableOn(DateOnly)`.** `Movable` reads `IsStarted`, but
      `InternshipAssignment.Start()` is a **whole-student start** — it flags every période at once,
      so a February séjour carries it from September. Measured on the live base 19/09/2026:
      **305 périodes of the 4ᵉ MED are `IsStarted` with a window entirely in the future**, so an axis
      recompute reading `Movable` would refuse to push exactly the future rotations it exists to
      push. `MovableOn` replaces the flag with « its window has not begun », keeping the recorded-fact
      vetoes (a mark, a présence) whatever the date. ⚠ **It is not a loosening and the two are *not*
      nested**: a never-opened période whose window is *past* is `Movable` and not `MovableOn`. Two
      questions, not two strengths of one. The date comes from `IDateTimeProvider`, never a `UtcNow`
      inside the class. `MovableOn ⊂ Extendable` does hold.
    - ⚠ **And it is the rule the *aggregate* enforces, since 19/09/2026 —
      `InternshipAssignment.Reschedule(id, start, end, on)` takes the date.** Leaving the aggregate on
      the date-free rule while a planner classified on the dated one is the two-rules defect this
      file names elsewhere, and it surfaced immediately: the recompute promised a move the aggregate
      then refused. `PublishedPeriodShifter.PlanAsync`/`ApplyAsync` take the date for the same
      reason. ⚠ It also **fixed the manual column move**, which had the same latent defect — moving a
      future column was refused for every whole-student-started rotation.
    - ⚠ **A fixture that seeds fixed dates now rots.** A column whose window has passed is correctly
      immovable, so an integration test written with March dates passes until March and then refuses
      for a reason unrelated to what it checks. Seed relative to now
      (`PublishedColumnMoveEndpointTests.Anchor`), or freeze the clock
      (`TestHarness.ClockOn` / `BeforeAnyWindow`).
    - ⚠ **And `ShortenTo` is its counterpart, added 20/09/2026.** ⚠ **It is not an undo, and calling
      it one misleads**: there is no history, no stored previous state, nothing to replay. The axis
      is a *calculation* — anchor date + column length + the promotion's calendar — and the act
      **overwrites** whatever is stored with what that calculation now yields. Delete a declared
      window and the same calculation simply produces earlier dates. What was missing was only the
      ability to *write* in that direction: `ExtendTo` pushes an end later and never earlier, so
      nothing could put a shortened column down. ⚠ **The two directions do not share a guard, which
      is why they are two acts**: lengthening can orphan nothing (a pointed day lives between the
      start and the *old* end, so a growing window still holds it), shortening can — the days
      between the new end and the old one would be présences on dates the rotation no longer
      covers. `ShortenTo` counts them and names the date, because « impossible » without the number
      indicates no gesture; a rotation pointed only in its first week shortens to that week fine.
      Detection had the same one-way hole: `FirstDivergentColumn` now looks for a column that is too
      **long** as well as one too short, or a deleted window leaves the axis stretched with the act
      answering « rien à rattraper ».
    - ⚠ **`ExtendTo` writes an *absolute* date, never a delta** — the property that lets an axis
      recompute be replayed after every pause declared, corrected or revoked without the rotation
      growing a little each time. `ExtendBy(days)` would have been the accumulation that got the
      stage-scoped pause retired rather than repaired. It reuses
      `ServicePeriodRescheduledDomainEvent` with an unchanged start: an extension *is* a window
      change, and two event types for one fact would make every future consumer subscribe twice.
      → `PGSH.Tests/Domain/PeriodExtensionTests.cs`
  - **The state is sent to the client, never re-derived there.** The chef page had the same four-way
    split written again in TypeScript — one rule, two sides of a network boundary, nothing able to
    catch them disagreeing.
- **Execution scoping** — `ExecutionAuthorizer` in `Application/Employees/MyServices/`. Every handler acting on a period/evaluation/attendance goes through it: `EnsureCanActOnPeriodAsync`, `EnsureCanActOnEvaluationAsync`, `EnsureCanRecordAttendanceAsync` (write), `EnsureCanReadAttendanceAsync` (read — wider, includes the owning student).
- **Period overlap** — `SlotOverlapGuard` in `Application/Stages/Slots/`. Any handler creating or moving a `StageSlot` must call it; the rule is level-wide, not per-stage.
- ⚠ **A service's load over a window is the *peak inside it*, never the sum of what touches it.**
  `ServiceOccupancyLookup.LoadOn` summed every cell overlapping the asked-for window, so two cells
  that each touch the window *without touching each other* were added. Measured 2026-09-03: the
  planning grid showed **118** on Pédiatrie2 where the service never held more than **62** — the two
  4ᵉ année Pédiatrie columns are consecutive (one ends 06/10, the next starts 07/10). The grid, the
  arranger's balance and **the pre-publish guard** all read this class, so publication was refused on
  loads that never occur; the per-service page and the charge report were right throughout because
  they go through `OccupancyTimeline`. `LoadOn` now sweeps the same way. It cannot miss a real
  breach — a real one is an instant where the sum crosses the ceiling, and that instant is one of the
  evaluated candidates.
- **Where a recompute lands a promotion on *another* one** — `AxisRelayCrossingReader`. Pushing a
  promotion's columns does not change *which* service a cohorte occupies, only *when*, so it lands
  where another promotion is already standing — and no existing read looks at that crossing: a
  service's page shows the load as it is, the grid shows one plan at a time.
  ⚠ **A report, never a guard** (12/09 rule): it refuses nothing, and it names *which* promotions
  share the peak, which is the part nothing else can answer. ⚠ It does **not** re-derive the
  arithmetic — `OccupancyTimeline` already cuts the year at window boundaries and yields one exact
  simultaneous load per segment, so the reader just feeds it the *proposed* dates instead of the
  stored ones. Summing windows that touch the asked-for range instead of taking the **peak inside
  it** is the 03/09 defect that showed 118 on a service that never held more than 62.
  → [`docs/services.md`](docs/services.md)
- **Service capacity** — two numbers, never one. `ServiceOccupancyCalculator` says how many students are
  *there*; `ServiceIntakeCalculator` says how many are *allowed*. Every capacity decision compares the two.
  - ⚠ **…and the comparison is *shown*, never enforced.** Settled 12/09/2026: ~10 000 students over 148
    services all carrying the import's default 20, so over-capacity is how this faculty runs. Capacity
    features are **reads** — no new refusal, no new guard, no screen that demands a correction before
    it will act. The one exception stays as built: `Service.AllowsOverCapacity = false`, a service's
    own statement that its number is firm (`true` on every row today).
    → [`docs/services.md`](docs/services.md)
  - …and a third question they cannot answer: **how many will be there once a promotion is planned**.
    `PromotionAxis` (`Domain/Stages/`) is that arithmetic and it is pure — `kₛ = durée_s / pgcd`,
    `T = Σkₛ`, and the slice standing in a stage at one instant is `⌈N·kₛ/T⌉`. Never restate it: the
    fit panel and any future « can this promotion be planned » read share the one class, and the
    round is **up** because the remainder is a real student. Note the partition count cancels out —
    cutting a promotion into more groups cannot relieve an overloaded stage.
    → [`docs/services.md`](docs/services.md)
- **Who goes in which roster is *drawn*, not sorted** — `RosterDraw` (`Application/AcademicGroups/Manage/`).
  The cut read its candidates by family name and dealt them in that order, so rosters formed in slices of
  the alphabet: siblings and namesakes landed together, and a student's alphabetical rank decided his
  périodes, his services and his chefs for the whole year. Nobody chose that; it was the query's read order
  become a répartition rule. ⚠ **The draw moves *who*, never *how many*** — sizes and counts stay
  `RosterCut`'s, which is pure and stays pure — and the draw number travels to the register through
  `IAuditTrail.RecordOutcome(("drawSeed", …))`, because « pourquoi cet étudiant dans ce groupe ? » has no
  answer otherwise. The candidates are read in a **total** order (name, then id) so that number still
  designates something.
- **Naming a group of students is `StudentSelectionResolver`** (`Application/Students/Selection/`),
  never a per-act parse. Roster ids ∪ registration ids ∪ a pasted list of CNE/Apogée lines, with a
  row for every line that names nobody and `NotFound` kept distinct from `WrongYear`. Shared by the
  mass délocalisation and the nominative roster assignment; the FIFO choice will be the third.

### The year is constitutive, not an attribute — know which side a table is on
- **Year-constituted** — `AcademicGroup`, `Cohort`, `Registration`, `Curriculum`, `StageSlot`. Remove
  the year and the row is meaningless. `AcademicGroup.AcademicYearId` being non-nullable is the schema
  already saying so.
- **Year-invariant catalog** — `Stage`, `Level`, `Service`, `Hospital`, `Center`, and the
  `Student`/`Employee` identities. "Chirurgie" and "Service de Cardiologie" outlive every promotion.

Every bug in this class lives exactly on that boundary: a year-invariant key (`stageId`, `serviceId`)
used to reach year-constituted rows. When you write `.Where(x => x.StageId == id)` against cohorts,
assignments or slots, the year predicate is not optional.

⚠ **« L'année en cours » is a singleton the database enforces** — `IX_AcademicYear_IsCurrent`, unique
with filter `"IsCurrent"`. `AcademicYearResolver` takes the *first* row flagged current and every
handler that omits a year gets it, so two rows flagged at once means two screens quietly disagreeing
about which promotion they show, with nothing on either to say so. `CreateAcademicYear` demotes the
others, but that is one write path guarding an invariant of the table.

A global EF query filter was considered and rejected: of ~101 handlers touching year-constituted
tables, ~15 are *deliberately* cross-year — student parcours, level dossier, curriculum comparison,
revalidation's cross-level retake — and those are the load-bearing reads, not edge cases.
`IgnoreQueryFilters()` is also all-or-nothing, so the escape hatch would disable unrelated filters.

⚠ **An invariant the caller must remember is not an invariant — close the type.** `StageSlot` is keyed
`(StageId, AcademicYearId, PeriodNumber)`: the rule was written here, enforced by a unique index in
PostgreSQL, and **guaranteed nowhere in code**. Three places built one with an object initializer, two
stamped the year, one forgot — writing a créneau of year **0**. An object initializer cannot demand a
field; a constructor can. `StageSlot.For(...)` is now the only way to make one, the three keys are
`private set`, and the change *named its own offenders*: the compiler listed all three handlers plus a
test fixture that was seeding a slot with no year — an object no real path could produce.
- ✅ **The other two planning roots are closed too, since 14/09/2026.** `Cohort.For(...)` — identity
  `(StageId, AcademicGroupId)`, five construction sites — and `AcademicGroup`, identity
  `(AcademicYearId, LevelId, GroupNumber)`, three. Both named their own offenders exactly as
  `StageSlot` did: an `AbolishedStageRevalidationTests` fixture omitted its roster's level, so it was
  seeding **« Non réparti » without meaning to** and building a retake cohorte on it — the state
  `CreateCohortCommandHandler` refuses; and `RosterTeardownGuardTests` seeded « le panier » as a
  *promotion* roster numbered 0, which is neither thing.
- ⚠ **Where a legitimate shape is the absence of a key, it gets its own door — not a nullable
  parameter.** A roster with no promotion is « Non réparti », the year's holding pen for every
  promotion's unassigned registrations (4 725 of them in 2025-2026), so `AcademicGroup` has **two**
  factories: `ForPromotion(yearId, levelId, number, label)` and `AsUnassignedBucket(yearId, label)`.
  Under one factory with a nullable `levelId`, *forgetting* the promotion and *meaning* the bucket
  are the same call — and that is the shape the 4 725-student incident came from. The bucket factory
  also takes no `rotationGroup` at all: a partition label on the bucket is what pulls the whole thing
  into `CohortProvisioner`, and here there is no parameter to pass it through.
- ⚠ **Closing the type does not mean the schema agrees.** `StageSlot`'s identity is held by a unique
  index; `Cohort`'s `(StageId, AcademicGroupId)` is held by **nothing** — `CreateCohortCommandHandler`
  and `CohortProvisioner` each look for the duplicate themselves. A constructor sees one object, never
  the table, so say in the XML doc which half the factory actually closes.
- ⚠ **A graph built before the store numbers anything needs the same demand, by navigation.**
  `LegacyImportPlanner` creates the année, the roster and the cohorte in one pass, so an id-keyed
  factory would be satisfied there by two zeros — precisely the row being refused. Both classes carry
  a navigation overload for that path rather than letting it go round.
- ⚠ **The dates stay open, the identity does not.** Moving a column is a legitimate edit of
  `StartDate`/`EndDate`; changing a créneau's year is not a correction, it is a different créneau.
  Same for a roster's label, zone, partition and purpose — what `UpdateGroupCommand` edits — against
  its (année, niveau, numéro), and for a cohorte's label against its two keys.
- **Fixtures go through the factories too**, via `TestHarness.NewSlot` / `NewGroup` / `NewCohort`
  (the id is the store's, so the helper sets it after). ⚠ `SeedUnassignedBucket` is deliberately a
  *separate* helper for the same reason the factory is: a fixture reaching for `SeedGroup` and
  leaving the level out would be seeding the bucket by accident. A fixture that violates an identity
  throws on the spot — it would be posing a row no real path can produce, and the test built on it
  proves nothing.
- → `PGSH.Tests/Application/PlanningIdentityTests.cs`, which covers what the compiler cannot: a key
  that is present but meaningless (`0`, negative), which an `int` still lets you write — and the
  witness that the two roster shapes are not the same object.

⚠ **Filtering by level *and* year is one `Any`, never two.** Both predicates have to hold on the
**same** `Registration`, because a student past his second year satisfies each on a different row —
and 2 635 students in this base have repeated, so the false positive is the ordinary case, not an
edge one. `GetStudentsQueryHandler`'s promotion filter is the shape to copy:
`s.registrations.Any(r => r.LevelId == levelId && (yearId == null || r.AcademicYearId == yearId))`.
Measured on the live base 2026-08-29: « 5ᵉ année Médecine, 2026-2027 » is **833** students; asked as
two independent `Any`s it is **2 127** — 2.6×, all of them people who merely *passed through* that
level. The same trap applies to any (year-invariant key, year) pair reached through a collection.

⚠ **`RegistrationStatus` joins that same `Any`, and it is the stricter case.** A verdict is a fact
about *one* registration, so « Diplômé » ∧ « 2026-2027 » asked as two conditions returns every
student who ever graduated and happens to hold a 2026-2027 registration — which, in a thesis year
re-registered every September until the defence, is most of them. `GetStudentsQuery.Status` (and its
twin on `GetStudentsExportQuery`, so the file matches the list it is downloaded from) is what makes
the 1 217 diplômés a réinscription records reachable from a screen instead of only from a file.

### A summary response feeds the edit form, so it must carry every field that form writes back
`HospitalSummaryResponse` omitted `Description`, and the admin form dutifully sent `''` — so
**editing any hospital erased its description**. The same shape was about to eat the coordinates.
When adding a column that an admin form edits, add it to the *summary* too, or make the form load the
detail (`ServiceFormModal` does the latter, since quotas do not belong in a list row).

### Identifiers of external provenance get a format check, not a shape check
`StudentIdentifierRules.ValidCne` in `Application/Students/`, used by both the create and update
validators — a rule enforced on one path only is a student who can be created and then never saved.
The old `^[A-Z]\d{6,12}$` described the modern CNE correctly and **rejected 5,646 of the 10,204
students in the base**, so more than half of them could not be edited at all, whatever field was being
corrected: 4,695 `LEGACY-nnnnn` placeholders the Access import manufactured, 835 digits-only codes,
plus faculty codes like `22FMPR1444` and codes with an internal space (`R 13089613`). PGSH is not the
authority on the grammar of a national code; **uniqueness is the constraint that protects anything
here**, and it is enforced separately.

#### ⚠ …and a student may have no CNE at all (2026-09-01)
`Student.CNE` is **optional**. The Access base records a national code for 5 510 of its 10 203
students, and the import manufactured `LEGACY-{NO_ORDRE}` for the other 4 693 — a value that reads,
in every list, every export, every déliberation canvas and every évaluation-import match, exactly
like a code somebody holds. **46% of the roll carried one.**

- **The column was already nullable** (`Users` is TPH and an `Employee` has no CNE); the requirement
  lived in EF's model and in the validators. `IX_Student_CNE` is now `UNIQUE … WHERE "CNE" IS NOT
  NULL` — Postgres already treated NULLs as distinct, so the filter states the intent rather than
  changing the rule. `StudentCneOptional` also clears the placeholders, guarded on
  `^LEGACY-[0-9]+$`.
- ⚠ **`Appogee` is the identifier that is in practice always present** — it carries the legacy
  `NO_ORDRE` verbatim for all 10 203 imported rows, and it is what the faculty's own réinscription
  roll keys on. Every canvas that matches on a CNE already indexes both.
- ⚠ **`null == null` is true in memory and NULL — i.e. false — in SQL.** So a uniqueness check on an
  optional identifier is guarded on the *request* value being present (`CreateStudentCommandHandler`,
  `UpdateStudentCommandHandler`), or the in-memory suite reports « CNE déjà utilisé » against the
  next student without one while PostgreSQL passes silently. The filtered index says the same thing.
- **`InscriptionPlanner` no longer manufactures one either.** A row with an Apogée and no CNE is
  stored with none. The `SANS-APOGEE-` half stays: a row must carry *one* of the two, and a student
  with neither could not be found by any of the canvases afterwards.
- ⚠ **`UpdateStudentCommandValidator` required `int.TryParse` on the Apogée**, which no
  `SANS-APOGEE-…` value satisfies — so every student the inscription created that way was read-only
  the day somebody opened his file. Third instance of the same defect class; it is length and
  presence that the column actually requires.
- **Phase 16.2 is answered** (measured 2026-09-01 against `Medecine.mdb`): **no** source row carries
  a `NO_ORDRE` in its CNE column — 0 identical to its own, 0 matching another row's, and only 1 of
  the 5 508 usable codes has the eight-digit shape. The CNE column is carried across verbatim, and
  there is nothing to move into `Appogee`.

### ⚠ A validator describes what a *save* must satisfy, not what a good record looks like
Every rule on an update path is applied to rows that already exist. If the imported data does not
satisfy it, those rows become **read-only** — and the refusal names a field the user was not editing,
so it reads as a broken button rather than as a rule.

- **It has happened five times.** `StudentIdentifierRules` rejected 5,646 of 10,204 students (above). And
  `UpdateStageCommandValidator` required `Objectives.NotEmpty()` while the Access import carried no
  objectives at all — **0 of 27 stages** satisfied it, so the entire stage catalogue could not be
  saved. Reported as « switching the rotation mode gives an error »; the actual message was
  *« At least one stage objective is required »*.
  - ⚠ **Three more found on 14/09/2026, all on `UpdateStudentCommandValidator`, all against values the
    write paths produce *deliberately*.** `Gender.NotEqual(Gender.None)` — `LegacyIdentityMapper.MapGender`
    writes `None` for the 1 050 blank rows and 3 « C » rows, its own comment reading « None is the honest
    answer; it is not a guess », and `InscriptionPlanner` writes it for every canvas row with an empty
    Sexe column, so the base **keeps acquiring them**. `DateOfBirth.NotEmpty()` — the column is
    `DateOnly?` and both paths store `null` when the source carries no date. `FirstName/LastName
    .NotEmpty().MaximumLength(50)` — `SplitName` gives a single-token name an empty *first* name by
    design, and the import truncates to **100**, the column's real width, so a 51–100-character name
    imported fine and never saved again. Pinned by `ImportedRowsStaySaveableEndpointTests`.
  - ⚠ **Read the write path, not the form.** Each of the three is refuted by one file — the importer or
    the planner — saying in a comment *why* it stores that value. The question a new rule has to answer
    is never « should a good record have this? » but « does anything already in the base lack it? », and
    the answer is in whatever wrote the rows.
  - ⚠ **The create side may be stricter, and saying so is part of the fix.** Nothing stored is judged by
    a create validator, so `CreateStudentCommandValidator` keeps demanding a gender and a date of birth:
    a human at a form can be asked, an imported row cannot. The asymmetry is documented on both classes
    rather than left to look like an oversight.
- ⚠ **A validator *looser* than its column is the same defect upside down, and it produces a 500.**
  Both hospital and both centre validators allowed a **200**-character name against `varchar(100)` and a
  **100**-character city against `varchar(50)`; description, e-mail and the three coordinates were
  bounded **nowhere**. An over-long value passed validation and PostgreSQL answered
  `22001 string_data_right_truncation` → `DbUpdateException` → **500**, whose `detail` the client
  discards — the screen says « Une erreur serveur est survenue » and never that a name is too long.
  It is « a delete asks the schema first, or the constraint answers for it », reached through a length.
  The widths are named once in `HospitalTextLengths` / `StudentIdentifierRules.MaxNameLength` and pinned
  from both sides by `TextLengthBoundsEndpointTests` — every refusal paired with the value that must
  still be accepted.
- **Ask where the requirement is really true.** Objectives are needed only by
  `EvaluationMode.ValidateObjectives`, and that is already enforced by the evaluation validators and
  `EvaluationObjectiveResolver`. A stage-level `NotEmpty()` asserted it for every stage, in every
  mode, forever. Validate what *is* supplied — a blank label, a zero weight — never that something
  optional was supplied at all.
- ⚠ **Handler tests cannot see any of this.** The validator runs in `ValidationPipelineBehavior`, so
  a test that calls the handler directly passes a malformed command straight through:
  `StageRotationModePersistenceTests` passes `[]` for objectives and stayed green the whole time.
  **A validator rule is covered in `PGSH.Tests/Integration/` or it is not covered**
  (`StageEndpointTests`).
- ⚠ **And the message has to reach the screen.** `StagesPage` caught every failure with a bare
  `catch` and showed « Erreur lors de l'enregistrement », so the one sentence that explained the
  refusal was discarded at the last step. Validation failures carry their messages in `errors[]`
  (`detail` is only the generic « One or more validation errors occurred »); every other refusal is
  in `detail`. Read both — the pattern is `(err as { data?: … })?.data`, as on `GroupsPage`.
  - ⚠ **`errors[]` sits at the *top level* of the problem document, never under `extensions`.**
    `Results.Problem(extensions: …)` fills `ProblemDetails.Extensions`, which is `[JsonExtensionData]`
    — the members are written flat, exactly as RFC 7807 says extension members are. The `ApiError`
    type declared `extensions.errors`, so the global `errorMiddleware` never found the array and
    *every* refused save toasted « Données invalides · One or more validation errors occurred » — the
    fixed sentence of `ValidationError`, which names no field and no rule. `StagesPage` read the real
    shape and was right all along; the middleware behind it was not. Pinned by
    `StageEndpointTests.A_refusal_carries_a_message`.
  - ⚠ **And a non-validation refusal carries its code in `title`, not in `errors[]`.**
    `Error.Conflict("Schedule.AlreadyPublished", …)` produces no `errors` array at all, so
    `StageDetailPage.extractErrorCode` — which read only `errors[0].code` — always returned null and
    both of its branches were dead code. Read `title` first.

### Search handlers — one shape, and it is a shared helper now
**Searching for a person goes through `StudentSearch.WhereStudentMatches` / `EmployeeSearch.WhereEmployeeMatches`,
never through a predicate written on the spot.** Each call site names only the path to the person
(`r => r.Student`, `p => p.InternshipAssignment.Registration.Student`, `s => s`); the columns, the casing, the
tokenisation and the accents live in one place.

- ⚠ **A term is a conjunction of *words*, not a string.** All seven student searches compared the whole
  term to each column, so « Mohamed Alami » — the full name, the first thing anyone types — matched
  **nobody**: the first name does not contain it, the last name does not contain it, and no column carries
  both. `SearchTerms.Split` cuts the term into at most five distinct lowered words and each one must land
  somewhere on the **same** person. The rule strictly widens the old one (a column containing the whole
  string contains each of its words), so nothing that used to be found stops being found — the tests pair
  every new case with that control.
- ⚠ **The column list is identical on every screen, and that is the point.** The list searched six columns,
  a roster five, a service's occupants three: a student found by his Apogée from the list was *not* found
  from the service he stands in, which reads as an absent student rather than as a narrower search.
- **An accent is an extra spelling, never a replacement.** The term is also searched with its diacritics
  stripped, and only when that differs — folding the term alone would lose « BENAÏSSA » for someone who
  types it correctly. Folding the *column* needs PostgreSQL's `unaccent` (an extension plus a generated
  column), so a term typed without an accent still misses a stored one; that half is not built.
- ⚠ **The predicate is *recomposed*, not invoked.** One rule written on `Student` is applied to
  registrations, périodes and cells by substituting the path for its parameter —
  `ExpressionComposition.Through`. The naive way is `Invoke`, which **EF refuses**: it would have turned
  seven screens into 500s with the whole suite green. Pinned by `SqlTranslationTests`.
- Every field is still lowered on both sides and guarded against null (`Appogee` was case-sensitive for
  months, so `"ap2200a"` never found `AP2200A`; the CNE is absent on 46 % of the base, and the in-memory
  provider throws where PostgreSQL just answers « not true »).
- On the frontend, pair every server-querying search with `useDebouncedValue(…, 350)`, an `isFetching`
  indicator, and `skip` below 2 characters.

### Store-generated keys — never pre-set them on children of a tracked parent
Assigning `Id = Guid.NewGuid()` to an entity added to an **already-tracked** aggregate makes EF classify it
`Modified` instead of `Added` → `UPDATE … WHERE Id = <new guid>` → 0 rows → `DbUpdateConcurrencyException`.
Let the store generate the key (see the comments at `InternshipAssignment.cs` `Delocalize` / `TransferToCohort`).
`dbContext.Add(root)` on a brand-new graph is safe — `Add` marks the whole graph `Added` regardless of key values.

### Shared response types — use these, never duplicate
- `UserResponse` → `Application/Users/UserResponse.cs`
- `LevelResponse` → `Application/Stages/Levels/LevelResponse.cs`

### Handler patterns
- **Existence checks**: use `AnyAsync` when you only need to verify a FK exists. Use `FirstOrDefaultAsync` when you need to modify the entity.
- **Uniqueness checks**: always exclude the current entity on updates (`c.Id != request.Id`).
- **GetMany**: always start with `AsNoTracking()`, apply filters, then `OrderBy(...).ToPaginatedResponseAsync(...)`.
- **Validators**: use `IsInEnum()` for enum parameters, not string length checks.

### Endpoint patterns
- **POST (no route id)** — bind the `Command` directly, no inner `Request` record needed:
  ```csharp
  app.MapPost("entities", async (CreateEntityCommand command, ISender sender, CancellationToken ct) => ...)
  ```
- **PUT (route id + body)** — use an inner `Request` record to merge route param with body, then construct the command:
  ```csharp
  public sealed record Request(string Name, EntityType Type, ...);
  app.MapPut("entities/{id:int}", async (int id, Request request, ISender sender, CancellationToken ct) =>
  {
      var command = new UpdateEntityCommand(id, request.Name, request.Type, ...);
      ...
  })
  ```
- **GET list** — use `[AsParameters]` to bind the Query directly from the query string:
  ```csharp
  app.MapGet("entities", async ([AsParameters] GetEntitiesQuery query, ISender sender, CancellationToken ct) => ...)
  ```
- **Enum fields in requests** — always use the actual enum type (not `int`). `JsonStringEnumConverter` is globally registered so `"Medical"` deserializes correctly. Never cast `(ServiceType)request.ServiceType`.
- **Routes** — no leading slash. Correct: `"hospitals/{id:int}"`. Wrong: `"/hospitals/{id:int}"`.
- ⚠ **A non-nullable value type bound from the query string cannot be omitted — and it throws *before*
  your validator runs.** ASP.NET raises `BadHttpRequestException` inside `EndpointMiddleware`, so
  `ValidationPipelineBehavior` never executes: the query's own rules are dead code for that request, the
  caller gets a bare **400** with no `detail` and no `errors[]`, and the client's `errorMiddleware`
  shows its generic sentence — the screen reads as broken rather than as « renseignez une date ». It
  also pauses the process for anyone running under a debugger. Observed on the live application
  13/09/2026: *« Required parameter "DateOnly StartDate" was not provided from query string »*, from
  the rotation-cycle screen with the date field cleared. **A parameter a screen can leave blank is
  bound nullable (`int?`, `DateOnly?`) and refused by a validator, in words**; the handler then reads
  it into a non-nullable local. A POST act's `confirmedCount` stays required — a caller that omits that
  is a broken client, not a user with an empty field.
  - ⚠ **And `.WithMessage` attaches to the validator immediately before it**, so
    `NotNull().GreaterThan(0).WithMessage(…)` leaves the *null* case on FluentValidation's default
    text — « 'Level Id' ne doit pas avoir la valeur null », which names a property and no remedy. Two
    rules, two messages.
  - ⚠ **Only a request through the real pipeline can see any of this.** A handler test constructs the
    query object, and a validator test constructs it too — both skip model binding entirely.
    → `PGSH.Tests/Integration/RequiredQueryParameterEndpointTests.cs`
  - ⚠ **An `enum` is a value type too.** An omitted `scope` throws exactly like an omitted `DateOnly`;
    it does not quietly become the zero member. Missed on the first hand sweep, and it is half of why
    the second one also took the stack down.
  - ⚠ **Do not sweep this by hand — it is a test.**
    `PGSH.Tests/Integration/NoRequiredQueryStringValueTypesTests.cs` reflects over every mapped
    endpoint and fails on any required query-string value type. It was written because the hand sweep
    was run twice and was wrong once: it scanned a single project for the declarations, so the types
    declared *inside* endpoint classes (`OccupantsRequest`, `ImportOptions`) were reported as « not
    found » and read as noise rather than as the answer. The pre-existing offenders were an explicit
    **shrinking** list in that file — nothing may be added to it, and an entry that is fixed must be
    removed (a second test enforces that, so the list cannot become a lie).
    ✅ **The list is empty since 13/09/2026**: all 24 routes bind nullable and refuse the omission in
    words. It is kept as an empty set rather than deleted, because the ratchet is the two tests and
    they have to outlive the list — a new offender now fails immediately with no amnesty to join.
  - **The refusal is written through `RequiredParameterRules`** (`Application/Extensions/`):
    `.IsARequiredReference(…)`, `.IsARequiredDate(…)`, `.IsARequiredChoice<TEnum>(…)`. ⚠ Unlike
    `PaginationRules` it **takes the sentence** rather than supplying one — there is one page ceiling
    and one thing to say about it, but « la promotion est obligatoire » and « précisez l'année de
    départ » are different facts, and twenty routes sharing one sentence would say no more than the
    bare 400 they replaced. What is shared is the *mechanics*: one `Must` predicate covering both
    absence and a meaningless value, so the `NotNull().GreaterThan(0).WithMessage(…)` trap — which
    leaves the null case on FluentValidation's default text — is structurally impossible.
  - ⚠ **A write act refuses its year; it does not resolve one.** An omitted academic year means « the
    current one » on a *read* (`AcademicYearResolver`) and that stays right. It is the wrong rule for
    an act that destroys: the four roster acts (`groups/assign-partitions`, `groups/partitions`,
    `groups/all`, `groups/all/students`) name their own year or are refused, because the current one
    is precisely the promotion everybody is working on. An omitted `levelId` on the two teardowns is
    different — it is the year-wide scope, deliberately chosen — and that asymmetry is the point.
- **Error mapping** — always use `result.Match(Results.Ok/Created/NoContent, CustomResults.Problem)`. Never return `Results.Ok` unconditionally on a command that can fail.
- **DomainException subclasses** — `GlobalExceptionHandler` catches all `DomainException` subclasses automatically via the base class. Add new exception types by inheriting `DomainException` — no handler changes needed.

## Key Design Conventions

- **NuGet versions** are centralized in `Directory.Packages.props` — never add `Version=` in `.csproj` files.
- **Implicit usings** and **nullable reference types** are enabled project-wide — don't add `using System;` etc.
- Domain entities go in `PGSH.Domain/`, grouped by domain folder. Value objects and enums alongside their entity.
- `int` PKs for reference/catalog data (Level, Stage, Cohort, Hospital, Center, Service). `Guid` PKs for operational/transactional data (Registration, InternshipAssignment, ServicePeriod, User, etc.).
- Enum-backed status fields stored as `varchar` via `.HasConversion<string>()` in EF configuration.
- CORS is open (`AllowAllForDev`) in development. Lock it down before production (see Phase 12).
- `PermissionProvider.GetForUserIdAsync` is a stub returning empty — do not rely on it until Phase 8.
- No step-comments (`// 1. Fetch`, `// 2. Validate`) — code should be self-documenting. Only add comments when the WHY is non-obvious.
