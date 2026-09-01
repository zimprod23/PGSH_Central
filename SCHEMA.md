# SCHEMA.md — PGSH Database Schema

This document is the authoritative reference for the PostgreSQL schema used by PGSH.
All tables live in the `public` schema. IDs are `uuid` unless otherwise noted.
EF Core configurations are in `PGSH.Infrastructure/` — one config file per domain folder.

---

## Entity Relationship Overview

```
Center (int)
  └── Hospital (int)
        └── Service (int)
              ├── Staff ←→ Employee (many-to-many, shadow join table)
              └── ServiceChef → Employee (nullable FK)

AcademicYear (int)
  └── AcademicGroup (int)
        └── Registration (uuid)          ← Student (uuid, TPH)
              ├── PriorEnrolment (uuid)  [équivalence, only on an entry from outside]
              └── InternshipAssignment (uuid)
                    ├── CohortMembership (uuid)   [transfer history]
                    └── ServicePeriod (uuid)
                          ├── AttendanceRecord (uuid)
                          └── ServiceEvaluation (uuid)
                                └── ObjectiveScore (uuid)

CnpnVersion (int)                    ← one ministerial text (arrêté)
  └── Curriculum (int)               ← what it requires of one level  [+ CnpnVersionId on Student]
        └── CurriculumStage (int)    → Stage

Level (int)
  └── Stage (int)
        ├── StageObjective (int)          ← ObjectiveScore references these
        ├── StageSlot (int)               ← time period columns (P1, P2, ...) — per academic year
        └── Cohort (int)
              ├── CohortSlotAssignment (int)  → StageSlot + Service (grid cells)
              └── InternshipAssignment         [CurrentCohortId FK]

ServicePeriod → CohortSlotAssignment (nullable, tracks planned vs. ad-hoc)
Student → History (audit trail)
```

---

## Tables

### `Users` — TPH for User / Student / Employee

| Column | Type | Constraints |
|---|---|---|
| `Id` | uuid | PK |
| `UserType` | varchar | Discriminator: `User`, `Student`, `Employee` |
| `Email` | varchar(255) | NOT NULL, UNIQUE |
| `UserName` | varchar(100) | nullable |
| `FirstName` | varchar(100) | nullable |
| `LastName` | varchar(100) | nullable |
| `CIN` | varchar(20) | nullable |
| `Gender` | varchar | enum: `Male`, `Female` |
| `DateOfBirth` | date | nullable |
| `PlaceOfBirth` | varchar | nullable |
| `IdentityProviderId` | varchar(100) | UNIQUE (nullable filter) — Keycloak subject |
| `IdentityLinkedAt` | timestamp | set on first Keycloak login |
| `Address_FullAddress` | varchar(250) | owned, nullable |
| `Address_City` | varchar(100) | owned, nullable |
| `Address_Street` | varchar(100) | owned, nullable |
| `Address_ZIP` | varchar(20) | owned, nullable |
| `Address_HouseNumber` | varchar(20) | owned, nullable |
| `Address_Country` | varchar(100) | owned, nullable |
| `Status_CivilStatus` | varchar(50) | owned enum: `Civil`, `Militaire` |
| `Status_NationalityStatus` | varchar(50) | owned enum: `Marocaine`, `Etrangaire` |
| **Student columns** | | Discriminator = `Student` |
| `AcademicProgram` | varchar | enum: `Medecine`, `Pharmacie`, `Master`, `Doctorat` |
| `CNE` | varchar(50) | NOT NULL, UNIQUE |
| `Appogee` | varchar(50) | **NOT NULL**, UNIQUE — see the warning below |
| `AccessGrade` | decimal(5,2) | default 10.01 |
| `BacSeries` | varchar | enum |
| `AgreementType` | varchar | enum, default `None` |
| `BacYear` | varchar(10) | |
| `Academy` | varchar | enum, nullable |
| `Province` | varchar | enum, nullable |
| `Ranking` | int | nullable |
| `CnpnVersionId` | int | nullable, FK → CnpnVersions — the text governing this student, fixed at entry. **The frozen membership**: written only by `Student.AssignCnpnVersion`, never moved in bulk once confirmed |
| `CnpnAssignmentIsInferred` | bool | nullable — true when entry was deduced from the level rather than read. An inferred stamp may be upgraded; a confirmed one may not be moved |
| **Employee columns** | | Discriminator = `Employee` |
| `PPR` | varchar(50) | nullable |
| `Grade` | varchar | enum: `MC`, `PES`, `PH`, `Nurse`, `Administrator` |
| `Position` | varchar | enum: `ServiceChef`, `Normal`, nullable |
| `Label` | varchar(100) | nullable |
| `WorkPlace` | varchar | enum: `Hospital`, `Fmpr`, nullable |
| `PvSignatureDate` | date | nullable |

**Indexes:** `IX_Users_Email` (unique), `IX_Users_IdentityProviderId` (unique, null filter), `IX_Student_CNE` (unique), `IX_Student_Appogee` (unique, filtered `"Appogee" IS NOT NULL`)

⚠ **`IX_Student_Appogee`'s filter is vestigial, and it reads as though absence were allowed.**
The column is `IsRequired()` in the model, so `"Appogee" IS NOT NULL` can never be false — which
means an empty string is a *value*, and the second student written without an Apogée collides
with the first. **CNE and Apogée are both NOT NULL UNIQUE**: any path creating a student must
supply or manufacture both. This line said "nullable" until 2026-08-30 and the inscription import
was written against it — the in-memory provider caught it, unusually, because it enforces required
properties even though it enforces no unique index.

---

### `Histories`

| Column | Type | Constraints |
|---|---|---|
| `Id` | uuid | PK |
| `StudentId` | uuid | FK → Users, CASCADE |
| `HistoryData` | varchar | enum: `Inscription`, `ValidationStage`, `NonValidation`, `Fraud`, `Revalidation` |
| `CreatedAt` | timestamp | NOT NULL |
| `Metadata` | jsonb | nullable — arbitrary event payload |

---

### `AcademicYears`

| Column | Type | Constraints |
|---|---|---|
| `Id` | int | PK (identity) |
| `Label` | varchar(20) | NOT NULL, UNIQUE — e.g. `2025-2026` |
| `StartDate` | date | NOT NULL |
| `EndDate` | date | NOT NULL |
| `IsCurrent` | bool | flag for the active year |

---

### `AcademicGroups`

| Column | Type | Constraints |
|---|---|---|
| `Id` | int | PK (identity) |
| `AcademicYearId` | int | FK → AcademicYears, CASCADE |
| `Label` | varchar(100) | NOT NULL |
| `GroupNumber` | int | NOT NULL |
| `GeographicZone` | varchar | nullable — used by auto-arrange clustering |

**Indexes:** `IX_AcademicGroup_Year_Number` (AcademicYearId, GroupNumber) UNIQUE, `IX_AcademicGroup_Year_Label` (AcademicYearId, Label) UNIQUE

---

### `Levels`

| Column | Type | Constraints |
|---|---|---|
| `Id` | int | PK (identity) |
| `Label` | varchar(100) | NOT NULL |
| `Year` | int | NOT NULL, range 0–10 |
| `AcademicProgram` | varchar | NOT NULL, enum: `Medecine`, `Pharmacie`, `Master`, `Doctorat` |

**Indexes:** `IX_Level_Year_Program` (Year, AcademicProgram) UNIQUE

---

### `Registrations`

| Column | Type | Constraints |
|---|---|---|
| `Id` | uuid | PK |
| `StudentId` | uuid | FK → Users, CASCADE |
| `AcademicYearId` | int | FK → AcademicYears, RESTRICT |
| `LevelId` | int | FK → Levels, RESTRICT |
| `AcademicGroupId` | int | FK → AcademicGroups, SET NULL, nullable |
| `Status` | varchar | NOT NULL, enum: `Pending`, `Active`, `Validated`, `Failed`, `Withdrawn` |
| `RegistrationDate` | timestamp | nullable |
| `failureReasons_Description` | varchar(500) | owned, nullable |
| `failureReasons_Notes` | jsonb | owned, nullable — list of note strings |
| `failureReasons_Cheat` | bool | owned, nullable |

**Indexes:** `IX_Registration_Student_Year` (StudentId, AcademicYearId)

---

### `Centers`

| Column | Type | Constraints |
|---|---|---|
| `Id` | int | PK (identity) |
| `Name` | varchar(100) | NOT NULL |
| `CenterType` | varchar | NOT NULL, enum: `None`, `CHU`, `CHR`, `CHP`, `CSU` |
| `City` | varchar(50) | nullable |
| `X` | varchar(50) | owned GPS x-coordinate, nullable |
| `Y` | varchar(50) | owned GPS y-coordinate, nullable |
| `Z` | varchar(50) | owned GPS z-coordinate, nullable |

---

### `Hospitals`

| Column | Type | Constraints |
|---|---|---|
| `Id` | int | PK (identity) |
| `CenterId` | int | FK → Centers, RESTRICT |
| `Name` | varchar(100) | NOT NULL |
| `City` | varchar(50) | NOT NULL |
| `HospitalType` | varchar | NOT NULL, enum: `None`, `Autre`, `Spetialité`, `Central`, `CHU`, `LHOMA` |
| `Description` | varchar(500) | nullable |
| `Email` | varchar(100) | nullable |
| `X` | varchar(50) | owned GPS, nullable |
| `Y` | varchar(50) | owned GPS, nullable |
| `Z` | varchar(50) | owned GPS, nullable |

---

### `Services`

| Column | Type | Constraints |
|---|---|---|
| `Id` | int | PK (identity) |
| `HospitalId` | int | FK → Hospitals, RESTRICT |
| `ServiceChefId` | uuid | FK → Users (Employee), RESTRICT, nullable |
| `Name` | varchar(100) | NOT NULL |
| `Description` | varchar(500) | nullable |
| `ServiceType` | varchar | NOT NULL, enum: `Biologie`, `Chirurgie`, `Medical` |
| `Capacity` | int | NOT NULL, default 20 |

**Many-to-many:** `EmployeeService` shadow join table (ServiceId → Services, EmployeesId → Users)

---

### `Stages`

| Column | Type | Constraints |
|---|---|---|
| `Id` | int | PK (identity) |
| `LevelId` | int | FK → Levels, RESTRICT |
| `Name` | varchar(100) | NOT NULL |
| `Coefficient` | int | NOT NULL, default 1 |
| `DurationInDays` | int | NOT NULL, default 30 — planning reference |
| `Description` | varchar | nullable |

---

### `CnpnVersions` — one issue of the Cahier des Normes Pédagogiques Nationales

| Column | Type | Constraints |
|---|---|---|
| `Id` | int | PK (identity) |
| `Code` | varchar(50) | NOT NULL — the arrêté number as cited, e.g. `1650.25` |
| `Label` | varchar(200) | NOT NULL |
| `AcademicProgram` | text | NOT NULL — varchar via `HasConversion<string>()` |
| `TotalYears` | int | NOT NULL — 7 under arrêté 2174.18, 6 under 1650.25 |
| `Reference` | varchar(300) | nullable — Bulletin Officiel number and date |
| `AppliesToEntrantsFromAcademicYearId` | int | **nullable**, FK → AcademicYears, RESTRICT |

**Indexes:** `IX_CnpnVersion_Program_Code` (AcademicProgram, Code) UNIQUE

> A CNPN applies to a **cohort**, not to a year, and follows it to graduation. Arrêté 1650.25
> (BO 7422, 17 July 2025) took Médecine from 7 years to 6 from 2024-2025 while art. 2 leaves everyone
> registered earlier under 2174.18 *in its pre-2175.22 form*. A **null**
> `AppliesToEntrantsFromAcademicYearId` marks a text kept for citation that governs no intake — which
> is exactly what 2175.22 became.

> ⚠ **Deleting a row here is asymmetric, and the application gates it.** `Curriculums` cascades from
> this table (its requirement sets and their `CurriculumStages` go silently), while `Users` is
> `NO ACTION` (a raw foreign-key violation). `DeleteCnpnVersionCommand` therefore refuses while any
> student carries the stamp — inferred stamps included — and reports how many requirement sets the
> cascade removed. Allowing the cascade is safe only because of that gate: a text nobody follows has
> nobody who could owe anything.

> **There is deliberately no `CnpnTargetRules` table.** Who a text binds is authored as a rule
> (programme + `année ≤ N` + as-of year) and applied once — see `Application/Stages/Cnpn/Targeting/`.
> Persisting the rule as live state would re-target people: re-evaluated next September, "année ≤ 2"
> selects a different set, and the whole point of the stamp is that a student's text does not move
> under them. What survives is the **membership** (`Users.CnpnVersionId`) plus the apply command's
> row in `AuditLogs`, which records the criteria, the author and the date.
>
> The rule reaches only students who already exist. Future intakes are covered by
> `AppliesToEntrantsFromAcademicYearId` above — a text needs both halves.

---

### `Curriculums` — what one CNPN requires of one level

| Column | Type | Constraints |
|---|---|---|
| `Id` | int | PK (identity) |
| `LevelId` | int | FK → Levels, RESTRICT |
| `CnpnVersionId` | int | FK → CnpnVersions, CASCADE |
| `Reference` | varchar(200) | nullable — the ministerial text this set came from |

**Indexes:** `IX_Curriculum_Version_Level` (CnpnVersionId, LevelId) UNIQUE

> Keyed on the **text**, not the academic year. It was keyed on the year until 1650.25 made that
> impossible: from 2026-2027 one (level, year) holds students of two texts — those arriving on the
> six-year CNPN and those repeating under the seven-year one — so a year cannot identify a
> requirement set. Still a **requirement set** rather than a validity window on `Stage`: a window
> would force someone to know when a stage ends and could not express one dropped then reinstated.
> `Stage` stays the timeless catalogue entry so historical assignments keep a stable identity.

---

### `CurriculumStages` — one stage required by a curriculum

| Column | Type | Constraints |
|---|---|---|
| `Id` | int | PK (identity) |
| `CurriculumId` | int | FK → Curriculums, CASCADE |
| `StageId` | int | FK → Stages, RESTRICT |
| `Coefficient` | int | NOT NULL — **that text's** weight |
| `DurationInDays` | int | NOT NULL — **that text's** duration |

**Indexes:** `IX_CurriculumStage_Curriculum_Stage` (CurriculumId, StageId) UNIQUE

> Coefficient and duration live here, not only on `Stage`, because a CNPN can keep a stage and reweight
> it. `Stage.Coefficient` remains the catalogue default used when adding a new entry.

---

### `StageObjectives`

| Column | Type | Constraints |
|---|---|---|
| `Id` | int | PK (identity) |
| `StageId` | int | FK → Stages, CASCADE |
| `Label` | varchar(200) | NOT NULL |
| `Description` | varchar | nullable |
| `Weight` | int | NOT NULL |
| `IsMandatory` | bool | NOT NULL |

---

### `Cohorts`

| Column | Type | Constraints |
|---|---|---|
| `Id` | int | PK (identity) |
| `StageId` | int | FK → Stages, RESTRICT |
| `AcademicGroupId` | int | FK → AcademicGroups, RESTRICT |
| `Label` | varchar(100) | NOT NULL |

---

### `StageSlots`

Time period columns for the schedule grid — one row per period (P1, P2, ...) per Stage **per academic
year**. The window carries concrete dates and the stage runs again next year over different ones, so
the year is part of the identity, not decoration.

| Column | Type | Constraints |
|---|---|---|
| `Id` | int | PK (identity) |
| `StageId` | int | FK → Stages, CASCADE |
| `AcademicYearId` | int | FK → AcademicYears, RESTRICT |
| `PeriodNumber` | int | NOT NULL — display order (1, 2, 3, ...) |
| `Label` | varchar(50) | nullable — optional human-readable name (e.g., "Janvier") |
| `StartDate` | date | NOT NULL |
| `EndDate` | date | NOT NULL |

**Indexes:** `IX_StageSlot_Stage_Year_Period` (StageId, AcademicYearId, PeriodNumber) UNIQUE

---

### `CohortSlotAssignments`

Grid cells — maps one Cohort to one Service for one StageSlot. Unique per (Cohort, Slot) pair.

| Column | Type | Constraints |
|---|---|---|
| `Id` | int | PK (identity) |
| `CohortId` | int | FK → Cohorts, CASCADE |
| `StageSlotId` | int | FK → StageSlots, CASCADE |
| `ServiceId` | int | FK → Services, RESTRICT |

**Indexes:** `IX_CohortSlotAssignment_Cohort_Slot` (CohortId, StageSlotId) UNIQUE

> **Capacity rule:** For each (StageSlot × Service) pair, the sum of students across all cohorts assigned there must not exceed `Service.Capacity`. Enforced at publish time by `PublishCohortScheduleCommandHandler`.

---

### `InternshipAssignments`

| Column | Type | Constraints |
|---|---|---|
| `Id` | uuid | PK |
| `RegistrationId` | uuid | FK → Registrations, CASCADE |
| `CurrentCohortId` | int | FK → Cohorts, RESTRICT |
| `Status` | varchar | NOT NULL, enum: `Planned`, `Ongoing`, `Completed`, `Evaluated`, `Validated`, `Rejected` |
| `FinalScore` | decimal(5,2) | nullable — derived from ServiceEvaluations, stored for performance |
| `Result` | varchar | nullable, enum: `NonÉvalué`, `Validé`, `NonValidé` |

**Indexes:** `IX_InternshipAssignment_RegistrationId`, `IX_InternshipAssignment_CohortId`

> `FinalScore` is a cached aggregate — it must be recomputed whenever ObjectiveScores change.

---

### `CohortMembership`

| Column | Type | Constraints |
|---|---|---|
| `Id` | uuid | PK |
| `InternshipAssignmentId` | uuid | FK → InternshipAssignments, CASCADE |
| `CohortId` | int | FK → Cohorts, RESTRICT |
| `StartDate` | date | NOT NULL |
| `EndDate` | date | nullable — null means currently active |
| `TransferReason` | varchar(500) | nullable |

**Indexes:** `IX_CohortMembership_AssignmentId`

---

### `ServicePeriods`

| Column | Type | Constraints |
|---|---|---|
| `Id` | uuid | PK |
| `InternshipAssignmentId` | uuid | FK → InternshipAssignments, CASCADE |
| `ServiceId` | int | FK → Services, RESTRICT |
| `CohortSlotAssignmentId` | int | FK → CohortSlotAssignments, SET NULL, nullable |
| `StartDate` | date | NOT NULL |
| `EndDate` | date | NOT NULL |
| `IsComplete` | bool | NOT NULL |

**Indexes:** `IX_ServicePeriod_ServiceId`, `IX_ServicePeriod_AssignmentId`

> `CohortSlotAssignmentId` links execution back to the schedule grid cell. NULL means the period was created ad-hoc (outside the published schedule). Set to NULL (not deleted) when the assignment is removed, preserving the execution record.

---

### `ServiceEvaluation`

| Column | Type | Constraints |
|---|---|---|
| `Id` | uuid | PK |
| `ServicePeriodId` | uuid | FK → ServicePeriods, CASCADE (one-to-one) |
| `TotalScore` | decimal(5,2) | NOT NULL |
| `SupervisorComment` | varchar | nullable |

---

### `ObjectiveScores`

| Column | Type | Constraints |
|---|---|---|
| `Id` | uuid | PK |
| `ServiceEvaluationId` | uuid | FK → ServiceEvaluation, CASCADE |
| `StageObjectiveId` | int | FK → StageObjectives, RESTRICT |
| `Score` | int | NOT NULL — grade given by the Service Chief |
| `Note` | varchar | nullable — per-objective feedback |

---

### `AttendanceRecords`

| Column | Type | Constraints |
|---|---|---|
| `Id` | uuid | PK |
| `ServicePeriodId` | uuid | FK → ServicePeriods, CASCADE |
| `Date` | date | NOT NULL |
| `Status` | varchar | NOT NULL, enum: `Present`, `Absent`, `JustifiedAbsent`, `Late` |

**Indexes:** `IX_AttendanceRecord_Period_Date` (ServicePeriodId, Date) UNIQUE

---

### `ServicePeriodSlotCoverage` — which grid cells a période actually covers

| Column | Type | Notes |
|---|---|---|
| `Id` | int (PK) | |
| `ServicePeriodId` | uuid (FK) | the published période |
| `CohortSlotAssignmentId` | int (FK) | one row **per covered cell** |

**Indexes:** `(CohortSlotAssignmentId, ServicePeriodId)`

⚠ **This is the only correct answer to "is this cell published?"** `ServicePeriod.CohortSlotAssignmentId`
names the **first** cell of a run; under `StageRotationMode.SingleService` one période covers a whole
run, so reading that FK reports the lead cell locked and every trailing cell free. Everything that
rewrites, clears or deletes part of the grid goes through `PublishedCells` (`RotationArranger`,
`DeleteStageSlot`, `ClearCohortSlotAssignment`, `ClearSlotAssignments`, `RotationCycleContext`).

---

### `ServiceLevelCapacities` — a service's intake rules

| Column | Type | Notes |
|---|---|---|
| `Id` | int (PK) | |
| `ServiceId` | int (FK) | |
| `LevelId` | int (FK) | a `Level` is already (programme × année) |
| `Capacity` | int | how many of *that* promotion the service takes at once |

**Indexes:** `(ServiceId, LevelId)` unique

⚠ **No rows means the service admits everyone**, capped by `Service.Capacity` — that is a service
nobody has restricted, not an unconfigured one. The **first** row closes the service to every level
without one, and from then on `Service.Capacity` is dead data for it: quotas *replace* the total, they
do not sit under it. Read it through `Service.CapacityFor(levelId)`, never field by field.

---

### `Holidays` — the entered half of the calendar

| Column | Type | Notes |
|---|---|---|
| `Id` | int (PK) | |
| `StartDate` / `EndDate` | date | inclusive; the ones that matter span days (Aïd is two, vacances two weeks) |
| `Name` | varchar | |
| `Kind` | varchar | `National`, `Religious`, `Academic` |
| `IsConfirmed` | bool | a provisional lunar date still blocks its days, but every window laid over one is flagged |

**Indexes:** `(StartDate, Name)` unique, `EndDate`

⚠ **National dates are law; religious dates are observation.** The ten fixed Gregorian days are
generated (`MoroccanPublicHolidays.FixedFor`); Aïd, Moharram and Mawlid follow the Hijri calendar and
are announced by decree — they can only be **entered**. An empty table is not a neutral one: « jours
ouvrables » then quietly means "minus weekends".

---

### `FinalYearEntryWaivers` — the exception to the final-year rule, as a row

| Column | Type | Notes |
|---|---|---|
| `Id` | uuid (PK) | |
| `StudentId` | uuid (FK) | CASCADE |
| `AcademicYearId` | int (FK) | RESTRICT |
| `Reason` | varchar(1000) | required — an override nobody can explain is not a record |
| `OutstandingAtGrant` | int | **snapshot** of what was owed the day it was granted |
| `OutstandingSummary` | varchar(1000) | « Cardiologie (3ème année), … » |
| `GrantedByUserId` | uuid? | |
| `GrantedOn` | timestamptz | |

**Indexes:** `(StudentId, AcademicYearId)` unique

⚠ The snapshot is the point: by the time it is read back the stage may have been revalidated or
dropped by a new text, and a waiver that cannot say what it excused is not a record. Refused when
nothing is owed, and **irrevocable once the registration it permitted exists**.

---

### `PriorEnrolments` — what a transfer did before he got here

| Column | Type | Notes |
|---|---|---|
| `Id` | uuid (PK) | |
| `RegistrationId` | uuid (FK) | CASCADE — the registration he entered the faculty on |
| `Institution` | varchar(200) | required — free text; PGSH is not the register of the world's faculties |
| `Country` | varchar(100)? | null means Morocco |
| `LastLevelYearCompleted` | int | **the boundary**: 2 for a student entering our 3ᵉ année |
| `EquivalenceReference` | varchar(200) | required — the arrêté, PV or décision d'équivalence |
| `EquivalenceDate` | date? | |
| `Note` | varchar(1000)? | |
| `RecordedByUserId` | uuid? | |
| `RecordedOn` | timestamptz | |

**Indexes:** `IX_PriorEnrolment_Registration` unique — one entry into the faculty, one équivalence

⚠ **Why the row exists before anything reads it.** Today a transfer owes nothing:
`OutstandingStageFinder` reads « owed » as *every attempt came back NonValidé*, and a student with no
attempt has no failed one. That holds only while the definition is negative — **the day « owed »
widens to the CNPN's requirement set** (the stated plan once 1650.25's sets are entered) **a student
transferred into 5ᵉ année owes every stage of the four years he did elsewhere.**
`LastLevelYearCompleted` is what that widening must not look below, and it cannot be reconstructed
from anything else in the base.

⚠ **No `InternshipAssignment`s are invented for the years done elsewhere.** It would make the dossier
look complete at the price of rows nobody served — which every count, mean, chef worklist and
occupancy figure would then have to learn to exclude.

Same shape as `FinalYearEntryWaivers`: a required reference and a snapshot, because a decision that
cannot say what it recognised is not a record.

---

### `CnpnLevelEffectivities` — « ce texte régit ce niveau à partir de cette année »

| Column | Type | Notes |
|---|---|---|
| `Id` | int (PK) | |
| `CnpnVersionId` | int (FK) | CASCADE |
| `LevelId` | int (FK) | RESTRICT |
| `FromAcademicYearId` | int (FK) | RESTRICT |
| `Note` | varchar? | |
| `RecordedOn` | timestamptz | |

**Indexes:** `(CnpnVersionId, LevelId)` unique, `(LevelId, FromAcademicYearId)` unique

⚠ The second index is the substantive one: two texts starting to govern one level in one year has no
defensible winner. « et en dessous » is **one row per level**, never a stored comparison — a
comparison would have to be re-evaluated to be read, and a level renumbered later would silently
change which promotions a published text binds. Read once, at the creation of a registration, then
frozen onto it (`RegistrationCnpnStamper`).

---

### `AuditLogs`

| Column | Type | Notes |
|---|---|---|
| `Id` | uuid (PK) | |
| `PerformedByUserId` | uuid? | |
| `Action` | varchar | `ROTATION_CYCLE_APPLIED`, `ROTATION_CYCLE_DELETED`, `STUDENT_TRANSFERRED`… |
| `EntityType` / `EntityId` | varchar | |
| `Metadata` | jsonb-as-text | the command's own payload |
| `CreatedAt` | timestamptz | |

**Indexes:** `IX_AuditLog_CreatedAt`, `IX_AuditLog_PerformedBy`, `IX_AuditLog_Entity (EntityType, EntityId)`

Written by the pipeline for any `IAuditableCommand`. ⚠ `GetRotationCycleQuery` **reads** it: the axis
cannot state `kₛ`, so the apply's own audit entry is where an authored period count is recovered from.
The metadata carries the *request's* `AcademicYearId`, which is null whenever the caller left it to
the resolver — the stage set is matched against the slots on disk instead.

---

### `TodoItems` *(template placeholder — no active endpoints)*

| Column | Type | Constraints |
|---|---|---|
| `Id` | uuid | PK |
| `Description` | varchar | NOT NULL |
| `DueDate` | timestamp | nullable |
| `StartDate` | timestamp | nullable |
| `Labels` | varchar | JSON array |
| `IsCompleted` | bool | |
| `CompletedAt` | timestamp | nullable |
| `Priority` | int | enum: `Low`, `Medium`, `High`, `Critical` |

---

## Delete Behavior Reference

| Relationship | Behavior | Rationale |
|---|---|---|
| AcademicYear → AcademicGroup | CASCADE | Groups are year-scoped, no meaning without a year |
| AcademicGroup → Registration | SET NULL | Student stays enrolled even if group is dissolved |
| Student → Registration | CASCADE | Registrations are owned by the student |
| Student → History | CASCADE | Audit trail follows the student |
| Registration → InternshipAssignment | CASCADE | Assignment is meaningless without its registration |
| InternshipAssignment → ServicePeriod | CASCADE | Periods follow the assignment |
| InternshipAssignment → CohortMembership | CASCADE | Transfer history follows the assignment |
| ServicePeriod → AttendanceRecord | CASCADE | Attendance follows the period |
| ServicePeriod → ServiceEvaluation | CASCADE | Evaluation follows the period |
| ServiceEvaluation → ObjectiveScore | CASCADE | Scores follow the evaluation |
| Curriculum → CurriculumStage | CASCADE | The stage list is part of the CNPN record |
| CurriculumStage → Stage | RESTRICT | A stage any CNPN ever required cannot be deleted out from under it |
| Level / AcademicYear → Curriculum | RESTRICT | A year or level with a recorded CNPN cannot be deleted |
| Stage → StageObjective | CASCADE | Objectives are part of the stage definition |
| Stage → StageSlot | CASCADE | Slots are part of the stage schedule definition |
| StageSlot → CohortSlotAssignment | CASCADE | Grid cells are invalidated when the slot is deleted |
| Cohort → CohortSlotAssignment | CASCADE | Grid cells belong to the cohort |
| ServicePeriod → CohortSlotAssignment | SET NULL | Period survives assignment deletion (keeps execution record) |
| Hospital → Service | RESTRICT | Cannot delete a hospital with active services |
| Center → Hospital | RESTRICT | Cannot delete a center with hospitals |
| Service (chef/staff) → Employee | RESTRICT | Cannot delete an employee assigned to a service |
| CohortSlotAssignment → Service | RESTRICT | Cannot delete a service that is referenced in the schedule grid |
| ServicePeriod → ServicePeriodSlotCoverage | CASCADE | Coverage describes a période that no longer exists |
| CohortSlotAssignment → ServicePeriodSlotCoverage | CASCADE | …and a cell that no longer exists |
| Service → ServiceLevelCapacity | CASCADE | Quotas are part of the service's own intake rules |
| Level → ServiceLevelCapacity | RESTRICT | A promotion a service has a quota for cannot vanish under it |
| Student → FinalYearEntryWaiver | CASCADE | The waiver is about that student and nobody else |
| AcademicYear → FinalYearEntryWaiver | RESTRICT | The year it permitted entry to must stay nameable |
| Registration → PriorEnrolment | CASCADE | An équivalence attached to an entry that no longer exists explains nothing |
| CnpnVersion → CnpnLevelEffectivity | CASCADE | The rule is part of the text that states it |
| Level / AcademicYear → CnpnLevelEffectivity | RESTRICT | The (level, year) the rule names must stay nameable |

---

## Enum Quick Reference

| Enum | Values | Used In |
|---|---|---|
| `AcademicProgram` | `Medecine`, `Pharmacie`, `Master`, `Doctorat` | Student, Level |
| `RegistrationStatus` | `Pending`, `Active`, `Validated`, `Failed`, `Withdrawn` | Registration |
| `InternshipStatus` | `Planned`, `Ongoing`, `Completed`, `Evaluated`, `Validated`, `Rejected` | InternshipAssignment |
| `StageAssignmentResult` | `NonÉvalué`, `Validé`, `NonValidé` | InternshipAssignment |
| `AttendanceStatus` | `Present`, `Absent`, `JustifiedAbsent`, `Late` | AttendanceRecord |
| `HistoryType` | `Inscription`, `ValidationStage`, `NonValidation`, `Fraud`, `Revalidation` | History |
| `ServiceType` | `Biologie`, `Chirurgie`, `Medical` | Service |
| `HospitalType` | `None`, `Autre`, `Spetialité`, `Central`, `CHU`, `LHOMA` | Hospital |
| `CenterType` | `None`, `CHU`, `CHR`, `CHP`, `CSU` | Center |
| `Grade` | `MC`, `PES`, `PH`, `Nurse`, `Administrator` | Employee |
| `Position` | `ServiceChef`, `Normal` | Employee |
| `WorkPlace` | `Hospital`, `Fmpr` | Employee |
| `AgreementType` | `None`, ... | Student |
| `BacSeries` | ... | Student |
| `CivilStatus` | `Civil`, `Militaire` | User (owned) |
| `NationalityStatus` | `Marocaine`, `Etrangaire` | User (owned) |
