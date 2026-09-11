# Services — capacity, occupancy, the chef, and the chef's worklist

> Read before touching a capacity or admissibility decision, the occupancy maths, who leads a service, or the chef's worklist.
>
> Split out of `CLAUDE.md` on 2026-09-06 — the text is unchanged. See [`CLAUDE.md`](../CLAUDE.md) for the always-on rules and the map of these documents.

## What a chef sees is a slice of his services, and the axis is the period's lifecycle
`Employees/MyServices/`, sliced by `ServicePeriodState` — the domain's own four-way split
(`ServicePeriodLifecycle` — see the shared helpers in [`CLAUDE.md`](../CLAUDE.md)),
not a vocabulary invented for this screen. The four **partition** the periods of a service: every row
falls in exactly one, the counts add up to the whole, and one page of one slice is what the endpoint
returns.

- ⚠ **A published rotation is `IsStarted = false`, and the worklist only ever showed started rows.**
  So "the schedule is published" and "there is no schedule" looked identical from the one screen that
  has to tell them apart: 4MED Pédiatrie 2026-2027 published 898 périodes into six services and its
  chef was shown nothing at all. `Planned` makes them visible; it does not make them actionable
  (`ServicePeriodResponse.State` is what the UI refuses to draw a button on). **Starting is still an
  administrative act** — « Démarrer les affectations » — exactly as closing is.
- ⚠ **The list must be bounded, and the year is the wrong axis to bound it on.** Measured 2026-08-29,
  one chef's two services held **3 220 periods back to 2019**, returned unpaginated and mounted at
  once, which is what took the browser down. Year scoping was tried twice as the fix and blanked live
  worklists both times — an `AcademicYear` record drifts out of step with the dates rotations really
  run on. `IsStarted` / `IsComplete` / "has an evaluation" are facts about the rotation itself, so
  bounding on them can hide nothing that is live. The same 3 220 split 300 / 0 / 683 / 2 237, and only
  the last is an archive.
- **The year narrows on top of that, defaults to the current one, and is never silent**
  (2026-08-30). The state is what makes the list finite; the year is a second axis the chef drives,
  because « quelle année ? » is a real question on every slice — next year's plan, this year's
  evaluations, last year's marks — and not only on the archive.
  - ⚠ **What makes it safe is `OutsideYearCount`, not restraint.** Both previous incidents were
    silent: rows the filter removed left nothing behind to say they had existed, so an empty screen
    and an empty service were indistinguishable. The response now says how many further periods
    *of the slice being shown* the year is holding back, the UI states it, and `AllYears` is one
    click away. A filter that announces what it removed cannot reproduce that failure; a filter kept
    away from live work only postpones it.
  - ⚠ **The year is *read*, never inferred from dates** — `p.InternshipAssignment.Registration.
    AcademicYearId`. The schema already states it and states it **totally**:
    `ServicePeriods.InternshipAssignmentId`, `InternshipAssignments.RegistrationId` and
    `Registrations.AcademicYearId` are all `NOT NULL`, the last behind a `RESTRICT` FK. So the years
    **partition** the périodes by construction — no row outside every year, none in two — which is
    the property a date comparison has to approximate and gets wrong.
  - **Measured 2026-08-30, the two rules disagree on 7 030 of 105 626 périodes (6.7%), and the
    registration is right every time.** 5 043 are 2019-2020 stages that ran into 2020-2021 because
    the year was postponed; 1 841 are 2024-2025 stages finishing after 31 août; 145 the same for
    2023-2024. A date rule cannot tell *a year that ran late* from *the next year's work* — it has
    no fact to tell them apart with, and the registration is that fact.
  - ⚠ **This is what the reported defect actually was.** 41 6ᵉ année Pédiatrie périodes, registered
    2025-2026 and run 08 jul → 08 sep 2026, appeared under 2026-2027 « à évaluer » for a promotion
    with **no** partitioning, no planning and nothing published that year — because a date predicate
    saw them finish eight days into the new one. Their registration always said 2025-2026. Reading it
    does not *fix* the case so much as make it unrepresentable.
  - **Every path that creates a `ServicePeriod` hangs it off the right year already**, which is why
    the read needs no fallback: `SchedulePublisher` and `LateArrivalScheduler` use the registration
    being planned, `RevalidateStageCommand` opens the retake on *the registration the student holds
    now*, and `Delocalize` / `TransferToCohort` keep the assignment they were given. The old worry —
    "a retake carries an old year" — is not a thing the code can produce.
  - ⚠ **Anything unresolvable widens, never empties**: no row flagged `IsCurrent`, or a year id that
    no longer exists, spans every year. That is why the window is resolved here rather than through
    `AcademicYearResolver`, whose contract is to *fail* when no year can be named — right for a
    handler that writes, wrong for the one read that has to survive it. Showing too much is visible
    and recoverable in a click; showing nothing is neither.
  - `AllYears` is the explicit widening the "omitted year means the current one" rule demands, and it
    wins over an explicit `AcademicYearId`: the two together can only come from a caller that has
    just changed its mind.
- **One predicate, `ScopedQuery`, answers both the page and the four counts**, so a badge and its list
  cannot disagree — and the state half of it is `ServicePeriodLifecycle`'s, not this handler's. It is
  `internal static` and named for the usual reason: a query buried in a private async method cannot be
  handed to `ToQueryString()`, and the in-memory provider translates nothing.
- ⚠ **The counts travel with every page, including an empty one.** A bounded list has a failure mode
  the unbounded one did not — landing on an empty slice reads exactly like "this chef has no work" —
  so the client opens on the first slice that has something in it and says where the rest is.
- ⚠ **The search had to move to the server with the pagination.** Filtering the rows the client
  happens to hold answers « aucun étudiant » for anyone sitting on page 3, and nothing distinguishes
  that from a real absence. It narrows the counts too, which turns the badges into an answer to
  "where is this student?" across the four slices.
- ⚠ **`ToPaginatedResponseAsync` clamps a page size of 0 *upward* to 1.** So `?pageSize=0` would
  answer a 50-student window with one student and nothing anywhere saying so; the query resolves a
  non-positive value as "unstated" (`EffectivePageSize`). `[AsParameters]` *does* honour a declared
  default on .NET 9 — measured, and pinned by `ChefWorklistEndpointTests` — but the fallback is the
  handler's own so it does not depend on that, nor on the order the enum is written in.
- **A `SingleService` stage already gives one evaluation for the whole run.** `SchedulePublisher`
  collapses the `kₛ` cells into one `ServicePeriod` spanning them, so the chef sees one row per
  student and marks it once. Verified on the live base 2026-08-29: 4MED Pédiatrie is 898 périodes for
  898 assignments across three windows, and all five 4MED stages are `SingleService`. Nothing extra is
  needed for that promotion; a `PerPeriod` stage genuinely does want one evaluation per column.
- ⚠ **`EmployeeDashboardPage` counted the same way and was worse**: it fetched every *closed* period
  of each chef service — 2 920 rows — to render one number, on the landing page. It reads
  `counts.awaitingEvaluation` now, and — since the worklist defaults to the current year — that badge
  and the list it leads to name the same set. The rule from `PGSH.Frontend/CLAUDE.md` applies
  unchanged: to show a count, ask the server for it.

## A service states its capacity one way, and which way depends on whether it is restricted
`Service.CapacityFor(levelId)` is the single answer; nothing outside the domain should reason about
the two fields separately.

| service | limit in force | load counted against it |
|---|---|---|
| no `ServiceLevelCapacity` rows | `Service.Capacity` | every promotion at once |
| rows, one for this level | that row's quota | **this promotion alone** |
| rows, none for this level | 0 — not admitted | — |

`ServiceLevelCapacity` is keyed `(ServiceId, LevelId)`, and a `Level` is already (programme × année),
so one key expresses "10 first-year Médecine, 15 third-year, no pharmaciens" — the last by omission.

- ⚠ **No rows means the service admits everyone.** That is not "unconfigured", it is a service nobody
  has restricted, and it is what keeps the 148 imported services plannable without a data-entry pass.
  Restriction is an act: the *first* row closes the service to every level without one. Any UI showing
  this must say so — an empty table reads as "nothing set yet" when it means "open".
- ⚠ **Quotas replace `Service.Capacity`, they do not sit under it.** On a restricted service that
  number is **dead data**: a service of 20 granting 10 and 15 will hold 25 and nothing objects. Chosen
  deliberately — the quotas *are* the statement of what the service accepts, and a second ceiling
  silently contradicting them was judged worse than the arithmetic. Consequences: no "quota exceeds
  capacity" validation (it contradicts nothing), and the service form must say the total is ignored
  once a quota exists, or admins keep tuning a number with no effect.
- **The load must be counted the way the limit is written** — per promotion against a quota, across
  all promotions against a total. Mixing them is the bug this table exists to prevent.
- **Only a restricted service can breach a quota.** On an unrestricted one the guard reports the plain
  `CapacityExceeded` — naming a quota nobody authored sends the user hunting for a rule that is not there.
- **`AllowOverCapacity` waives a target, never an admissibility rule** — `SchedulePublisher.
  EnsureIntakeAsync` (renamed from `EnsureCapacityAsync`, which described half of what it does).
  Two rules of different kinds:
  - **Admissibility** (`LevelNotAdmitted`) — the service carries intake rules and none names this
    promotion. Checked **whatever the caller asks for**. Publishing anyway sends students to a
    service that does not take them, which no checkbox makes true.
  - **Occupancy** (`CapacityExceeded`, `LevelCapacityExceeded`) — over the number. Waivable, because
    the number is a target.
  - ⚠ **Why the split had to happen: the override is ticked as a matter of routine.** The base is
    structurally over-subscribed — measured 2026-08-14, **233 of 353 planned cells are over capacity
    (66%), worst 85 against 20** — so one flag governing both meant the hard rule was switched off
    every time it was reached. *A rule enforced only when nobody needs the override is not enforced.*
  - The refusal **says it cannot be forced**, because the checkbox is on screen promising otherwise;
    the checkbox's own description says so too, and is now labelled « dépassement d'**effectif** ».
  - The occupancy lookup is built **only when a number will be read** — with the override on,
    admissibility is answered by the intake rules alone, so splitting the flag did not make the
    common publish do more work than when it skipped everything.
  - ⚠ Still true: all 148 services carry the imported default `Capacity = 20` and **not one quota is
    authored**, so every *capacity* verdict today is measured against a number nobody wrote. That is
    an argument about the soft half only — it is exactly why the soft half stays waivable.
  - ⚠ **…and the predicted collision has arrived on real cells (2026-09-04).** MED3 and MED4 share
    **17 services** on overlapping calendars, so the load in each is the sum of both promotions:
    **all 138 of MED4's (service, créneau) pairs are over capacity, worst 196 against 20**, and
    publishing that promotion will therefore be refused wholesale until the box is ticked. The
    figure is *correct* — verified against an independent boundary sweep, Pneumologie (HMIMV) really
    does hold 126 on 10/10/2026 (56 MED3 Pneumo + 56 MED4 Pneumo + 14 MED3 Médecine). So this is not
    a defect in the occupancy maths; it is the calendar, and it is the first real reason to author
    quotas. `HANDOFF.md` item `0d`.

### …and a service may refuse to be forced — `Service.AllowsOverCapacity`
The number above is a target on every service, and « autoriser le dépassement d'effectif » lifts it.
`AllowsOverCapacity` (**true by default**) is how one service says its number is not a target.
Requested by the faculty 2026-09-06: *« certains chefs de service n'acceptent pas qu'on dépasse leur
effectif »*.

- ⚠ **It exists because the override is ticked as a matter of routine**, which is the same
  measurement that forced the admissibility split: with **233 of 353 planned cells over capacity**, a
  ceiling nothing could make binding was in practice advisory *for every service in the faculty*.
  Making it firm is therefore a decision **per service**, taken by the people the number is about —
  not a stricter default nobody could work under, and not a second global flag.
- **The publish guard now reads three rules, and `allowOverCapacity` is a *request*, not a decision**
  (`SchedulePublisher.EnsureIntakeAsync`): admissibility (never waived) · occupancy on a permissive
  service (waived) · occupancy on a firm one (**not** waived, `Schedule.OverCapacityRefusedByService`).
  Forceability is asked **per cell**, because one publish spans many services and they do not answer
  alike.
- ⚠ **The override no longer means the occupancy half can be skipped.** That shortcut was the cheap
  path the 2026-08-17 split preserved — with the box ticked, no load was ever counted — and under it a
  firm service is *unreachable*, because its number is never read. The lookup is now built over
  exactly the firm services of the call (`ServiceIntakeLookup.FirmServicesAmong`), so a publish
  touching only permissive ones still measures nothing. Several tests publish with
  `allowOverCapacity: true` for no other reason than to hold that shut.
- **Its own error code, not a sentence appended to the other two.** Same reasoning as
  `LevelNotAdmitted`: what a reader needs first is not how far over the cell is but that the control
  on screen will not move it, and a message ending on « cochez « autoriser le dépassement » » sends
  the admin round a loop the service has already closed. The message still distinguishes quota from
  total, because the remedies differ.
- **The aggregate refusal counts the two unforceable halves apart** (`PublishRefusedByIntake` takes
  `notAdmittedCount` *and* `refusedOverrideCount`) and stops offering the checkbox when nothing
  waivable is left. They are fixed in different places — a promotion the service does not take, versus
  a service standing on its number.
- ⚠ **True by default is what makes the migration safe.** The column lands on 148 services no chef has
  been asked about; false would have refused the next publication of every promotion over a
  restriction nobody authored. Same rule at every layer — the entity, both commands (a trailing
  optional `= true`), and `Update`'s endpoint `Request`, where the field is **nullable** so an
  omission reads « le client n'en dit rien » rather than binding to `false`.
- ⚠ **It binds publication, never planning.** `RotationArranger` still balances by `CapacityFor` and
  will fill a firm service past its number; what changes is that the plan cannot then be published.
  That is the right order — a plan is a draft, and refusing to draw one leaves the admin nowhere to
  see the problem.
- **The screen says it before the click**: `SaturatedCellResponse.Forceable` travels on every
  saturation of the planning grid, and the publish dialog names the firm services it is about to be
  refused by. ⚠ It is **not derivable from `Reason`** — the numbers of a firm service and of a
  permissive one are identical and only the service says which — so it is sent, never re-derived on
  the client, for the reason `ServicePeriodResponse.State` is.
- ⚠ **The list marks the rare state only.** A lock beside all 148 rows says nothing; what an admin
  needs to spot is the handful whose number actually binds. Same rule as `ExportNotes`.
- **Not carried onto the charge report** (`Services/OccupancyReport/`), deliberately and for now: that
  page and its printable document would have to agree, and the four places a publish is decided from —
  the fiche, the list, the grid, the two dialogs — are covered. Named here so the gap is a decision
  rather than an oversight.

## ✅ A service can be held out of the rotation entirely (2026-09-07)

`StageAllowedService.PlacementMode` — `Rotation` (the default) or `Reserved`. Reserved means the
rotation never draws it: only a cell somebody pinned puts anybody there. It is how « ces services
sont réservés à ces étudiants » is said about a partner hospital — the GST services at Kénitra, the
HMIMV.

- **It is a third statement about a service, beside the two capacities.** `ServiceOccupancyCalculator`
  says how many are *there*, `ServiceIntakeCalculator` how many are *allowed*, and this says *who may
  be put there at all*. ⚠ It is **not** a capacity of zero: the service takes students, just not ones
  the rotation chose.
- ⚠ **The withheld capacity leaves `totalCapacity` with the service**, so « il manque N places » is
  measured against a smaller ceiling on purpose — and `RotationArrangeResult.ReservedServices` is
  reported beside it, because a promotion losing places in silence is the « say what a blank means »
  defect.
- ⚠ **Do not simulate it with a level quota of 0.** It does drop the service from the arranger's pool
  (`Where(s => s.Capacity > 0)`) while leaving a pin acceptable — and it writes « ce service n'admet
  aucun étudiant de ce niveau », which is false, and reads that way in the occupancy report, the
  charge report and the publish guard for every other promotion.
- ⚠ **And a service cannot be reserved by filling it first.** `RotationArranger` computes
  `saturatedServices` **after** `SaveChangesAsync`, as a report; the tiling weights by
  `CapacityFor(levelId)` and never reads live occupancy. The reservation has to be a declaration.
- `PUT stages/{id}/allowed-services/{serviceId}/placement-mode`, journalised
  `STAGE_SERVICE_PLACEMENT_MODE_SET`. It rewrites nothing already placed — the next arrange is the
  act that reads it, exactly like `Rank`.

## A service's load is not readable one period at a time
`Services/Occupancy/` answers "what does this service actually hold, and when" — the question
`RotationArranger` could only answer as a bare count of saturated services and `SchedulePublisher`
only as a refusal, one service at a time.

- ⚠ **The timeline is segmented at every window boundary, never one row per `StageSlot`.** Nothing
  ties two stages' periods together — a slot is keyed (stage, year, number) — so Chirurgie P1 and
  ANES REA P1 have independent dates and legitimately different lengths. Per-slot rows show each
  slot's own cohorts, while the students standing there on a given morning are the union of every
  window covering that day: **the peak lives in the overlap and a per-slot list never shows it**.
  `OccupancyTimeline` is pure (like `PeriodAxis` and `RotationTiling`) so the boundary arithmetic is
  tested exhaustively; boundaries are `start` and `end + 1`, or back-to-back windows merge.
- **It measures the load exactly as the guard does** — the cohorte's members **minus its
  délocalisés**, cells not `ServicePeriod`s (a plan is worth inspecting before it is published), date
  overlap and no year predicate, and the same `HasLevelRestrictions` branch. A page that explained a
  refusal with a number that never produced it would be worse than no page.
  - ⚠ **It was `Cohort.Assignments.Count` until 2026-09-06, and that counted students who had left.**
    A délocalisation drops the student's périodes but leaves him in his cohorte — which is what lets a
    cancellation put him back — so sending sixty students to an external CHU relieved the grid, the
    arranger's balance and the pre-publish guard by exactly nothing, while the service was in fact
    sixty lighter. The same arithmetic is now written in five places that must never disagree; see
    [`delocalization.md`](delocalization.md).
- The year bound is the year's **dates**, not `AcademicYearId`: two academic years never overlap on
  the calendar, so nothing is lost, and a slot stamped with the wrong year but dated inside this one
  is exactly the drift worth surfacing.
- `GetServiceStagesQuery` is the reverse of `Stage.AllowedServices`, and flags the contradiction
  neither side can see alone: the stage lists the service, the service's quotas exclude the stage's
  promotion, so auto-arrange silently drops it.
- `RotationArranger` drops services that refuse the stage's level *before* building the rotation, and
  weights by `CapacityFor(levelId)`: weighting by `Capacity` hands a service of 40 that accepts 5
  first-years the largest share of the first-year rotation. All refusing → `NoServicesAdmitLevel`,
  because "no services" and "no services *for you*" are different screens.
- **`AddAllowedServiceCommand` refuses a service `IsExternal` outright.** A place the faculty does not
  run is not a rotation candidate: nobody is planned there, it has no chef to run the périodes and no
  ceiling worth measuring. Students reach it through a délocalisation only —
  [`delocalization.md`](delocalization.md).
- **`AddAllowedServiceCommand` refuses a service whose quotas exclude the stage's level**, naming the
  promotions it does take. Caught when the list is authored, not weeks later when auto-arrange skips
  it silently. The Stage page's picker passes `admitsLevelId` so the option never appears.
- The level of a cell is `Cohort.Stage.LevelId` (non-nullable), not `AcademicGroup.LevelId` (nullable).
  ⚠ This inherits the `Stage.LevelId` problem noted in [`cnpn.md`](cnpn.md): when one stage spans two levels,
  quotas will need the same rework.

### …and two findings exist only when every service is read at once
`Services/OccupancyReport/` — `GET services/occupancy-report`, and the «&nbsp;Charge des
services&nbsp;» page + printable document behind it. The per-service read answers « what does *this*
service hold »; nothing answered « which services are the problem », which is the question asked
before publishing a promotion and which opening 148 pages does not answer.

- ⚠ **A service that holds nobody all year is invisible from its own page** — there it looks like a
  service with nothing planned, which is exactly what it is. It is the *other half* of a saturation
  elsewhere: measured on 5MED Psychiatrie, all nine columns went to one service and **two of the five
  were never used**, and the printed répartition was the only place it showed.
- ⚠ **`OccupancyStageRow.ServicesUnused` is the number this report exists for.** A stage listing five
  services and placing everybody in two has an arrangement defect no single service page can produce,
  because the denominator is `Stage.AllowedServices` and the numerator is the cells.
- ⚠ **A filter narrows what is *listed* and what is *attributed*, never the load a saturation is
  measured on.** A service is shared and the ceiling that refuses a publish counts every promotion
  standing in it, so measuring « la 5ᵉ année seule » against the service total prints « ok » for a
  service that is over because of the 3ᵉ — and refuses the publish anyway. `Share` carries the
  filtered half; `PeakStudents` stays the whole load. Same class as reading an omitted year as « all
  of them »: one number quietly standing in for another.
- **A peak is simultaneous presence, never a sum over the year.** Built with the same pure
  `OccupancyTimeline`, so one cohort of 40 passing through a service in three windows is 40, not 120.
  Summed it would be a saturation that never happened and would look exactly like a real one.
- **A month's bar is the peak reached inside it, not its mean.** A month with one saturated week
  reads comfortable on an average, and the week is what somebody has to act on.
- ⚠ **`Saturation` is `null`, never 0, when there is no ceiling to divide by.** 0 sorts as « the
  least saturated », which is exactly wrong for a service admitting nobody — and those sort *first*,
  above even a service at 400 %, because theirs is the refusal publication cannot force.
- **Three flat reads, one for every service.** 148 services × a query each is the shape that made a
  single « Générer le plan » ~700 round trips. `PlacementsQuery` projects the cohort's assignment
  **count** — an aggregate over a navigation, which translates — where a projected collection of
  those assignments is the element with no key Npgsql refuses. Pinned by `SqlTranslationTests`.
- ⚠ **An empty report has two causes calling for opposite acts** — no créneau authored (author an
  axis) or créneaux nobody is in (arrange) — and « 0 étudiant » collapses them into a third reading
  the user arrives at first: that the report is broken. `Notes` separates them, exactly as
  `RepartitionSummary.DeclaredSlotCount` does. **This is the live base's state today** (0 slots, 0
  cells on every year), so it is the screen the user meets first.
- ⚠ **Never print a placement count as an effectif.** « Étudiants placés » was
  `Σ cells (cohort size)` — 11 148 against **933** real 3ᵉ année students, because a student counts
  once per créneau he occupies. Removed from the header (the **peak** is the measure of people) and
  the two table columns renamed « Placements » with a line saying what they count. A number that
  looks like a headcount and is not one is worse than no number.
- ⚠ **A printed document needs `print-color-adjust: exact`, declared on every element.** Browsers
  drop background graphics when printing: the SVG figures survive (`fill` is content) but the year
  strip draws each band as a `background` on an empty span, so the one figure showing *when* a
  service is full came out blank in the PDF and perfect on screen.
- ⚠ **…and `break-inside: avoid` belongs on figures only.** On the section holding the 148-row table
  it does not move the block, it **clips** it — the longest table was the likeliest to lose its tail.
  Tables break across pages, keep their rows whole, and repeat `thead`.
- **`Notes` follows `ExportNotes`' rule** — silent when the data has nothing to say. It names the
  uniform imported `Capacity = 20` only when every open service really does carry the same number,
  because a warning that fires whatever the data says is noise, and noise is dismissed, which puts
  the real one out of sight.

## A service's chef is usually only a string in its description
The Access base named the professor as free text and nothing else — no email, no PPR — so the import
could not create an `Employee` without inventing an identity. Measured 2026-08-09: **140 of 148
services carry `Responsable (source) : Pr.A.Settaf` in `Description`, and 0 have `ServiceChefId` set.**
`ServiceChefSourceNote` in `Domain/Hospitals/` owns the format (the importer writes it, the
répartition reads it) so the two ends cannot drift.

Resolution order is authority order: the tenure open on the planning start date → the sitting chef →
the note. ⚠ Only the first is **dated**, which is what lets a répartition reprinted years later name
the chef it was published under. The note is undated and says who the legacy base last recorded, so
it is flagged (`ChefIsFromSourceNote`) rather than blended in. Linking real chefs is what upgrades
those rows; until then, printing the note beats printing nothing on 95% of the document.

⚠ **…and as of 2026-09-03 the documents read the note *alone*** — `ServiceChefPolicy.InForce`
= `SourceNoteOnly`. The two `ServiceChefAssignment` rows in the base were linked to try
the mechanism out, so resolving them prints a **test account** beside real students. The order above
is still the rule and is still tested; the constant says how much of it is in force. See below.

## ✅ Supprimer un service : ce qui le retient, et ce que la cascade emporte (2026-09-11)
`DeleteServiceCommandHandler` ne gardait **rien** — un commentaire
« *(e.g., Check if students are currently assigned to this service)* » tenait la place de la garde —
et le schéma répondait donc à sa place, de deux façons opposées selon ce qui nommait le service.

- **`CohortSlotAssignment.ServiceId` et `ServicePeriod.ServiceId` sont `RESTRICT`.** La suppression
  remontait en `DbUpdateException` → **500 « Server failure »**, dont le client jette même le
  `detail`. Sur cette base c'est le cas **ordinaire** et non le cas rare : les ~105 000 périodes
  reprises de l'Access suffisent à retenir presque tout service réel, et l'écran ne disait pas que
  le bouton ne marcherait jamais.
- **`ServiceLevelCapacity`, `ServiceChefAssignment` et le rattachement du personnel sont `CASCADE`.**
  Un service libre partait donc avec ses quotas, l'historique de ses chefs et ses employés,
  silencieusement — et l'acte n'était pas audité, donc rien ne pouvait plus dire combien.

Le handler compte maintenant **toutes** les raisons ensemble et refuse en `Conflict` ; ce que la
cascade emporte part au registre (`SERVICE_DELETED`) avant le `SaveChanges`.

⚠ **Les stages qui autorisent le service refusent, au lieu de cascader** — et c'est la seule
différence de forme avec `DeleteStageCommand`. `StageAllowedServices` est en `CASCADE` lui aussi,
mais la ligne joint **deux entités indépendantes** et c'est le *stage* qui survit amputé : elle porte
un `Rank` et un `PlacementMode`, deux décisions humaines, et `ServiceRotationOrder` tient les rangs
pour **contigus depuis 1**. Une ligne retirée par la base laisse un trou, donc un numéro affiché à
côté d'un service qui n'est plus la place qu'il occupe dans la file — un nombre pour deux choses.
Le retrait passe par `ServiceRankWriter`, qui rebase, et c'est là que le refus renvoie.

⚠ **Le conseil du refus dépend de ce qui retient**, parce que les deux situations n'ont pas la même
issue : des cellules et des autorisations se retirent ; des périodes déjà enregistrées, **non** —
elles sont au dossier de l'étudiant. Dire « retirez ces rattachements d'abord » à quelqu'un que
retient l'histoire l'enverrait chercher une manœuvre qui n'existe pas. Il n'y a pas encore d'archivage
d'un service : la seule chose à faire d'un service fermé est de le retirer des listes de services
autorisés pour que plus aucun stage ne l'utilise.

Couvert par `PGSH.Tests/Application/DeleteServiceGuardTests.cs` (la garde, et le constat déposé au
registre) et `PGSH.Tests/Integration/ServiceDeleteEndpointTests.cs` (le **409 avec sa phrase**, qu'un
test de handler ne voit pas).
