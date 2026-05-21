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
              └── InternshipAssignment (uuid)
                    ├── CohortMembership (uuid)   [transfer history]
                    └── ServicePeriod (uuid)
                          ├── AttendanceRecord (uuid)
                          └── ServiceEvaluation (uuid)
                                └── ObjectiveScore (uuid)

Level (int)
  └── Stage (int)
        ├── StageObjective (int)          ← ObjectiveScore references these
        └── Cohort (int)
              ├── CohortRotationTemplate (int)  → Service
              └── InternshipAssignment          [CurrentCohortId FK]

ServicePeriod → CohortRotationTemplate (nullable, tracks planned vs. ad-hoc)
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
| `Appogee` | varchar(50) | UNIQUE (nullable filter) |
| `AccessGrade` | decimal(5,2) | default 10.01 |
| `BacSeries` | varchar | enum |
| `AgreementType` | varchar | enum, default `None` |
| `BacYear` | varchar(10) | |
| `Academy` | varchar | enum, nullable |
| `Province` | varchar | enum, nullable |
| `Ranking` | int | nullable |
| **Employee columns** | | Discriminator = `Employee` |
| `PPR` | varchar(50) | nullable |
| `Grade` | varchar | enum: `MC`, `PES`, `PH`, `Nurse`, `Administrator` |
| `Position` | varchar | enum: `ServiceChef`, `Normal`, nullable |
| `Label` | varchar(100) | nullable |
| `WorkPlace` | varchar | enum: `Hospital`, `Fmpr`, nullable |
| `PvSignatureDate` | date | nullable |

**Indexes:** `IX_Users_Email` (unique), `IX_Users_IdentityProviderId` (unique, null filter), `IX_Student_CNE` (unique), `IX_Student_Appogee` (unique, null filter)

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
| `HospitalType` | varchar | NOT NULL, enum: `None`, `Public`, `Private`, `Military` |
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

### `CohortRotationTemplates`

| Column | Type | Constraints |
|---|---|---|
| `Id` | int | PK (identity) |
| `CohortId` | int | FK → Cohorts, CASCADE |
| `ServiceId` | int | FK → Services, RESTRICT |
| `PlannedStart` | date | NOT NULL |
| `PlannedEnd` | date | NOT NULL |
| `SequenceOrder` | int | NOT NULL — position within the rotation plan |

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
| `CohortRotationTemplateId` | int | FK → CohortRotationTemplates, SET NULL, nullable |
| `StartDate` | date | NOT NULL |
| `EndDate` | date | NOT NULL |
| `IsComplete` | bool | NOT NULL |

**Indexes:** `IX_ServicePeriod_ServiceId`, `IX_ServicePeriod_AssignmentId`

> `CohortRotationTemplateId` links execution back to plan. NULL means the period was created ad-hoc outside the rotation template.

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
| Stage → StageObjective | CASCADE | Objectives are part of the stage definition |
| Cohort → CohortRotationTemplate | CASCADE | Templates are part of the cohort plan |
| ServicePeriod → CohortRotationTemplate | SET NULL | Period survives template deletion (keeps execution record) |
| Hospital → Service | RESTRICT | Cannot delete a hospital with active services |
| Center → Hospital | RESTRICT | Cannot delete a center with hospitals |
| Service (chef/staff) → Employee | RESTRICT | Cannot delete an employee assigned to a service |

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
| `HospitalType` | `None`, `Public`, `Private`, `Military` | Hospital |
| `CenterType` | `None`, `CHU`, `CHR`, `CHP`, `CSU` | Center |
| `Grade` | `MC`, `PES`, `PH`, `Nurse`, `Administrator` | Employee |
| `Position` | `ServiceChef`, `Normal` | Employee |
| `WorkPlace` | `Hospital`, `Fmpr` | Employee |
| `AgreementType` | `None`, ... | Student |
| `BacSeries` | ... | Student |
| `CivilStatus` | `Civil`, `Militaire` | User (owned) |
| `NationalityStatus` | `Marocaine`, `Etrangaire` | User (owned) |
