# The CNPN — which text governs a student, and when

> Read before touching anything that resolves, stamps, or reads a requirement set: registration creation, the réinscription, group cutting, cohort provisioning, or the Stages catalogue figures.
>
> Split out of `CLAUDE.md` on 2026-09-06 — the text is unchanged. See [`CLAUDE.md`](../CLAUDE.md) for the always-on rules and the map of these documents.

## The CNPN is a cohort's text, not a year's
`Curriculum` is keyed **(CnpnVersionId, LevelId)** — never on the academic year. Arrêté 1650.25
(BO 7422, 17 July 2025) took Médecine from 7 years to 6 from 2024-2025, while art. 2 leaves everyone
registered *before* that year under arrêté 2174.18 in its pre-2175.22 form. From 2026-2027 one
(level, year) therefore holds students of two texts, so the year cannot identify a requirement set.

### What a student owes is a fact about a *registration*, not about the student
`Registration.CnpnVersionId` (+ `CnpnSource`) is the governing text for **one student, at one level,
in one year** — resolved once, when the registration is created, and never recomputed.
`Student.CnpnVersionId` remains "the text he is on now"; it is the default a new registration is
stamped from, not the answer to what any given year required.

- ⚠ **The case that forces it.** A 4ᵉ année student still owing two stages from his 3ᵉ année owes them
  under the 3ᵉ année of *his* 3ᵉ année. Reshaping that level for the promotions behind him must not
  reach back and change his debt — and with one stamp per student it did, because requirements were
  always resolved from where he stands today.
- **Read order everywhere: `r.CnpnVersionId ?? r.Student.CnpnVersionId`.** Null is not "owes nothing",
  it is "never resolved" — the six imported years were backfilled from the student's stamp
  (`Backfilled`, deliberately not `StudentStamp`: nobody was asked at the time), and ~2,200 enrolled
  students carry no stamp at all. Moved onto the registration: `CohortProvisioner`,
  `AutoArrangeGroupsCommandHandler`, `DeliberationPlanner`, `RecordRegistrationOutcomeCommand`,
  `GetStudentRegistrationsQuery`.
- ⚠ **A pronounced year freezes its text** (`Registration.CnpnFrozenByOutcome`). The verdict was
  recorded against a requirement set; moving that set afterwards leaves nobody able to say what the
  jury ruled on. There is no override — re-opening the year is the act that makes the change
  legitimate. Note the guard lives in **two** places (aggregate and `CnpnEffectivityPlanner`), and the
  planner's fires first: a test that only goes through the planner proves nothing about the aggregate.
- `DeleteCnpnVersionCommand` gates on registrations as well as students (`CannotDeleteWithRegistrations`).
  Not redundant: a text can govern a closed year of a student who has since moved on, so the student
  count reaches zero while registrations still name it — and the FK is `Restrict`, i.e. a 500.

### « À partir de la 3ᵉ année de 2026-2027 » — `CnpnLevelEffectivity`
One authored row: **this text governs this level from this year onward, whoever is sitting in it.**
`Cnpn/Effectivity/`. The intake year on the text governs the promotion *arriving*; these govern the
promotions already in the building.

- ⚠ **No entry-based rule can express it.** After the 7→6 reduction was contested the cut actually
  applied was « la 3ᵉ année de 2026-2027 et en dessous » — two students with the *same entry year*
  land on different texts, one repeating the named level and one a year ahead of it.
- **Read once per registration, then frozen** (`RegistrationCnpnStamper`). That is what makes both
  halves true at once: the repeater re-registering gets a *new* registration, so the rule sees him
  automatically; the student who moved on is judged by the stamp on his *old* one.
- ⚠ **This is not the live-state rule `CnpnTargeting` avoids.** That objection is about re-evaluating
  an existing student's stamp — « année ≤ 2 » selects different people every September. Evaluating
  once, at creation, preserves the guarantee exactly while removing the need for somebody to remember
  to run a bulk command each year (which is what leaked on repeaters and on returners).
- **« et en dessous » is one row per level, never a stored comparison.** A comparison would have to be
  re-evaluated to be read, and a level added or renumbered later would silently change which
  promotions a published text binds. Rows say which levels were meant, forever.
- ⚠ **A rule is the one path allowed to move a *confirmed* student stamp.** It fires on one
  registration, at creation, from a rule authored for that exact (level, year) — not over a population
  re-selected each September, which is what the bulk guard exists to stop.
- **Resolution order** (`RegistrationCnpnStamper`): effectivity rule → the student's stamp → the text
  on his most recent earlier registration → `CnpnAssignment` from his intake. A registration being
  created is its own entry evidence, so a genuine new entrant needs no prior save.
- **Deleting a rule is prospective.** Nothing already stamped moves; the count is returned so the
  confirmation can name it. `ApplyCnpnEffectivityCommand` exists only for the order that actually goes
  wrong — the réinscription ran in September, the faculty settled the cut in October — and echoes back
  `ConfirmedMoveCount`, like the déliberation's `ConfirmedDefaultCount` and for the same reason.
- Uniqueness: `(CnpnVersionId, LevelId)` and `(LevelId, FromAcademicYearId)`. The second is the
  substantive one — two texts starting to govern one level in one year has no defensible winner.

### The text is an aggregate, and what it may decide alone is the whole question
`CnpnVersion` was a property bag until 2026-09-01: no `Entity` base, public setters on every member,
and every invariant of the text living in whichever handler happened to need it. In the *same*
namespace `Curriculum : Entity` had `AddStage` / `RemoveStage` / `CopyFrom` and raised events, and
`Registration.StampCnpnVersion` / `Student.AssignCnpnVersion` were already model aggregates — the
text was the odd one out, and it had already cost the usual price.

- ⚠ **One rule was written twice, in two directions.** « Un texte ne peut pas régir un niveau
  au-delà de sa durée » lived in `CreateCnpnEffectivityCommandHandler` as `level.Year >
  version.TotalYears` **and** in `UpdateCnpnVersionCommandHandler` as `deepestEffective >
  TotalYears`, with nothing tying them. Both now come from `CnpnVersion`.
- **It carries `init` accessors over explicit backing fields**, exactly as `AcademicYear` does and
  for the same reason: the seeder, the migration and the tests still build one with an object
  initialiser, while nothing changes one *afterwards* except `Correct` / `DeclareEffectivity` /
  `WithdrawEffectivity`. `AcademicProgram` has no mutator at all — the type now says what the
  comment used to.
  - The compiler found the two places that were reaching in (`FinalYearGateTests` assigning
    `TotalYears` on a seeded text), which is the point of the change.
- **`DeclareEffectivity` / `WithdrawEffectivity` raise domain events.** Stamping a registration and
  moving a student both raised one; *the act that decides the text of every registration created at
  that level from that year on* was silent. It is the widest act in the area and the only one
  nothing could observe.
- ⚠ **Three rules stay with the handler, and the line is principled**: a code unique within a
  programme, an intake year claimed by one text, and a (level, year) a **rival** text already takes
  effect for. Those are about the *other* texts and no aggregate can see them — the same division
  `AcademicYear` makes, where « does it end before it starts » is the year's and non-overlap is the
  handler's.

⚠ **…and one rule deliberately does *not* read the aggregate's own children** — `CnpnSpanFloor`.
`Correct` is handed the deepest level year already carrying requirements and the deepest one already
governed, read from the store by the handler, rather than counting `Curricula` and
`LevelEffectivities` itself.

- **An un-Included collection is indistinguishable from an empty one.** Counted on the aggregate, a
  caller that forgot an `Include` gets « rien enregistré », the shortening goes through, and every
  requirement set below the new span is stranded — with no unique index to catch it.
- ⚠ **And this suite cannot see that mistake.** Measured 2026-09-01: deleting the `Include` from
  `UpdateCnpnVersionCommandHandler` left **all 23 `CnpnVersionManagementTests` green**, because the
  in-memory provider fixes navigations up from the change tracker. On PostgreSQL the collection
  would simply be empty. Same family as the translation blind spot: the store is asked for the fact,
  the aggregate decides what to do about it.
- `DeclareEffectivity` *does* read `LevelEffectivities` for « déjà déclaré », and that is a
  different bargain on purpose: `IX_CnpnLevelEffectivity_Version_Level` is unique, so a missed
  `Include` degrades to a constraint violation rather than to silent loss — the shape
  `Curriculum.AddStage` already had.

### Entry is deduced once, by `EntryYearDeduction`
« On ne peut pas être en 3ᵉ année sans avoir passé deux ans » — walk back `level - 1` academic years
from the earliest registration on record. It is the **single assumption the whole backfill rests
on** (~2,200 students the legacy import caught mid-cursus), and it was written **twice**:
`CnpnAssignment.DeduceEntryYearId` and `RegistrationCnpnStamper.WalkBack`, each with its own private
`YearRef`. Pure, no store and no clock, like `PeriodAxis` / `RotationTiling` / `StagePeriodFolder`,
so the clamping cases are exact rather than approximately seeded.

- `CnpnAssignment` now answers only the half that needs the store — which text governs an intake.
  Its `ResolveAsync` is gone: it had **no production callers** (seven tests kept it alive) and its
  `asOfAcademicYearId` parameter was never read in the body while the doc comment described what it
  supposedly anchored.

### `RegistrationCnpnStamper` returns a report, not a `Result`
It has no failure path and never had one: every refusal it can meet is a fact about *one*
registration — no text could be resolved, or the year is already pronounced — and stopping the batch
on one of them would refuse the other six hundred. They are counted into `Unresolved` /
`FrozenByOutcome` instead. The five callers were each testing `stamp.IsFailure` on a `Result` that
could not fail.

- **Assignment is by first registration and sticky** — `CnpnAssignment` in `Application/Stages/Cnpn/`.
  Never by the level a student currently sits in: those agree only for students who never repeated,
  and 2,635 have. `Student.CnpnVersionId` is written solely by `Student.AssignCnpnVersion`, which
  refuses to move a confirmed stamp without `overrideExisting` — or by an effectivity rule, above.
- **Who a text binds is authored, not inferred** — `Cnpn/Targeting/`. A rule
  (`programme + année ≤ N + as-of year`) is previewed, reviewed, then frozen onto
  `Student.CnpnVersionId`. Preview and apply share `CnpnTargetPlanner`, so the dry run *is* the plan —
  the same guarantee the evaluation import makes, for the same reason.
  - ⚠ **The rule is never stored as live state.** Re-evaluated next September, "année ≤ 2" selects a
    different set of people, and the whole point of the stamp is that a student's text does not move
    under them. What survives is the membership plus the command's audit entry (`IAuditableCommand`
    records the criteria, author and date) — that is why there is no `CnpnTargetRule` entity.
    ⚠ Do not read this as an argument against `CnpnLevelEffectivity`: what must never be re-evaluated
    is an *existing* stamp. A rule read once, at the creation of a registration, and frozen onto it
    moves nobody's text under them — which is why the two mechanisms coexist rather than compete.
  - ⚠ **A selector covers only students who already exist.** Future intakes are the version's
    `AppliesToEntrantsFromAcademicYearId`. A text needs *both* halves or next year's first-years land
    under nothing.
  - ⚠ **Bulk never moves a confirmed stamp.** Doing it wholesale is exactly how the per-student guard
    gets defeated, so a conflict is reported (`ConfirmedOnAnotherText`) and left alone. Upgrading an
    *inferred* stamp is not a move and is allowed — that is how scolarité confirms the ~2,200
    deduced assignments.
  - ⚠ **Where the rule and the arrêté disagree, the system reports and the faculty decides.**
    `EntryPredatesText` is the repeater sitting in an early level; `IncludeEntryContradictions` is
    the faculty saying yes, and it is never assumed.
- ⚠ **Entry is often unrecorded.** The legacy import only carried students once they had stages, so
  ~2,200 enrolled students have no registration before 2025-2026. `CnpnAssignment` then deduces entry
  from their level (you cannot be in year 3 without two prior years) and sets
  `CnpnAssignmentIsInferred` — surfaced for scolarité, never presented as fact.
- A version with a null `AppliesToEntrantsFromAcademicYearId` is recorded for citation and never
  selected (arrêté 2175.22 is exactly this).
- **Groups are homogeneous by CNPN.** `AutoArrangeGroupsCommandHandler` splits by
  (year, level, `CnpnVersionId`) — a group rotates through a stage set together, so two students
  owing different sets cannot share one. Unstamped students form their own bucket, labelled
  "CNPN à confirmer", rather than being folded into a text they may not follow.
- **`CohortProvisioner` will not give a group a cohort for a stage its text does not require** of
  its level. Where no requirement set is recorded for a (text, level) the check stands aside — an
  enforcing check would block all planning for six-year students, since 1650.25's requirements are
  not entered yet. Refusals are counted, not dropped (`NotRequiredByCnpn`).
- ⚠ **`Stage.LevelId` is the next thing to break.** A stage belongs to exactly one level, but the
  new text moves stages between years ("les stages du 7e glissent vers le 6e"), so one `Stage` row
  needs two levels. `CurriculumStage` already expresses `(version, level) → stage`; `Stage.LevelId`
  should become advisory. Deferred deliberately — it is the same problem as the semester gap below.
- **Recording a text** — `Cnpn/Manage/`: create, correct, delete, and « X reprend Y » (clone every
  level of one text from another in one act, skipping levels the target already has or that fall
  outside its span). Two guards worth knowing: a code is unique *per programme*, and **two texts of
  one programme cannot claim the same intake year** — version selection resolves "the latest intake
  at or before entry" and a tie has no defensible winner. `TotalYears` cannot be shortened below a
  level that already carries requirements. `AcademicProgram` is not editable: curricula and student
  stamps hang off the row.
- ⚠ **Deleting a text is gated on students, not on curricula.** `Users → CnpnVersions` is `NO ACTION`
  (a raw FK violation, i.e. a 500) and `Curriculums → CnpnVersions` is `CASCADE` (silent destruction
  of authored requirement sets). So `DeleteCnpnVersionCommand` refuses outright while any student is
  stamped — including an *inferred* stamp — and otherwise reports how many requirement sets the
  cascade took, so the confirmation can name the number. The justification for allowing the cascade
  at all is the gate: **a text nobody follows has nobody who could owe anything**, so removing its
  requirements strands no obligation. Deletion is for the mistyped row; a superseded arrêté stays,
  because the students who followed it stay.
  - The UI disables the control when `studentCount > 0` rather than letting it fail, and warns that
    removing the only text governing an intake sends new registrations to the previous one.
- ⚠ **`Stage.Coefficient` / `Stage.DurationInDays` duplicate `CurriculumStage`'s — and as of
  2026-09-01 they disagree on real rows.** The catalogue carries its own weight *and* every text
  carries one for the same stage. They agreed only because the history reconstruction seeded one from
  the other, and **1650.25 is the first text to reweight a stage**: MED3 Chirurgie and Médecine read
  coefficient **3** in the catalogue and **1** in 1650.25, **30 j.o.** in the catalogue and **66** in
  2174.18's set. Neither number is wrong *of its text* — a 5ᵉ année student revalidating a 3ᵉ année
  credit is still under 2174.18, which is why the alignment migration recorded 66 there before
  overwriting the catalogue. What was wrong is that the **Stages page showed the catalogue value**
  unqualified — a number no CNPN necessarily states, with nothing on screen saying a text disagreed.
  - **Closed for the display half** (`StageCatalogueFigure`, 2026-09-01): the row now carries
    `TextFigures` — every text's own coefficient and duration — and the cell marks the figure and
    names each text only **when one disagrees**. Silent when they agree and silent when no text
    mentions the stage: a marker that fires whatever the data says is noise, and noise is dismissed,
    which puts the real one out of sight. Same rule as `ExportNotes`.
  - ⚠ **Read by a second flat query keyed on the page's stage ids**
    (`GetStagesQueryHandler.TextFiguresQuery`), never as a collection inside the row projection —
    that element carries no key and is the shape Npgsql refuses. Pinned by `SqlTranslationTests`.
  - The *substantive* half — which of the two numbers is authoritative — is still 15.1's.
  - ⚠ **…and no number is *applied* anywhere.** Measured 2026-09-01: nothing on the revalidation,
    dossier, progression or export path reads `DurationInDays` at all — neither the catalogue's nor
    the text's — **except in the revalidation dialog, closed 2026-09-01**. So the 92 6ᵉ année
    students who owe MED3 Chirurgie under 2174.18 owe **66 j.o.**, that figure is recorded and now
    visible, and no screen proposes it. The one such window on record ran **65 j.o.**, matching 66
    and not the catalogue's 30 — so the catalogue is the wrong default precisely where it would be
    reached for. When a duration is eventually proposed it must resolve through the registration's
    own text (`r.CnpnVersionId ?? r.Student.CnpnVersionId`), like every other CNPN read.
  - **`GetRevalidationContextQuery` is that read**, and it is the one place a duration is now
    resolved from the governing text. It lays the proposed window with `WorkingDayCalendar.Lay`
    and returns **null when the text states nothing** — absence is not zero, and a proposal
    invented from the catalogue would be indistinguishable from one somebody authored.
  - ⚠ **Proposed, never imposed.** The command still writes the dates it is given; a retake
    shortened by agreement stays possible. Closing the gap meant removing the *silence*, not
    adding a guard.
  - **`RevalidationPlanner` is shared by the preview and the command**, so `CanOpen` is decided by
    the rules that would refuse the act — the same guarantee `CnpnTargetPlanner` and the évaluation
    import make. A dialog offering an act the command then refuses is worse than no dialog. The Stages page is also not year- or CNPN-scoped at all: it is the timeless
  catalogue, so switching the navbar year changes nothing there. Resolve with Phase 15.1.
⚠ **Open, and deliberately parked: when a 1650.25 student starts revalidating.** Under 2174.18 the
6ᵉ *and* 7ᵉ années are stage years with no year exam, the 7ᵉ being final; under 1650.25 there is only
the 6ᵉ, and it is final. PGSH treats « dernière année » as `level.Year == TotalYears` per student
(`FinalYearTest`), which reproduces the old text's behaviour on the new one — the deliberate holding
pattern until the faculty states the rule. **Nothing hard-codes 7 anywhere**, so closing this is a
change to one test, not a hunt. Not urgent: the first 1650.25 promotion is in its 3ᵉ année, so the
question does not bite for two years. ⚠ Do not "helpfully" invent a rule here in the meantime — a
revalidation window opened under the wrong text is indistinguishable from one somebody authored.

- ⚠ **Still year-based, and shouldn't be:** the new CNPN organises 12 *semesters* with typed
  placements (immersion / nursing / part-time clinical / full-time / family medicine) and credits
  (10 per semester S5–S8, 20 for S9–S10, 30 for S11–S12). PGSH models year-levels and a free
  coefficient. Recording 1650.25's requirements is an approximation until that gap is closed.
