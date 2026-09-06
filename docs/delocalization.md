# Délocalisation — a stage served outside the faculty

> Read before touching `InternshipAssignment.Delocalize`, the bulk act, `Service.IsExternal`, or
> anything that counts how many students stand in a service.
>
> Written 2026-09-06, when the single-student act became a mass one.

## What it is, and what it is not

A délocalisation says: **this student served this whole stage somewhere PGSH does not supervise.**
The in-faculty rotation never happened, so the periods it produced are dropped and replaced by one
ad-hoc period at an external service, created already started and complete — the stage is over by the
time anybody records it. The verdict arrives on paper and scolarité enters it.

It is **not** a transfer (that moves a student between rosters or cohortes, both of ours) and **not**
a délocalisation of the *service* (the service is a real row in the catalogue; what is outside is the
place). The three are separate acts on purpose: only this one produces a period nobody here started,
closed, or evaluated.

⚠ **It is per stage, not per student.** « Le G3 part à Kénitra » means for *this* stage. The same
students keep their normal rotation on every other stage of the year.

## `Service.IsExternal` — the one lever

A place the faculty does not run — a CHU in another region, a private clinic, a hospital abroad — held
in the catalogue **only so a délocalisation has something to name**. False on all 148 imported rows,
and an external service is created deliberately, never by a migration.

One flag, three consequences, and they are the reason it exists rather than being inferred from the
name:

| It is excluded from | Enforced by |
|---|---|
| a stage's allowed services | `AddAllowedServiceCommandHandler` refuses it; `RotationArranger` drops it from the pool anyway, for rows authorised before the flag existed |
| a cell of the planning grid | `SetCohortSlotAssignmentCommandHandler` refuses it — ⚠ **25 of the 27 stages authorise no service at all**, so the allowed-services whitelist guards nothing on them and this is the only thing standing between a hand-placed cell and a fictional ceiling |
| the capacity and saturation maths | it never appears in a cell, so `ServiceOccupancyCalculator` never sees it |

And it has **no chef and never will**, so no worklist covers it and no evaluation arrives through the
app. The stage export writes « hors faculté » in its chef column for such a rotation rather than
leaving it blank — ⚠ a blank there reads as a name the export failed to resolve, and sends somebody
looking for it.

⚠ **On the update command the flag is `bool?` and null means *unchanged*.** `UpdateServiceCommand` is
a full replace, so an older client saving an external service without the field would quietly pull it
back into the rotation and into the saturation of every service it borders — with students already
délocalisés standing on it. Every other field on that command assumes the caller holds an opinion;
this one does not.

## ⚠ A délocalisé does not occupy the service he left — and for eight months he did

This is the finding the mass act was built on, and nothing in the app said so.

`ServiceOccupancyCalculator.EntriesQuery` measured a cell's load as `a.Cohort.Assignments.Count` —
**every member of the cohorte**, whether or not he was in the country. A délocalisation drops the
student's periods but deliberately leaves him in his cohorte (that is what lets a cancellation put him
back), so sending sixty students to an external CHU relieved

- the planning grid's saturation,
- `RotationArranger`'s balance, and
- `SchedulePublisher`'s pre-publish guard

by **exactly nothing** — while the service they left was, in fact, sixty lighter. Since the whole
point of a mass délocalisation is that number, the feature would have bought the faculty a spreadsheet
and no relief.

The count is now `Count(x => !x.ServicePeriods.Any(p => p.IsDelocalized))`, and the same arithmetic is
written in five places that must never disagree:

| Where | Why it has to match |
|---|---|
| `ServiceOccupancyCalculator.EntriesQuery` | the authority — the grid, the arranger's balance and the publish guard all read it |
| `GetServiceOccupancyQueryHandler` | the per-service page: a page that measured the load differently from the guard would explain a refusal with a number that never produced it |
| `GetOccupancyReportQueryHandler` | the charge report, same reason |
| `RotationArranger.CohortsQuery` | a student serving his stage outside needs no place in the rotation; weighting the queue by him spreads a cohorte over services to hold people who are not there |
| `GenerateScheduleCommandHandler` | the older per-group scheduler's own occupancy hydration |

⚠ **The grid does not hide the difference, it states it.** `CohortScheduleRow` carries
`StudentCount` (the cohorte's membership, délocalisés included — they are still members) **and**
`DelocalizedCount`. A roster délocalisé en masse shows a full membership beside cells loading nothing,
which reads exactly like a bug unless the screen can say why. Zero is the ordinary case.

⚠ **The cells are never deleted.** A mass délocalisation leaves the planning grid exactly as it was and
simply stops counting the students who left. That is what makes the act reversible: cancelling and
re-publishing restores the rotation, which deleting the cells would not.

## What `Delocalize` refuses — and what it used to refuse

Until 2026-09-06 it refused as soon as **any** period had begun. That reads as the safer rule and it is
not the useful one: a student leaves for the external hospital mid-rotation, and the faculty's dates
are a formality the place he actually goes to does not follow. What comes back is a verdict, not a
schedule.

So a started period is now dropped like a planned one. **An evaluated one is not.** A mark is the
single thing here that nothing puts back — the chef who gave it has no reason to give it again — and
no bulk act may be able to erase one.

| State of the stage | What happens |
|---|---|
| planned, or never planned at all | délocalisé; a missing assignment is created (`DelocalizationAssignmentFactory`) |
| started or interrupted, unmarked | délocalisé, and the preview says how many rotations it deletes |
| already délocalisé | **replaced** — which is what makes a corrected list safe to re-send |
| carrying an evaluation | **refused**, `Delocalizations.OverMark` |

`InternshipAssignment.PreflightDelocalization()` is what the bulk preview reads, and it reads the same
expressions the guard enforces — a preview computed one way and a guard written another is two rules
with nothing to catch them disagreeing.

⚠ **Every field of the preflight reads `ServicePeriod.Evaluation`, so the navigation must be
Included.** An un-Included evaluation is indistinguishable from an absent one, the answer becomes
« rien à perdre » on a stage that carries a mark, and the in-memory suite cannot see the mistake
because it fixes navigations up from the change tracker.

## The dates are the faculty's, and an omission is not an error

Omitted, they are the stage's own window for that promotion — `min(StartDate)` and `max(EndDate)` over
its créneaux (`DelocalizationWindow`). What the student actually does once he is in Kénitra follows
that hospital's calendar and PGSH has no way to learn it; what is recorded is the period the stage
officially occupies, which is what every other read is measured against. Scolarité may override them
when it does know.

⚠ **It refuses rather than inventing a window.** A stage whose grid was never authored has none at all
— the imported years carry 105 626 periods behind zero créneaux — and a fabricated pair of dates would
sit in the dossier looking exactly like a recorded fact. `Delocalizations.NoWindow` asks for them
instead, which is a sentence the operator can act on.

⚠ **The year is the registration's, never the current one.** Scolarité enters last year's papers well
into the next year; resolving « l'année en cours » here would date the stage to a promotion the student
is no longer in, and read its window off the wrong grid.

## The verdict — all three modes, not just « validé »

A délocalisation used to record a pass/fail and nothing else, so a CHU returning a note /20 — or a
fiche ticked objective by objective — had it flattened on the way in and the number the student earned
existed nowhere. `DelocalizationVerdict` carries `Numeric`, `ValidatePeriod` and `ValidateObjectives`,
and its validator states **the same rules** as `CreateServiceEvaluationCommandValidator`: a mode
accepted here and refused there would make a mark enterable by délocalisation and uncorrectable
afterwards.

It goes through `DelocalizationVerdictWriter` → `assignment.SubmitEvaluation`, so the stage note rolls
up and the submission is raised exactly as when a chef enters one. `EvaluatedByUserId` is whoever typed
the paper in — the external chef has no account here, and null would read as an evaluation nobody
entered.

### ⚠ The évaluation canvas already reaches délocalisés — use « stage entier »

The bulk import (`EvaluationImportPlanner`) refuses periods that are **not** closed; a délocalisation is
created closed, and the canvas lists every assignment of the stage and year. So the verdicts of a whole
Kénitra cohort are entered the way every other stage's are: download, fill, upload, duplicates
« Remplace la note déjà enregistrée ». Two limits:

- **the per-période canvas (P1/P2) skips them by design** — a délocalisation has no period number,
  because it follows no grid. Use the stage-entier scope.
- **`ValidateObjectives` is refused by the import** (`ImportEvaluationsCommand`) — there is no column
  per objective in the sheet. Per-objective verdicts are entered one student at a time.

## The mass act

`PreviewBulkDelocalizationQuery` / `ApplyBulkDelocalizationCommand`, both running
`BulkDelocalizationPlanner` **and nothing else** — a preview computed by different code is a preview of
nothing.

**Who goes** (`DelocalizationTargets`) — three ways of naming students, unioned, because that is how
the faculty answers the question: « le G3 au complet, plus ces douze-là, plus la liste du formulaire ».

- `AcademicGroupIds` — ⚠ **by id, never by label.** A partition label repeats in every promotion, and a
  label-scoped act reaches into past years. That is the defect that made publish, auto-arrange and
  close do exactly that.
- `RegistrationIds` — named students.
- `Identifiers` — the paste from the Google Form: a CNE **or** an Apogée per line, lowered on both
  sides and on both columns. Both columns, because the Apogée is the identifier always present and 46%
  of the roll carried no real CNE until the placeholders were cleared. ⚠ A single field left un-lowered
  is a silent miss — `Appogee` was case-sensitive for months.

**It skips what it cannot do, and never silently.** A student whose stage already carries a mark is
refused, named on the report, and the others are still written. Refusing the whole batch was the
alternative and it is worse here: a roster is selected by one id, so a single refused member would
block a promotion the operator cannot edit member by member — and « corrige la liste et recommence » is
how a real list stops being re-run at all.

The eight row states are the vocabulary of the preview: `WillDelocalize`, `WillReplace`,
`WillDropUnderway`, `AlreadyMarked`, `NoRoster`, `NoCohort`, `NotFound`, `WrongYear`. ⚠ `NotFound` and
`WrongYear` are deliberately different answers — « je ne le trouve pas » and « il est en 5ᵉ, pas en 6ᵉ »
are two different corrections — which is why the identifier lookup is *not* scoped by year and the
year comparison happens in memory.

**⚠ The report's rows are capped at 200, and every count is measured before the cap.** A selection is
a whole promotion when the operator asks for one — 933 students on the 3ᵉ MED — and the report is a
*single object*, which is the shape that shipped 4 725 students in one response and took the browser
down. The rows are ordered **refusals first** so the cap can only ever drop lines that need no
decision, `TotalRowCount` / `RowsTruncated` say what was left out, and the counts are stored fields
rather than derived: a number computed off a capped list reads low in silence.

**Two classes, because there are two questions.** `DelocalizationTargetResolver` answers *which
students did the operator mean* — roster ids, registration ids, pasted identifiers, and a row for
every line naming nobody. `BulkDelocalizationPlanner` answers *what would happen to them*. They fail
for unrelated reasons: a typo in a pasted CNE has nothing to do with a stage carrying a mark.

**⚠ What guards it is `ConfirmedCount`, not a checkbox.** The act lands on students nobody typed the
name of. A registration created, transferred into the roster, or evaluated between the preview and the
apply changes what the act does without changing anything the operator saw; a boolean « oui j'ai
vérifié » cannot catch that and the number can. The refusal names both counts.

**⚠ Expect the write to take a while on a whole promotion.** Each student raises
`StudentDelocalizedDomainEvent`, and `ApplicationDbContext` publishes events after the commit, one at a
time, each writing a `History` row. The transaction is quick; the dossier entries appearing afterwards
are progress, not a hang.

## The way back

`CancelDelocalizationCommand` removes the ad-hoc period and returns the student to the répartition.
Where the cohorte still holds its cells, re-publishing restores the in-faculty rotation; where they were
cleared he lands in « non réparti », which is the truth about him.

⚠ **It exists because the bulk act does.** Until 2026-09-06 there was no way back at all, in any
quantity: the ad-hoc period is not a published one, so `RemovePublishedPeriods` deliberately leaves it
alone. A mistake applied to a roster is measured in promotions.

⚠ **Refused once the paper verdict is on record** (`Delocalizations.AlreadyMarked`). That mark is the
only trace the faculty holds of a stage nobody here supervised — no chef to ask again, no attendance
behind it. Correcting it is `AmendEvaluation`'s job; undoing the délocalisation would delete it.

It raises `DelocalizationCancelledDomainEvent` → `HistoryType.DelocalizationCancelled`, its own type
rather than a metadata field: the dossier is read as a sequence of things that happened, and a
cancellation that looked like a délocalisation would leave the timeline saying the student served the
stage abroad twice.

## Who may do any of it

Scolarité, on every route. There is no in-app chef for an external service to scope the act to, so the
check is on **who you are** — `EnsureIsAdministrative`. Left open to any authenticated user, a student
could post their own `registrationId` with a passing verdict and validate their own stage.
