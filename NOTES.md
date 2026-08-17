# NOTES.md — Project Familiarization Notes

This file captures accumulated context, domain knowledge, and non-obvious observations built up while working through the PGSH codebase. It is a living document — update it as understanding deepens.

---

## Domain Understanding

### What PGSH actually is

PGSH is the internship management platform for a Moroccan medical faculty (likely FMPR — Faculté de Médecine et de Pharmacie de Rabat, inferred from an old Keycloak realm name `fmpr` found during cleanup). Medical and pharmacy students must complete clinical rotations in hospitals as part of their degree. PGSH manages the full administrative cycle: enrolling students each year, assigning them to groups, planning their rotation schedule through hospital services, tracking attendance, and recording evaluations.

### How the academic year works

Each `AcademicYear` (e.g., "2025-2026") has a set of `AcademicGroups` (e.g., "Group 1 - Rabat Zone", ~20 students each). Students are enrolled via `Registration`, which ties a student to a year, a level, and a group. The group assignment can happen automatically via the `AutoArrangeGroups` operation, which uses `GeographicZone` for geographic clustering.

### How rotations work

A `Stage` defines a rotation type tied to a `Level` (e.g., "Cardiology Rotation, Year 4 Medicine"). It has a duration, coefficient, and objectives (scored criteria). A `Cohort` is a specific instance of a Stage for a particular AcademicGroup in a given year.

The schedule is modelled as a **grid**: rows = cohorts, columns = time periods (StageSlots), cells = service assignments (CohortSlotAssignments). `StageSlot` defines a named time period (P1, P2...) belonging to a Stage **for one academic year** — the window carries concrete dates, and the stage runs again next year over different ones. `CohortSlotAssignment` is a grid cell — it maps one Cohort to one Service for one StageSlot. This mirrors the actual paper scheduling documents used by the faculty.

When an admin **publishes** a cohort's schedule (`POST /cohorts/{id}/publish-schedule`), the system creates `ServicePeriod` records for each student in the cohort × each slot assignment. **The capacity check was removed** — it blocked bulk-publishing multiple cohorts assigned to the same service (after the first cohort published, occupancy exceeded capacity for subsequent cohorts even when the total was valid). The schedule grid UI already shows a red capacity badge as a warning. Unpublishing (`DELETE`) removes all ServicePeriods for that cohort where `CohortSlotAssignmentId != null`.

### The student journey through the system

```
Enrollment:  Student → Registration (year + level + group)
Planning:    AcademicGroup → Cohort (group + stage)
             Stage → StageSlot (time period columns P1, P2, ...)
             Cohort → CohortSlotAssignment (service per slot — the grid cell)
Assignment:  Registration → InternshipAssignment (student in cohort)
Publishing:  POST /cohorts/{id}/publish-schedule → creates ServicePeriods
Execution:   InternshipAssignment → ServicePeriod (at a specific service, real dates)
             ServicePeriod → AttendanceRecord (daily presence)
             ServicePeriod → ServiceEvaluation → ObjectiveScores (graded)
Result:      InternshipAssignment.FinalScore (cached aggregate of scores)
```

### Key terminology

| Term | Meaning |
|---|---|
| Stage | A type of hospital rotation (e.g., "Chirurgie S6") |
| Cohort | A group doing a specific stage together in a given year |
| StageSlot | Time period column (P1, P2...) in the schedule grid — belongs to a Stage **and an academic year** |
| CohortSlotAssignment | Grid cell — maps one Cohort to one Service in one StageSlot |
| Period | `ServicePeriod` — actual execution record for a student in a service |
| Assignment | `InternshipAssignment` — one student enrolled in one cohort |
| Level | Academic year of study. Médecine ran to 7 under arrêté 2174.18 and runs to 6 under 1650.25 |
| CnpnVersion | One ministerial text (arrêté). Decides how many years a programme lasts and, via Curriculum, what each level owes |
| Curriculum | What one CnpnVersion requires of one Level — keyed on the text, never on the year |
| CNE | Code National de l'Étudiant — unique national student ID |
| Appogee | University software student number (second unique ID) |
| PPR | Employee government registration number |
| PV | Procès-Verbal — official document (used for employee signature dates) |
| Grade | Academic rank: `MC` (Maître de Conférences), `PES` (Professeur Enseignement Supérieur), `PH` (Praticien Hospitalier), Nurse, Administrator |
| ServiceChef | Head of a hospital department; evaluates students in that service |
| GeographicZone | Field on AcademicGroup used by the auto-arrange clustering algorithm |
| Revalidation | A student retaking a stage they previously failed (`NonValidé`). Tracked as a new `InternshipAssignment` + `History(Revalidation)`. The old failed assignment is never deleted. |

---

## Business Rules Discovered

These are rules enforced in domain or application code — not always obvious from the schema alone.

- **One registration per year**: A student cannot have two `Registration` records for the same `AcademicYearId`. Enforced in `Student.AddRegistration` and `CreateRegistrationCommandHandler`.
- **Validated registrations are locked**: `Student.RemoveRegistration` blocks deletion if `Status == RegistrationStatus.Validated`. Updates are allowed.
- **Program mismatch blocked**: A registration's `LevelId` must reference a level whose `AcademicProgram` matches the student's `AcademicProgram`. Enforced in `Create` and `Update` handlers. Error: `RegistrationErrors.ProgramMismatch`.
- **Chronological consistency enforced**: For any two registrations of the same student, the one with the higher level must belong to the later academic year (compared by `AcademicYear.StartDate`). Repeating a year (same level in two years) is allowed. Violation returns `RegistrationErrors.ChronologicalInconsistency`. Checked in `Create` and `Update` handlers; update excludes the registration being edited from the comparison set.
- **ServiceChef must be staff first**: `Service.AssignChef` throws if the employee isn't already in `Staff`, and checks `Position == Position.ServiceChef`.
- **Unique student identifiers**: CNE is hard-unique (index, never null). Appogee is unique with a nullable filter (some students may not have one yet).
- **Group uniqueness is year-scoped**: `(AcademicYearId, GroupNumber)` and `(AcademicYearId, Label)` are both unique — same group numbers can appear in different years.
- **Level uniqueness**: `(Year, AcademicProgram)` is unique — you can't have two "Year 4 Medicine" levels.
- **Attendance uniqueness**: `(ServicePeriodId, Date)` is unique — one attendance record per student per day per rotation.
- **FinalScore is a cache**: `InternshipAssignment.FinalScore` is described as "stored, not authoritative" — it must be recomputed from `ObjectiveScore.Score × StageObjective.Weight`. Currently never written. (Phase 7 item.)
- **Ad-hoc vs. planned periods**: `ServicePeriod.CohortSlotAssignmentId` is NULL for ad-hoc rotations (created outside the published schedule), non-null for schedule-driven ones (created by publish-schedule).

- **"Valider" means RATIFY, not pass** *(clarified by the user 2026-08-07 — this is the single most
  misreadable term in the domain)*: admin *Valider* on Suivi des affectations **officialises the professor's
  evaluation whatever verdict it carries**. It does not decide that the student passed. Two concepts must stay
  separate on `InternshipAssignment`:
  - **`Status`** = workflow state — who has signed off (`Planned → Ongoing → Completed → Evaluated →
    Validated/Rejected`). Moved by `Validate()` / `Reject()`.
  - **`Result`** (`StageAssignmentResult`) = academic outcome — written **only** by `RecomputeFinalScore` from
    the marks, never by an administrative action.
  Ratifying a stage the chef failed records an *official failure*. `Reject()` symmetrically means "I refuse to
  officialise this evaluation" (contested, wrong student, incomplete) — it does not retroactively fail anyone;
  the marks are corrected by amending the evaluation. Anything gating on "the student passed" checks `Result`;
  anything gating on "it is official" checks `Status`. (Before 2026-08-07 `Validate()` set `Result = Validé`
  unconditionally, so ratifying a 6/20 flipped the student to passed while `FinalScore` still read 6.)
- **Bulk ratification only touches fully evaluated assignments**: `ValidateCohortAssignmentsCommandHandler`
  filters `Status == Evaluated`, which is reached only when every non-interrupted period has an evaluation — so
  it can never validate a student the chef has not evaluated. The skip is **silent**, though: 20 students with 3
  evaluated reports "3 validés" and says nothing about the other 17.
- **No two periods of the same academic LEVEL may overlap** *(added 2026-08-06, `SlotOverlapGuard`)*: a level's
  students follow every one of its stages, so overlapping windows — whether inside one stage or across two of
  the same level — would put a group in two services on the same day. Different levels may share dates freely.
  Windows are **inclusive of both ends**, so a period ending 31/03 and one starting 31/03 collide; the next must
  start 01/04. Enforced on both create and update of a `StageSlot`. Error: `StageErrors.SlotOverlap`.
  - ⚠ **Superseded 2026-08-08 — the rule above is now per-STAGE, not per-level.** It used to refuse any two
    overlapping windows anywhere in a level, which contradicted the faculty's own published planning: in
    `Med3.png` Médecine and Chirurgie run the **same** windows, partition A in one and B in the other. That is
    the whole point of partitioning — it halves the load on every service — and the old rule made the published
    layout unauthorable. See *No group in two services at once* below for what replaced it.
- **The chef worklist is never year-scoped by default** *(learned the hard way — two live incidents)*: a chef's
  list is "what is live in my services", expressed by `IsStarted`. Any implicit scoping to the current academic
  year couples live work to a bookkeeping record that drifts out of step with the dates rotations actually run
  on — and when it drifts, the chef silently sees nothing at all. `GetMyServicePeriodsQuery.AcademicYearId` is
  **opt-in**; an unknown year id leaves the list unscoped rather than empty.
- **Reading presence is wider than recording it**: attendance may be **recorded** by an administrative user or
  the chef/staff of the period's service; it may be **read** by all of those *plus the student whose own
  rotation it is* (`ExecutionAuthorizer.EnsureCanReadAttendanceAsync`). Consulting your own attendance is not a
  privileged act — gating the read behind the write scope made the student portal 403 on every stage it showed.
- **Non-validated stages stay with the student (revalidation)**: A student who receives `Result = NonValidé` on a stage does **not** stop progressing. They continue through subsequent academic years normally. The failed stage "sticks" with them — the original `InternshipAssignment` with `Result = NonValidé` remains in the system as a permanent record. At some later year (could be final year), the student is assigned to a cohort doing that same stage again and receives a new `InternshipAssignment` for that attempt. If they pass, that new assignment gets `Result = Validé`. The old failed assignment is never deleted or modified — it is the audit trail. A `History` record of type `Revalidation` marks when this process begins. **Implication:** a student can have multiple `InternshipAssignment` records for the same `Stage` across different academic years. Queries that check "has the student passed Stage X" must look for any `InternshipAssignment` where the `Cohort.StageId == X` and `Result == Validé`, not just the most recent one. **Why the retake can be years later** *(confirmed 2026-08-07)*: a stage is **not necessarily a criterion for failing the whole registration** — a student can fail a stage, pass the year on their subjects, and carry the unvalidated stage forward. So revalidation must stay flexible across levels, not just across years at the same level. `GetStudentLevelDossierQuery` answers "what is still owed at level L" and `RevalidateStageCommand` re-opens it; the latter deliberately does **not** constrain the stage to the registration's own level, because the prior failed attempt is the real constraint.

---

## Current State of Each Layer

### Domain — Clean
All orphan models removed. Entities correctly model the domain with proper navigation properties. The main gap is that most entities outside the User/Registration/Student cluster are plain POCOs (no domain methods, no business logic encapsulated). Domain events are only raised on Registration operations.

### Infrastructure — Clean
EF configurations are complete and well-structured. All relationships are explicitly configured. The 10 new indexes are in place. `PermissionProvider` is a stub. The Aspire connection name `"TodoDatabase"` is a legacy artifact from scaffolding — it has no functional meaning and refers to the main PostgreSQL database.

### Application — Complete through Phase 7
- **Hospital/Center/Service**: Full CRUD + GetById (returns chef, staff list, hospital city/GPS). `GetServiceByIdResponse` includes `HospitalCity`, `HospitalDescription`, `Latitude` (y), `Longitude` (x).
- **Academic**: AcademicYear (GetMany, Create), AcademicGroup (GetMany, GetById, Update — now includes `RotationGroup`, Delete, **EmptyGroup**, TransferStudent, AutoArrange), Level (full CRUD). `DeleteGroup` guards against both active cohorts AND assigned registrations (returns `Error.Conflict` for each). `EmptyGroup` (`DELETE /groups/{id}/students`) sets `AcademicGroupId = null` on all registrations in the group, returning the count of students unassigned. `AcademicGroup.RotationGroup` is a persistent partition label (A, B, C…) used by auto-arrange to ensure consistent rotation across all stages of the year; also exposed via `GetMany`, `GetById`, `Update`, and `GetStageSchedule` responses.
- **Student/Registration**: Full CRUD complete. Bulk registration works. History read works.
- **Stage/Cohort**: Full CRUD + AssignStudents + AssignAllByStage + Bulk (Start/Complete/Validate). Schedule grid (GetStageSchedule, slot CRUD, assignment CRUD). Publish/Unpublish. Auto-arrange (`AutoArrangeStageScheduleCommandHandler`) — capacity-proportional cyclic rotation (see dedicated section below).
- **InternshipAssignment**: GetMany, GetById, Start, Validate, Reject.
- **ServicePeriod**: GetMany, Complete, GenerateAttendance.
- **Attendance**: GetByPeriod, Record.
- **ServiceEvaluation**: GetByPeriod, Create. (Update handler exists in Application but no endpoint yet.)
- **Employee**: Full CRUD + GetCurrent + service staff ops (AssignStaff, RemoveStaff, AssignChef, RemoveChef).
- **Users**: GetById and GetByEmail work. `UserRegisteredDomainEventHandler` is an empty placeholder.

### API — Clean
All endpoints follow the `IEndpoint` pattern. All dead files removed. All routes consistent (no leading slashes, no `/api/` prefix in Created URLs). Enum fields in request types use proper enum types — no `int` casts anywhere. `GlobalExceptionHandler` catches all `DomainException` subclasses generically. `CustomResults.Problem` correctly maps all error types to HTTP status codes.

### SharedKernel — Clean
All types are correct and concise. `BulkResponse<TId, TResponse>` is well-designed for partial-success scenarios (used by bulk registration).

---

## Frontend Architecture Notes

### RTK Query cache tags — known gotchas
- `assignStudentsToCohort` and `assignAllStudentsByStage` both invalidate `Stage.cohorts-{stageId}` in addition to `Assignment.LIST`. Without this, `CohortResponse.studentAssignmentCount` stays stale (0) after assigning students, which hid the "Publier toutes" button.
- `publishSchedule` and `unpublishSchedule` invalidate `Stage.schedule-{stageId}`, `Stage.cohorts-{stageId}`, `Stage.cohort-detail-{cohortId}`, and `Assignment.LIST`.
- `autoArrangeStageSchedule` now accepts `{ stageId, partitionCount? }` and invalidates `Stage.schedule-{stageId}` + `Level.GROUPS` (since it may write `RotationGroup` labels onto `AcademicGroup` records for the first time).

### Global AcademicYear context
`AcademicYearContext` (`src/features/admin/contexts/AcademicYearContext.tsx`) wraps `AdminLayout`. All admin pages access `useAcademicYear()` to get `currentYearId` / `currentYear` / `setCurrentYearId`. The selector in the header updates this globally. `StageDetailPage` and `AssignmentsPage` sync their local year filter from this context via `useEffect`.

### ServiceDetailPage (student)
Route: `/student/services/:serviceId`. Loaded lazily in `routes/index.tsx`. Entry point: clicking a service name in `PeriodCard` inside `StageDetailsPage`. Uses `GET /services/{id}` which returns `hospitalCity`, `hospitalDescription`, `latitude` (Hospital.LocalisationMaps.y), `longitude` (Hospital.LocalisationMaps.x). Layout: back-nav + title → stats chips row (serviceType, capacity, chef badge, staff count) → full-width 360 px OpenStreetMap iframe embed with overlaid "Ouvrir dans OpenStreetMap" link button → hospital address strip (name + city + coordinates) → two-column grid: left = description card + chef card (teal-tinted panel with initials avatar), right = staff list (per-member rows with grade badge and PPR). Falls back to a placeholder when coordinates are missing.

---

## Non-Obvious Technical Observations

### TPH means StudentId == UserId
The `User` table uses Table-Per-Hierarchy (TPH) with a `UserType` discriminator. A student's `Guid Id` is the same as their `User.Id`. There is no separate `Students.Id` column — it's the inherited PK. Querying `dbContext.Students` hits the `Users` table with `WHERE UserType = 'Student'`.

### UserContext.SyncAsync links Keycloak to DB on first login
When a user logs in for the first time, `SyncUserMiddleware` calls `UserContext.SyncAsync`. This:
1. Tries to find the user by `IdentityProviderId` (Keycloak `sub` claim)
2. Falls back to email if no match
3. Calls `user.LinkIdentity(keycloakId)` to store the link
4. Throws `UserProfileNotFoundException` → 403 if neither matches

This means: **a local `User` record must exist before anyone can log in**. The seeder in `MigrationService` creates initial users. In production this needs an admin flow to create profiles.

### Execution authorization — a chef controls only his own services
The internship-execution writes are scoped per service chef so "each one controls only what he has":
- `ExecutionAuthorizer` (`Application/Employees/MyServices/`, DI-scoped) is the single rule: an action on a `ServicePeriod` (or its evaluation) is allowed only for an **administrative role** (`Roles.Administrative` = Scolarite/Secretaire/SuperUser) **or the chef of that period's service** (`Service.ServiceChefId == IUserContext.UserId`). Otherwise `StageErrors.NotServiceChef` (the new `ErrorType.Forbidden` → HTTP 403; added to `Error`, `ErrorType`, and `CustomResults`).
- Enforced in `CompleteServicePeriodCommandHandler`, `CreateServiceEvaluationCommandHandler`, `UpdateServiceEvaluationCommandHandler`.
- `IUserContext` gained `bool IsInRole(string)` (impl. delegates to `ClaimsPrincipal.IsInRole`; realm roles arrive as `ClaimTypes.Role` via `KeycloakRoleTransformer`). Role name constants live in `Application/Abstractions/Authentication/Roles.cs` (mirror of the frontend `common/constants/roles.ts`).
- The chef's worklist read is also scoped: `GetMyServicePeriodsQuery` (`GET /employees/me/service-periods`) derives the chef's services server-side from the identity — a `serviceId` the caller doesn't lead is ignored, so a chef can never read another chef's periods. The generic `GET /service-periods` (admin) and the student-facing `GET /service-periods/{id}/evaluation` (read-only) are unchanged.
- **Frontend**: `EmployeeServicesPage` calls `/employees/me/service-periods`; active rotations have a **"Terminer"** action (`PUT /service-periods/{id}/complete`, now chef-enforced) so a chef can complete a rotation → then **Évaluer** (submit the note). Secrétaire absences already work through the admin `AttendancePage` (its `AuthGuard` includes `Secretaire`).

### CohortMembership tracks transfer history, not current cohort
`InternshipAssignment.CurrentCohortId` is the FK for the *current* cohort. `CohortMembership` is a history table — it records every cohort a student has ever been in, with start/end dates and transfer reason. A null `EndDate` means they're currently in that cohort. This enables tracking cohort transfers mid-rotation.

### GenerateScheduleCommandHandler creates Cohorts and CohortSlotAssignments
Located at `Application/AcademicGroups/Manage/Schedule/GenerateScheduleCommandHandler.cs`. This operation:
1. Creates `StageSlot` records per-Stage (find-or-create, saves to get IDs)
2. Creates `Cohort` records per AcademicGroup per Stage
3. Creates `CohortSlotAssignment` grid cells (one per Cohort × StageSlot, assigning services by rotation)
It does **not** create `ServicePeriod` records — that is done later by `POST /cohorts/{id}/publish-schedule`.

### Seeder scale and safety patterns
The `MigrationService/Seeder.cs` generates ~5700 students (Med: 7 promos × 600, Pharma: 6 promos × 250). Three critical patterns:

1. **UniqueIndex-based identifiers** — `f.UniqueIndex` in Bogus is a sequential counter (per `Faker` instance) that guarantees uniqueness. CIN uses `$"MA{f.UniqueIndex:D6}"`, CNE uses `$"G{f.UniqueIndex:D9}"`, Appogee uses `(20_000_000 + f.UniqueIndex).ToString()`. Using `f.Random.Number(range)` is dangerous at this scale: with 5700 students and 6 M possible Appogee values the birthday-problem collision probability is ~93% per run.

2. **Batch inserts with ChangeTracker.Clear()** — students and registrations are inserted in batches of 500, with `ChangeTracker.Clear()` after each `SaveChangesAsync`. Without clearing, all previously tracked entities accumulate in the change tracker across seeder steps, making each subsequent `SaveChangesAsync` progressively slower (and eventually OOM).

3. **`SeedShowcaseServiceAsync`** — runs after `SeedCentersAsync`, before `SeedStagesAsync`. Sets GPS coordinates on Hôpital Ibn Sina (Rabat: lon=-6.8498, lat=34.0167), updates Youssef Alaoui to Position.ServiceChef with PPR "PHC-10042", creates 3 staff members (Karim/Bensouda PH, Sara/El Ouafi MC, Omar/Tahiri PES), assigns them to the Cardiologie service. Guard: `if (await context.Hospitals.AnyAsync(h => h.LocalisationMaps != null, ct)) return;`

### GetStudentsQueryHandler filters CNE, Appogee, CIN as exact match
These are not partial-text filters — they use `==` equality. The `SearchTerm` filter does a `Contains` on FirstName, LastName, Email. This is intentional: CNE/Appogee/CIN are precise identifiers.

### The `int? AcademicProgram` bug in GetLevelsQuery is now fixed
Previously the query accepted `int?` and the handler cast it: `(int)l.AcademicProgram == request.AcademicProgram`. This was type-unsafe and broke JSON deserialization from string enums. Now correctly typed as `AcademicProgram?`.

### Planning services (shared, partition + window scoped)
The complex planning operations were extracted into DI-registered services under
`Application/Stages/Planning/` so command handlers and the macro orchestrator share one source of truth:
- `PartitionAllocator` (static) — the A/B/C labelling rule. Used by `AssignRotationGroupsCommandHandler` and `RotationArranger`.
- `RotationArranger` — the cyclic rotation below, **scoped to optional partition labels + period numbers**. Removal of prior cells is restricted to `targetCohortIds ∩ targetSlotIds`, so arranging one partition's window never wipes another's. **The cyclic shift is anchored to the targeted cohort set's *participation footprint* — the slots they already occupy in this stage ∪ the slots being arranged now (`phaseBySlotId`), step = `n / footprintLength`.** This single rule is correct for both planning paths: (a) the **macro matrix** (a partition runs one window, e.g. A→P1-2) keeps footprint = that window → the clean half-cycle swap is preserved (no regression); (b) **adding new periods to an already-arranged set** grows the footprint to include them → the new periods get fresh phases and *continue* the rotation instead of repeating services the cohorts already did. All cohorts of the stage participate in the queue (not just unpublished ones) so the cycle stays consistent; only individual **published cells** (a `ServicePeriod` points at them) are protected — never deleted nor rewritten — letting a started stage keep its history while its new periods are arranged. Backs `AutoArrangeStageScheduleCommand`.
- `StudentAffectationService` — affectation per-cohort or per-stage (optionally partition-filtered). Backs `AssignStudentsToCohort` + `AssignAllStudentsByStage`. `BulkResponse.TotalProcessed` now reports eligible registrations considered (not just newly created).
- `SchedulePublisher` — `ServicePeriod` generation per-cohort (strict) or per-stage+partition+window (lenient/idempotent). Backs `PublishCohortScheduleCommand` + new `PublishStageScheduleCommand`.
- `CohortProvisioner` — idempotent cohort creation per (partition, stage). Backs `BulkCreateCohortsFromPartitions` + macro plan.
- `ServiceOccupancyCalculator` — global cross-stage service load (`LoadOn(serviceId, start, end)` = students on a service over any overlapping window, all stages). Used by `GetStageScheduleQueryHandler`, `RotationArranger`, and `SchedulePublisher` (see cross-stage capacity note below).

### Stage timeline / calendar (read-only Gantt)
`GetYearTimelineQuery` (`Application/Stages/Timeline/`, endpoint `GET /academic-years/{id}/timeline?levelId=`) returns a `Level → Stage → Partition → Group` tree for the calendar view. Each `TimelinePartition` now carries its `Groups` (`TimelineGroup`: id, label, number, student count) — the academic groups whose cohorts make up that partition in the stage. Frontend `PartitionDrawer` renders each partition as a clickable row (`PartitionRow`) that expands to a grid of its group cards. **A `Stage` has no dates** — every span is *derived* from `StageSlot.StartDate/EndDate`: a stage spans the union of its slots; a partition spans the slots its cohorts occupy. The year is reached via `AcademicGroup.AcademicYearId` (slots/cohorts are not year-stamped). Reuses `ServiceOccupancyCalculator` for the per-partition saturation flag. Frontend: `StageTimelinePage` (`/admin/timeline`, nav "Calendrier") — a **custom CSS Gantt** (date→% offset via dayjs, no Gantt library); year picker, month axis, collapsible level rows, stage bars → partition-window Drawer. Cache tag `Stage/TIMELINE` + `refetchOnMountOrArgChange` (fine-grained invalidation from the ~15 plan-mutations is not wired yet — Phase 7.6 Phase B / robustness).

### Répartition annuelle des stages (the published planning table)
`GetLevelRepartitionQuery` (`Application/Stages/Repartition/`, endpoint
`GET /levels/{levelId}/repartition?academicYearId=`) is the schedule grid **turned a quarter**: the
grid asks "where does this cohort go each period", this asks "which groups does this service hold
each period", across every stage of the level at once. Pure pivot of `CohortSlotAssignment` — no new
modelling, no marks, no execution state.

- **The column axis is the finest partition present.** Stages of one level do not share a period
  length (`Med6`: ANES REA monthly, Chirurgie two-monthly), so `PeriodAxis.Build` drops any window
  that strictly contains another and keeps the atoms. A stage then occupies the columns whose
  **midpoint** falls inside its own window — bare overlap would let a window spilling a few days past
  a boundary seize the next column from the stage that really runs there. A multi-column period
  repeats its cell in each column carrying one `SlotId`, exactly as the published table does.
- **Rows and stages share one sort key: the lowest group number the line opens on.** That single rule
  reproduces both reference images — `Med3` (Médecine 1-40 above Chirurgie 41-80) and `Med6`
  (Chirurgie 1-20, ANES REA 21-30, URGS-TRAUMA 31-40…) — and keeps the rotation cycle readable down
  the page. Neither is alphabetical or by id; don't "fix" it to either.
- **The colour band is the partition of the row's *first* period.** A row visits groups from every
  partition over the year, so only the opening cell says which block of the rotation it is.
- **The chef is resolved as of the first column's start**, from `ServiceChefAssignment`, falling back
  to the sitting chef where no tenure covers it. A répartition reprinted years later keeps naming the
  chef it was published with.
- **It ships as an export, not a public page** — the faculty uploads the file to its own site.
  `RepartitionPage` (admin → Formation → Répartition annuelle) previews it, then serializes **the
  very DOM node on screen** into a standalone `.html` (`buildRepartitionFile`, stylesheet inlined via
  Vite `?raw`) and prints that same file to PDF. Preview, print and upload are one document by
  construction, not three implementations kept in step.
- ⚠ **Nothing renders yet: the database holds 0 `StageSlots` and 0 `CohortSlotAssignments`** (13,604
  cohorts, but no grid). The legacy Access base had no planning grid — see *No periods in legacy*.
  Every level's répartition is empty until periods and cells are authored in `ScheduleGridModal`.
- **Verified end-to-end 2026-08-08** on level 3 (Médecine + Chirurgie, 80 groups each): allowed
  services and 2 periods per stage seeded, then **the real `RotationArranger`** run from the UI —
  320 cells, 26 rows, 4 columns, 12.8 KB self-contained export. The union axis did its job: two
  stages declaring *different* windows merged into one 4-column axis. Partition banding tracked the
  arranger's own A/B allocation, and the 52 unplanned cells were hatched and counted. Non-contiguous
  cells printed as `1, 3, 5, 7, 9, 11` — correctly *not* collapsed to `1-11`.
  Revert script: `scratchpad/revert-repartition-testdata.sql` (slots cascade to cells).
  - ⚠ That table is **half empty by construction**, and the empty half *is* the `SlotOverlapGuard`
    defect made visible: because the two stages may not share date windows, they were forced into
    sequential ones, so Médecine holds P1-P2 and Chirurgie P3-P4. In the published `Med3.png` all
    four columns are full for **both** stages.
- ⚠ **Two things block authoring a real planning**, both found in that pass — see
  `SlotOverlapGuard` above, and the empty-`AllowedServices` note below.

### No group in two services at once (`GroupScheduleConflictGuard`, 2026-08-08)
The rule that actually prevents double-booking, checked **where a group is really placed** rather than
where a date column is merely declared. A `StageSlot` on its own places nobody; only a
`CohortSlotAssignment` puts a group somewhere.

- **The planning model it exists to permit.** For a level with *S* stages, each stage declares
  `periods-per-stage × number-of-partitions` periods over **one shared date axis**, and the partitions
  cross over. Med3: 2 stages, 2 partitions → 4 periods in each stage, A→Médecine P1-P2 + Chirurgie
  P3-P4, B mirrored. Every column is full for every stage, and each service carries only half the
  promotion at a time. `SlotOverlapGuard`'s old level-wide rule made this impossible to author.
- **What it refuses**: assigning a cohort to a slot when *its academic group* already occupies another
  slot whose window overlaps. A group has one cohort per stage, so the conflict is naturally
  cross-stage. Error `Schedule.GroupAlreadyPlaced`, which names the group — that is what lets it tell
  the legitimate case (A in Médecine P1, B in Chirurgie P1, same dates) from the mistake (group 1 in
  both). A windows-only comparison never could.
- **Applied in three places**, because there are three ways to double-book:
  1. `SetCohortSlotAssignmentCommandHandler` — assigning a cohort to a slot.
  2. `RotationArranger` — bulk, one snapshot query via `BuildAsync`, so no N+1. Skips conflicting
     cells and returns `GroupConflicts`; `AutoArrangeResult` and `MacroPlanResult` both carry it and
     both UIs show it, because a run that writes nothing otherwise looks like it had nothing to do.
     `GenerateMacroPlanCommand` inherits the check — both arrange paths go through the arranger.
  3. `UpdateStageSlotCommandHandler` via `EnsureSlotCanMoveAsync` — **dragging a period's dates
     double-books every group already in it without touching a single cell**, and the per-stage
     `SlotOverlapGuard` cannot see that. Missing this was a regression opened by narrowing the
     overlap rule.
  - ⚠ The slots being rewritten are **excluded** from the snapshot (`ignoredSlotIds`), or a second
    arrange of the same stage would see the cells it is about to replace and refuse every one.
  - ⚠ **Conflicts are computed before the stale-cell removal, and conflicting pairs are excluded
    from it.** Deciding afterwards deleted a cohort's existing cell and then declined to write the
    replacement — re-running an arrange across all partitions silently destroyed a correct plan.
    Covered by `A_refused_cell_keeps_the_plan_it_already_had`.
- Changing only the *service* of a cell the group already holds is not a conflict.

### An empty `Stage.AllowedServices` blocks planning — it does not mean "all services"
`RotationArranger` refuses with `Schedule.NoAllowedServices` when the list is empty, so
"Répartition auto." can never run. **25 of 27 stages have zero allowed services** (2 rows in
`StageAllowedServices` for 148 services), which is a large part of why the grid has never been filled.
`StageDetailPage` used to read *"Aucune restriction — tous les services peuvent être utilisés dans la
grille"* and badge it **Tous** — the exact opposite of what the backend does — so an admin had no way
to learn why the button was disabled. Corrected 2026-08-08 to an orange **Aucun** badge and a warning.
The backend is the side that is right: auto-arranging Médecine across all 148 services, Pharmacie and
Réanimation included, is not a sensible default.

### Date pickers (`@mantine/dates`)
`@mantine/dates` + `dayjs` are installed; `@mantine/dates/styles.css` is imported in `main.tsx` and the app is wrapped in `<DatesProvider settings={{ locale: 'fr', firstDayOfWeek: 1 }}>`. Mantine 8 date components use **string `"YYYY-MM-DD"` values** (no `Date` conversion), which matches the backend `DateOnly`. `StageSlot` start/end in `ScheduleGridModal` uses `DatePickerInput type="range"`.

`GenerateMacroPlanCommand` (`Application/Stages/MacroPlan/`, endpoint `POST stages/macro-plan`) fans out per `(RotationGroup, StageId, PeriodNumbers)` entry to those services — one call creates cohorts → affects → arranges → optionally publishes. This is what realises the macro split (e.g. Med3: A→Médecine[1,2]+Chirurgie[3,4], B mirrored). The frontend Macro Plan tab (`GroupsPage`) drives it via a partition×stage matrix with a per-cell period window.

### Auto-arrange algorithm (capacity-proportional cyclic rotation + RotationGroup partitions)
`RotationArranger.ArrangeAsync` (invoked by `AutoArrangeStageScheduleCommand`) uses the same pattern visible in the faculty rotation documents (`example_stage_assignement/`):

**How it works** (over the scoped cohort/slot subset — by default all cohorts/slots of the stage):
1. **Capacity-aware proportional allocation** (largest-remainder method): the weight of a service is `floor(capacity / avgStudents)` = the number of whole average cohorts it can hold. **A service smaller than one cohort gets weight 0 and is excluded** (forcing an atomic group into it would always overflow). N cohort-slots are distributed proportionally to weight, leftover by largest fractional remainder → `allocated[i]` cohorts per service per period, summing to N. When total cohort-capacity ≥ N this never over-fills a service; only a genuine per-period shortfall saturates. (Planning preview, before students are assigned, falls back to raw-capacity proportions.)
2. **Service queue**: build an ordered list `[S1 × allocated[0], S2 × allocated[1], ...]` of length N.
3. **Cyclic shift per period**: period P reads the queue at offset `phase(P) × (N / cycleLength)`, where **`cycleLength` = the targeted cohorts' participation footprint** (the slots they already occupy in this stage ∪ the slots being arranged now) and `phase(P)` = P's position within that ordered footprint — *not* the filtered-window index (which would restart at offset 0 and repeat services) and *not* the full stage-slot count (which would break the macro matrix's single-window half-cycle swap). With no prior assignments the footprint equals the window, so a first-time full arrange behaves exactly as before. Every cohort visits a different service block each period.

**Why this matches the faculty documents:**
- Service "Méd A" takes 2 groups per period: P1=groups 1-2, P2=21-22, P3=41-42, P4=61-62. The offset is 80/4=20. This exact pattern falls out of the cyclic rotation naturally.
- **Capacity is a per-(service × period) constraint, not a global one.** Total allowed-service capacity ≥ total students does NOT guarantee no saturation: groups are placed as atomic ~20-student units, so any allowed service smaller than a group always saturates, and a single period can stack two partitions onto the same service if both are arranged into it (the reason to use period windows — see saturation diagnosis below). `RotationArranger` counts saturated services from the **actual** per-cell load, not the global average.

**RotationGroup / Partition system:**
- `AcademicGroup.RotationGroup` is a persistent nullable string label (A, B, C…). **Partitions are scoped per (AcademicYear, Level)** — different levels can have different partition counts (e.g. 2 in 1Med, 4 in 2Med). `AssignRotationGroupsCommand` takes an optional `LevelId`; a group belongs to a level by `AcademicGroup.LevelId` **and nothing else** (since `SplitAcademicGroupsPerLevel`, 2026-08-13). ⚠ It used to fall back to "has a registration at that level", for legacy rows that carried no `LevelId` — but that also reached « Non réparti », which holds every promotion's unassigned students at once, so cutting one level handed a partition label to a bucket of 4,725 people. `GetAcademicGroupsQuery` keeps the wider reach on purpose: it is the screen those students are assigned *from*. `CohortProvisioner` matches groups to each stage's level so a label reused across levels never produces cross-level cohorts. The label is set once and reused across all stages **of that level**.
- ⚠ **Before that migration one roster served several promotions at once.** The importer keyed rosters on `(ANNEE_UNIV, GROUPE_STG)`, and legacy group numbers restart at 1 per promotion — so the 3rd year's "Groupe 1" and the 5th year's became one row. 80 of the 100 numbered rosters of 2025-2026 carried four or five promotions. Since a roster is the unit `GroupScheduleConflictGuard` forbids from being in two services at once, the 3rd year's placements refused the 5th year's, and one global `RotationGroup` cut per year meant re-cutting any promotion silently re-cut every other. Keyed `(AcademicYearId, LevelId, GroupNumber)` `NULLS NOT DISTINCT` now, and numbering restarts at 1 per promotion.
- `numPartitions` = `existingLabels.Count` if any groups already carry a label; otherwise = `request.PartitionCount ?? services.Count`. This respects the structure set up on a previous stage's auto-arrange.
- Unassigned groups are distributed round-robin into the smallest partition (by current count) in `GroupNumber` order.
- Cohorts are sorted by `(RotationGroup, GroupNumber)` before building the queue. All cohorts in partition A occupy a contiguous block → the cyclic shift moves the entire A-block to a different service section each period, consistent across stages.
- **Frontend**: `ScheduleGridModal` shows a violet dot badge per cohort row and partition filter chips above the grid. The "Répartition auto." dialog targets the active partition chip + a period-window multi-select (sent as `partitionLabels` + `periodNumbers` in the POST body), and shows an orange Alert when the chosen window already holds another partition's cells (stacking guard). The saturation banner lists the real offending cells (`{service} · P{n} : {occupied}/{capacity}`) using the backend's per-cell `occupiedSeats`, not a misleading global total. `GroupsPage` table shows `RotationGroup` badge; `EditGroupModal` has a text input to set/override the label manually.

**Why the greedy approach (previous version) was wrong:**
- The greedy treated each period as an independent assignment race — large cohorts competed for capacity every period, consistently over-filling high-capacity services and under-filling others.
- The `visited` tracking plus capacity competition caused cascading saturation in later periods.

**Capacity is measured GLOBALLY across stages (fixed 2026-06-03, was Phase 7.5 #1):** occupancy is no longer grouped by `(StageSlotId, ServiceId)` within one stage. `ServiceOccupancyCalculator` (`Application/Stages/Planning/`) loads every planned `CohortSlotAssignment` targeting a service and exposes `ServiceOccupancyLookup.LoadOn(serviceId, start, end)` = total students on that service whose slot window **overlaps** `[start,end]` (overlap = `a.Start <= end && start <= a.End`), across **all** stages. This is the single source for the three places load matters:
- `GetStageScheduleQueryHandler` — each cell's `occupiedSeats` shows the real cross-stage load (the macro case: a service shared by partition A in stage X and partition B in stage Y over overlapping dates shows the combined load, not a per-stage half).
- `RotationArranger` — saturation is counted after the save against the global load per `(slot window, service)`.
- `SchedulePublisher.EnsureIntakeAsync` (named `EnsureCapacityAsync` until 2026-08-17) — **pre-publish guard** (previously absent): both `PublishCohortAsync` and `PublishStageAsync` refuse with `StageErrors.CapacityExceeded` if any service would exceed capacity over an overlapping window — **unless `allowOverCapacity` is set**, an explicit opt-in override threaded from the publish commands/endpoints (`PublishCohortScheduleCommand.AllowOverCapacity`, `PublishStageScheduleCommand.AllowOverCapacity`, `GenerateMacroPlanCommand.AllowOverCapacity`). ⚠ Since 2026-08-17 the override waives only the *occupancy* half: `LevelNotAdmitted` is checked whatever the caller asks for, because a service that does not take a promotion is not a target being missed. The frontend exposes it as an « Autoriser le dépassement d’effectif » checkbox in the publish confirm dialogs (per-cohort and "Publier tout"). Distinct cohorts across stages are different physical students, and a group's cohort-in-X vs cohort-in-Y run in non-overlapping windows, so summing overlapping windows does not double-count. Counting `CohortSlotAssignment`s covers both planned and published load (publish keeps the cells); purely ad-hoc `ServicePeriod`s (null `CohortSlotAssignmentId`) are not yet included.

### Long-running requests are not aborted by navigation or other requests
RTK Query mutations ("démarrer", publish, macro plan) each get their own `AbortController` and are independent of queries (student search etc.), so one never cancels another. SPA navigation does not reload, so an in-flight mutation runs to completion server-side (RTK auto-aborts only *queries* when their last subscriber leaves, never mutations). There is no client request timeout. Implication: a slow mutation finishes invisibly in the background — keep handlers idempotent so an accidental re-trigger is safe (Phase 7.5 robustness item).

### Endpoint binding pattern (POST / PUT / GET)

Three patterns coexist intentionally — all are correct:
- **POST**: bind the Command directly from the request body. No wrapper needed when there is no route parameter to merge. Enum fields deserialize as strings thanks to the global JsonStringEnumConverter.
- **PUT**: inner sealed Request record merges the route id with body fields, then constructs the Command. Enum fields use proper enum types, not int.
- **GET list**: [AsParameters] binds the Query record directly from query string parameters.

The previous codebase used int for all enum fields in Request records, forcing manual casts. All eliminated.

---

## Patterns to Follow When Adding New Features

### Adding a new GetMany handler
```csharp
// 1. Build filtered IQueryable with AsNoTracking()
var query = dbContext.Entities.AsNoTracking().AsQueryable();
if (request.Filter.HasValue) query = query.Where(...);

// 2. Use the extension — no manual count/skip/take
var response = await query
    .OrderBy(e => e.Name)
    .ToPaginatedResponseAsync(request.PageNumber, request.PageSize,
        e => new SummaryResponse(...), cancellationToken);

return Result.Success(response);
```

### Adding a new Create handler
```csharp
// Existence check (FK validation)
bool parentExists = await dbContext.Parents.AnyAsync(p => p.Id == request.ParentId, ct);
if (!parentExists) return Result.Failure<int>(Error.NotFound("Parent.NotFound", "..."));

// Uniqueness check
bool duplicate = await dbContext.Entities.AnyAsync(e => e.Name == request.Name, ct);
if (duplicate) return Result.Failure<int>(Error.Conflict("Entity.Duplicate", "..."));

// Map, add, save
var entity = new Entity { ... };
dbContext.Entities.Add(entity);
await dbContext.SaveChangesAsync(ct);
return Result.Success(entity.Id);
```

### Adding a new Update handler
```csharp
var entity = await dbContext.Entities.FirstOrDefaultAsync(e => e.Id == request.Id, ct);
if (entity is null) return Result.Failure(Error.NotFound("Entity.NotFound", "..."));

// Uniqueness: exclude self
bool nameExists = await dbContext.Entities.AnyAsync(
    e => e.Name == request.Name && e.Id != request.Id, ct);
if (nameExists) return Result.Failure(Error.Conflict("Entity.Duplicate", "..."));

// Mutate, save
entity.Name = request.Name;
await dbContext.SaveChangesAsync(ct);
return Result.Success();
```

---

## Legacy Access database (`Medecine.mdb`)

The system PGSH replaces was VB.NET over a Microsoft Access file. **The file is gitignored** (`*.mdb`)
— it carries 10,203 real students' CIN, date of birth, address and a plaintext `password` column, and
must never enter the repository. Read it with `Microsoft.ACE.OLEDB.16.0` (64-bit, already installed);
ADO uses **ANSI-92 wildcards** (`%`, `_`), not Access's `*`/`#`.

**It is a properly normalized schema, not a spreadsheet** — 29 declared foreign keys, composite primary
keys, and zero orphans across `AffectStage→stages`, `AffectStage→SERVICES`, `AffectStage→Inscription`,
`Inscription→ETUDIANT`. The joins are structurally guaranteed; an importer needs no orphan handling.

| Legacy | Rows | PGSH |
|---|---|---|
| `AffectStage` (PK `NUMINS,CODEST,PER1`) | 104,924 | `ServicePeriod` |
| `Inscription` (PK `Numins`) | 43,608 | `Registration` |
| `ETUDIANT` (PK `NO_ORDRE`) | 10,203 | `Student` |
| `SERVICES` / `stages` / `Niveaux` / `anneeuniv` | 148 / 27 / 20 / 21 | `Service` / `Stage` / `Level` / `AcademicYear` |

**Grain mismatch — the key structural fact.** `AffectStage` is at *period* grain. There is no
stage-level row: the internship is implicit in `(NUMINS, CODEST)` — 92,187 single-period, 6,368
two-period, 1 three-period. So one `(NUMINS, CODEST)` → one `InternshipAssignment`, each row → one
`ServicePeriod` + `ServiceEvaluation`, and `FinalScore` is **derived** by `StageScoring`, never imported.

**Repeating students are the norm, and the model already handles them.** 2,920 `(student, level)` pairs
repeat (2,269 ×2, 515 ×3, 136 ×4), concentrated in MED07. `CreateRegistrationCommandHandler` already
allows this deliberately — its duplicate guard is on `(StudentId, AcademicYearId)`, not level, and its
chronological check skips when `levelDiff == 0`. The old app re-created the full stage set each year and
the operator graded only what was genuinely redone, so marks accumulate across registrations. That is
what `GetStudentLevelDossierQuery` folds together.

**Mark semantics — settled with the user 2026-08-07.** `Note >= 10` is validated, `Note < 10` is not
(so `0` is a real failing mark, not a "not applicable" sentinel); `-1` and NULL mean ungraded. That is
exactly `StageScoring.ValidationThreshold`, so an imported row becomes an `EvaluationMode.Numeric`
evaluation with `TotalScore = Note` and the verdict falls out of the existing domain rules untouched.

Consequence: **`Resultat` never needs decoding.** PGSH derives the verdict from the mark, so the
undecodable `{0,3,6,10}` column (which does not correlate with pass/fail — `Resultat=0` holds 71,320
passing *and* 2,744 failing rows) is simply not imported. Rows with `Note` of `-1`/NULL import as
periods with no evaluation, i.e. `NonÉvalué`.

**Other migration hazards.** No email column at all (`Users.Email` is NOT NULL UNIQUE) — `prenom_nom@um5.ac.ma`
yields only 31 collisions over 10,203 names, but the numeric suffix must be allocated in a **deterministic
order** (by `NO_ORDRE`) or re-running the import swaps identities between real people. Names are one
`NOM PRENOM` field, surname first, and 2,730 (27%) have 3+ tokens where the split is genuinely
undecidable. Only 5,510 of 10,203 have a CNE (`Student.CNE` is NOT NULL UNIQUE). `SERVICES` has no
hospital FK — the hospital is embedded in the name string (`"Hôp.IbnSina: Médecine A - Pr.H.Harmouch"`),
~16 parseable prefixes, 10 with none, and `CHEF_SERV` is empty throughout. Dates are `dd/MM/yyyy` text;
824 `PER2` values hold free text like `"31/05/2019 & de: 25/06/2019 à:12/07/2019"` — a split period the
old app could not model and PGSH can, as two `ServicePeriod`s.

### The importer — `PGSH.LegacyImport`

A console tool, run manually, **dry run by default**:

```bash
dotnet run --project PGSH.LegacyImport -- --source Medecine.mdb                    # plan only, no DB needed
dotnet run --project PGSH.LegacyImport -- --source Medecine.mdb --review           # hospital tree, for checking
dotnet run --project PGSH.LegacyImport -- --source Medecine.mdb --connection "…" --apply
```

Split so the rules are testable without the gitignored .mdb: `AccessLegacyReader` (OleDb, Windows-only,
reads verbatim) → `LegacyImportPlanner` (pure, builds the whole graph) → `LegacyImportWriter` (saves in
dependency order, batched). 53 tests cover the mapping; the reader has none by design.

**✅ Applied to the development database 2026-08-07** — 1 min 24 s, counts match the plan exactly.
The fixture data it replaced was backed up first (`pgsh-prefixture-backup-*.dump`, gitignored).

**Maps all 104,924 rows with no failures:**

| | |
|---|---|
| 1 Center / 16 Hospital / 148 Service | hospital tree parsed out of the service name strings |
| 16 Level / 27 Stage / 21 AcademicYear / 1,003 AcademicGroup | |
| 10,203 Student | 4,695 with a synthesised `LEGACY-{NO_ORDRE}` CNE |
| 43,605 Registration / 13,604 Cohort | 3 legacy duplicates dropped, see below |
| 98,555 InternshipAssignment / 105,626 ServicePeriod / 87,092 ServiceEvaluation | 83,439 marks ≥ 10, 3,653 below |
| 727 notes | 702 split periods, 18 students with no registration, 4 services naming no hospital, 3 duplicate registrations |

Counts reconcile exactly: 98,555 = the distinct `(NUMINS, CODEST)` pairs; 105,626 = 104,924 rows + 702
interrupted rotations expanded into a second period.

**Decisions worth knowing before re-running it:**
- **Cohorts and groups are derived, not imported.** Legacy has no cohort concept; `AcademicGroup` comes
  from `(ANNEE_UNIV, GROUPE_STG)` and `Cohort` from `(Stage, AcademicGroup)`. 17,529 registrations carry
  no group — only 67 of those have rotations — so they land in a per-year "Non réparti" group (number 0).
- **`CohortSlotAssignmentId` is always null.** There was no planning grid, and null already means
  "recorded outside the published schedule", which is exactly what history is. No `StageSlot` rows.
- **The lifecycle is replayed, never back-filled.** `Start() → CompletePeriod() → SubmitEvaluation() →
  Validate()`, so `FinalScore`/`Result` come from `StageScoring` like everything else. Fully-marked
  stages are ratified so 100k finished rotations don't sit in the "to ratify" worklist.
- **Registrations import as `Active`** (withdrawals excepted): whether a student passed the *year*
  depends on subject marks, which are out of scope. Inventing a pass or fail would be a lie.
- **No Employee rows are created.** The professor is a name inside the service string; a unique email
  cannot be conjured from "Pr.H.Harmouch". `ServiceChefId` stays null, the name goes in `Description`.
- **One synthetic Center** ("Centre Hospitalier Universitaire de Rabat") because `Hospital.CenterId` is
  required.
- **City and `ServiceType` do not exist in the source** — both are inferred, and `--review` prints the
  whole reconstructed tree so they can be checked in one pass. Cities the legacy string actually names
  (Kénitra, Salé, Témara) are kept; the other 13 of 16 default to Rabat and are flagged `?` in the
  review. Type comes out 82 Medical / 55 Chirurgie / 11 Biologie.

  ⚠ **Classify on word boundaries, never bare `Contains`.** The first version put every neurology ward
  in surgery — `"Neurologie"` contains `"urologie"` — and missed `"Urg.Porte.Chirurgicale"`, because
  `"Chirurgicale"` does not contain `"chirurgie"`. `ServiceNameParser` now matches accent-stripped text
  with `\b` anchors and a `chirurg` stem; `LegacyMapperTests.Look_alike_specialities_are_not_confused`
  pins all ten of the real catalogue entries that exposed it.

**One student, two registrations in one year** — `IX_Registration_Student_Year` is UNIQUE, Access
enforced nothing, and 3 pairs out of 43,608 break it. Each is a stale row somebody re-entered instead of
correcting (student 21025416 holds both a `MED00` retrait with no rotations and the real `MED04` with
seven, same year). The importer keeps the row with the most rotations, tie-breaking on the later
`Numins`; on the real data that picks correctly all three times and loses no rotations, because the
discarded side has none in every case.

> ⚠ This one only surfaced against real PostgreSQL. `UseInMemoryDatabase` does not enforce unique
> indexes, so the 406-test suite was green while the import died 25 s into writing. The regression is
> pinned in `LegacyImportPlannerTests` at the planner level, which is the only layer that *can* catch it.

### ⚠ The sample seeder collides with the import

`PGSH.MigrationService` runs `Seeder.SeedAsync` on **every Aspire start**, and it creates 13 `Level`
rows and 3 `AcademicYear` rows. Both carry unique indexes — `UNIQUE(Year, AcademicProgram)` and
`UNIQUE(Label)` — and the importer creates 16 levels and 21 years overlapping them. Seeded and imported
data cannot coexist.

Set **`Seeding:Enabled = false`** (`PGSH.MigrationService/appsettings.json`, or the env var
`Seeding__Enabled=false`) before importing, and leave it false. Migrations still run; only the Bogus
fixtures are skipped. The importer's own `--apply` guard now counts every table it writes — not just
students — so it refuses rather than failing part-way through on a constraint.

**The three static users are not fixtures, and have their own flag.** `amine.bennani@um5.ac.ma`,
`admin.pgsh@um5.ac.ma` (Scolarité) and `employee.test@um5.ac.ma` carry fixed GUIDs and known e-mails so
Keycloak's email-matching resolves them — they are the only way into the application. They are
therefore seeded by **`Seeding:StaticUsers`** (default `true`), which is deliberately independent of
`Seeding:Enabled`: a database holding real imported records still needs the accounts even though it
must never receive the Bogus data. Each is created only when its e-mail is absent, so it is safe on
every start, and none of their identifiers collide with imported students (checked: `Appogee`
`22003344` and CNE `G135000111` are unused by the legacy set).

That split exists because re-enabling the whole seeder is *not* a safe way to get them back: every
section skips when its table has rows — **except** `SeedShowcaseServiceAsync`, whose guard is
`Hospitals.AnyAsync(h => h.LocalisationMaps != null)`. No imported hospital carries GPS, so that one
would fire and inject a demo service into real data.

**Out of scope by decision:** the pedagogical half of the file (`MATIERES`, `notes*`, `ResExamClin`,
`creditmat`, `jury`, `amphi`, `groupetp`, anonymized double-marking, credits) belongs to a separate
project/microservice. PGSH owns stages only, so the Access app cannot simply be switched off at cutover.
Useful signal: `CRDST` (stage credits) is `0` on every row while `CRDMAT`/`CRDTP` are used on ~42,500 —
the stage half of their credit system was already dead, which is a clean seam.

## Scale: what real data broke, and the rule it taught

Seeded fixtures hid every unbounded query. The legacy import turned them into crashes overnight, so
treat these figures as the baseline any list screen must survive:

| | |
|---|---|
| 1,003 AcademicGroups (101 in 2025-2026) | 13,604 Cohorts (1,684 in the current year) |
| **"Non réparti" 2025-2026 holds 4,725 students in one group** | 681 cohorts on a single stage across years |
| 10,203 Students · 43,605 Registrations · 98,555 Assignments | 105,626 ServicePeriods |

Fixed (2026-08-07):

| Handler | Was | Now |
|---|---|---|
| `GetGroupByIdQuery` | whole roster + 2 correlated loan lookups **per student** — 4,725 rows | paged (25) + debounced search; header count via `StudentCount` |
| `GetCohortsByStageQuery` | **no year filter at all** — every year the stage ever ran | year-scoped + paged |
| `GetAcademicGroupsQuery` | all 1,003 groups | paged, with `StudentCount` per row |
| `GetStageScheduleQuery` | unfiltered year returned every year's cohorts | falls back to the current year |
| `GetYearTimelineQuery` | all 1,684 cohorts of a year to draw one stage | optional `StageId` narrows the tree |

> ⚠ **A nested collection is the one pagination audit misses.** Grepping for handlers returning
> `List<T>` finds the flat offenders; it does *not* find `GetGroupByIdQuery`, whose 4,725 rows sit
> inside a single `GroupDetailResponse`. When reviewing a query for scale, ask what the *response*
> contains, not what the handler's generic parameter says.

> ⚠ **Counting is not listing.** `AdminDashboardPage` fetched all 1,003 groups to render one number.
> Ask for `pageSize: 1` and read `TotalCount`.

On the frontend, screens that need a list as a *lookup* (dropdowns, filters, assignment grids) use
`getAcademicGroupOptions` / `getCohortOptionsByStage` — thin wrappers that still hit the paged
endpoint but ask for one large page and unwrap `.items`. Screens that display a list paginate for real.

## CNPN versioning — why the curriculum is its own aggregate

The CNPN (national pedagogical standards) is reissued whenever the ministry decides, and what it
contains is a political outcome — a new minister can change a level's stages at will. **This already
happened five times in the imported history**, e.g. *Pharmacie Clinique 3* ran 2019-20 → 2022-23 and
then vanished while Clinique 1 and 2 carried on.

Before `Curriculum`, the model could not express any of it: `Stage` was `(Id, Name, Coefficient,
DurationInDays, LevelId)` with **no temporal dimension at all**. The only link to a year was indirect,
through `Cohort → AcademicGroup → AcademicYear` — which records *what was executed*, not *what was
required*. Three consequences: "which stages did 3e année Médecine have in 2024?" was answerable only
by inference (a stage nobody ran that year looks removed); editing a `Stage` retroactively rewrote
what every past assignment displays; and adding a successor left the old one indistinguishable from a
live one.

**The rejected design was a validity window** (`Stage.EffectiveFrom/To`). It is the wrong shape, not
merely impractical: a window says *"this stage is valid from X to Y"*, which forces someone to know
when it ends, and cannot express a stage that is dropped and later reinstated. A CNPN says
*"in year Y, level L requires exactly this set"* — a per-year requirement set predicts nothing.

```
Curriculum  (aggregate root, UNIQUE(LevelId, AcademicYearId))
  └── CurriculumStage  (StageId, Coefficient, DurationInDays)   ← that year's weight
```

- `Stage` stays the **timeless catalogue entry**, so historical `InternshipAssignment`s keep pointing
  at a stable identity. Everything that varies between years lives on `CurriculumStage` — including
  coefficient and duration, because a text can keep a stage and reweight it.
- `Curriculum.CopyFrom(previous)` is the realistic flow: most years repeat the last one with one edit,
  and cloning keeps each year an independent record rather than a diff nobody can read back.
- `RemoveStage` raises `CurriculumStageRemovedDomainEvent`. Removal settles nothing on its own —
  students who failed that stage still owe it, and the administration decides each case by hand.

### An abolished stage is still served — settled 2026-08-07

**When a stage is removed from the CNPN, a student who failed it still has to serve it.** Removal
releases *new* students from the requirement; it does not erase an obligation already incurred. There
is **no waiver and no substitution** — no "dispense", no equivalent stage standing in for another.

This is exactly why `Stage` is a timeless catalogue entry rather than something with an expiry date:
the stage record survives its removal from the curriculum, so it can still be served. The service is
still physically there too — only the curriculum entry went away.

Nothing extra was needed to support it: `RevalidateStageCommand` already re-opens any stage the
student has a settled `NonValidé` on, regardless of whether the current CNPN still lists it, and
places the retake back in the service of the failed rotation. `AbolishedStageRevalidationTests` walks
the whole *Pharmacie Clinique 3* scenario — comparison flags it Removed, the retake is served in the
original service, the original failure stays untouched as history, and passing the retake moves the
dossier to `Validated` with both attempts on record.

> The one wart: `InternshipAssignment.CurrentCohortId` is non-null, so a retake of an abolished stage
> needs a cohort created for it even though no *group* runs that stage. It is harmless but it models
> a group activity for what is one student's individual rotation. Fixing it properly means moving
> `StageId` onto `InternshipAssignment` and making the cohort optional — a wide refactor, deliberately
> not done for a case that currently affects nobody.

**Writes:** the aggregate's behaviour is reached through two commands, both Scolarité-only.

| | |
|---|---|
| `PUT levels/{levelId}/curriculum/{yearId}` | `SaveCurriculumCommand` — the **whole text at once**, because that is how a CNPN is issued. Idempotent, so re-sending the same set changes nothing. |
| `POST levels/{levelId}/curriculum/{yearId}/copy` | `CopyCurriculumCommand` — opens a year from another year's text. Refuses if the target already has one; amending goes through Save. |

Save **reconciles** against what is stored rather than replacing it wholesale: a stage that disappears
from the submitted set goes through `Curriculum.RemoveStage` and raises its domain event, while one
that is kept with a different coefficient is amended in place and raises nothing. Without the
reconciliation a dropped stage would vanish silently, which is precisely the event students who failed
it depend on.

**Screen:** `CurriculumPage` (admin → Académique → *CNPN (programme)*, `/admin/curriculum`). Pick a
level and two years; the table shows each stage as `Ajouté | Retiré | Recoté | Inchangé` with both
coefficients, changes first. When a stage is `Retiré` a red banner states the rule explicitly — the
student repasses it anyway, because removal releases only new inscrits.

> **The page is still read-only.** It compares and displays; recording a newly published CNPN is
> API-only for now. The editing UI — stage picker, per-stage coefficient/duration, and a "cloner
> l'année précédente" button over `CopyCurriculumCommand` — is the remaining piece.

**Reads:** `GetCurriculumQuery(levelId, yearId)` answers "what was the CNPN then";
`CompareCurriculaQuery(levelId, fromYear, toYear)` returns per stage
`Unchanged | Added | Removed | Reweighted` with both sides' coefficients — the view behind manual
revalidation, where a student is judged against the text of the year they failed in but can only be
re-planned against today's.

**Backfilling history — ✅ applied 2026-08-07: 51 curricula / 175 stage entries.** The derivation lives
in `CurriculumHistoryReconstructor` (Application) because it has two callers:

```bash
# authenticated endpoint, dry run by default
POST curricula/seed-from-history          { "dryRun": true }

# same rule without an HTTP identity — the pass that follows a legacy import
dotnet run --project PGSH.LegacyImport -- --seed-curricula --connection "…"          # dry run
dotnet run --project PGSH.LegacyImport -- --seed-curricula --connection "…" --apply
```

It is an approximation and says so: a stage the text required but which no group ran leaves no trace.
**Idempotent** — verified by re-running: the second pass reported `0 created, 51 already recorded,
left alone`, so a year confirmed by hand survives.

The history is now explicit data rather than inference:

| Pharmacie Y5 | required |
|---|---|
| 2019-20 → 2022-23 | Clinique 1, Clinique 2, **Clinique 3** |
| 2023-24 → 2025-26 | Clinique 1, Clinique 2 |

> Migrations can now be authored with `--startup-project PGSH.MigrationService` (it carries
> `Microsoft.EntityFrameworkCore.Design`), which avoids having to stop a running `PGSH.API` to free
> its build output.

## Revalidation is demande-driven and served where the student failed

Confirmed with the user 2026-08-07:

1. **It starts from a demande.** Revalidation is never automatic — the student requests it. The
   Demande service is Phase 5, so `RevalidateStageCommand.DemandeId` carries the reference and, since
   only `Delocalization` has a column for it today, the link survives in the audit entry. **A
   revalidation needs its own `DemandeId` column when Phase 5 lands.**
2. **The retake is served in the service where the stage was failed**, not wherever this year's grid
   would send the student's group. Leave `ServiceId` null and the failed rotation's service is reused.
   A change of service is itself subject to an approved demande.
3. It is therefore an **ad-hoc placement like a délocalisation** — `CohortSlotAssignmentId` stays null,
   the schema's own meaning for "outside the published schedule".

**Batch or individual?** Neither, exactly — the target service is *per student*, and batching is only a
convenience over that rule. Failures scatter far too widely to send everyone to one place:

| Stage | Failed attempts | Distinct services |
|---|---|---|
| CHIRURGIE | 377 | **26** |
| MEDECINE | 441 | **21** |
| GYNECOLOGIE OBSTETRIQUE | 386 | 5 (≈77 per service — a real cluster) |

So: resolve each student's own service, then group the resulting list by service to process a whole
cluster in one window.

> ⚠ **`Result<T>`'s implicit operator maps a null value to a FAILURE** (`Error.NullValue`), so an
> optional result can never be expressed as `Result<T?>` returning null — it silently becomes an
> error. Resolve optional values only when they are wanted, and keep `Result<T>` non-nullable.

## There is no average above the stage — settled 2026-08-08

The **only** mean in this domain is *inside* a stage: a stage holds several periods, so its note is
the mean of its periods' marks. That is `StageScoring` / `InternshipAssignment.RecomputeFinalScore`,
and it already exists.

Above that, **nothing**. No year average, no cursus average, no coefficient-weighted roll-up. A
relevé prints each stage's own note and stops. `Stage.Coefficient` exists in the schema but **no rule
uses it**, and it must not be pressed into service to invent a moyenne.

This closes an open question carried since the student portal was built (session 10), where the
dashboard deliberately shipped without a "moyenne générale" rather than guess a formula. Do not add
one — if a future requirement needs it, it is a new rule to be agreed, not a derivation to be
assumed.

## `Registration.Status` is unmanaged — and why past years all read "En cours"

*(Established with the user 2026-08-08, after the student portal made it visible.)*

**The gap.** PGSH covers stages only. It is **not linked to the pedagogical side of the faculty** —
the system that knows a student's subject marks and therefore who passed the year and who repeated
it. Nothing in PGSH ever moves a `Registration` off `Active`, so every past registration a student
holds still reads `Status = Active` → the badge "En cours" on a year that ended two years ago. The
badge is faithful; the data was simply never closed out. Do **not** patch this in the UI.

⚠ Not to be confused with `InternshipAssignment.Status`/`Result`, which *are* managed and are the
subject of the ratify-vs-pass rule above. This is the **registration** (the academic year enrolment),
one level up.

**The rule to apply later** *(user, 2026-08-08 — an inference, deliberately deferred)*. Order a
student's registrations by academic year. The most recent one is the year in progress; every earlier
one can be settled from the shape of the sequence alone, because a student only re-registers at a
level they did not clear:

| Level sequence | Reading |
|---|---|
| 1, 2, 3, 4 | 4 = **en cours**; 1, 2, 3 = **validated** (they progressed past each) |
| 1, 2, 3, 3, 4 | the **first** 3 = **failed** (repeated), the second 3 = validated, 4 = en cours |

So: *a registration is failed when a later registration exists at the same level; otherwise it is
validated; the latest is in progress.*

**Why it is not implemented yet.** It is an inference from enrolment history, not a fact — the
authoritative answer lives in the pedagogical system PGSH cannot see. It also has cases the table
does not cover and which need a ruling before any code is written:

- a student whose **latest** registration is not in the current academic year (dropped out, on leave,
  graduated) — is the last one still "en cours"?
- a **skipped** level, or a non-consecutive repeat (1, 2, 3, 4, 3) — the 2nd-year-later retake is a
  revalidation pattern, not a repeat of the year;
- a student who **passed the year while still owing a stage** — already a settled rule here (see the
  revalidation notes above), so "registration validated" must **not** be read as "all stages
  validated". The two verdicts are independent, and the graduation gate is the place that joins them.
- writing an inferred verdict into `Registration.Status` **destroys the distinction** between
  "inferred" and "known". If it is implemented, it should be a derived/read-model field, or carry a
  provenance flag, so a later link to the pedagogical system can overwrite guesses without
  overwriting facts.

**⚠ Superseded in part, 2026-08-09 — a year is now closed by declaration, not by inference.** The
last bullet above is answered: `Registration.OutcomeSource` is the provenance flag, and
`RecordYearOutcome` refuses to let an `Inferred` verdict overwrite a `Declared` one. See *Closing a
year* below. What is **still open** is the inference itself, for the six imported years nobody will
ever upload a canvas for — the first three bullets need rulings before that is written (Phase 14.3c).

## Stages of unequal length on one axis — the general solution (2026-08-09, session 15)

The first version of the rotation cycle took **one** `periodsPerStage` for a whole block, which is fine
for the new 3rd year (two semesters × three stages × one period) and useless for the 6th, where four
stages take two periods and two take one. Generalised properly rather than worked around, because the
user's framing was right: it is a pure mathematical problem.

**The counting identity.** A partition needs `T = Σkₛ` columns to visit every stage of the block. If `Lₛ`
partitions sit in stage *s* at once, counting partition-columns two ways gives `Lₛ·T = P·kₛ`, hence
`Lₛ = P·kₛ/T`. Integrality pins **`P` to a multiple of `T / gcd(kₛ)`** — the only arithmetic condition.

Run the real 6th year through it: `k = [2,2,2,2,1,1]` → `T = 10`, `gcd = 1`, so `P = 10`, and
`L = [2,2,2,2,1,1]` summing to 10. **That is the ten monthly columns of `Med6.png`**, with the two-period
stages holding two partitions at a time. The reference document *is* the formula.

⚠ **A period is one service, not one stage — and I modelled it backwards first.** "Chirurgie has 2
periods" means the group passes through **two different services**. I initially gave a 2-period stage a
single two-column slot, which keeps a group in one service for two months. The ported tests failed
immediately and correctly. The right model: every stage carries a slot **per axis column** (so `SlotCount`
is `T` for all stages, as before), and a partition takes a **run of `kₛ` consecutive** ones. This also
removed a `kₛ | T` condition I had briefly imposed — a run need not start on a multiple of its length.

⚠ **The closed form had to go.** `(lane + t) mod S` is a cyclic Latin square and only exists when the
durations are equal; with unequal `kₛ` the stage boundaries stop lining up, so shifting a partition by one
stage no longer maps a valid schedule to another. `RotationTiling` solves an exact cover instead,
backtracking across **partitions and columns together** — filling each partition greedily would report
"impossible" whenever an early partition took a column a later one needed, which is a wrong answer rather
than a slow one. The search space is tiny (a level has ≤ ~12 columns, ≤ ~8 stages).

⚠ **Some mixes are genuinely impossible.** Stages of 2 and 1 give `T = 3`; a two-column run must cover
column 2 wherever it starts, so every partition is in that stage there and the other stands empty. No `P`
rescues it. Because the search is exhaustive, `NoFeasibleArrangement` is a proof rather than a giving-up —
worth keeping that property if it is ever optimised.

**Authoring the dates once** was the other half of the request, and it falls out for free: the caller
supplies the axis at its **finest** granularity (10 monthly windows), and every stage's slots are cut from
that one list. A 2-period stage and a 1-period stage on the same block therefore cannot drift, because
there is only one set of dates. `PeriodAxis` on the read side already handled multi-column stages by
repeating their cell across the columns they span, so the répartition needed no change.

**Chosen for the partition-count question:** the command **takes** `P` (from the promotion's real
partitioning) and validates it, rather than deriving one. Deriving would silently re-cut partitions to
suit one block, which fights the `Reassign` guard, and a level's partitioning is shared across its blocks.
The refusal names the multiples that would work, so it is as helpful as deriving would have been.

## The crossover is generated now, and the shared axis is authored once (2026-08-09)

Asked for as a "mirror effect": configure one stage, get the opposite for the other. Generalised,
because two stages is the easy case and the new CNPN's 3rd year is six.

**`Stages/RotationCycle/`** produces the `PartitionStagePlan[]` matrix that `GenerateMacroPlanCommand`
already consumes — so this generates what was previously ticked by hand and changes nothing downstream.

- ⚠ **`S × k` columns, not `partitions × k`.** The user's formula (partitions × periods) held only
  because their example had 2 stages *and* 2 partitions. The timeline must fit one partition visiting
  every stage of the block; partitions subdivide *who is where*, they do not lengthen it. Three stages,
  k = 1, six partitions ⇒ 3 columns with two partitions per stage per turn.
- **Lane `p mod S`, stage `(lane + t) mod S` in turn `t`.** A Latin square. S = 2 gives exactly the
  requested mirror; S = 3 gives the three-way rotation the new CNPN needs; P a multiple of S puts
  several partitions in a lane and they travel together.
- **Uneven or short is reported, not refused.** P not a multiple of S still gives every partition every
  stage — the turns just carry unequal effectifs, which is a capacity surprise worth naming. Fewer
  partitions than stages leaves a stage empty for a whole turn, which gets its own warning. Neither
  blocks: only the faculty knows whether it was intended.
- **`RotationCyclePlanner` is pure.** No DB, no clock — which is why the crossover is tested by
  properties over many (S, k, P) combinations (every partition visits every stage exactly once; no
  partition is in two stages at a time; every stage is occupied in every column) rather than through
  a fixture that only proves one shape.
- **Authoring the axis ≠ running the plan.** Apply writes the slots and returns the matrix; the caller
  passes it to the macro plan. Same split as déliberation / réinscription, and it keeps
  `CohortProvisioner` / `RotationArranger` / `SchedulePublisher` on their existing path.
- **Replacement is wholesale and scoped to the named stages.** Half-old, half-new columns are the exact
  misalignment this removes; and two blocks of one level (semester 1 / semester 2) do not disturb each
  other. Refused outright while any cell is published.
- ⚠ Stage order in the command **is** the rotation — `RotationCycleContext` preserves it deliberately
  rather than sorting, because reordering would silently change which stage a partition starts in.

### The gap it works around: nothing declares that two stages share a period

`StageSlot` is keyed (stage, year, period number), so Médecine P1 and Chirurgie P1 are independent rows
with independent dates. Nothing asserts they are the same window — the axis is *derived* from dates by
`PeriodAxis`, never declared. And neither guard notices: `SlotOverlapGuard` is per-stage (that is what
makes the crossover authorable at all) and `GroupScheduleConflictGuard` only fires on a group genuinely
double-booked, which a crossover never is.

- ⚠ **The small drift is the dangerous one.** Chir P1 = 01/10–02/11 against Med P1 = 01/10–31/10:
  Chirurgie's window *strictly contains* Médecine's, so `PeriodAxis` treats it as a composite, drops it,
  and Chirurgie claims Médecine's column by midpoint. The two-day error vanishes without trace. A
  *partial* overlap is louder — both survive as columns, the table grows one, and the holes are hatched
  and counted.
- `PeriodAxisDiagnostics` (pure) reports period numbers whose stages declare different windows, on
  `LevelRepartitionResponse.AxisDisagreements`. **Never an error:** Med6 legitimately runs Chirurgie's
  P1 over two months and ANES REA's over one. Code cannot separate that from a typo; a human can.
- It reads the **slots**, not the cells — a period nobody is placed in yet still has dates, and that is
  exactly when you want to hear about it.
- ⚠ `AxisDisagreements` sits on the *response*, not on `RepartitionSummary`. The summary is a bag of
  counts compared by value in tests; a collection member silently breaks record equality (caught by two
  existing tests when it was first put there).
- The structural fix — one declared axis entity per block, with slots referencing it — is deferred to
  15.1, same root cause as the semester gap. `RotationCycle` avoids the class in practice by writing all
  of a block's windows in one act.

## Why the répartition prints `1, 3, 5, 7` where the faculty prints `1-10` (2026-08-09)

Raised by the user from reading the annual planning, and their diagnosis was exactly right. Three links
in one chain, none of them a defect:

1. **`PartitionAllocator` fills the smallest partition, walking groups in `GroupNumber` order.** With two
   empty partitions that alternates on every single group → `A = {1,3,5,7…}`, `B = {2,4,6,8…}`. The
   stripe was never a deliberate interleave for efficiency; **balance was the only property sought**, and
   the stripe is what falls out of chunking one group at a time. In general the step is the *partition
   count* — four partitions give `1,5,9,13`.
2. **`RotationArranger` orders by `(label, groupNumber)` and indexes the service queue by position**
   (`serviceQueue[(ci + offset) % n]`, line 265). The queue repeats each service in a run sized by its
   capacity share, so consecutive `ci` share a service. Contiguity is real — it just lives in *index*
   space, and index space is one partition.
3. **`GroupNumberRanges` then has nothing to collapse.** It is correct to refuse: `47-50` is a promise
   that 48 and 49 are in that service too, so merging across a hole would misdirect two whole groups.

`Med3.png` reads `47-50` because the faculty cuts in **blocks** (1-40 Médecine / 41-80 Chirurgie).
PGSH cut in stripes. `PartitionStrategy` now offers both, `Interleaved` remaining the default so no
existing plan moves.

- **Correctness-wise the two are interchangeable** — equal-sized, disjoint, and the crossover
  (A→P1-2, B→P3-4) works identically. The difference is entirely in the *published artefact*, whose
  function is to be read at a glance: ten comma-separated numbers per cell over 26 rows × 10 columns is
  a materially worse table than the one the faculty publishes. Cosmetic in the code, not cosmetic on paper.
- ⚠ **Re-cutting is destructive to a plan.** `Reassign: true` is refused while any cell of the promotion
  is **published** (a `ServicePeriod` points at it — students were sent there, possibly already served),
  and reports `PlannedCellsAffected` for the merely-planned ones so the caller knows to re-arrange.
- ⚠ **Contiguous partitions inherit whatever ordered the group numbers**, and
  `AutoArrangeGroupsCommandHandler` numbers sequentially *per CNPN bucket*. From 2026-2027 a contiguous
  partition can therefore be entirely one text where an interleaved one mixes both — which changes which
  rows exist in that partition's half of the matrix, since `CohortProvisioner` skips unrequired stages.
  Verify on real data before switching the default.
- The interleaved tie-break used to be `counts.MinBy(...)` over a `Dictionary`, i.e. it depended on
  dictionary enumeration order — stable in practice, guaranteed nowhere. It is now an explicit
  `(count, labelIndex)` ordering, so A-before-B is stated rather than inherited.
- `AssignRotationGroupsCommand` returns each partition's membership through `GroupNumberRanges.Format`,
  which is what makes the two strategies comparable at a glance without arranging anything first.

## Closing a year — the déliberation canvas and the réinscription (2026-08-09)

The user's framing, and it is the right one: *"we do not possess the pedagogical side — exams, TP,
déliberation — so we cannot know who graduated or failed, so we adjust our side"*. PGSH stops trying to
derive the verdict and accepts it as input, in the shape the évaluation import already proved works.

**Two acts, months apart, and they must stay apart.**

| | when | what it does | on re-run |
|---|---|---|---|
| Déliberation (`…/Deliberation/`) | July | writes each registration's verdict | replaces (a jury corrects itself) |
| Réinscription (`…/Reinscription/`) | September | creates next year's registrations | **skips** — idempotent |

Fusing them was considered and rejected: not every *admis* comes back, so the combined act would
invent registrations for students who abandoned, and it would need next year's `AcademicYear` row to
exist in July.

- **The canvas is per (year, level)** — one jury, one file. It is also what makes the CNE index mean
  something: unique within a promotion, ambiguous across years.
- **`RegistrationStatus` gained `Graduated` and `Excluded`**, and both distinctions earn their keep in
  exactly one consumer — the réinscription, which sends *Admis* up a level, *Redoublant* round again,
  and *Diplômé / Exclu / Abandon* nowhere. Collapsing either pair breaks it.
- ⚠ **The two error policies differ on purpose.** All-or-nothing for the déliberation, because the
  uploaded file is *not stored* and a half-closed promotion could not be reconstructed. Skip-and-report
  for the réinscription, because re-running it is safe, and refusing 690 legitimate rows over three
  anomalies buys nothing. Do not "make them consistent".
- ⚠ **`NextLevelMissing`** — *Admis* on a level with nothing above it. Almost always a PV that meant
  *Diplômé*, and it is reported rather than converted: guessing a graduation is guessing the one thing
  the faculty is there to say.
- ⚠ **`Diplômé` is checked against `CnpnVersion.TotalYears`, and stands aside when the stamp is
  missing.** ~2,200 stamps are inferred and 19 students carry none; refusing on absence would make the
  feature unusable on the real base. Same standing-aside rule as `CohortProvisioner`'s.
- **An *Admis* with an unvalidated stage is flagged, not blocked.** PGSH sees stages; the jury sees the
  year. With **0 authored `StageSlots`** an unmarked stage is currently the *normal* state, so enforcing
  this would refuse every import — and the graduation gate (still unbuilt) is the place that joins the
  two verdicts, not the déliberation.
- **A motif is kept only on an adverse verdict** (`FailureReasons`), and a row that had its motif
  dropped says so. Silently discarding what someone typed is how a user learns not to trust a preview.
- New registrations are `Active` with **no group**. Nothing in the app filters planning by
  `Registration.Status` — checked — so `Pending` would be planned exactly like `Active` while claiming
  not to be enrolled. The empty group is deliberate: auto-arrange reads the "Non réparti" bucket next.
- `IX_Registration_Year_Level` added while here — `Registration.LevelId` had no index at all, which
  Phase 13 had logged.

## Code review 2026-08-08 — what it caught

Two real defects, both now fixed with regression tests:

- **`RevalidateStageCommandHandler`: `Any` where it needed `All`.** The guard read
  `!priorAttempts.Any(r == NonValidé)`, so a student with a settled 2022 failure *and* a 2023 attempt
  still awaiting its verdict got a retake opened alongside the live one — the exact "two live
  attempts" the comment claimed to prevent, and the dossier would call the same student `InProgress`.
  `DossierStageState` uses `All`; the command now matches it.
- **The level dossier missed cross-level retakes entirely.** Attempts were found through the
  registrations *of that level*, but a retake of an earlier level's stage hangs off the registration
  the student holds *now*. It was therefore absent from the earlier level's dossier (wrong
  registration) and dropped from the later one (wrong stage) — so the earlier level read
  `ToRevalidate` forever, even after the retake passed. Attempts are now scoped by the **stage's**
  level, and each carries its own year rather than looking it up in the level's registration list.

Smaller fixes: `BacYear` was the one string not `Truncate`d (varchar(10) — one long `ANNEE_BAC` would
abort the whole students batch); the reader's `when (provider == Provider)` filter made its own
friendly ACE-provider error unreachable and leaked the connection; `LegacyPeriodParser.DanglingDate`
was computed and never reported, contradicting its own "reported, never guessed at" comment (now a
`DanglingPeriodDate` problem kind); the retake's default service came from an unordered `First()`.

**Checked and accepted, not fixed:** the importer folds a legacy `revalide='O'` row into the same
assignment when it shares `(NUMINS, CODEST)` with a normal rotation. That happens on **3 keys out of
98,555**, and in all three every mark involved is ≥ 10 — so the verdict is `Validé` either way and only
the displayed `FinalScore` differs. Not worth a re-import.

**Left for a decision:** `Seeding:Enabled=false` sits in the committed base `appsettings.json`, not a
Development/user override — correct for the imported database, but anyone checking out this branch
against a fresh one gets migrations plus three login accounts and nothing else.
`GetStageScheduleQuery` also now substitutes the current year silently when none is given, with no way
to ask for all years and no field in the response saying which year was applied.

## Open Questions / Things to Verify

- **`GenerateScheduleCommandHandler`**: Does it correctly handle the case where students are transferred between cohorts mid-year? The `CohortMembership` model supports it but the handler logic needs review.
- **`IsCurrent` flag on `AcademicYear`**: Is this maintained automatically or manually? Currently no logic enforces that only one year has `IsCurrent = true`. Should there be a check in `CreateAcademicYear` or `UpdateAcademicYear`?
- **`TodoDatabase` connection name**: This is the Aspire-assigned connection name for the main PostgreSQL database. It's a legacy artifact from project scaffolding. Consider renaming it in `AppHost/Program.cs` and `appsettings` to something like `"pgsh-db"` for clarity.
- **`Employee.WorkPlace`**: The enum has values `Hospital` and `Fmpr`. Is `Fmpr` still the correct name for the faculty workplace, or should it be renamed to match the actual institution name?
- **Domain events on Hospital/Center/Service**: Currently no domain events are raised on creation or modification of these entities. If downstream notifications (e.g., service capacity changes affecting rotation scheduling) are needed, events should be added.
- **`Student.Ranking`**: What is this field for? National ranking for program entry? It's nullable and has no business logic around it.
- **Revalidation cohort assignment** *(answered 2026-08-07 — see `RevalidateStageCommandHandler`)*: `RevalidateStageCommand` takes an optional `CohortId`. Left null it falls back to the cohort of the student's own registration group for that stage (the repeating student, same level, next year). Given explicitly it slots the student into any cohort currently running that stage — which is how a **cross-level** retake works, e.g. a 6th-year student redoing a 1st-year stage joins a current 1st-year cohort. No cohort of that stage hangs off any group they still belong to, so the fallback fails with `NoCohortForRevalidation`, whose message names the missing field. No schema change was needed: `Cohort.AcademicGroupId` still means "one specific group" — the student joins that group's cohort for the duration of the retake.
- **Settling `Registration.Status` for past years** *(raised 2026-08-08, deferred by the user)*: PGSH has no link to the pedagogical side, so no registration is ever closed out and every past year reads "En cours". The inference rule (a registration is failed when a later one exists at the same level) and the four cases that still need a ruling are written up in "`Registration.Status` is unmanaged" above. Feature work is queued as **Phase 14.3**.
- **Graduation gate on revalidation**: Is there a check before a student can graduate (registration `Status → Validated`) that all stages in their program have at least one `InternshipAssignment` with `Result = Validé`? This would be the enforcement point for the revalidation rule. Not yet implemented.

## Regression / Stress Checks (verification recipes)

These are the scenarios used to verify the pagination/auth hardening. Re-run after touching
pagination, the execution-authorization path, or `SyncUserMiddleware`.

**Environment gotchas (Aspire dev) — learned the hard way:**
- The API's HTTPS listens on the `launchSettings` `sslPort` (e.g. `https://localhost:7014`); the
  Aspire-assigned HTTP ports **307-redirect** there, and `curl` can't TLS-handshake the HTTP ports.
  Hit the HTTPS port directly with `-k`. First authenticated call is slow (~12s EF/JIT cold start).
- **All endpoints are under the `/api` route group** (`app.MapGroup("api")` in `Program.cs`) — e.g.
  `GET /api/service-periods`, not `/service-periods`.
- **Every request needs a local `User`**: `SyncUserMiddleware` returns **403 "Profile Not Found"** if the
  Keycloak `sub` has no matching `Users` row (by `IdentityProviderId`, then email). For headless tests,
  temporarily set an existing user's `IdentityProviderId` to the test token's `sub` and restore after.
- **Minting a test token**: `pgsh-frontend` has `directAccessGrantsEnabled = false`. Create a throwaway
  public client with direct grants + temp users with realm roles (`Scolarite`/`Secretaire`/`SuperUser` =
  admin; `Student`/`Professor` = not), password-grant against `localhost:8082`, then delete the artifacts.
  Tokens must carry `aud=account` (the API's configured audience) and `iss=…/realms/pgsh`.

**Scenarios:**
1. **`GET /api/service-periods` is administrative-only** — non-admin token → **403 `ServicePeriods.AdministrativeOnly`**; admin token → **200**. (Verified ✅)
2. **Central `pageSize` clamp** — request `?pageSize=100000` on an endpoint **without** its own validator
   (`/services`, `/service-periods`, `/employees`, `/hospitals`, `/centers`, `/academic-groups`,
   `/internship-assignments`, `/academic-years`) → response echoes **`pageSize: 200`**, `totalCount`
   accurate. Endpoints **with** a validator (`/students`, `/stages`, `/levels`) return **400** instead
   (their own `InclusiveBetween` rule) — both are safe, just different contracts. (Verified ✅)
3. **Chef worklist returns all periods (no 100 cap)** — as the chef of a service with **>100** periods,
   `GET /api/employees/me/service-periods` returns the full set; every academic group shows its true
   student count (regression guard for the "group of 8 rendered as 2" truncation). *Needs planning data
   seeded (ServicePeriods > 0) to exercise.*
4. **Planning grid perf (manual/GUI)** — open/close `ScheduleGridModal` on a stage with many cohorts×slots;
   should be snappy (only the edited cell mounts the heavy Combobox). Click-to-edit still assigns/clears.
5. **Optimistic allowed-services (manual/GUI)** — add/remove a service on a stage: chip toggles instantly
   and persists after reload.

## Jours ouvrables — measured, not assumed (2026-08-13)

`Stage.DurationInDays` is **already in worked days** for 25 of 27 stages: 14×7, 22×7, 30×2, 42×3, 44×6,
66×2. 22 is a month of worked days, 44 two, 66 three, 14 about three weeks. Only the two 30s (pharmacie
officine, stage hospitalier d'initiation) are ambiguous — 30 worked days is six weeks, so they are most
likely calendar days the import left behind.

**Consequence for planning: author the axis in `jours ouvrables`.** Med6 at 22 j.o. per column meets every
stated duration exactly (CHIRURGIE k=2 → 44) while its calendar span swings 60–67 days. A monthly axis
cannot make that guarantee — the same six months give columns of 18 to 22 worked days.

The academic year 2025-2026 (01/09/2025 → 31/08/2026) holds **365 calendar days, 261 weekdays, 252 worked
days** with the ten fixed national fériés, **247** once the four lunar ones are entered. Two of the fixed
days fall on a weekend and cost nothing.

⚠ The lunar dates are not computable — Aïd al-Fitr, Aïd al-Adha, 1ᵉʳ Moharram, Aïd al-Mawlid turn on
observation of the crescent and are fixed by decree. Enter the estimate unconfirmed (a Hijri date drifts
~11 days earlier per Gregorian year) and confirm when the decree lands. Under `mois`/`semaines` correcting
it moves no dates; under `jours ouvrables` it shifts that column and every one after it.

---

## "The system knows and does not say" — three of them, closed (2026-08-13, session 17)

Session 16 ended with three open findings. They looked unrelated; they are one shape. In each case the
backend held the distinguishing fact and the surface collapsed it into a state that read as something
else — and in each case the wrong reading pointed the user at the *opposite* action.

### 1 · An empty répartition has two causes

`PeriodAxis` was fed the **cells**, so a level whose axis had just been applied — slots written, nothing
arranged — produced **zero columns**, and the document printed « Aucune période n'est planifiée ». The
apply had worked perfectly and looked like it had failed.

The axis is now built from `declaredSlots ∪ cells`, which is what `PeriodAxis`'s own doc comment always
claimed ("out of the windows its stages actually declare"). `RepartitionSummary.DeclaredSlotCount` names
the fact that separates the two states:

| `declaredSlotCount` | `rowCount` | what it means | what to do |
|---|---|---|---|
| 0 | 0 | no period exists for this (level, year) | author an axis |
| > 0 | 0 | the periods exist, nobody is in them | arrange |
| > 0 | > 0 | a table | read it |

⚠ **The union, not the declared slots alone.** A cell is tied to the level through its *cohort*
(`a.Cohort.Stage.LevelId`), while a declared slot is tied through its *stage*. They are the same set in
practice, but assuming it means a slot reached by some other route silently drops its column — and the
cells in it — out of the printed table. Unioning costs a `Distinct()` and cannot lose a cell.

⚠ **Deliberate knock-on: `EmptyCells` rises on partially arranged levels.** Unarranged periods now print
as columns of hatched cells and the orange alert counts them. Those holes were always there; hiding the
column hid the hole with it, which is the opposite of what a pre-publication review is for.

### 2 · A safe act with no control is not a safe act

`AssignUnlabelled` has always existed and has always been the harmless half of partitioning — it fills
only `null` labels, so it cannot move a group that an existing plan placed. But the UI showed the assign
form **only while no label existed** (`showSetup = partitions.length === 0`). A promotion that later grew
groups could therefore be repaired only through « Redécouper », a full re-cut, sitting one button away
from « Supprimer les partitions » with no confirmation on it. That adjacency is what cleared level 3.

The rule worth keeping: **an unreachable safe path makes the destructive one the default**, and that is a
defect of the same size as an unguarded destructive action. Backend needed no change at all — the command
had taken `Reassign: false` since it was written.

### 3 · Moving a date is deleting it, for every window laid over it

Delete reported `SlotsSpanning`. Edit reported nothing — and edit is the path that actually runs, because
the whole lunar-date workflow *is* "enter the estimate in September, correct it when the décret lands".

`UpdateHolidayCommand` now reports the same number over the **union of the span it left and the span it
arrived at**. Both halves are affected, for opposite reasons: the old span's windows were laid around a
holiday that is no longer there; the new span's have just gained one they never counted. Counted **once**
where the spans overlap — the usual correction is a day, so both fall inside one window and counting twice
would name a number the confirmation cannot justify — and counted **before** the write, or the old span is
already gone.

`DatesMoved` gates the whole thing. Ticking « Date confirmée » on a span that was already right moves no
day count; reporting slots there is how a real report becomes one users dismiss. Same reasoning as
`IsConfirmed` itself: the flag is load-bearing precisely because it is *not* what changes the arithmetic.

### 4 · A partition is a fact about a cell (2026-08-13, reported from the running base)

3Med with two partitions printed **Chirurgie orange and Médecine green** under a legend reading
« Partition A / Partition B ». Both partitions pass through both stages — they mirror — so a colour
that tracks the stage cannot be a colour that tracks the partition.

`RepartitionRow.RotationGroup` was "the partition its first period belongs to". The row is a *service*,
and over the year a service holds groups from every partition; the crossover **is** that. So the row
band was never a fact about the row, and with `P = 2` it degenerates exactly onto the stage:

```
P1: A → Médecine,  B → Chirurgie        every Médecine row opens on A
P2: A → Chirurgie, B → Médecine         every Chirurgie row opens on B
```

⚠ **The dangerous shape of this bug is that it is self-consistent.** The colours were stable, the
legend matched the data it was given, and every row really did open on the partition named. Nothing
was inconsistent — the model was just answering a question nobody had asked. The tell was that the
answer never varied along a row, which is the one thing a crossover guarantees it must.

The handler already computed the band per cell and then discarded all but the first
(`bandByColumn[firstOccupied]`); the fix moves `RotationGroup` onto `RepartitionCell` and tints the
`<td>`. Med3 now reads A,B along the Médecine row and B,A along the Chirurgie row — the mirror, which
is the single most important thing the published document communicates.

⚠ **The old test encoded the bug**: `Rows.Select(r => r.RotationGroup).Should().Equal("A","A","B","B")`
passed, described the wrong model, and would have kept passing after any "fix" that preserved it. It
survived because `SeedAsync`'s fixture puts partition A only in Médecine and B only in Chirurgie —
a static split, not a crossover. A fixture that cannot exhibit the phenomenon cannot catch the bug in
it, and this one had been green through every session. Its replacement mirrors.

---

## The promotion split, audited against the base (2026-08-14, session 19)

`SplitAcademicGroupsPerLevel` is applied. Re-measured on the live dev database, every cross-promotion
invariant now holds:

| check | rows |
|---|---|
| rosters whose registrations span more than one promotion | **11** — and all 11 are the year's « Non réparti » bucket, which is what it is for |
| registrations whose year disagrees with their roster's | **0** |
| registrations whose promotion disagrees with their roster's | **0** |
| cohortes whose stage's promotion disagrees with their roster's | **0** |
| cells whose slot year disagrees with their roster's year | **0** |
| rosters with no promotion | **11** — exactly one per académic year, all `GroupNumber = 0` |

3,696 of 3,707 rosters carry a promotion. The data is clean; what was still open was the *code*.

### An index makes rosters distinguishable; it cannot stop them being mixed

`IX_AcademicGroup_Year_Level_Number` gives every roster an identity. It says nothing about who may
point *at* one, and two writes did so by plain FK with no promotion check — after which every
downstream guard is keyed on the roster the row claims, so nothing objects:

- **`TransferStudentCommand`** took any `TargetGroupId` that existed. A 3rd-year could be moved into a
  5th-year roster, or into last year's; `StudentAffectationService` then affects him to that roster's
  cohorts (stages he does not owe) and `SchedulePublisher` books him against the other promotion's
  service quota, because a cell's promotion is read off `Cohort.Stage.LevelId`. This is the single
  write that could have recreated by hand what the migration repaired across 1,003 rows.
- **`CreateCohortCommand`** paired any roster with any stage. `CohortProvisioner` has always checked
  this on the bulk path; the hand-built path had no equivalent.

Both refuse now, naming both promotions — see `AcademicGroupErrors` / `StageErrors`.

### The bucket needed closing from two more sides

`CohortProvisioner` still carried the legacy "or has a registration at that level" fallback. Post-split
the only rows without a `LevelId` are the buckets, and a bucket holds every promotion at once — so the
fallback matched it for *every* level named in a plan. It could only fire on a bucket carrying a
partition label, which `AssignRotationGroups` can no longer write; but `CreateGroup` and `UpdateGroup`
write `RotationGroup` directly and had no guard at all. Both are refused, and the fallback is gone.

⚠ **12 cohortes hang off buckets already** (2018-2019 ×3, 2019-2020 ×3, 2024-2025 ×6, 161 assignments,
0 cells). Legacy-import artefacts for students who had rotations but no roster. Left alone: they are
history, they carry no cell so they never reach a répartition, and the level filter in
`StudentAffectationService` keeps their assignments promotion-correct. The guard is on *creation*.

### « Groupe 1 » has to exist twice

The label index was `(year, label)`, so the obvious name for the 4th year's first roster was already
taken by the 3rd year's — a promotion could be *numbered* 1-60 per the faculty's own document but not
*named* that way. `GroupLabelPerPromotion` widens it to `(year, level, label)`, `NULLS NOT DISTINCT`.
A pure relaxation: the old key is a superset of the new one, so no existing row can collide.

---

## Why the 5th year's document became unreadable, and what the cell actually costs (2026-08-14)

Reported as "on stages with few services and many students we can't see all the groups in a période —
like médecine sociale in 5Med". Two independent causes, both in the published document.

**1 · The cell was held to one line.** `.repartition-doc__groups` was `white-space: nowrap` inside a
`table-layout: fixed` table. A `<td>` does not clip, so a cell wider than its column paints across its
neighbours and *both* périodes become unreadable. Measured at a 1280px document width on 5Med
2025-2026: **15 group tokens outside their own cell**. Now 0, at most two lines, and the whole document
20px taller.

⚠ **The length of that cell is a consequence of the partition strategy, not a defect.** Santé Publique
is *one* service for the whole 5th year, so a période's cell holds 6-7 groups whatever happens. Cut
`Contiguous` they collapse to « 21-27 » — five characters, and exactly what `MED05.png` prints. Cut
`Interleaved` (the default) they cannot collapse at all: « 3, 12, 21, 30, 39, 48, 57 », twenty-five.
Both are correct plans. The renderer must survive the second; the faculty may still prefer the first.

**2 · The palette wrapped at six.** `bandIndexes` mapped label → `i % 6`. The 5th year has **nine**
partitions (A–I) and the 6th has **ten** (A–J), so A and G printed in the same tint — under a legend
giving each its own swatch. A key asserting that two different things are one is worse than no key.

That was first fixed by suppressing the tint past six partitions and saying so in the legend. The
right answer, settled the same day, is that **the document should never have coloured by partition at
all**: a partition is scolarité's internal division for building the rotation, and the reader of this
page is a student looking for his own group, to whom « Partition G » explains nothing he can act on.
It now colours by **stage** and does not mention partitions anywhere — verified: the word does not
appear in the document's text, and the only surviving `title` is the chef-source note.

⚠ **Tinting by stage is not the old per-row partition band coming back.** That one asserted a row
belonged to one partition, which is false — a row visits every partition over the year, and that is
exactly what the crossover is. A row belongs to exactly one *stage*, so this states something true,
and it states what the first column already says in words. Which is also why the stage palette may
cycle where the partition palette could not: blocks are contiguous, separated by a heavy rule, and
each names itself, so five tints cover any promotion with **no two adjacent blocks alike** (verified
on 5Med: 7 stage blocks → tints 0,1,2,3,4,0,1, zero adjacent clashes). `RepartitionCell.rotationGroup`
is still sent and still shown where it is actionable — `ScheduleGridModal`, `AssignmentsPage`.

The legend is now one item, the hatch, which is the only mark the document makes that is not also
written out in words.

Two smaller things fell out of the same pass: the identity columns were `nowrap` + ellipsis, which cut
the *end* of every long service line — and the end is where the chef's name is; and the table's
`min-width` (a screen affordance bought against a horizontal scrollbar) was still in force in print,
where A4 landscape gives about 1060px and there is no scrolling, so the last périodes ran off the sheet.


---

## A service's load is not readable one period at a time (2026-08-14)

Asked for "saturation across all the stages active in the service at the same time". The reason that
could not already be read anywhere: **nothing ties two stages' periods together**. `StageSlot` is
keyed (stage, year, number), so Chirurgie P1 and ANES REA P1 have independent dates and legitimately
different lengths. List a service's load one créneau at a time and each number is that créneau's own
cohorts — while the students standing in the service on a given morning are the union of every window
covering that day. **The peak therefore lives in the overlap, and a per-slot list never shows it.**

`OccupancyTimeline` cuts the year at every boundary instead (each window's first day, and the day
after its last) so each row carries one exact simultaneous load. Pure, like `PeriodAxis` and
`RotationTiling`, so the arithmetic is tested rather than seeded. Two traps worth keeping:

- boundaries are `start` and **`end + 1`**. Using `end` makes the last day of one window and the
  first of the next share a boundary, and two back-to-back windows merge into one row showing a load
  neither ever had;
- stretches with nobody in them are dropped. A row reading 0 suggests something is planned there.

⚠ **It measures the load exactly the way the guard does** — `Cohort.Assignments.Count`, planned cells
rather than `ServicePeriod`s, date overlap, and the same `HasLevelRestrictions` branch as
`SchedulePublisher.EnsureCapacityAsync`. A page that explained a refusal with a number that never
produced it would be worse than no page.

### What the measurements say about why this was needed

| | |
|---|---|
| planned cells 2025-2026 | 353 |
| over capacity | **233 (66%)** |
| worst cell | **85 students against 20** |
| services carrying the imported default `Capacity = 20` | **148 of 148** |
| `ServiceLevelCapacity` rows authored | **0** |

So today every capacity verdict in the base is measured against a number nobody wrote, and the whole
per-level quota machinery — built, documented, tested — is unused. The page states which of the two
rules is in force on every service, because an empty quota table reads as "not configured yet" when
it means "open to everyone", and a total on a restricted service reads as live when it is dead data.

✅ **`AllowOverCapacity` is split** (2026-08-17). `EnsureCapacityAsync` → `EnsureIntakeAsync`, called
unconditionally: **admissibility is checked whatever the caller asks for**, occupancy only when the
override is off.

The 66% is not background colour here — it *is* the argument. A flag that has to be ticked on two
thirds of the plan is ticked as a matter of routine, so whatever else it happened to govern was
switched off every time it was reached. **A rule enforced only when nobody needs the override is not
enforced**, and this one was the rule against sending 1ère année to a service that does not take
them. The same table also says why the *other* half must stay waivable: with 148 of 148 services on
the imported default and 0 quotas authored, a capacity verdict is measured against a number nobody
wrote, and refusing on it outright would stop planning altogether.

Two consequences worth keeping. The refusal now says it cannot be forced — the checkbox is on screen
promising otherwise, and its description literally listed « service n'accueillant pas cette
promotion » among what it would push through. And the occupancy lookup, the expensive half, is built
only when a number will actually be read, so splitting the flag did not make the common publish
slower than when it skipped everything.

⚠ **Still open** (same review, in the order that pays): real capacities first; then turn
`RotationArranger`'s bare `saturatedServices` count into the same service × période × overflow report
this page now gives; then change `BuildServiceQueue`'s objective to distribute unavoidable excess in
proportion to capacity instead of dropping services smaller than one cohort; and close the two silent
degeneracies (`shiftPerSlot == 0` when cohorts < périodes, weight 0 excluding a service outright).

---

## Several périodes is not several services (2026-08-14)

Asked for the right to run a multi-période stage in **one** service with **one** evaluation — the
example given was 5MED Gynécologie: k=3 périodes of ~20 days each, and splitting that across three
services is not how the faculty runs it.

Before designing anything I asked the imported Access history what the faculty actually did. Periods
recorded per (student, stage):

| promotion | 1 period | 2 | 3 |
|---|---|---|---|
| **5ᵉ Médecine** | **30,614** | 0 | 0 |
| **6ᵉ Médecine** | 21,309 | 1 | 0 |
| 4ᵉ Médecine | 26,156 | 294 | 1 |
| 3ᵉ Médecine | 6,557 | **5,385** | **409** |

So this is not a new mode: **5ᵉ and 6ᵉ année have always been one service, one mark**, and the
per-période rotation PGSH assumed as universal is the 3ᵉ/4ᵉ année case. 5MED Gynécologie in
2025-2026 is one imported period averaging **70 calendar days** against a catalogue of 44 j.o. —
three columns of the axis, one service. The old CLAUDE.md note "a period is one *service*, not one
stage" was right about why the axis needs `kₛ` columns and wrong to conclude that the group must move
between them. `Stage.RotationMode` separates the two questions.

The mechanical change is small and that is the point: the axis, the cells, the conflict guard and the
printed répartition are all untouched, because the group genuinely does occupy all `kₛ` columns. The
arranger freezes its rotation offset across the run instead of advancing it per column; the publisher
collapses the run's cells into one `ServicePeriod`. Nothing in `StageScoring` changed at all — the
mean of one mark is that mark.

⚠ **What it cost was the cell↔period 1:1.** Four guards ask "is this cell published?" by reading
`ServicePeriod.CohortSlotAssignmentId`, which under `SingleService` names only the *first* cell of a
run — so the trailing cells read as free, and the arranger would rewrite them or `DeleteStageSlot`
would drop a column out from under a running stage. `ServicePeriodSlotCoverage` is the honest
one-to-many, written under both modes so the guards read one table. **The backfill was zero rows**
(see below), which is the cheapest this refactor will ever be.

### Did the automatic répartition overwrite the imported data? No — nothing was ever published

| 2025-2026 | slots | cells arranged | cells published |
|---|---|---|---|
| 5MED | 63 | 540 | **0** |
| 6MED | 60 | 0 | 0 |
| 3MED | 8 | 320 | **0** |

Across all 51 (year × promotion) pairs, `ServicePeriods` with a grid link = **0**. Every one of the
~104k periods is an ad-hoc row (`CohortSlotAssignmentId IS NULL`) exactly as `LegacyImportPlanner`
wrote it. Auto-arrange only ever writes `CohortSlotAssignment` cells; `SchedulePublisher.BuildPeriods`
only ever `Add`s. There is no delete path from planning to execution.

⚠ **But publishing would have duplicated every 5MED student.** All 706 assignments of 5MED 2025-2026
already carry an imported period per stage, and `IsPublishedAsync` counts only *grid-linked* periods —
so it would not have seen them, would have published on top, and each student would have ended with two
sets for one stage: averaged into the note by `RecomputeFinalScore`, and waited on by the lifecycle
before the stage could reach `Evaluated`. Publishing now skips any assignment that already holds a
period and reports the count (`SkippedAlreadyServed`). Per assignment, not per cohort — a cohort mixes
repeaters and délocalisés with students who still need their schedule.

### The undo chain was right, and its first link was not guarded

`unpublish → clear cells → delete slot` is the correct shape, and links 2 and 3 were properly guarded
(`SlotPublished`; `ClearSlotAssignments` skips published cells and reports the count). Link 1 was not:
`UnpublishCohortScheduleCommandHandler` deleted the periods with **no check at all**, and
`ServiceEvaluation`, `AttendanceRecord`, `PeriodPause` and `Delocalization` all cascade from
`ServicePeriod`. So unpublishing a cohort mid-rotation silently destroyed every mark a chef had
entered and every day of attendance recorded. It also left `Status` and `FinalScore` untouched, so an
assignment could read *Validated, 14.5* with zero periods behind it.

Now: refused with `ScheduleUnderway` naming what would be lost, `Force: true` for the caller who has
read that sentence, removal through the aggregate so status and note are recomputed, and ad-hoc
periods explicitly left alone (`AdHocPeriodsKept`). The UI asks twice — the second time showing the
server's own count.

---

## A count taken from a subset — the same defect in three places (2026-08-16, session 18)

Session 17 ended with five open findings. Three of them are one shape, and it is not the shape the
handoff guessed: **a number computed over part of a set and then used as if it described the whole.**
Each time the wrong number was plausible, self-consistent, and silently written to disk.

### The partition count of a promotion, read from one stage's cohorts

`PartitionAllocator.BuildLabels` resolves "how many partitions are there" from the labels it is given,
and `RotationArranger` gave it the cohorts of the stage being arranged. That is a subset by
construction — `CohortProvisioner` skips a stage a text does not require, and cohorts are provisioned
stage by stage — so a promotion cut into ten, seen through a stage whose cohorts carried only A and B,
*is* a promotion cut into two. Every unlabelled roster was then filled into those two and written.

Med6's **A = 42, B = 42, C–J = 2 each** is exactly that: 80 rosters filled 40/40 on top of a clean
A–J × 2. It is not what `ReassignAll` produces (it fills the smallest partition each time, so it cannot
leave that distribution), which is what made it look inexplicable last session. The suspect named in
the handoff, `AutoArrangeGroupsCommandHandler`, never writes `RotationGroup` at all.

Two further consequences of the same subset:

- **The balance was measured over it too.** "Fill the smallest partition" against a stage's cohorts is
  not the promotion's smallest partition, so two stages gap-filling in different orders disagree and
  neither matches the promotion.
- **The mirror case is silent.** A stage whose own cohorts are all unlabelled made `alreadyCut` false,
  so a promotion that *is* cut got `PromotionNotPartitioned` — or, with no partition targeted, its
  rosters were simply left unlabelled and invisible to every partition filter downstream.

`PromotionPartitioning` reads the cut from (année, niveau). ⚠ The write is deliberately narrower than
the read: an arrange labels only the rosters **it is placing**. Partitioning a roster it never touches
is `AssignRotationGroupsCommand`'s act, and that command has the strategy, the published-cells refusal
and the audit entry that make it one.

### The promotion itself, read as "the year"

`AssignRotationGroupsCommand` and `ClearRotationGroupsCommand` applied the level filter *only when a
level was given*. Year-wide they cut every promotion at once — three numberings with different
partition counts, folded by `BuildLabels` into one — and reached « Non réparti », whose labelling moves
4,725 students of every promotion as a single body. CLAUDE.md already asserted that
`AssignRotationGroups` could no longer reach the bucket. It could. `int LevelId` on both commands is
the guard, expressed where it cannot be forgotten.

### The promotion's size, read off a page

The Plan macro tab derived the partitions, each one's size and « N groupes sans partition » from
`GET /groups` at `pageSize: 200`. A promotion adds ~100 rosters a year, so past 200 every one of those
numbers reads low — including the one whose only job is to say a gap-fill is owed.
`GetPromotionPartitioningQuery` counts them where the rows are. **Raising a page size does not fix a
count; it moves the cliff.**

### And one number that was never wrong, because nobody could write it twice

Not from that family, but closed in the same pass: `IX_AcademicYear_IsCurrent`, a partial unique index
on `IsCurrent`. `AcademicYearResolver` takes the *first* row flagged current, so two flagged at once is
two screens disagreeing about which promotion they are showing, with nothing on either to say so.
`CreateAcademicYear` demotes the others — one write path guarding an invariant of the table.

⚠ **Two Phase-13 items turned out never to have been real.** `Registration.LevelId` and
`Registration.AcademicGroupId` are not missing indexes: EF Core creates one per foreign key by
convention, and both exist. Scaffolding the "fix" produced a `RenameIndex` and nothing else.

### Measured live, 2026-08-16 — and the promotion was not the one we thought

The lopsided cut was **not** on Med6. Med6 reads A–J × 10, clean. The defect is on **4ème année
Médecine** and **5ème année Pharmacie**, and the two are identical to the roster:

```
A  13  1,10,19,28,37,46,55, 61,63,65,67,69,71
B  13  2,11,20,29,38,47,56, 62,64,66,68,70,72
C   7  3,12,21,30,39,48,57      G  6  7,16,25,34,43,52
D   7  4,13,22,31,40,49,58      H  6  8,17,26,35,44,53
E   7  5,14,23,32,41,50,59      I  6  9,18,27,36,45,54
F   7  6,15,24,33,42,51,60
```

Groups **1–60** are a textbook interleave over nine partitions. Groups **61–72** — the twelve rosters
the promotion gained afterwards — alternate over **two**. That is the subset gap-fill and nothing else:
no other write path can produce a label set of exactly {A, B} on a promotion cut nine ways.
`ReassignAll` fills the smallest partition each time and cannot leave it; `AssignRotationGroupsCommand`
would have been shown all nine labels and used them.

⚠ **The severity of this defect is invisible in the partition sizes.** A = 13 against C = 7 looks like
rounding. It is not: it is 12 rosters — ~240 students — in the wrong half of every crossover the
promotion will ever run. Session 17 spotted it on Med6 only because the numbers there were absurd
(42 / 42 / 2), and Med6 was subsequently re-cut by hand, which is why the absurd case had disappeared
by the time the cause was found. **The mild case is the dangerous one**, and it survived a whole
session of looking straight at it.

Both promotions were repaired the same day — cleared and re-cut into 9 *Alterné*, giving 8 per
partition. Neither carried a planned cell or a published period, and the totals were byte-identical
across the repair: 13,604 cohortes, 860 cellules, 98,555 affectations, 105,626 périodes. That is the
`ClearRotationGroupsCommand` guarantee holding in practice, on real data, for the second time.

## « Retrait » — not damaged data, and that is the point (2026-08-16)

Raised as "damaged data we fetched from the Access db". It is not. `CODE_N = 'MED00'` is a **withdrawal
marker** the old base wrote in place of a year of study, and `LevelMapper` mapped it to a `Level` with
`Year = 0` on purpose — with a comment saying so — because dropping those rows would have destroyed
real history.

**The evidence that it is coherent:**

| | |
|---|---|
| registrations at level « Retrait » | 12, **all** `Status = Withdrawn` |
| carrying real rotations | **8 of 12** — up to 5 périodes each |
| stages / cohortes at that level | **0 / 0** |
| parcours shape | 1ère → 2ème → 3ème → **Retrait** |
| students who came back afterwards | 2 (Retrait 2023-24 → 5ème année 2025-26; Retrait 2018-19 → 4ème 2019-20) |

Eight students served stages that year and then left; two later returned. Dropping the marker rows
would have deleted both facts. Keeping them was right.

⚠ **And it cannot be repaired anyway**: MED00 *replaced* the real year in the source, so the year each
student withdrew from is gone, not corrupted. There is nothing to reconstruct.

### What it does cost — a marker offered wherever a promotion is

Because it is a `Level`, it behaves like a promotion in every screen and every command that takes a
`levelId`. Two things followed:

- « Retrait » was **selectable in the planning pickers**, next to « Troisième Année ».
- One of its rosters carried partition **E**. Not a deliberate cut: `SplitAcademicGroupsPerLevel`
  shredded the folded roster « Groupe 59 » into one row per (year, level) and copied the parent's
  `RotationGroup` onto every shard, the Retrait one included. Inert — the marker has no stages, so
  nothing could ever be provisioned — but wrong, and 1 of its 10 rosters carried it.

`CnpnTargetPlanner` had **already** been forced to special-case year 0 by hand (« année ≤ 2 » must not
sweep the withdrawn into a new text). That is the tell: a rule that has to be rediscovered at each
call site is a rule that belongs on the entity. Hence `Level.IsPromotion`, the refusal on assign and
auto-arrange, and `PromotionsOnly` on the levels read.

⚠ **The read filter is off by default, deliberately.** A withdrawn registration still has to be able to
name its level in the dossier, the parcours and the catalogue. Only the screens that ask "which
promotion am I planning?" pass it. Same reasoning as `GetAcademicGroupsQuery` keeping the wider reach
so scolarité can still see « Non réparti »: **hiding a row from the screen that exists to show it is
its own defect.**
