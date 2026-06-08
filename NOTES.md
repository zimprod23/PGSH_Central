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

The schedule is modelled as a **grid**: rows = cohorts, columns = time periods (StageSlots), cells = service assignments (CohortSlotAssignments). `StageSlot` defines a named time period (P1, P2...) belonging to a Stage with start/end dates. `CohortSlotAssignment` is a grid cell — it maps one Cohort to one Service for one StageSlot. This mirrors the actual paper scheduling documents used by the faculty.

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
| StageSlot | Time period column (P1, P2...) in the schedule grid — belongs to a Stage |
| CohortSlotAssignment | Grid cell — maps one Cohort to one Service in one StageSlot |
| Period | `ServicePeriod` — actual execution record for a student in a service |
| Assignment | `InternshipAssignment` — one student enrolled in one cohort |
| Level | Academic year of study (Year 1–6 in Medicine/Pharmacy) |
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

- **Non-validated stages stay with the student (revalidation)**: A student who receives `Result = NonValidé` on a stage does **not** stop progressing. They continue through subsequent academic years normally. The failed stage "sticks" with them — the original `InternshipAssignment` with `Result = NonValidé` remains in the system as a permanent record. At some later year (could be final year), the student is assigned to a cohort doing that same stage again and receives a new `InternshipAssignment` for that attempt. If they pass, that new assignment gets `Result = Validé`. The old failed assignment is never deleted or modified — it is the audit trail. A `History` record of type `Revalidation` marks when this process begins. **Implication:** a student can have multiple `InternshipAssignment` records for the same `Stage` across different academic years. Queries that check "has the student passed Stage X" must look for any `InternshipAssignment` where the `Cohort.StageId == X` and `Result == Validé`, not just the most recent one.

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
- `AcademicGroup.RotationGroup` is a persistent nullable string label (A, B, C…). **Partitions are scoped per (AcademicYear, Level)** — different levels can have different partition counts (e.g. 2 in 1Med, 4 in 2Med). `AssignRotationGroupsCommand` takes an optional `LevelId`; a group belongs to a level by `AcademicGroup.LevelId`, or (legacy groups without one) by having a registration at that level. `CohortProvisioner` matches groups to each stage's level so a label reused across levels never produces cross-level cohorts. The label is set once and reused across all stages **of that level**.
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
- `SchedulePublisher.EnsureCapacityAsync` — **pre-publish guard** (previously absent): both `PublishCohortAsync` and `PublishStageAsync` refuse with `StageErrors.CapacityExceeded` if any service would exceed capacity over an overlapping window — **unless `allowOverCapacity` is set**, an explicit opt-in override threaded from the publish commands/endpoints (`PublishCohortScheduleCommand.AllowOverCapacity`, `PublishStageScheduleCommand.AllowOverCapacity`, `GenerateMacroPlanCommand.AllowOverCapacity`). When the override is on, the guard is skipped and over-booking is permitted intentionally. The frontend exposes it as an "Autoriser le dépassement de capacité" checkbox in the publish confirm dialogs (per-cohort and "Publier tout"). Distinct cohorts across stages are different physical students, and a group's cohort-in-X vs cohort-in-Y run in non-overlapping windows, so summing overlapping windows does not double-count. Counting `CohortSlotAssignment`s covers both planned and published load (publish keeps the cells); purely ad-hoc `ServicePeriod`s (null `CohortSlotAssignmentId`) are not yet included.

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

## Open Questions / Things to Verify

- **`GenerateScheduleCommandHandler`**: Does it correctly handle the case where students are transferred between cohorts mid-year? The `CohortMembership` model supports it but the handler logic needs review.
- **`IsCurrent` flag on `AcademicYear`**: Is this maintained automatically or manually? Currently no logic enforces that only one year has `IsCurrent = true`. Should there be a check in `CreateAcademicYear` or `UpdateAcademicYear`?
- **`TodoDatabase` connection name**: This is the Aspire-assigned connection name for the main PostgreSQL database. It's a legacy artifact from project scaffolding. Consider renaming it in `AppHost/Program.cs` and `appsettings` to something like `"pgsh-db"` for clarity.
- **`Employee.WorkPlace`**: The enum has values `Hospital` and `Fmpr`. Is `Fmpr` still the correct name for the faculty workplace, or should it be renamed to match the actual institution name?
- **Domain events on Hospital/Center/Service**: Currently no domain events are raised on creation or modification of these entities. If downstream notifications (e.g., service capacity changes affecting rotation scheduling) are needed, events should be added.
- **`Student.Ranking`**: What is this field for? National ranking for program entry? It's nullable and has no business logic around it.
- **Revalidation cohort assignment**: When a Year 4 student needs to redo a Year 1 Stage, which `AcademicGroup` / `Cohort` do they get assigned to? Are there dedicated revalidation cohorts mixing students from different years and groups, or are they slotted into an existing cohort for that stage? The current `Cohort.AcademicGroupId` FK assumes a cohort is for one specific group. Needs clarification before implementing the revalidation assignment flow.
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
