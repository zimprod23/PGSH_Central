# Progression — the final-year gate and revalidation

> Read before touching what a student owes, who may enter a final year, or how a failed stage is re-opened.
>
> Split out of `CLAUDE.md` on 2026-09-06 — the text is unchanged. See [`CLAUDE.md`](../CLAUDE.md) for the always-on rules and the map of these documents.

## The last year does not begin until everything below it is validated
`Stages/Progression/` — `OutstandingStageFinder` (what a student still owes, cursus-wide) and
`FinalYearGuard` (whether that stops him). Enforced by `ReinscriptionPlanner`,
`CreateRegistrationCommand`, `CreateManyRegistrationsCommand` and `InscriptionPlanner` alike.

- **The rule is the faculty's**: a 7ᵉ année under arrêté 2174.18, a 6ᵉ under 1650.25, cannot be
  **entered** while a stage from an earlier year is unvalidated. Asked per **student** from his own
  `TotalYears` — from 2026-2027 one 6ᵉ année Médecine holds students of both texts, so the level alone
  cannot answer "is this his last year?".
- ⚠ **« Entrer » is the whole rule, and reading it as « être inscrit en » inverts it.** The final year
  is not a year one passes or fails — there is no déliberation for it. The student validates and
  revalidates his stages one at a time, never redoing one already validated, and sits the *examens
  cliniques* once they are all done; he is **re-registered each September until both are cleared**. So
  the re-registration *is* the mechanism by which he clears the debt, and refusing it because he still
  owes a stage refuses him the only way to stop owing it. `FinalYearGuard` therefore stands aside for a
  student who **already holds a registration at that level** — he is continuing, not beginning.
  - **Measured 2026-09-01 against the faculty's own roll**: of the 651 7ᵉ année Médecine it
    re-registers into the 7ᵉ, **182 were refused** — a quarter of the promotion, every one named by the
    faculty as coming back. With the rule corrected the gate refuses **60**, which is exactly the
    MED06 → MED07 population it was written for.
  - **A gap does not make it a beginning.** A student who sat in the final year, dropped out and comes
    back has the same stages to revalidate; the guard reads « has he ever been registered at this
    level », not « was he there last year ».
  - ⚠ **`Debt.LevelYear` is the *registration's* level, not the stage's** (`OutstandingStageFinder`
    projects `a.Registration.Level.Year`). A failed attempt recorded against the final-year
    registration is therefore not an *earlier* debt, and the gate rightly ignores it — which is how
    the first version of the test for this passed with the rule removed.
- ⚠ **The existing déliberation check cannot answer it.** `StudentsWithUnvalidatedStagesAsync` is
  scoped to `a.Registration.AcademicYearId == yearId` — stages of the year being deliberated. A 6ᵉ
  année student owing a 4ᵉ année stage is invisible to it. The debt has to be read across every
  registration, which is what `OutstandingStageFinder` does.
- **Owed = every attempt came back `NonValidé`** — the same test `DossierStageState.ToRevalidate`
  uses, deliberately, because two screens disagreeing about what a student owes is worse than either
  being slightly wrong.
  - ⚠ **…counting only the attempts a year still stands behind** (`RegistrationStatus.AnnulsItsStages`,
    settled 2026-09-01). A redoublant repeats the year from scratch, stages included, so an attempt
    served inside a `Failed` year establishes **nothing** — not an acquisition, and not a debt. The
    case: pass a stage, fail the year, repeat it, fail that stage. Without the filter he reads as
    having acquired it and enters his final year owing it, while the last thing he did was fail it.
    Only `Failed` annuls — `Withdrawn`/`Excluded` end the cursus instead — and `Active` annuls
    nothing, which is what keeps the imported cursus (all `Active`, no verdict ever recorded) intact.
  - ⚠ **`NonÉvalué` is not owed.** An unmarked stage is one nobody graded, not one he failed, and this
    base holds almost no marks — counting it would block the whole faculty on missing data.
  - ⚠ **Nor is a stage never attempted.** Reading "owes" from the CNPN's requirement set would be
    stricter and today wrong: 1650.25's requirements are not entered, so every six-year student would
    owe everything. Widen *there* when the sets are complete, not at each call site.
- ⚠ **`TryGetValue`, never `GetValueOrDefault`, on a `Dictionary<Guid, int>` of final years.** The
  default is `0`, not null, and a 0 read as "his text runs 0 years" makes *every* year his last —
  which blocked every student with no CNPN on record, i.e. the one case the guard must stand aside
  for. It fired hardest exactly where it should not have fired at all.
- ⚠ **…and the faculty's own roll no longer refuses at all — it registers and holds.** See
  `RegistrationHold` in [`year-closing.md`](year-closing.md). The gate still *decides*, and its sentence becomes the hold's evidence;
  what changed is what is done about the answer. The 60 the corrected rule refuses on the real roll
  are now 60 registrations created and frozen, which is the difference between a promotion that is
  complete on paper with 60 rows to review, and 60 students nobody re-registered.
- **Enforced on the manual paths too.** A guard the bulk rollover applies and the registration form
  does not is a guard anyone steps around by using the other button. The inscription import asks it of
  the students PGSH already holds — a returner really can be re-entering a final year owing a stage —
  and never of a newcomer, who has no cursus here to owe anything from.
- **Asked once for a batch** — `EnsureMayEnterManyAsync`, which is the implementation; the
  single-student call delegates to it, so the two paths cannot drift. Per student it is four queries —
  the level's year, his text, his whole cursus, his waiver — i.e. ~2,800 round-trips to enrol a
  promotion of 700 through `CreateManyRegistrationsCommand`. Narrowed twice so the batch stays cheap
  *and* the single call gets no dearer: the cursus is read only for the students this level is the last
  year of, the waivers only for those who then owe something, so a batch where nobody is in his final
  year is two queries whatever its size.
  - ⚠ **`Contains` is right on a list and wrong on a promotion.** `ForStudentsAsync` takes the ids the
    caller named — bounded by what somebody selected; `ForPromotionAsync` is scoped by the predicate
    that selects the promotion, because 8,077 registrations is a set nobody enumerated. Reach for the
    predicate whenever the set is *described* rather than *listed*.
  - `ReinscriptionPlanner` still carries its own copy of the decision, for exactly that reason: it is
    predicate-scoped. Folding it in means teaching the guard to take a predicate, not handing it ids.
- **The exception is a row, not a flag** — `FinalYearEntryWaiver`, keyed (student, year), with a
  required reason and a **snapshot of what was owed** at the moment it was granted. By the time it is
  read back the stage may have been revalidated or dropped by a new text, and a waiver that cannot say
  what it excused is not a record. Refused when nothing is owed (it would assert an exception that
  never happened) and **irrevocable once the registration it permitted exists** — removing it would
  leave a student in a final year with nothing on record saying who allowed it.
- `ReinscriptionReport` counts `FinalYearBlocked` **and** `FinalYearWaived`: an override nobody sees
  is an override nobody reviews.

## Revalidation is the escape valve, and it is deliberately loose
`Stages/Revalidation/` — `RevalidateStageCommand` re-opens a failed stage on the registration the
student holds **now**, as a fresh `InternshipAssignment`; the failed one stays as history.

- **Not constrained to the registration's own level** — a 6ᵉ année student redoing a 1ʳᵉ année stage is
  the case it was built for. The real constraint is that a failed attempt exists.
- **Served where he failed it**: the original service is reused unless overridden, as an **ad-hoc
  placement outside the published grid** (`CohortSlotAssignmentId` stays null, like a délocalisation).
  It is one student making good one stage, not a cell in his group's rotation.
- Placement is all-or-nothing and **optional**: create the assignment now, schedule it later.
  `CohortId` slots him into any cohort currently running the stage.
- Guards: scolarité only · a stage validated on *any* registration is never re-opened · **every** prior
  attempt must be settled `NonValidé` (one still pending blocks, so two attempts can never run at once).
- ⚠ **Gap:** it requires a prior *failed* attempt (`NothingToRevalidate`), so there is no way to hand a
  student a stage he never attempted — a legacy record never entered, a case somebody has to fix by
  hand. And no generic "assign this student to this cohort" command exists: every other creation path
  is bulk (`GenerateSchedule`, `StudentAffectationService`) or specific (`Delocalize`,
  `LateArrivalScheduler`). That is the flexibility hole to close next.

## ⚠ « Un stage acquis ne se refait jamais » est vrai du dossier et faux de la planification
`OutstandingStageFinder.Fold` le dit en toutes lettres — *« One validated attempt clears the stage for
good — a stage once acquired is never repeated, whichever year earned it »*. C'est la règle, et le
**dossier** l'applique. La **répartition**, elle, ne la connaît pas.

### Ce que la réinscription conserve — et c'est la bonne moitié
Un étudiant réinscrit garde tout ce qu'il a validé. La dette est lue **par étudiant, à travers toutes
ses inscriptions** (`OutstandingStageFinder` groupe sur `(StudentId, StageId)`), jamais par
inscription — donc un stage validé sous l'inscription de l'an dernier est vu par le dossier de niveau,
par le parcours, par la déliberation et par `FinalYearGuard`. Une seule tentative `Validé` suffit et
elle vaut définitivement.

- ⚠ **Sauf si l'année a été prononcée `Failed`** : `AnnulsItsStages` écarte alors *toutes* les
  tentatives de cette année, les réussies comprises — le redoublant refait l'année de zéro. C'est le
  verdict de **l'année** qui annule, jamais celui du stage : un stage échoué dans une année réussie
  reste un crédit reporté ordinaire, réglé par `RevalidateStageCommand`.
- ⚠ **Une 7ᵉ année n'est jamais `Failed`** — il n'y a pas de déliberation pour la dernière année.
  L'étudiant y est réinscrit `Active` chaque septembre jusqu'à ce que ses stages soient tous validés,
  puis jusqu'aux examens cliniques. Ses stages validés tiennent donc, et sa réinscription **est** le
  mécanisme par lequel il éteint sa dette — d'où la correction de « entrer » = *commencer*.

### ⚠ La 7ᵉ année ne porte aucun stage, donc le problème ci-dessous ne l'atteint pas
Mesuré le 07/09/2026 : **`Septième Année Médecine` a 0 stage au catalogue**, et les 1 347 inscrits de
2026-2027 y portent **0 affectation**. Ce qu'un 7ᵉ année « doit encore » sont des stages de **6ᵉ**,
reportés — la dernière année est celle de la thèse et des examens cliniques.

- **Conséquence pratique** : la répartition annuelle ne lui donne rien, donc elle ne peut pas le
  replacer dans un stage déjà validé. Ses stages en retard se règlent **un par un**, par
  `RevalidateStageCommand`, qui le place hors grille.
- ⚠ **Le piège est ailleurs** : « Revalider » exige une tentative **échouée** au préalable
  (`NothingToRevalidate`). Un stage de 6ᵉ jamais tenté, ou resté `NonÉvalué`, n'a donc aucun chemin —
  c'est le trou de flexibilité de la section précédente, et il tombe exactement sur cette population.
- **Le problème de replacement ci-dessous vise donc les niveaux qui ont des stages** — 3ᵉ à 6ᵉ MED,
  4ᵉ et 5ᵉ Pharmacie — c'est-à-dire le redoublant, pas le 7ᵉ année.

### Ce qu'elle ne fait pas — la répartition replace le redoublant dans tout
`StudentAffectationService.AssignAsync` dédoublonne sur **`(RegistrationId, CohortId)`**. Une nouvelle
inscription est un nouvel identifiant et les cohortes de l'année suivante sont de nouvelles lignes,
donc la clé ne peut pas coïncider : l'affectation automatique lui crée une affectation pour **chaque**
stage de la promotion, y compris ceux qu'il a déjà validés.

- **Rien dans `Stages/Planning/` ne lit un `InternshipAssignment.Result` passé** — balayé le
  07/09/2026, le namespace entier. `CohortProvisioner` filtre bien les cohortes, mais sur ce que le
  **CNPN exige au niveau** (le compte « hors CNPN » affiché à l'écran), ce qui est un filtre par
  promotion et non par étudiant. Il n'existe aucun rétrécissement par étudiant nulle part.
- **Le coût est réel** : il occupe une place dans un service, il est compté dans l'occupation, il
  paraît sur la liste de travail du chef, et il ressort avec une seconde ligne validée. La dette,
  elle, n'en souffre pas (`Fold` retient « au moins une tentative validée »), mais la place, si.
- ⚠ **Mesuré sur la base vivante le 07/09/2026 : 792 lignes, 300 étudiants** ont été réaffectés à un
  stage déjà validé sous une inscription antérieure non annulée, au même niveau. **Toutes sur
  2018-2019 → 2025-2026, aucune sur 2026-2027** — ce sont donc des lignes **importées** d'Access, la
  pratique réelle de la faculté (un redoublant qui refait effectivement le stage, dont l'échec d'année
  n'a jamais été enregistré : voir le trou `RegistrationStatus`, Phase 14.3). **PGSH n'a encore jamais
  emprunté ce chemin** : 2026-2027 est sa première année planifiée et personne n'y a encore été
  réinscrit *et* replanifié au même niveau.
- **Aujourd'hui le remède est manuel** : générer le plan, puis retirer les affectations redondantes.
  ⚠ Et il n'existe **aucune commande de suppression d'une affectation seule** — voir le trou de
  flexibilité ci-dessus, c'est le même. `HANDOFF.md` A7.
### ✅ La règle est tranchée (07/09/2026) : un stage acquis ne se ressert jamais
Décision de l'utilisateur, en toutes lettres : « one stage is done means it is alright he wont do it
again ». Un stage validé est **définitivement** acquis ; ce qui reste se poursuit à l'inscription
suivante ; un stage échoué se règle par « Revalider ». **La répartition doit donc cesser de replacer
un étudiant dans un stage qu'il détient déjà** — ce que le dossier affirmait déjà et que la
planification ignorait.

- ⚠ **Ceci ne contredit pas les 792 lignes importées.** Elles sont toutes sur des années dont le
  verdict n'a jamais été prononcé (`Active`, le trou Phase 14.3) : là où l'échec d'année *est*
  enregistré, `AnnulsItsStages` écarte déjà les tentatives de cette année, les réussies comprises,
  et le redoublant refait bien tout. Les deux règles se composent — « acquis » veut dire « validé
  dans une année que la faculté n'a pas annulée ».
- **Mesuré le 07/09/2026 : appliquer le filtre aujourd'hui ne changerait *rien*.** Les seuls
  redoublants des promotions planifiées de 2026-2027 sont **27 étudiants** (12 en 3ᵉ MED, 10 en 4ᵉ,
  5 en 5ᵉ) et **toutes** leurs tentatives antérieures sont `NonÉvalué` : rien d'acquis, rien de dû.
  Le filtre est donc à installer au moment le moins cher possible — il est un no-op sur la base
  actuelle et correct dès la première année portant des notes.
- ⚠ **Il devra *nommer* ce qu'il écarte**, jamais sauter en silence : « 3 étudiants non affectés :
  stage déjà acquis ». `BulkResponse` porte déjà le résultat par ligne. Une promotion sortie un
  étudiant plus courte ressemble exactement à une promotion de cette taille.
- **Reporté, pas abandonné** : rien n'est écrit à ce jour, `HANDOFF.md` A7.
