# Planning a promotion's year

How the répartition annuelle is built: what identifies a roster, the arithmetic that decides the
shape of the crossover, and the steps to produce a document like
[`example_stage_assignement/demo/MED05.png`](example_stage_assignement/demo/MED05.png).

Written 2026-08-13, after the roster split (`SplitAcademicGroupsPerLevel`) and the concurrent-partition
fix. Figures are measured against the real base unless marked otherwise.

---

## 1 · The four things that get confused

| Thing | Is | Keyed by |
|---|---|---|
| **`AcademicGroup`** (roster) | the fixed set of students who move together | **(year, promotion, number)** |
| **`Cohort`** | one roster *doing one stage* | (roster × stage) |
| **`CohortSlotAssignment`** (cell) | that cohort *in one period, in one service* | (cohort × créneau → service) |
| **`RotationGroup`** (partition) | a label (A, B, C…) grouping rosters that move together through the block | a column on the roster |

A roster is not "in a service" — it is in a *sequence* of them, one per column. The service lives two
levels out.

---

## 2 · Identity: a roster belongs to exactly one (year, promotion)

`IX_AcademicGroup_Year_Level_Number` — `UNIQUE (AcademicYearId, LevelId, GroupNumber) NULLS NOT DISTINCT`.

This is what makes all of the following true at once, and they are all *required*:

- **Groupe 1 of 3ème année ≠ Groupe 1 of 5ème année**, in the same year. Different rows, different
  students. (2025-2026 has five distinct « Groupe 1 »: Med3, Med4, Med5, Med6, Pharma5.)
- **Groupe 1 of 3ème année 2024-2025 ≠ Groupe 1 of 3ème année 2025-2026.** A year apart is a different
  promotion entirely. (Med3 « Groupe 1 » exists once per year, six rows, 8–14 students each, and **no
  student appears in two of them**.)
- Numbering therefore **restarts at 1 for each (year, promotion)** — Med3 runs 1-80, Med5 1-60, Med6
  1-100, concurrently.

⚠ **Why this is load-bearing and not cosmetic.** `GroupScheduleConflictGuard` forbids one roster from
sitting in two services at the same time. Before the split, the legacy import keyed rosters on
`(year, number)` alone, so 80 of the 100 rosters of 2025-2026 carried registrations from four or five
promotions at once. The 3rd year's April–July placements *were* the 5th year's, so seven of the 5th
year's nine columns were refused and the printed document came out with two.

### Integrity audit (all zero, measured 2026-08-13)

Several tables reach a roster indirectly and **no composite FK protects those joins** — they are held
by the handlers. Re-run these after any bulk operation:

```sql
-- a registration must sit in a roster of its own year and its own promotion
SELECT count(*) FROM "Registrations" r JOIN "AcademicGroups" g ON g."Id"=r."AcademicGroupId"
WHERE r."AcademicYearId" <> g."AcademicYearId";
SELECT count(*) FROM "Registrations" r JOIN "AcademicGroups" g ON g."Id"=r."AcademicGroupId"
WHERE g."GroupNumber">0 AND g."LevelId" IS DISTINCT FROM r."LevelId";

-- a cell's créneau and its cohort must agree on the year
SELECT count(*) FROM "CohortSlotAssignments" a
JOIN "StageSlots" sl ON sl."Id"=a."StageSlotId"
JOIN "Cohorts" c ON c."Id"=a."CohortId" JOIN "AcademicGroups" g ON g."Id"=c."AcademicGroupId"
WHERE sl."AcademicYearId" <> g."AcademicYearId";

-- a cohort's stage and its roster must agree on the promotion
SELECT count(*) FROM "Cohorts" c JOIN "Stages" st ON st."Id"=c."StageId"
JOIN "AcademicGroups" g ON g."Id"=c."AcademicGroupId"
WHERE g."GroupNumber">0 AND g."LevelId" IS DISTINCT FROM st."LevelId";

-- an internship assignment must not span years
SELECT count(*) FROM "InternshipAssignments" ia JOIN "Registrations" r ON r."Id"=ia."RegistrationId"
JOIN "Cohorts" c ON c."Id"=ia."CurrentCohortId" JOIN "AcademicGroups" g ON g."Id"=c."AcademicGroupId"
WHERE r."AcademicYearId" <> g."AcademicYearId";
```

⚠ **One check that is *not* expected to be zero**: an assignment whose stage level differs from the
student's registration level. 290 of these exist and are legitimate — 275 are *Interne CHU* students
doing 6th-year stages, 15 are withdrawn students with stage records, and cross-level retakes are a
supported feature (`RevalidateStageCommand`).

⚠ **« Non réparti » (`GroupNumber = 0`) is the one roster with no promotion.** It holds every
promotion's unassigned registrations (4,725 in 2025-2026), carries no cohorts, and is never given a
partition label. `NULLS NOT DISTINCT` keeps a year to exactly one of them.

---

## 3 · The arithmetic

A **block** is the set of stages that run in parallel on one shared axis of columns.

| Symbol | Meaning |
|---|---|
| `kₛ` | columns a partition spends in stage *s* |
| `T` | length of the axis in columns |
| `P` | number of partitions |
| `Lₛ` | partitions sitting in stage *s* at any moment |
| `N` | students in the promotion |

### The three rules

```
T  = Σ kₛ                     the axis is as long as one partition needs to visit every stage
Lₛ = P · kₛ / T               must be a whole number
P  ≡ 0  (mod T / gcd(kₛ))     which is what the integrality of Lₛ pins P to
```

`T = Σkₛ`, **never** `partitions × k`. Partitions do not lengthen the timeline; they subdivide who is
where. Three stages at `k=1` with six partitions is **3** columns with 2 partitions per stage, not 6.

⚠ **A column is a column of the shared axis, not a stage.** Every stage carries a créneau on *every*
column, and a partition takes a run of `kₛ` consecutive ones. That is what the other stages of the
block need in order to cross over, and it holds whatever happens inside the run.

⚠ **`kₛ` columns is not automatically `kₛ` services.** That is `Stage.RotationMode`, and it is a
separate decision:

| mode | inside a run of `kₛ` columns | evaluations |
|---|---|---|
| `PerPeriod` (default) | the partition moves S1 → S2 → … | one per column, note = their mean, all must pass |
| `SingleService` | the partition stays in one service | **one**, and it is the stage's note |

The imported history says which stages are which: **5ᵉ and 6ᵉ année are `SingleService` in essentially
100% of their placements** (30,614/30,614 and 21,309/21,310), 3ᵉ année genuinely rotates. So on the 5th
year below, Gynécologie's `k=3` means *three columns in one service*, not three services — the axis
arithmetic is identical either way.

⚠ **A `SingleService` stage must be arranged one run at a time.** Unscoped, "auto-arrange this stage"
treats all nine Gynécologie columns as one run and gives a cohort one service for the year. The macro
plan always scopes; the bare auto-arrange button is refused with `SingleServiceRunNotScoped`.

### Some mixes are impossible, not unsupported

Stages of `k = 2` and `k = 1` give `T = 3`. A two-column run must cover column 2 wherever it starts, so
every partition is in that stage at column 2 and the other stands empty. **No `P` fixes it.** The
search is exhaustive, so `NoFeasibleArrangement` is a proof, not a timeout.

### Worked: the 5th year (reproduces `MED05.png`)

Seven stages — Gynécologie `k=3`, plus Neurologie, Ophtalmologie, ORL, Psychiatrie, Santé Publique,
Urologie at `k=1`:

```
T = 3 + 1+1+1+1+1+1 = 9 columns
gcd(kₛ) = 1  →  P must be a multiple of 9/1 = 9      →  P = 9
L_gynéco = 9·3/9 = 3 partitions at once  (= 20 of the 60 rosters)
L_others = 9·1/9 = 1 partition at once   (= 6–7 rosters)
```

60 rosters ÷ 9 = partitions of 7,7,7,7,7,7,6,6,6.

Other blocks in the base: **3rd year** `k=[2,2]` → `T=4`, `P` multiple of 2, `L=[1,1]`.
**6th year** `k=[2,2,2,2,1,1]` → `T=10`, `P` multiple of 10, `L=[2,2,2,2,1,1]`.

---

## 4 · Capacity — what the arithmetic decides, and what it doesn't

Balancing was fixed (see §5), but **capacity was not**, and one common instinct about it is wrong.

> "A partition holds too many students — cut the promotion into more partitions."

It cancels out. Stage *s* holds `Lₛ = P·kₛ/T` partitions of `N/P` students each:

```
students in stage s at any moment = (P · kₛ / T) · (N / P) = N · kₛ / T
```

**`P` disappears.** How many students sit in a stage at once depends only on the fraction of the year
that stage occupies — never on how finely the promotion is cut.

Checked against the built plan (N = 706): Santé Publique `706·1/9 ≈ 78` predicted, 69–85 observed;
Gynécologie `706·3/9 = 235` over five services ≈ 47 each, observed a mean of ~47.

So the **only** levers are:

1. **raise `kₛ`** — give the stage more of the year, which shrinks every other stage;
2. **add allowed services** to the stage;
3. **enter true capacities** and accept the overflow as a recorded fact.

### Where it actually stands (2025-2026, 5th year)

| Stage | Services | Busiest service | Declared capacity |
|---|---|---|---|
| Santé Publique | 1 | **85 students** | 20 |
| Gynécologie | 5 | 61 | 20 |
| ORL | 2 | 50 | 20 |
| Ophtalmologie | 3 | 38 | 20 |
| Neurologie | 7 | 14 | 20 |

⚠ Every 20 is an **import default** — nobody has entered what these services really take. Capacity is
checked only at **publish**, and is waivable via `AllowOverCapacity`. Note the faculty's own `MED05.png`
also puts 7 rosters in Santé Publique: this load is reality, not a modelling error.

---

## 5 · Concurrent partitions must be arranged together

When `Lₛ > 1` — which happens exactly when the block's stages have unequal durations — the partitions
sharing a stage over the same window are handed to `RotationArranger` in **one call**
(`ConcurrencyBlock`, same stage + same window).

The service queue is balanced over the cohorts of a single call. One call each balances every partition
against the full service list in ignorance of the others, and the remainders *stack*: the queue builder
gives the leftover to the same leading services every time, and every partition of a column carries the
same rotation offset. Measured on Gynécologie (`L=3`, five services, 20 rosters): three calls of 7/7/6
gave **6/5/3/3/3**; one call of 20 gives **4/4/4/4/4**, which is what the faculty prints.

Per-column spread in the current plan is ≤ 1 roster for every stage — optimal, since 60 is not
divisible by 9 (columns hold 21, 19 or 20 rosters → 5/4/4/4/4, 3/4/4/4/4, 4/4/4/4/4).

---

## 6 · Steps, from the frontend

Order matters: the rotation cycle reads the partition labels, so they must exist first.

### 0 · Prerequisites

- **Rosters exist** — *Académique → Groupes → Répartition automatique* distributes students who have
  no group. Groups are created per (year, promotion) and numbered from 1.
- **Each stage has allowed services** — *Formation → Stages → «stage» → Services autorisés*. Without
  them the plan refuses with `Schedule.NoAllowedServices`. The 6th year's 51 were authored 2026-08-24
  and it has been planned end to end since (see §9).
- **The navbar year is the year you mean.** There is one year selector and it scopes everything.
- ⚠ **No student is signalé** — *Académique → **Signalements***. A held registration is deliberately
  invisible to planning: it is cut into no roster, given no cohorte and published no période. The
  réinscription roll of 2026-2027 raises ~1 449 of them in one act (1 267 absentees plus 182 final-year
  debts), so on a freshly rolled-over year this is the first thing to clear, not the last.
  - The roster cut **names** the students it left out rather than dropping them silently — a cut one
    student short otherwise looks exactly like a promotion that size.
  - They come off one at a time, with a motif. There is no bulk release, and that is the point: each
    is a different question — is the évaluation keyed in, did this student really defend, is he
    simply coming back late.

### 1 · Cut the promotion into partitions

*Académique → Groupes → **Planification Macro*** → choose the promotion.

- Never cut: **Nombre de partitions** + **Découpage** → *Assigner les partitions*.
- Already cut: **Nouveau nombre** → *Redécouper* (re-cuts), or *Supprimer les partitions* (clears).

⚠ A promotion that already carries labels **keeps its count** whatever number you type — that is what
stops a stray re-run from rebattling an existing plan. To change the count you must *Redécouper* or
clear first. Both are **refused while any cell is published**.

`Découpage`: `Alterné` gives A = 1,3,5… (prints as `1, 3, 5, 7`); `Contigu` gives A = 1-40 (prints as
`1-40`). Same sizes, different printed cells.

The count must satisfy §3. Get it wrong and step 2 refuses, naming the valid multiples.

### 2 · Author the axis

*Formation → **Bloc de rotation*** → choose the promotion.

1. List the stages that run in parallel, each with its **périodes** (`kₛ`). The banner computes
   `T = Σkₛ` and tells you how many date windows it needs.
2. **Axe partagé**: *Début de l'axe*, *Durée d'une colonne*, *Unité*, then *Générer les N fenêtre(s)*
   (or *Saisir à la main*). Each window is editable afterwards.
   - `mois` / `semaines` — calendar-exact. A monthly axis must start on the 1st.
   - `jours ouvrables` — weekends and jours fériés excluded, so **every column is the same amount of
     stage**. This is the only unit under which février and mars are equal.
3. ***Simuler***. Check:
   - **Partitions simultanées** matches `Lₛ` (Gynécologie 3, the rest 1).
   - **Durée réelle par stage** — worked and calendar days against each stage's declared duration,
     as a range because partitions take different runs. Informative, **never blocking**.
   - Warnings about provisional religious holidays: the dates will move if the décret does.
4. ***Appliquer l'axe*** — writes the créneaux. Replaces any existing axis **wholesale**, and is
   refused outright if any cell of the block is already published. ⚠ The cells hanging off the
   replaced créneaux go with them (they cascade); the preview and the toast both name how many, and an
   arrange rebuilds them from the returned matrix.

⚠ **Reopening the page shows the block in force**, restored from the axis on disk — stages, `kₛ` and
the windows — with the date it was applied and where `kₛ` was recovered from. So **modifier** a block
is: correct the form, re-simuler, ré-appliquer. **Supprimer** it is its own button in that banner:
replacing an axis is not undoing one, and without it a block entered by mistake could only be written
over. It is scoped to the stages on screen (a promotion can hold two semesters), refused while
anything on it is published, and it names the créneaux and the cells it removed.

### 3 · Run the plan

***Générer le plan*** on the same page — provisions cohorts, affects students, arranges services.

⚠ **Read the toast.** It reports cells written *and* cells refused. A refusal count means groups were
already placed elsewhere over those dates; a plan missing columns is what that looks like. It also
reports combinations skipped because a group's CNPN does not require that stage.

⚠ **…and then read the base, because the toast is not evidence.** The session-25 defect wrote 60 cells,
reported no failure, and put a whole promotion in one service. What to check, and what the 6ᵉ année
gave on 2026-08-26 (1 000 cells):

| check | expected | measured |
|---|---|---|
| every roster visits every stage | `kₛ` columns each | 2·2·2·2·1·1, min = max |
| partitions at once in a stage | `Lₛ` in every column | 2·2·2·2·1·1 |
| a roster in two stages at once | 0 | 0 |
| services used per column | all of them | 13/13, 5/5, 13/13, 7/7, 7/7, 6/6 |
| spread inside one column | ≤ 1 roster | ≤ 1 (Gynéco exactly 4·4·4·4·4) |

### 4 · Publish the document

*Formation → **Répartition annuelle*** → choose the promotion.

Check the **Périodes non planifiées** banner (hatched cells — a service short, or a partition smaller
than the service count), confirm the legend names the partitions you cut, then *Imprimer / PDF* or
*Télécharger (.html)*.

---

## 7 · What the refusals mean

| Message | Cause | Fix |
|---|---|---|
| `RotationCycle.PartitionCountIncompatible` | `P` is not a multiple of `T/gcd(kₛ)` | re-cut to a named multiple |
| `RotationCycle.NoFeasibleArrangement` | the duration mix cannot tile (§3) | change a `kₛ` |
| `RotationCycle.CannotReplacePublished` | a cell of the block is published | nothing — the axis is frozen |
| `RotationCycle.CannotDeletePublished` | same, on *Supprimer le bloc* | dépubliez le planning d'abord (§8) |
| `RotationCycle.NoBlockToDelete` | those stages carry no créneau for the year | nothing to undo — check the navbar year |
| `Schedule.NoAllowedServices` | the stage has no services | add them on the stage page |
| `Schedule.NoServicesAdmitLevel` | services exist but their quotas exclude this promotion | add a `ServiceLevelCapacity` row |
| `Schedule.NoSlots` | the axis is not authored yet | do step 2 |
| `Schedule.PromotionNotPartitioned` | a partition was targeted on a promotion nobody cut | do step 1 |
| `Schedule.GroupAlreadyPlaced` | a roster would be in two services at once — the message names the **promotion**, because the collision is often another one | check the crossover |
| `CannotClearPublished` / `PlannedCellsAffected` | re-cutting under an existing plan | re-arrange after |
| `Schedule.SingleServiceRunNotScoped` | a `SingleService` stage was arranged without naming the run's périodes | scope the arrange (P1–P3), or use the macro plan, which always does |
| `Schedule.SingleServiceRunNotContiguous` | the périodes given do not follow each other | a single stay cannot have a hole — pick a consecutive run |
| `Schedule.Underway` | dépublier would delete periods that have started, marks, or attendance | read the count it names, then confirm a second time if you mean it |
| `Stages.RotationModeLockedByPublication` | changing a stage's mode under a published répartition | dépubliez that stage first |

---

## 8 · Undoing a plan

The chain, outermost first. Each step is refused while the step outside it still holds.

```
dépublier la cohorte   →  vider les cellules       →  supprimer le créneau
(ServicePeriods)          (CohortSlotAssignments)     (StageSlot)
```

- ⚠ **Dépublier is the destructive one.** Evaluations, attendance, pauses and délocalisations all
  cascade from a `ServicePeriod`. Once anything has started it is refused (`Schedule.Underway`) and
  the refusal names what would be lost; the second confirmation is you agreeing to that number. « Dépublier
  tous les plannings » never forces — a started cohort is counted as an error instead.
- **Periods that came from no cell are never touched**: imported history, délocalisations,
  revalidations. The result reports them as kept.
- After dépublier, the assignments go back to *Planned* and lose their computed note — a verdict over
  evaluations that no longer exist is what had to be walked back.
- To change a partitioning instead, see §6 step 1: *Redécouper* or *Supprimer les partitions*, both
  refused while published.
- To remove the **axis** itself: *Bloc de rotation* → *Supprimer le bloc*. It takes the block's
  créneaux and the cells planned on them, is refused while anything on it is published, and is scoped
  to the stages of that block — the promotion's other semester stays standing.

⚠ **Re-publishing does not re-do a stage a student has already served.** Any assignment that already
holds a period is skipped and counted (`skippedAlreadyServed`). This is what stops the new répartition
from doubling up on the Access history — all 706 5MED assignments of 2025-2026 carry one imported
period per stage.

---

⚠ **An empty répartition has two causes and they need opposite acts**: no créneaux (author an axis) or
créneaux nobody is in (run the plan). `DeclaredSlotCount` on the response is what separates them.

---

## 9 · The 6ᵉ année, end to end (measured 2026-08-26)

The first promotion planned through the whole chain on real data, and the reference the arithmetic of
§3 can be checked against.

| | |
|---|---|
| stages | CHIRURGIE, GYNÉCO, MÉDECINE, PÉDIATRIE (44 j.) · ANES RÉA, URGENCES (22 j.) |
| `kₛ` at 22 jours ouvrables per column | 2·2·2·2·1·1 |
| `T = Σkₛ` | **10** columns, 01/09/2025 → 17/06/2026 |
| `P` | **10** partitions (A–J) of 10 rosters |
| `Lₛ = P·kₛ/T` | 2·2·2·2·1·1 |
| written | 60 créneaux, **1 000 cellules**, 0 double-booked |
| printed | 51 rows × 10 columns, 0 empty cells, 510 document cells |

⚠ **Authoring the axis in *jours ouvrables* is what makes the durations land**: every stage got
exactly its catalogue figure (44/44/44/44/22/22) while its calendar span swung 60–67 days. In calendar
months it would have been the other way round.

⚠ **What the plan does not fix: 88 of the 510 (service × colonne) pairs are over capacity, worst 30
against 20.** That is not the arranger — all 148 services carry the same imported default of 20 and
not one quota is authored. It is the soft, waivable half of the rule, and it is what publishing will
ask you to override.

⚠ **Publishing states all 88 of them at once, in one refusal** — `Schedule.PublishRefusedByIntake`,
naming the count, how many are on a service that does not *admit* the promotion (which the
override does not lift) and the heaviest three. It used to stop at the first cell, so a plan on
this base was corrected one service at a time with a full re-publish between each — and « Publier
toutes » on the stage page looped cohorte by cohorte, which turned that into one refusal toast per
cohorte. Both fixed in session 33; the grid and the stage page now send the same single call.

---

## 10 · Printing what happened — the post-validation export

The répartition (`§6`) is the plan; this is the record. `GET stages/assignments/export` is the .xlsx
drawn **after** the évaluations are in — three sheets, and it is what a PV is transcribed from.

```
GET stages/assignments/export?levelId=<promotion>&academicYearId=<year>[&stageId=][&academicGroupId=][&onlyEvaluated=true]
```

| sheet | one row per | what it answers |
|---|---|---|
| **Stages** | attempt (`InternshipAssignment`) | did this student validate this stage, with what note |
| **Périodes** | `ServicePeriod` | where he actually stood, when, and what each stay was marked |
| **Synthèse** | (stage) | how the promotion did — validés / non validés / non évalués, taux, moyenne |

`Réf. stage` is on the first two sheets and is the join.

**Reading the « Période(s) » column.** A stage occupying several columns of the axis is one stay or
several, and the export merges on **the service**, never on the dates:

- `01/01/2025 – 02/03/2025` with `Nb périodes = 2` — two columns, one service, meeting end to end.
  The span is exactly true; the count is what says it was recorded in two.
- `01/01/2025 – 01/02/2025 · 17/02/2025 – 02/03/2025` — the same service with worked days nobody
  served in between. `Découpage` says « 1 interruption(s) ». The merged span would have claimed days
  the student was not there.
- `Cardiologie → Pneumologie` against two spans — a real rotation. Services and spans correspond
  position by position.

⚠ **Durations are summed over the périodes, never `Fin − Début`.** On an interrupted stage the span
contains days nobody served, which is what turns a 22-jour stage into a 60-jour one on paper.

⚠ **Scoped by the registration's level.** A 6ᵉ année student redoing a 3ᵉ année stage is on the 6ᵉ
année's document, with `Niveau du stage` naming the year the stage belongs to. Do not go looking for
him on the 3ᵉ année's file.

**The roll**, for the same promotion, is `GET students/export?levelId=…&academicYearId=…` — one row
per registration, carrying the groupe and the partition as well as the identifiers. Omit
`academicYearId` and both exports resolve to the current year, never to every year.
