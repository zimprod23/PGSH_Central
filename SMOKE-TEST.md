# Smoke test — sessions 11 → 32

Covers the year-scoping lockdown (11), CNPN versioning + targeting + text editing (12–13), per-level
service quotas and the répartition annuelle (14), the déliberation / réinscription flow (14), the
working-day calendar + un-partitioning (16 — steps **12e** and **12f**, both already executed), the
four reporting gaps closed in 17 (steps **12g**, **12h** and **12i** — executed, with the two gaps
noted in their headers), and the roster/promotion split plus concurrent-partition balancing in 18
(step **12j** — **not yet executed**, and the one that needs a migration), and in 19 the dense-cell
rendering of the published document (step **12k** — executed), its switch from partition colours to
stage colours (step **12i**, **rewritten** — the session-17 version checked the opposite thing and is
no longer the spec), and the promotion-isolation guards on transfer, cohorte creation and group
naming (step **12l** — **not yet executed**) plus the service occupancy view (step **15** — executed
against live data on two services, with two sub-steps left manual). Session 20 adds
`Stage.RotationMode` — one service for a whole multi-période run, with one evaluation — plus the
guard that stops unpublishing from silently deleting evaluations and attendance, and the rule that
publishing never lands on top of a stage a student has already served (step **16** — **not yet
executed**). Session 21 fixes the partition count itself: read from the promotion instead of from one
stage's cohorts, required to name its promotion, and counted server-side instead of off a 200-row page
(step **17** — **not yet executed**, and it carries the Med6 repair). Sessions 22-24 add the
admissibility/occupancy split on the capacity override (step **18**), the déliberation canvas as a
list of exceptions plus the single-row outcome (step **19**), the CNPN carried on the registration
and `CnpnLevelEffectivity` (step **20**) and the final-year gate (step **21**). Session 25 balances a
service queue per **column** instead of per call, refuses the unscoped arrange on a stage nothing has
crossed into, and reads the rotation block back into its form (step **22** — **not yet executed**,
and it carries a data repair: every cell arranged before it needs re-running). **Session 28 is the
first real clôture**: 2025-2026 closed and 2026-2027 rolled over on the live base, whole faculty
(step **26** — *executed*, and it is the record of what a correct run prints). Session 29 bounds the
chef-de-service worklist on the period's own lifecycle — four slices instead of every period ever —
and makes a published-but-unstarted rotation visible under « À venir », which is why a whole
promotion's publication had looked to its chef like nothing had happened (step **27** — **not yet
executed**, no migration). Session 30 adds the **third act of the year** — inscription, for the people
the déliberation and the réinscription structurally cannot see: the September intake, transfers
arriving from another faculty, returners and réorientations — as a sheet **and** as a one-student
form, with its own card on the clôture screen (step **28** — **executed 2026-08-30 except f/g**, and
it carries a migration, now applied). ⚠ It is the only act in PGSH that **creates people**, and there
is no undo — see §28's own header for what the run established and for the two test students it left
in the base. The same session made the réinscription's `FinalYearBlocked` rows visible: the count was
shown and the table listing them was empty, measured at **58** of the 8 077 considered.
Session 32 adds the two Excel exports — the roll and the post-validation stage record — and with them
the rule that a merged date span may not claim days nobody served (step **30** — **executed
2026-08-31 through the real buttons**, no migration; it found and fixed a double toast, and it left
one thing to re-check after an API restart).

**Rollback is at the bottom** — read it before you start, not after.

Prerequisites: `dotnet run --project PGSH.AppHost`, log in as an admin (Scolarité).

| Migration | Applied? |
|---|---|
| `StageSlotAcademicYear`, `CnpnVersioning` | ✅ already in your dev database |
| `AddServiceLevelCapacityAndLocalization` | ✅ already in your dev database |
| `RegistrationYearOutcome` | ✅ applied |
| `HolidayCalendar` | ✅ applied (session 16) |
| `SplitAcademicGroupsPerLevel` | ✅ applied — verified 2026-08-14: 3,696 of 3,707 rosters carry a promotion, the other 11 are the one « Non réparti » bucket per year; 0 rosters span two promotions, 0 registrations disagree with their roster's year or level |
| `StageRotationModeAndSlotCoverage` | ✅ applied — verified 2026-08-14: all 27 stages default to `PerPeriod` (no behaviour change), and `ServicePeriodSlotCoverage` holds 0 rows against 0 grid-linked periods, so the backfill is consistent. Additive only: a new column with a default and a new table |
| `PartitionScopeAndIndexGaps` | ✅ applied — verified 2026-08-24. Adds `IX_AcademicYear_IsCurrent` (unique, filtered on `"IsCurrent"`) and demotes any extra current year first, keeping the highest `Id`. It should touch 0 rows: `CreateAcademicYear` already demotes the others. No table or column is added |
| `GroupLabelPerPromotion` | ✅ applied — verified 2026-08-14: `IX_AcademicGroup_Year_Level_Label` and `IX_AcademicGroup_Year_Level_Number` both present, the two year-only indexes gone. A pure relaxation: `IX_AcademicGroup_Year_Label` → `IX_AcademicGroup_Year_Level_Label` (`NULLS NOT DISTINCT`). The old key is a superset of the new one, so no existing row can collide; see step **12l** |
| `RegistrationCnpnAndLevelEffectivity` | ✅ applied — verified 2026-08-24. Adds `Registration.CnpnVersionId` / `CnpnSource` and the `CnpnLevelEffectivity` table, and backfills the six imported years from the student's stamp as `Backfilled` |
| `FinalYearEntryWaiver` | ✅ applied — verified 2026-08-24. One new table, no data change |
| `PriorEnrolment` | ✅ applied — verified 2026-08-30 in `__EFMigrationsHistory`; MigrationService applied it at Aspire startup. One new table recording a transfer's équivalence, cascading from the registration that admitted him, unique on it. Additive only. ⚠ Still **0 rows**: step 28 f/g, the only path that writes one, has not been run |

Timings below are the real figures from your data — if you see a different number, that is the bug.

---

## 0 · Sanity (2 min)

```bash
rm -rf PGSH.Tests/bin PGSH.Tests/obj          # ⚠ see below
dotnet test PGSH.Tests/PGSH.Tests.csproj      # expect: 1157 passed, 0 failed, ~36 s
```

⚠ **Incremental `dotnet test` runs in this repo have been reporting phantom counts** — the same suite came
back as 696, then 47, then 14, in consecutive runs. A stale/partial test assembly is being picked up, and a
crashed host leaves one behind. Only a run after clearing `PGSH.Tests/bin` and `obj` is trustworthy, and a
line reading `Série de tests abandonnée` means a host crash swallowed part of the run however green the
count above it looks.

Then, with the app up, `GET /cnpn-versions` (Scalar at `/scalar/v1`, or the browser). Expect **four**:

| Code | totalYears | governsAnIntake | studentCount |
|---|---|---|---|
| `1650.25` | 6 | true | 1980 |
| `2174.18` | 7 | true | 6460 |
| `2175.22` | 7 | **false** | 0 |
| `PHARM-LEGACY` | 6 | true | 1745 |

❌ **Fail if** 2175.22 has students — it is a citation, it governs nobody.

---

## 1 · Groups page — pagination (3 min)

*Admin → Groupes → onglet "Groupes"*

1. It loads **25 rows**, with `N groupes — page 1 sur M` and a pager beneath. Previously it fetched
   one 200-row page.
2. Type a group label in **"Rechercher un groupe"**. The list must not re-query on every keystroke —
   there is a 350 ms debounce and a spinner in the field.
3. Go to page 3, then change the **Niveau** filter → you land back on **page 1**, not on an empty
   page 3.
4. "Tout supprimer" names the **total** count, not the 25 on screen.

---

## 2 · The navbar year actually drives things (5 min)

*Anywhere in admin — the year selector top right.*

1. **Dashboard**: "Étudiants inscrits" reads **8 077** with **2025-2026** underneath it, and
   "Groupes formés" shows the year too.
   ⚠ This is the number that surprised you: it is students *enrolled this year*, not the 10 204
   student records on file. 2 127 are alumni who last enrolled in an earlier year.
2. Switch the year to **2024-2025**. Both tiles must change **without a page reload** — that is the
   RTK Query cache key working. Switch back.
3. **Calendrier des stages**: it no longer has its own year dropdown; it follows the navbar and the
   subtitle names the year.
4. **Affectations**: pick a stage, note the cohort count, switch year → the cohort list changes.

❌ **Fail if** any of these need a refresh to update.

---

## 3 · The import canvas — the 3 500-row bug (5 min)

*Admin → Affectations → pick **CHIRURGIE (6ème année Médecine)** → Importer les notes → Télécharger le modèle*

1. The file downloads as `notes-chirurgie-2025-2026-stage.xlsx` — **the year is in the name**.
2. Open it: **≈688 rows**, not 3 553. That stage ran for six promotions; you are marking one.
3. Switch the navbar to a year the stage did not run and download again → a clear refusal,
   *"Aucun étudiant n'est affecté au stage … pour l'année …"*, not a blank sheet.
4. Upload a sheet containing a CNE from **another** year → that row reports **"Aucun étudiant de ce
   stage ne porte cet identifiant"**. It must not silently mark the wrong student.

❌ **Fail if** the canvas has thousands of rows, or the filename has no year.

---

## 4 · Periods are year-scoped (5 min)

*Admin → Stages → open a stage → Grille de planification*

> Your database has **zero** `StageSlots` — the Access base had no planning grid, only per-student
> date ranges. Everything below starts from empty, and that is correct.

1. The empty grid says **"Ni cohorte ni créneau pour cette année"** and mentions that créneaux are
   per year. It no longer claims only cohorts are missing.
2. **"Répartition auto." is disabled**, with a tooltip giving the actual reason
   ("Aucune période définie…"). Previously it was clickable and always failed.
3. Add créneau **P1**, 01/11/2025 → 15/12/2025. It appears.
4. Try a second **P1** in the same year → refused, *duplicate period number*.
5. Try **P2** overlapping P1's dates → refused, *Schedule.SlotOverlap*.
6. **Now switch the navbar to 2024-2025 and reopen the grid.** P1 is **gone** — it belongs to
   2025-2026. Add a P1 here with the *same* dates 01/11/2025 → 15/12/2025: **accepted**, because two
   promotions never share a student.
7. Switch back to 2025-2026 → your original P1 is still there, alone.

❌ **Fail if** step 6 is refused, or if P1 appears under both years.

---

## 5 · Stage-wide actions stay in their year (3 min)

*Admin → Affectations → a stage with started rotations*

1. Select cohorts, **Démarrer**. Note the count.
2. Switch to a previous year and look at that year's assignments — **untouched**.

This is the one to check carefully: publish / auto-arrange / start / close / pause / resume all used
to reach every promotion when scoped by partition label rather than by explicit cohorts.

---

## 6 · CNPN — the new screens (8 min)

*Admin → CNPN*

1. The two dropdowns are now **texts**, not years: "CNPN 2019 — Docteur en Médecine (7 ans)" and
   "CNPN 2025 — … (6 ans)".
2. **2175.22 must not be the default** in "Texte de référence" — it governs no intake and sorts last.
3. Pick niveau **6ème année**, from **2174.18** → to **1650.25**. Expect the orange
   *"Aucune exigence enregistrée … dans l'un des deux CNPN"* — **1650.25 has nothing recorded yet.
   That is expected and is Phase 15.2 work**, not a bug.
4. Compare **2174.18 → 2174.18** for a level that has data (3ème, 4ème, 5ème, 6ème) → the table
   renders with "Texte identique".
5. In the editor under the comparison, record a set for **1650.25 / 6ème année** and save. Re-open →
   it persists.
6. **Try to record a set for 7ème année under 1650.25** → refused:
   *"Ce CNPN organise 6 années — la 7ᵉ année n'en fait pas partie."* This is `TotalYears` earning
   its place.

---

## 7 · Groups never mix CNPN texts (5 min)

*Admin → Groupes → onglet "Répartition automatique"*

This is the 2026-2027 problem, testable today because levels 1–2 already hold 21 old-text students.

1. Pick niveau **2ème année Médecine**, taille 20, **Lancer la répartition**.
   (If everyone already has a group, use "Vider toutes" on a test year first — **not** on 2025-2026.)
2. Look at the created groups: those holding the 20 old-text students are labelled
   **`[2174.18]`**, the rest **`[1650.25]`**. No group contains both.
3. For a level with only one text, labels carry **no** `[...]` suffix — the code is only shown when
   there is something to tell apart.

❌ **Fail if** one group contains students of two texts.

---

## 8 · Planning refuses stages the text dropped (4 min)

*Admin → Groupes → onglet "Planification Macro"*

Needs a recorded requirement set (do step 6.5 first, or use a level with 2174.18 data).

1. Tick a (partition × stage) where the stage is **not** in that group's CNPN for that level.
2. **Générer le plan** → an orange **"n hors CNPN"** badge appears next to the results.
3. No cohort was created for that pair; the others were.

If nothing is recorded for that (text, level), the check **stands aside** and the cohort is created —
deliberate, so that missing data cannot block all planning.

---

## 9 · Student list is a population, not a formatting choice (2 min)

*Admin → Étudiants*

1. With **2025-2026** selected: **8 077** total.
2. Switch to **2019-2020**: a smaller list, and every row has a level/group — previously it listed
   all 10 204 with blank columns past the name.

---

## 9b · Recording a CNPN text (5 min)

*Admin → CNPN → « Textes CNPN — Medecine » (top card)*

> **Restart the API first** — these endpoints postdate the running process.

Four rows, with a **completeness bar** each — this is the thing that was previously invisible:

| Référence | Durée | Entrants | Saisie |
|---|---|---|---|
| 2174.18 | 7 ans | à partir de 2005-2006 | **6 / 7 niveaux** (orange) |
| 1650.25 | 6 ans | à partir de 2024-2025 | **0 / 6 niveaux** (grey) |
| 2175.22 | 7 ans | **citation seule** | 0 / 7 |

1. **Nouveau texte** → fill code/intitulé/durée/entrants → saved. Try the same référence twice for
   one filière → *« Un CNPN portant la référence … existe déjà »*.
2. Create two texts claiming **the same** « à partir de » year → refused,
   *« deux textes ne peuvent pas se disputer une même promotion »*. This one matters: version
   selection resolves "latest intake at or before entry", and a tie has no defensible winner.
3. **Rename `PHARM-LEGACY`** (pencil icon on the Pharmacie programme) to the real arrêté — that
   placeholder was mine, and this is how it goes away.
4. Edit `2174.18` and set durée to **2** → refused, *« comporte déjà des exigences pour la 6ᵉ
   année »*. Shortening a degree must not strand recorded requirements.
5. **« 1650.25 reprend… »** (copy icon) → source `2174.18` → six levels seeded in one action, and the
   completeness bar jumps to **6 / 6**. Run it again → *« 6 déjà saisi(s), conservé(s) »*, nothing
   overwritten.

6. **Deleting — the careful one.** The trash icon is **disabled** on `2174.18`, `1650.25` and
   `PHARM-LEGACY`: each has students, and the tooltip says how many. It is **enabled** only on a text
   nobody follows (`2175.22`, or one you just created).
   - Delete a text you created for the test → gone, and the toast names any requirement sets removed
     with it.
   - If the text governed an intake, the confirmation warns that new registrations will fall back to
     the previous text of the filière.

❌ **Fail if** the clone overwrites a level you had edited by hand, if a 7ᵉ année lands under a
six-year text (it should count as *hors de la durée du texte*), or if the trash icon is ever
clickable on a text with students.

---

## 10 · Rattacher une promotion à un CNPN (6 min)

*Admin → CNPN → bas de page, « Étudiants rattachés à ce CNPN »*

> **Restart the API first** — these endpoints are newer than the running process and will 404 until
> you do.

1. The panel states the current membership: **1980 étudiant(s) suivent actuellement 1650.25**, and
   that new entrants from 2024-2025 join automatically.
2. Leave « Jusqu'à l'année » on **2**, click **Simuler**. Expect roughly:
   - **2001 concernés** — levels 1 and 2 Médecine
   - **~1980 déjà à jour** — the migration already stamped them
   - **21 entrée antérieure** — the repeaters the arrêté excludes, listed individually
   - **Rattacher** disabled, because nothing new would be written
3. Tick **« Inclure les entrées antérieures au texte »** and Simuler again → the 21 move from
   *entrée antérieure* into *à rattacher*, and the button enables. **Do not apply** unless scolarité
   has actually decided those 21 belong on the new text.
4. Set the year to **6** and Simuler → a much larger population, and this time a real
   **déjà rattaché** count (the 3rd–6th years confirmed on 2174.18). Those are listed with a lock
   badge and are **never** moved in bulk.

❌ **Fail if** a *déjà rattaché* student changes text, or if the 21 are swept in without the box.

---

## 11 · A service's capacity, two ways (6 min)

*Admin → Hôpitaux → un service → modifier*

The rule under test: **no quota rows means the service admits everyone against `Service.Capacity`;
the first row closes it to every level without one.** An empty quota table reads as "nothing set yet"
but means "open" — the form has to say so.

1. Pick an unrestricted service (all 148 imported ones are). The quota section must state that it is
   **open to every promotion** and that the total applies.
2. Add one quota — say **3ème année Médecine: 10**. On save, the form must warn that
   `Service.Capacity` is **now ignored** and that every level without a row is no longer admitted.
3. Add the service to a stage of a level it has **no** quota for
   (*Formation → Stages → un stage → Services autorisés*). Expect a refusal naming the promotions it
   *does* take. The picker should not even offer it — it passes `admitsLevelId`.
4. Auto-arrange that stage. Expect **`NoServicesAdmitLevel`**, not "no services" — the two are
   different screens.

❌ **Fail if** a quota is validated against the total (it deliberately is not — the quotas *replace*
it), or if a service with no rows is treated as unconfigured.

---

## 12 · Répartition annuelle (4 min)

*Admin → Formation → Répartition annuelle*

⚠ **Expect it to be empty** unless you have authored a planning: the base holds **0 `StageSlots`** and
**0 `CohortSlotAssignments`**. That is the honest state, not a bug — see *No periods in legacy*.

1. Pick a level and the current year. With no planning, the page says so and names both missing
   pieces (no créneaux / no cohortes), and the export button is disabled.
2. To see it work, author two periods on one stage and run **Répartition auto.** Then: rows are
   `Stage | Service (Chef)`, columns are the periods with their date ranges, cells are collapsed group
   ranges (`47-50`), and unplanned cells are **hatched and counted** in an orange banner.
3. Export. The `.html` must open standalone with its styling intact — it is the same DOM node the
   preview rendered, serialized.
4. Chefs: on ~95% of services the name comes from the `Description` note (140 of 148 carry
   `Responsable (source) : Pr.X`, 0 have `ServiceChefId`). Those rows must be **flagged** as
   source-note derived, not presented as a dated tenure.

❌ **Fail if** a row's cells are silently shortened where a period has no assignment.

---

## 12c · Rotation cycle: the generated crossover (8 min)

`POST levels/{levelId}/rotation-cycle[/preview]`

Replaces ticking the macro matrix by hand. Body: the stages that run **concurrently** *with each stage's
own period count*, plus the block's date windows — entered **once**, at the finest granularity any stage
in the block uses.

```json
{ "stages": [ { "stageId": 1, "periods": 2 }, { "stageId": 2, "periods": 2 } ],
  "windows": [ {...}, {...}, {...}, {...} ] }
```

1. **The mirror.** Two stages at 2 periods, 4 windows → Médecine `A:P1-P2, B:P3-P4` and Chirurgie
   `B:P1-P2, A:P3-P4`.
2. **The arithmetic.** Send 6 windows for three stages at 1 period → refused, message states
   `T = Σkₛ = 3` columns however many partitions exist.
3. **Mixed durations — the 6th year.** Four stages at 2 periods and two at 1 → `T = 10`, and it needs
   **exactly 10 partitions** (`L = [2,2,2,2,1,1]`). With any other count it refuses and names the
   multiples of 10. Check the layout: each 2-period stage reports `concurrency: 2`, each 1-period stage
   `concurrency: 1`, and they sum to 10.
4. **Apply** → every stage gets a slot per column, all from the one list of dates. In the grid, Med P1 and
   Chir P1 must be the same window — they cannot drift, there is only one set of dates.
5. Post the returned `matrix` to `stages/macro-plan`; cohorts, affectation, arrange and publish behave
   exactly as for a hand-ticked matrix.
6. Re-apply → `slotsReplaced` equals the previous count; the axis is replaced, never merged.
7. Apply a **second block** (semester 2, other stages, non-overlapping windows) → `slotsReplaced: 0`,
   first block untouched.
8. On a block with a **published** cell, preview says `canApply: false` and apply refuses with
   `RotationCycle.CannotReplacePublished`.
9. **The impossible case.** One stage at 2 periods beside one at 1 → `T = 3`, refused with
   `RotationCycle.NoFeasibleArrangement`. That is a proof, not a timeout: a two-column run must cover
   column 2 wherever it starts, so no arrangement exists at any partition count.

❌ **Fail if** a partition appears twice in one column, or a stage's occupancy in some column differs from
its reported `concurrency`.

⚠ A period is **one service**, not one stage: a 2-period stage gives its partition *two consecutive
services*, so its `periodNumbers` has two entries.

---

## 12j · The 5th year plans all nine of its columns (10 min) — session 18

The bug this session started from: a full 9-column rotation cycle for the 5ème année médecine produced a
répartition with **périodes 8 and 9 filled and 1–7 empty**, and the UI called it a success. Two causes,
both fixed; this step checks both, and it is the one that needs `SplitAcademicGroupsPerLevel` applied.

**Before you start** — with the stack up, confirm the migration ran:

```sql
SELECT count(*) AS groups, count("LevelId") AS with_level FROM "AcademicGroups";
-- expect 3707 / 3696   (was 1003 / 0)
SELECT count(*) FROM (SELECT r."AcademicGroupId" FROM "Registrations" r
  JOIN "AcademicGroups" g ON g."Id"=r."AcademicGroupId" WHERE g."GroupNumber">0
  GROUP BY 1 HAVING count(DISTINCT r."LevelId")>1) x;   -- expect 0
```

Registrations (43 605), cohorts (13 604) and cells (440) must be **unchanged** — the split re-points rows,
it never creates or drops one.

1. **Rosters are per promotion now.** Groupe 1 of 2025-2026 is five rows, one per promotion:
   `Groupe 1 — Troisième Année Médecine`, `… Quatrième`, `… Cinquième`, `… Sixième`,
   `… Cinquième Année Pharmacie`. On the Groups page, filter by level: the 3rd year shows 1-80, the 5th
   1-60, the 6th 1-100 — three independent numberings.
2. ⚠ **Re-cut every promotion's partitions before planning.** The split carried each roster's old label
   over, and the base held **one global cut** (A=19, B=19, C-I=8-9, J=2 across 100 rosters) that was a
   mixture of the 5th year's 9-way and the 6th year's 10-way. It is not a partitioning of anything now.
   Per level: « Supprimer les partitions » → « Redécouper ». Refused while published, which is correct.
3. **Plan the 5th year.** Rotation cycle, level 5, stages Gynécologie `k=3` + the six others at `k=1`
   → `T = 9`, `P = 9`. Apply, then « Planifier ».
4. ❌ **Fail if** the toast reports any `conflit(s)` — that is the cross-promotion collision, and it should
   now be zero. Before the split it was **420** (60 rosters × 7 refused columns) and was not displayed at
   all; the toast now names it.
5. **Open the répartition.** All **nine** columns filled, 60 groups in each, `540` planned cells.
6. **The balance.** Gynécologie holds 3 partitions at once (`L = 3` — twenty groups over five services).
   Every column must read **4 / 4 / 4 / 4 / 4**, which is what `demo/MED05.png` prints.
   ❌ **Fail if** it reads 6/5/3/3/3 — that is each partition being arranged in its own call again.

```sql
SELECT sl."PeriodNumber", sv."Name", count(*) FROM "CohortSlotAssignments" a
JOIN "StageSlots" sl ON sl."Id"=a."StageSlotId" JOIN "Services" sv ON sv."Id"=a."ServiceId"
JOIN "Stages" st ON st."Id"=sl."StageId"
WHERE st."Name" LIKE 'Gyn%' AND sl."AcademicYearId"=21 GROUP BY 1,2 ORDER BY 1,2;
```

7. **The refusal now names the promotion.** Plan the 3rd year over dates that overlap the 5th year's, on
   purpose, then place one roster by hand into a second stage on the same window: the error reads
   « Le groupe N est déjà affecté au stage « X » (**Troisième Année Médecine**) période P… ». Without the
   promotion, that message sent you hunting through a level that has no such stage.
8. **« Non réparti » is never partitioned.** After a re-cut, its `RotationGroup` is still null — it holds
   4 725 students of every promotion and is not a rotation partition. It must still be **visible** on the
   Groups page under a level filter, because that is where those students get assigned from.
9. **A promotion nobody cut refuses a partitioned arrange.** On a level with no labels, « Répartition
   auto. » targeting a partition now answers `Schedule.PromotionNotPartitioned` and names the promotion.
   ❌ **Fail if** it reports 0 cells and looks like a success, or if it invents a cut of its own — the
   old fallback took the *stage's* service count, so running it on Santé Publique (one service) cut the
   whole promotion one-way and every later stage inherited it.

**Executed 2026-08-13** (session 18). Med5 replanned from an empty axis: 9 columns × 15 j. ouvrables from
03/11/2025, **540 cellules, 0 conflits**, Gynécologie 4/4/4/4/4 per column (21/19/20 groups per column
gives 5/4/4/4/4 and 3/4/4/4/4 — spread 1, which is optimal since 60 ∤ 9), 0 double-bookings, 3 hatched
cells on *Réa.Oto.Neuro.Oph* (7 services, partitions of 6). The `GroupAlreadyPlaced` refusal was verified
live as **HTTP 409** with the cell left unwritten; its **wording is covered by unit test only** — the
toast expires faster than a screenshot round-trip.

**Data repaired in the same pass** (both provable, both on promotions with 0 published cells):
- **Med3** carried Med5's 9-way labels over a plan that is a 2-way mirror (40 groups per stage per
  column, odd → Chirurgie P1-2, even → Médecine P1-2). Re-cut 2-way *Alterné* restores exactly the
  labels the plan was built on: A = the 40 odd rosters, B = the 40 even, **0 mixed cells**.
- **Med6** had 10 partitions — the right count for its authored 10-column axis — but sizes 2 to 19.
  Re-cut to a clean 10 × 10 before anything is planned on it.
- ⚠ **Med4 and Pharma5 are still lopsided** (9 partitions, 6–13 rosters). Harmless today: no axis, no
  cells. Cut them to whatever their block needs *before* planning, not after.

---

## 12d · Period drift between stages (2 min)

The gap 12c works around: nothing in the schema says Médecine P1 and Chirurgie P1 are the same window.

1. Author two stages by hand with the *same* period numbers and deliberately different dates — say
   Chir P1 running two days past Med P1.
2. `GET levels/{id}/repartition` → **`axisDisagreements`** names P1 and prints both windows with the
   stages declaring each.
3. Note the table itself looks **normal** in that case: Chirurgie's window contains Médecine's, so the
   axis drops it as a composite and absorbs the error. That is exactly why the diagnostic exists.
4. Widen the drift to a partial overlap → the table now grows an extra column with hatched holes.

⚠ **Not an error, by design.** Med6 legitimately has Chirurgie on two-month periods and ANES REA on
monthly ones, both numbered P1.

---

## 12b · Partition shape: stripes vs blocks (4 min)

`POST groups/assign-partitions?academicYearId=…&levelId=…`

The response now prints each partition's membership in the same collapsed form the répartition uses, so
the two strategies are comparable **without arranging anything**.

1. `{ "partitionCount": 2 }` on an unpartitioned promotion → `A: "1, 3, 5, 7…"`. That is the historical
   behaviour and the default; it is what makes the published table print comma lists.
2. `{ "partitionCount": 2, "strategy": "Contiguous" }` → `A: "1-40"`, `B: "41-80"`, matching `Med3.png`.
3. Send **either** a second time without `reassign` → `labeled: 0`, nothing moves. A gap-fill must never
   reshuffle a plan already built on the current cut.
4. Add `"reassign": true` → it re-cuts, reports `reassigned` and **`plannedCellsAffected`**. Those cells
   are untouched but were placed for a partition the group may have left, so **re-run auto-arrange**.
5. On a promotion with a **published** cell, `reassign: true` must be **refused**
   (`Partitions.CannotReassignPublished`) — students were sent there.
6. Then re-arrange and open the répartition: with `Contiguous`, cells read `47-50` instead of
   `47, 49, 51, 53`.

❌ **Fail if** partition sizes differ between the two strategies — they must be identical; only the
membership changes. ⚠ Check the CNPN interaction before adopting `Contiguous` as the default: group
numbers run contiguously *per text*, so a block can land entirely inside one CNPN.

---

## 12e · Jours fériés, and a column measured in worked days (7 min)

> ✅ **Executed 2026-08-13 against the real base.** 252 → 247 jours ouvrables after the four lunar
> holidays; Aïd al-Fitr 20–21/03 costs **1** day (the 21st is a Saturday) and Manifeste 11/01 costs **0**
> (a Sunday). `mois` swings 18–22 and warns; `jours ouvrables ×20` gives exactly 20 every column.

**Formation → Jours fériés.** The calendar every duration is counted against.

1. **Générer les fêtes nationales** → ~6 rows for 2025-2026 (the fixed dates falling between September
   and July). Re-click it: `created: 0`, everything `déjà présente` — idempotent on (date, name).
2. Check the **Ouvrables perdus** column. A férié landing on a Saturday or Sunday must read **0** — it
   costs nothing, and the count is deliberately measured against a weekend-only calendar so it can say so.
3. The banner must list what is **missing**: *Aïd al-Fitr, Aïd al-Adha, 1ᵉʳ Moharram, Aïd al-Mawlid*.
   These are lunar, fixed by decree, and PGSH cannot compute them.
4. **Ajouter** → Aïd al-Adha, two days, type *Religieuse*, **uncheck « Date confirmée »**. It appears with
   a `provisoire` badge, and the *Provisoires* tile goes to 1.
5. Add an *Facultaire* span of two weeks (vacances) — same table, different colour.

**Formation → Bloc de rotation** — level 3, two stages at 2 periods each (T = 4):

6. Unit **mois**, length 1, start 1 October → the four columns land on the 1st of each month, and each
   shows *n j. ouvr. / n j.* Any column containing a férié you entered shows a *n férié(s)* dot; hover
   for the names. A column over a provisional date is orange and a warning appears above.
7. Switch to **jours ouvrables**, length 20 → **every** column now reads `20 j. ouvr.` while the
   calendar-day figures differ. That is the whole point of the unit: février and mars are not the same
   amount of stage.
8. Switch to **semaines**, length 1…4 → columns of 7 / 14 / 21 / 28 calendar days. Starting on a Monday,
   each holds exactly `weeks × 5` worked days.
9. On a fresh base with nothing entered, the response is flagged **« Calendrier vide — week-ends seuls »**
   and a toast says so. Silence there would mean « jours ouvrables » quietly meant something narrower.
10. **Simuler** → a new *Durée réelle par stage* table: worked and calendar days per stage against its
    stated `DurationInDays`, as a **range** (partitions take different runs of the axis). With 30 stored
    for every stage, expect the note *« 30 jours annoncés : atteints en jours calendaires, pas en jours
    ouvrables »* — that is the ambiguity in the column, not a badly cut axis.

❌ **Fail if** a working-day column's count varies between columns, if a Sunday férié reports a loss, or
if editing a generated window by hand leaves the old counts displayed next to the new dates.
⚠ Deleting a férié reports `slotsSpanning`: those slots keep their dates, but the count that produced
them no longer reproduces.

---

## 12f · Taking back a partitioning (4 min)

> ✅ **Executed 2026-08-13.** Level 6 re-cut 2 → 10 (A–J × 10 groups, incl. 20 previously unlabelled).
> A clear on level 3 (accidental) left all 320 cells, 1872 assignments and 9283 periods intact and the
> original A/B fully reconstructible from the cells — restored and verified identical.

**Groupes → Plan macro**, pick a level. The *Corriger le découpage* card only appears once labels exist.

1. Cut a promotion into **2**. Then set *Nouveau nombre* to 10 and press **Redécouper** → 10 partitions,
   `reassigned` reported.
2. Now the trap this exists for: with labels in place, plain **Assigner** at 10 changes **nothing** — the
   existing count wins, by design, so a re-run cannot reshuffle a live plan. The card says so.
3. **Supprimer les partitions** → every label cleared, and a second toast naming the planned cells that
   now describe no partition. **Re-run auto-arrange.**
4. Verify nothing else went: cohort, cell and period counts must be unchanged. Nothing points at a label,
   so clearing removes no row.
5. On a promotion with a **published** cell it must be **refused** (`Partitions.CannotClearPublished`) —
   the printed répartition names the partition students were sent as.
6. After clearing, **Contigu** now takes: `A: 1-40`, `B: 41-80`. Before clearing it could not.

❌ **Fail if** clearing deletes a cohort, a cell or a period, or if it succeeds on a published promotion.

---

## 12g · The two empty répartitions, and the gap-fill button (6 min)

> ✅ **Part A executed 2026-08-13** against the real base, admin session, after an API restart (required:
> `GET levels/{id}/repartition` gained `summary.declaredSlotCount`). **1ère année Médecine** → « Aucune
> période n'est planifiée » (`declaredSlotCount = 0`); **6ème année Médecine** → « Les périodes de ce
> niveau sont définies (10 colonnes, 60 créneaux) mais aucun groupe n'y est encore réparti », plus the
> blue *Périodes définies, aucune répartition* alert. Before the change Med6 showed the *first* message —
> the reported bug, reproduced and fixed.
>
> ⚠ **Part B step 6-7 NOT executed**: no level currently has an unlabelled group, so the teal card could
> not be made to appear. Its *absence* was verified on Med6 (10 partitions) and Med5 (2). Step 9's
> confirmation was verified and cancelled.

**Part A — an axis that has been applied must not read as one that failed.**

1. Pick a level with **no** `StageSlot` for the current year. **Formation → Répartition annuelle** →
   « Aucune période n'est planifiée pour ce niveau en … ». `declaredSlotCount` is `0`.
2. Now **Formation → Cycle de rotation**: author and **apply** a block for that level (Med6:
   `k = [2,2,2,2,1,1]`, `T = 10`, 10 partitions, unit *jours ouvrables* × 22). Slots are written;
   nothing is arranged yet.
3. Re-open the répartition **without arranging**. It must now say the periods are **defined** and name
   them — « … (10 colonnes, 60 créneaux) mais aucun groupe n'y est encore réparti » — plus a blue
   *Périodes définies, aucune répartition* alert on the page telling you where to go next.
   ❌ **Fail if** it still says « Aucune période n'est planifiée ». That was the bug.
4. Run the auto-arrange / macro plan. The table fills; the blue alert goes.
5. **The deliberate knock-on:** on a *partially* arranged level the unarranged periods now print as
   columns of hatched cells, so `emptyCells` and its orange alert are **higher than before this
   change**. That is correct — those holes were always there and the table was hiding them.

**Part B — filling groups that have no partition, without re-cutting.**

6. **Groupes → Plan macro**, a level that already has labels. Create a new group (or find a level with
   unlabelled ones). A **teal** card appears: « N groupe(s) de ce niveau n'ont aucune partition
   (n° …) », listing the numbers (truncated past 12).
7. Press **Compléter les groupes sans partition**. Only those groups are labelled — every existing
   label is untouched, and the toast names each partition's new membership.
   ❌ **Fail if** an already-labelled group moves. That is a re-cut, and this path must never be one.
8. Press it again with nothing unlabelled: the card is gone entirely, so the act is unreachable when
   it would be a no-op.
9. **Supprimer les partitions** now opens a confirmation **naming the level** and saying what survives.
   Cancel it. ❌ **Fail if** the click clears anything before you confirm — that adjacency is what
   wiped level 3 in session 16.

---

## 12i · The document colours by stage, and never mentions partitions (3 min) — rewritten in session 19

> ✅ **Executed 2026-08-14** on 5Med, 2025-2026, machine-read from the DOM: 7 stage blocks tinted
> 0,1,2,3,4,0,1 with **zero adjacent clashes**, **0** rows untinted, identity cells painted with their
> row's tint (`rgb(227,237,247)` on the first), legend = « Période non planifiée » alone, no `band-`
> class and no « Partitions mêlées » anywhere, and the word *partition* absent from the document's
> visible text (the only surviving `title` is the chef-source note).
>
> ⚠ **This step replaces the session-17 version**, which checked the opposite thing: that every cell
> carried a *partition* band and that the tint alternated halfway across each row. It did — that
> mirror was real and the check was right about the model. It was the wrong thing to print. See the
> note below before assuming this is a regression.

**Formation → Répartition annuelle → Cinquième Année Médecine.**

1. Each stage is a **contiguous block in one tint**, across the whole row — stage, service and every
   période cell alike. Blocks read blue, green, cream, violet, peach, then round again.
2. **No two consecutive blocks share a tint.** Five tints cycling guarantees it; a promotion with more
   than five stages simply reuses a colour far down the page, where a heavy rule and the stage's own
   name separate them.
3. The word « partition » appears **nowhere** — no legend item, no swatch, no tooltip. The reader is a
   student looking for his own group; a partition is scolarité's internal division for building the
   rotation and explains nothing he can act on.
   ❌ **Fail if** a « Partition A / Partition B » key is back, or cells change colour along a row.
4. The legend has exactly one entry, « Période non planifiée » — the hatch is the only mark the
   document makes that is not also written out in words.
5. **Formation → Bloc de rotation** (or a stage's planning grid): the partition is still there, named
   per cohorte. It was never removed from the system, only from the published page.
6. **Télécharger (.html)** and open the file. The tints must survive: the export serializes the live
   DOM and inlines the same stylesheet.

⚠ **Why this is not the per-row band the session-17 step was written to catch.** That band asserted a
row belonged to *one partition*, which is false — a row visits every partition over the year, which is
precisely what the crossover is. A row belongs to exactly one **stage**. Tinting by stage states
something true, and states what the first column already says in words — which is also what makes it
safe to cycle the palette.

---

## 12k · A dense cell must be readable (4 min) — session 19

> ✅ **Executed 2026-08-14** on 5Med, 2025-2026, machine-read from the DOM. At a 1280px document width
> the old `white-space: nowrap` put **15 group tokens outside their own `<td>`** — measured by comparing
> each token's rect against its cell's — while the new rule gives **0**, at most **2** lines per cell,
> and a document only 20px taller (1122 → 1142). At the printed width (1062px = A4 landscape less 8mm
> margins) the table now lays out at **1026px** with **0** overflow and at most 3 lines: before the
> print rule it was pinned at its 1477px screen min-width and ran off the right edge of the sheet.
> The 3rd year still bands `A`/`B` with its legend intact.

**Formation → Répartition annuelle → Cinquième Année Médecine.** Nine partitions, interleaved, one
service for Santé Publique — so a single cell has to carry seven scattered group numbers.

1. Find the **Santé Publique** row. Its cells read `8, 17, 26, 35, 44, 53`, `7, 16, 25, 34, 43, 52`…
   Every number must sit **inside its own column**, wrapping to a second line if need be.
   ❌ **Fail if** the numbers run over the neighbouring période and the two cells overlap. That was
   the bug: a fixed-layout `<td>` does not clip, so the overflow painted across its neighbours and
   neither période could be read.
2. Narrow the browser window until the table scrolls. The cells take more lines; nothing is ever cut,
   ellipsed or hidden. Same for the **Service (Chef de service)** column — long names like
   « Hôp.Spécialités: ORL A - Pr.L.Essakalli Houssyni » now wrap instead of losing the chef's name to
   an ellipsis, which is the half the column exists for.
3. A range never splits across two lines: you must never see « 47- » ending a line and « 50 » opening
   the next. Only whole runs move.
4. Colour is by stage — see step **12i**. (The partition palette was what surfaced this: it wrapped at
   six while this promotion has nine partitions, so A and G printed identically. It is gone.)
5. **Imprimer / PDF**: the last période must be on the sheet. The screen min-width is dropped in print
   (`@media print { table { min-width: 0 } }`), so the table takes the page width and the cells take
   another line instead of the table running off the edge.

---

## 12l · A roster cannot be mixed across promotions by hand (5 min) — session 19

> ⏳ **Not yet executed.** Covered by `GroupPerLevelIdentityTests` (11 cases, green); this is the
> screen-level pass. Needs `GroupLabelPerPromotion` applied.

`SplitAcademicGroupsPerLevel` repaired the data and the unique index keeps two rosters
distinguishable. Neither stops a *student* being moved into another promotion's roster or a *cohorte*
being built across two — both are plain FKs to rows that exist. These are those paths.

1. **Étudiants → un étudiant de 3Med → Transférer**, and target a roster of the 5th year.
   Expect a refusal naming both promotions: « … est un groupe de Cinquième Année Médecine, or cet
   étudiant est inscrit en Troisième Année Médecine ». The registration must be unchanged afterwards.
2. Same with a roster of **2024-2025**: « … appartient à l'année 2024-2025, or cette inscription est
   celle de 2025-2026 ». A registration *is* a year; pointing it at another year's roster does not
   move the student, it makes the row describe two years.
3. **Académique → Groupes → Créer un groupe**, name it « Groupe 1 » on the 3rd year, then create
   « Groupe 1 » on the 4th year. **Both must succeed** — that is the point of the migration — and both
   must be numbered **1**. A third « Groupe 1 » on the 3rd year is refused, naming the promotion.
4. On a stage of the 5th year, try to create a cohorte for a 3rd-year roster (`POST /cohorts`, or the
   stage's cohort modal if it offers the roster at all): « Une cohorte ne peut pas relier deux
   promotions ». Same for « Non réparti »: « … n'appartient à aucune promotion ».
5. Edit « Non réparti » and set a partition label: refused. The bucket holds 4,725 registrations from
   nine promotions; a label on it hands all of them to `CohortProvisioner` as one body.

---

## 15 · La fiche d'un service : ce qu'il contient vraiment (6 min) — session 19

> ✅ **Executed 2026-08-14** against live data, on two services, machine-read from the DOM and checked
> against a **separately written SQL implementation** of the same boundary algorithm — **zero
> mismatches on both**.
>
> **Urologie (Hôpital Moulay Youssef, id 124)** — the cross-stage case: Urologie Néphrologie (5Med,
> 9 périodes) and Chirurgie (3Med, 4 périodes) share it on partly-overlapping windows. 16 segments,
> every window/day-count/load matching SQL; banner « 9 périodes au-dessus de la limite, sur 122 jours
> cumulés. Pic : 48 étudiants du 01/05/2026 au 18/05/2026 ». ⚠ **The largest single période in the
> database is 36** — so a per-slot table would have under-reported the peak by 12 students. That gap
> is the whole justification for segmenting. Drill-down on 06/04→24/04 gave Chirurgie P1 groups
> 75, 77, 79 = 32 plus Urologie P8 group 47 = 13, i.e. 45; « Voir les 45 étudiants nommément » loaded
> 45 across **2 pages** with the debounced search present.
>
> **Santé Publique (id 76)** — the one-service-per-promotion case that prompted this: 9 segments, all
> 9 over capacity, 193 days cumulative, peak **85 against 20** on 27/04→18/05.
>
> ⚠ **Steps 6 and 7 below were NOT executed.** 7 (year switch) — the automation could not drive the
> Mantine year dropdown reliably; the arg is in the RTK Query cache key by construction but that is a
> code reading, not an observation. 6 (quota edit) — authoring a quota *restricts* a service and
> changes what auto-arrange and publish will do, so it is not something to try unprompted on your
> database. Both are still worth a manual minute.

**Infrastructure → onglet Services → cliquer le nom d'un service.** New page, `/admin/services/:id`.

1. The header states **the limit in force**, and which of the two it is. With no quota:
   « 20 — toutes promotions confondues » plus the warning that the *first* quota closes the service
   to every promotion without one. With quotas: the per-promotion badges, and an explicit note that
   the total « n'est pas consultée ». ❌ **Fail if** both numbers are presented as live — they never
   are; quotas replace the total, they do not sit under it.
2. **Occupation réelle.** Each row is a stretch over which the occupants do not change — *not* a
   période. On a service shared by two stages whose windows only partly overlap you must see three
   rows, the middle one carrying the sum. ❌ **Fail if** there is one row per créneau: the peak lives
   in the overlap and per-slot rows never show it.
3. Pick a service you know to be over-subscribed. The orange banner names the number of stretches, the
   cumulative days over the limit, and the peak with its dates — and says how many *stages* and
   *promotions* the load comes from. That last part is the point: a service is usually over-filled by
   a stage nobody was looking at.
4. Expand a stretch → the stages, promotions, périodes and group numbers present. Then
   « Voir les N étudiants nommément » → a **paged**, debounced list. ❌ **Fail if** it loads unpaged;
   a saturated stretch holds 85.
5. **Stages autorisés**: the reverse of the stage's own list. A row badged « non admis » is a real
   contradiction — the stage lists this service, the service's quotas exclude the stage's promotion —
   and auto-arrange silently drops it today.
6. Edit the quotas via « Modifier le service et ses quotas ». On save the timeline's verdicts must
   change **without a reload**: the mutation invalidates `occupancy-{id}` and `stages-{id}`.
7. Change the navbar year → the timeline refetches. ❌ **Fail if** it keeps last year's load under
   this year's heading (that is `academicYearId` missing from the RTK Query arg, i.e. from the cache key).

---

## 17 · A partition count that describes the promotion (10 min) — session 21

> ✅ **Executed 2026-08-16** against the real base (admin session, after the migration and an API
> restart), **section E completed 2026-08-17**. Migration `PartitionScopeAndIndexGaps` applied;
> `IX_AcademicYear_IsCurrent` present; exactly **1** current year, so the migration's demotion touched
> **0** rows as predicted. **E 15 is no longer a manual step** — the refusal it could never reach is
> now covered end-to-end by `PGSH.Tests/Integration/PartitionEndpointTests`.
>
> ⚠ **The promotion named in session 17 was the wrong one.** Med6 is **clean** — A–J × 10 exactly. The
> defect is live on **4ème année Médecine** and **5ème année Pharmacie**, in a milder but identical
> form, and it is the same defect: both were cut nine ways at 60 rosters
> (`7,7,7,7,7,7,6,6,6`), then grew by 12, and every one of the 12 went into **A or B**:
>
> ```
> A  13  1,10,19,28,37,46,55,61,63,65,67,69,71
> B  13  2,11,20,29,38,47,56,62,64,66,68,70,72
> C   7  3,12,21,30,39,48,57      …    I  6  9,18,27,36,45,54
> ```
>
> Groups 1–60 are a textbook interleave over nine partitions; 61–72 alternate over two. Both promotions
> were repaired (below) → **9 × 8 each**. Nothing else moved: 13,604 cohortes, 860 cellules, 98,555
> affectations, 105,626 périodes, identical before and after.

**A · The repair — 4ème Médecine and 5ème Pharmacie.**

The lopsided cut is *data*: the write path is fixed, the rosters already sitting in A and B are not.
Neither promotion had a planned cell or a published period, so the repair was free.

1. **Groupes → Plan macro**, pick the promotion. The card now reads its numbers from the server; they
   must match the SQL above.
2. **Supprimer les partitions** → the confirm names the level and what survives, and the second toast
   names the planned cells that now describe no partition (**0** on both).
3. Re-cut into **9**, *Alterné* — the shape they already had. Sizes come out even: 8 per partition.
4. Check the other promotions the same way. Any promotion whose partitions are wildly uneven *and* has
   no published cell is the same defect; clear and re-cut. ⚠ One that **is** published cannot be
   repaired this way and must not be: students were sent under those labels.
   *(Checked 2026-08-16: 3Med 40/40, 5Med 7×6+6×3, Med6 10×10 — all even. Nothing else to repair.)*

**B · The count now comes from the promotion, not from one stage's cohorts.**

5. On a promotion cut into ≥ 3 partitions, find a stage whose cohorts cover only *some* of them (a stage
   the CNPN does not require of every group, or one provisioned before the promotion grew). Create one
   roster with no partition and give it a cohorte for that stage.
6. Run **Répartition automatique** on that stage with no partition count.
   - The new roster must join one of the promotion's **existing** partitions — including C, D, … which
     that stage cannot see. Before this session it could only ever land in the labels present among the
     stage's own cohorts.
   - A roster of the promotion with **no cohorte in that stage** must stay unlabelled: an arrange labels
     only what it places.
7. The mirror case: a stage all of whose cohorts are unlabelled, on a promotion that *is* cut. Arranging
   it must fill from the promotion's cut — not report `Schedule.PromotionNotPartitioned`, which is what
   it used to do.

**C · The cut cannot reach past its promotion.**

8. `POST /api/groups/assign-partitions?academicYearId=N` with **no** `levelId` → **400**. It used to cut
   every promotion of the year at once and label « Non réparti » along with them. Same for
   `DELETE /api/groups/partitions`. ✅ Confirmed against the published contract
   (`/openapi/v1.json`): `levelId` is `required: true` on **both**, where it used to be optional.
9. After any cut, « Non réparti » (groupe n° 0) must still carry **no** partition, and no other
   promotion's rosters may have changed.

**D · The count is not read off a page.**

10. `GET /api/groups/partitioning?levelId=N` returns `totalGroups`, `labelledGroups`,
    `unlabelledGroups`, `unlabelledGroupNumbers` and one row per partition. Compare `totalGroups` with
    the promotion's real roster count — for a promotion **past 200 rosters** the tab used to read low,
    silently. ✅ `GET /api/groups/partitioning?academicYearId=21&levelId=4` → **200**, the tab renders
    from it, and **no `pageSize=200` group fetch is issued for this tab at all** (network log).
11. The unlabelled rosters print collapsed the way the répartition prints a cell (`41-60`, or
    `3, 12, 21` for an interleaved cut), not as a truncated "first 12… +N" list.

**E · « Retrait » is not a promotion.**

14. **Groupes → Plan macro**, open the Niveau picker. « Retrait » must **not** be there any more —
    nor in *Répartition automatique*, *Créer un groupe*, *Cycle de rotation*, *Répartition annuelle*,
    *Curriculum*, *Stages*, or a service's quotas. ⚠ It **must** still appear in the **level catalogue**
    (Académique → Niveaux), in a student's dossier, and in the Groupes **browse filter** — a withdrawn
    registration has to be able to name its level, and 10 rosters sit under it.
    *(Executed 2026-08-17. Read off the DOM rather than the screenshot, because both halves are the
    same control and a picture cannot tell two mounted dropdowns apart: the browse filter's listbox
    carries **16** options **with** « Retrait », the Plan macro and Répartition automatique listboxes
    carry **15 without** it. ⚠ `find` reported a « Retrait » option in "the open dropdown" and was
    wrong — it had matched the browse filter's listbox, still mounted and hidden from an earlier
    click. Enumerate `[role="listbox"]` with its `getBoundingClientRect`, or this step passes and
    fails at the same time. Level catalogue: **16 niveaux**, « Retrait » year 0, present. Dossier of a
    withdrawn student: parcours reads 2ᵉ → 2ᵉ → **Retrait** (Abandonnée) → 4ᵉ, i.e. one of the two who
    came back.)*
15. ~~Force the act anyway~~ — **no longer a manual step.** `PGSH.Tests/Integration/
    PartitionEndpointTests` fires `POST groups/assign-partitions` at the marker through the real
    pipeline and asserts **400 / `Levels.NotAPromotion`** naming the level, **plus that nothing was
    written** — the half no handler test can see, since a guard ordered after the write returns the
    same failure.
    ⚠ **This step went unexecuted two sessions running**, and the reason is worth keeping: once step
    14 passes the marker is not offered anywhere, so the refusal is unreachable by hand, and driving
    it headlessly needs a bearer token. **A guard that can only be checked manually and can only be
    reached by defeating another guard will not be checked.** That is what moved it into the suite
    rather than into a better manual recipe.
16. `DELETE /api/groups/partitions` on it must still **succeed** — that is how a label already on a
    marker's roster comes off, and refusing the undo would leave only SQL.
    *(Executed 2026-08-16: « Groupe 59 — Retrait » carried partition **E**, an artefact of
    `SplitAcademicGroupsPerLevel` copying the folded roster's label onto each shard. Cleared through
    the UI → 0 of 10 Retrait rosters labelled, all 12 registrations intact.)*

**F · One current year, enforced.**

12. `SELECT COUNT(*) FROM "AcademicYears" WHERE "IsCurrent";` → **1**. ✅ The migration demotes extras
    (highest `Id` wins) before creating the index, so it should have touched **0** rows — it did.
13. Flag a second year current directly in SQL → the write must be **refused** by
    `IX_AcademicYear_IsCurrent`. ✅ `duplicate key value violates unique constraint
    "IX_AcademicYear_IsCurrent"`, rolled back.

❌ **Fail if** an auto-arrange labels a roster it is not placing, if a partition label appears on
« Non réparti » or on « Retrait », if a year-wide cut succeeds, if two years can be current at once —
or if « Retrait » disappears from the level catalogue or from a withdrawn student's dossier, which is
the opposite mistake and just as wrong.

---

## 18 · The override stops waiving a rule that is not negotiable (5 min) — session 22

> ⚠ **Not yet executed live.** Needs a restricted service, and **not one of the 148 services has an
> authored quota**, so the state this changes does not exist in the base yet — it has to be created.
> Covered by `ServiceLevelCapacityTests` (`But_it_never_admits_a_promotion_the_service_refuses`,
> confirmed by mutation), which is why this is a check of the *screens*, not of the rule.

1. Pick a service used by a planned but unpublished cohorte. **Fiche du service → quotas**: add a
   quota for **another** promotion only. That single row *restricts* the service — from then on it
   admits no promotion without one.
2. Publish the cohorte with « Autoriser le dépassement d'effectif » **ticked**. It must still be
   **refused** with `Schedule.LevelNotAdmitted`, and the message must say « Ce refus ne peut pas être
   forcé ». ⚠ Before this session the tick published it.
3. The checkbox's description must **not** promise to force a service that refuses the promotion —
   it used to, in both the per-cohorte and the publish-all modals. A control describing a power it
   lacks is worse than none: the admin ticks it, gets the same refusal, and concludes the screen is
   broken rather than the plan.
4. Now add a quota for **this** promotion, smaller than the cohorte. Publish with the box **unticked**
   → refused `Schedule.LevelCapacityExceeded`. Tick it → **published**. That half is unchanged, and it
   is the half that has to keep working: two thirds of planned cells are over capacity.
5. Remove the quotas afterwards, or the service stays restricted.

❌ **Fail if** the tick publishes onto a service that does not admit the promotion, if the refusal
does not say it cannot be forced, or if ticking it no longer gets a merely over-full service through.

---

## 12h · Correcting Aïd after the décret (4 min)

> ✅ **Executed 2026-08-13** on the real *Aïd al-Fitr* row, and **fully restored afterwards** —
> 20/03→21/03, provisoire, 247 jours ouvrables, 14 entrées, identical to the starting state.
> Three saves: a no-op re-save (`datesMoved: false`), a move to 21→22, and the move back. All persisted
> correctly, so the new `200`+body contract parses. Moving it onto a weekend took *jours ouvrables*
> 247 → **248** and that row's « ouvrables perdus » 1 → **0**, which is `WorkingDaysLost` behaving as
> designed.
>
> ⚠ **The info toast itself was never caught on camera** — toasts expire faster than the screenshot
> round-trip, so the `slotsSpanning` figure rests on unit tests, not on this run.
>
> ⚠ **One unexplained failure, not reproduced.** The *first* date-move save tripped the error boundary
> («&nbsp;Une erreur inattendue&nbsp;») and did **not** persist. Three subsequent identical saves all
> succeeded, the console held no error, and no data was corrupted. Cause not established — if it
> recurs, read the console before reloading.

The workflow this exists for: the lunar date is entered in September as an estimate, and corrected when
the decree lands. Deleting a holiday always said how many windows were laid over it; editing said nothing.

1. **Formation → Jours fériés**. Record *Aïd al-Fitr* on an estimated date, **unconfirmed**, 2 days.
2. Author an axis in **jours ouvrables** over a span containing it (12g step 2 will do).
3. Edit the holiday: **move it by one day** and tick « Date confirmée ». Expect the success toast **plus**
   an info toast: « La date a changé : N créneau(x) couvrent l'ancienne ou la nouvelle période … ».
   - N counts the union of the two spans **once** — a one-day correction sits inside one window, so a
     single slot must be reported as **1**, never 2.
4. Edit it again changing **only the name** (or only the confirmation flag): success toast, and **no**
   second toast. ❌ **Fail if** a slot count is reported here — a span that did not move costs no day,
   and crying wolf on it is what makes the real report ignorable.
5. Delete a holiday: the existing `SlotsSpanning` toast must still appear, unchanged.

❌ **Fail if** the edit succeeds but the dates it reports are the *new* ones on a count taken *after*
the write — the number must be computed against the span it left as well.

---

## 13 · Closing a year: the déliberation canvas (8 min)

*Admin → Étudiants → une promotion → « Clôturer l'année »*

> ⚠ **Superseded by step 19 (session 23).** The routes moved from `levels/{id}/deliberation*` to
> `deliberation*`, the canvas became a list of exceptions, and the screen this step describes was never
> actually built — 13 and 14 were written against the API alone. Kept because the *properties* they
> check (all-or-nothing, the contradiction count, idempotence) are unchanged and step 18 leans on them.

PGSH has no exams, no TP, no deliberation — so the verdict comes from the faculty as a file. This is
what replaces guessing at `Registration.Status`.

1. **Download the canvas** for a level + year. It must carry one row per registration of *that*
   promotion only — a 3ème année of the current year, not the six years the level ever ran. The
   `Décision` column is a dropdown (`Admis / Redoublant / Exclu / Diplômé / Abandon`), and a
   *Mode d'emploi* sheet explains each.
2. Fill a handful, leave the rest blank, **Simuler**. Expect:
   - the rows you filled as *à enregistrer*
   - **`NotCovered`** = everyone the file does not mention, shown before you apply
   - a **contradiction count** for any *Admis* whose stages are not all validated — flagged, **never
     blocking** (the jury deliberates on the whole year; PGSH sees only stages)
3. Mistype one decision (`Peut-être`) and Simuler → that row errors and **Appliquer is disabled**.
   One bad row refuses the whole file: a promotion half closed cannot be reconstructed, because the
   file is not stored.
4. Fix it, **Appliquer**. Each registration now carries the verdict, `OutcomeSource = Declared`, a
   recorded date, and a timeline entry on the student's Historique.
5. Re-download the canvas → the verdicts you just applied come back **pre-filled**. Change one,
   re-upload → it reports *remplace* and the correction lands.
6. Write `Diplômé` on a 3ème année student who carries a CNPN stamp → refused (`NotAFinalYear`). Do
   the same on an **unstamped** student → allowed, because ~2,200 stamps are inferred and 19 students
   have none, and refusing on absence would make the feature unusable.

❌ **Fail if** the good rows land when one row is in error, or if a motif written next to *Admis* is
dropped without the row saying so.

---

## 14 · Réinscription: next year from the verdicts (5 min)

> ⚠ **Superseded by step 19 (session 23)** — route now `reinscription`, `levelId` optional.

*Admin → Étudiants → une promotion clôturée → « Réinscrire »*

Deliberately a **separate act** from step 13 — the deliberation is July, re-registration is September,
and not every admis comes back.

1. Preview the rollover from the closed year into the next. Expect, per bucket:
   *Admis → niveau + 1*, *Redoublant → même niveau*, *Diplômé / Exclu / Abandon → rien*, and
   **NoOutcome** for anyone the deliberation never covered.
2. Apply. New registrations are `Active`, carry **no group** (répartition is auto-arrange's job and
   runs next), and no outcome of their own.
3. **Run it again.** Every row must read *AlreadyRegistered* and nothing new is created — it is
   idempotent on purpose, so you fix the odd verdicts and re-run. (This is the opposite choice from
   step 13's all-or-nothing, because here re-running *is* safe.)
4. An *Admis* on the last year of a programme with no level above → **NextLevelMissing**, reported
   rather than guessed as a graduation. Fix the PV to `Diplômé` and re-run.
5. Then run **auto-arrange groups** for the new year: the students you just registered are the
   "Non réparti" bucket it reads from, and groups stay homogeneous by CNPN.

❌ **Fail if** a second run duplicates registrations, or if a *Diplômé* gets a new year.

---

## 16 · Un stage en un seul service, et une seule évaluation (10 min) — session 20

> **Executed 2026-08-14 against the live base**, on 5MED / Gynécologie Obstétrique (k=3, 9 columns,
> 60 rosters, 9 partitions). Two things could **not** be executed and are marked as such — read those
> before trusting the rest.
>
> | check | result |
> |---|---|
> | 16a · Périodes column, drawer radios | ✅ render correctly |
> | 16a · saving the mode | ❌ → 🔧 **the PUT dropped it** — the endpoint's `Request` record lacked the field, so every save wrote `PerPeriod` back. Fixed; **needs an API rebuild to re-verify** |
> | 16b · unscoped arrange refused | ✅ `Schedule.SingleServiceRunNotScoped`, naming the stage and its 9 périodes |
> | 16b · non-contiguous window refused | ✅ `Schedule.SingleServiceRunNotContiguous` on P1 + P3 |
> | 16b · PerPeriod stage still arranges unscoped | ✅ 60 cells — the new guard does not touch it |
> | 16b · run-by-run arrange | ✅ **60 of 60 rosters: 3 cells, 1 service** |
> | 16b · contrast against PerPeriod | ✅ same axis re-arranged as PerPeriod → **all 60 rosters get 3 distinct services** |
> | document reads as one service per run | ✅ « Gynécologie Obs A » prints `1, 10, 19, 28, 37` across P1-P2-P3; all 5 Gynéco service rows constant within every run; Psychiatrie (PerPeriod) rotates every column; `axisDisagreements: []` |
> | 16c · publish → one collapsed period | ⛔ **not observable on this data** — see below |
> | 16d · unpublish guard | ⛔ nothing is published, so there was nothing to unpublish |
> | 16e · publish skips already-served | ✅ **706 skipped, 0 periods created** |
> | imported history untouched | ✅ 105,626 periods, **0 grid-linked**, 0 coverage rows |
>
> ⛔ **Why 16c and 16d could not run.** Every 2025-2026 assignment in the base already carries one
> imported period per stage — **0 assignments in the whole year lack one**. Publishing therefore skips
> 100% of them (correctly — that is 16e), so no grid-linked period is ever created, and with none
> created there is nothing to unpublish. Observing either on live data means first deciding what to do
> with the imported 2025-2026 periods (they are `IsComplete` but **0 are evaluated**). The collapse is
> covered by `SingleServiceRotationTests`, the guard by `UnpublishScheduleTests`.
>
> **Axis used:** 9 columns × 14 jours ouvrables from 2025-11-03. Six of seven stages then match their
> stated duration exactly; Gynécologie gets 42 jo against 44, Neurologie 14 against 22 — reported by
> `durationChecks`, never blocking, which is the intended behaviour.
>
> ⚠ **4MED could not be planned at all:** all five of its stages have **zero allowed services**, so it
> fails at prerequisite step 0 with `Schedule.NoAllowedServices` — the same state the 6th year is in.
> Its promotion is also cut into **9** partitions, which is invalid for its own axis (`T = 6` needs a
> multiple of 6). Both are data-entry decisions, not code.
>
> ⚠ **Unrelated bug found, pre-existing:** the **Répartition annuelle** page's « Niveau » select is
> stuck disabled on « Chargement… ». `GET /api/levels?pageSize=100` returns 200, but the RTK Query
> entry stays `status: "pending"` for ever, so `isLoading` never clears. Verified identical with all
> session-19/20 frontend changes stashed, so it is **not** from this work. The page is unusable until
> fixed; the underlying data is fine (read directly from `GET /levels/5/repartition`).


The feature: a stage occupying several périodes can keep the group in **one** service for the whole
run, with **one** evaluation instead of `kₛ`. Default is unchanged (`PerPeriod`), so nothing moves
until you switch a stage.

⚠ **Nothing in your base is published**, so all of this is safe to try and safe to undo. Pick a stage
whose répartition you are willing to re-arrange — **5MED Gynécologie Obstétrique** (k=3) is the case
the feature was built for.

### 16a — the switch is on the stage

1. **Stages** → the list now has a **Périodes** column: every row reads « Un par période » today.
2. Edit *Gynécologie Obstétrique* → « Déroulement des périodes » → **Un seul service pour tout le
   stage** → save. The row's badge turns teal, « Service unique ».

❌ **Fail if** saving reports an error, or the badge does not change. If it says *le mode de rotation
ne peut plus être modifié*, that stage's répartition is published — dépubliez d'abord (16d).

### 16b — arranging keeps the group put

3. Open the stage's planning grid and run **Répartition automatique** *without* choosing périodes.
   → refused: **« Précisez les périodes du passage à répartir (par exemple P1 à P3) »**.
   This is the guard that matters: unscoped, a cohort would get one service for all nine columns.
4. Re-run scoped to **P1–P3**. Every group now shows the **same service across the three columns**,
   where before it moved S1 → S2 → S3.
5. Try a non-contiguous window (P1 and P3) → refused: *les périodes … ne se suivent pas*.

❌ **Fail if** an unscoped arrange succeeds, or if a group's three cells hold different services.

### 16c — publishing produces one period, not three

6. Publish that partition. Open a student of one of those groups (**Étudiants → parcours**, or the
   cohort's assignment list).
   → **one** service period spanning P1's start to P3's end, not three.
7. The chef of that service sees **one** évaluation to fill for that student, not three. The stage
   note is that single mark.

❌ **Fail if** three periods appear, or if the single period stops at P1's end date.

### 16d — undoing is guarded now

8. **Dépublier** that cohort immediately → succeeds, and the toast says how many periods were removed.
9. Now publish again, **start** the assignments, and dépublier again.
   → refused, naming the toll: *« sur N période(s) publiée(s), N ont démarré, … »*. Confirm the second
   dialog (**Supprimer quand même**) and it proceeds.
   ⚠ This is the fix for a real hole: before, that first click deleted the periods **and cascaded away
   every evaluation and attendance record** with no warning at all.
10. « Dépublier tous les plannings » never forces — a started cohort is counted as an error, not
    silently emptied.

❌ **Fail if** step 9's first click deletes anything, or if the assignment afterwards still reads
*Validé* / carries a note with no periods behind it.

### 16e — publishing does not land on top of the imported history

11. Publish **any** 2025-2026 stage on a promotion that came from the Access import.
    → the result reports **`skippedAlreadyServed`**, and every student who already had an imported
    period for that stage keeps exactly that one — no second set.
12. Check one such student's parcours: **one** period for the stage, the imported one, unchanged.

❌ **Fail if** a student ends up with two periods for one stage, or if `skippedAlreadyServed` is 0 on
a promotion the import populated (all 706 5MED assignments of 2025-2026 have one).

### 16f — the toggle back

13. Dépublier the stage, then switch it back to « Un service par période » and re-arrange scoped to
    P1–P3. The groups move between services again.

---

## 19 · Clôture & réinscription, pour de vrai (15 min) — session 23

*Admin → Académique → « Clôture & réinscription »* — a screen that did not exist before this session.

> **Status: 19a, 19b, 19c and 19d executed 2026-08-18** against the live base (admin session), and the
> base was **restored afterwards** — 0 verdicts recorded, 0 registrations in 2026-2027. The only thing
> left behind on purpose is the **2026-2027 academic year** (created during the run, needed anyway) and
> two `History` rows for the test student, which honestly record what happened.
>
> The **year-wide apply is deliberately not executed** — see 19f. **19e is not executable on this base**:
> after the fix in 19f every registration of the current year has a group, and the only group-less
> registration reachable is one the rollover creates in 2026-2027, a year with no groups, no cohorts
> and no published schedule. The interesting half of 19e — materialising only the windows still open —
> needs a published grid, and the base holds **zero** grid-linked périodes anywhere.

> **Restart the API first.** `deliberation*`, `reinscription*`, `registrations/{id}/outcome[/reopen]`
> and `groups/assign-student` are new routes; the old `levels/{id}/…` ones are gone. No migration.

### 19a — the exceptions canvas

1. Leave **Promotion** empty (all of them), keep **Exceptions seulement**, and download the canvas.
   Two tabs matter: `Déliberation` is *empty* under a warning line, and `Étudiants (référence)` lists
   every registration of the year with its niveau and its « Décision enregistrée ».
2. Put 3–4 rows in `Déliberation` — copy CNEs from the reference tab — mixing `Redoublant`, `Exclu`
   and `Abandon`. Upload.
3. The simulation must show, **per promotion**: inscrits, dans le fichier, **admis par défaut**,
   **diplômés**, inchangés.
   - **Measured 2026-08-18** with a 3-row exceptions file, and every figure reconciles against SQL:
     8,077 registrations − 3 named − 1 already-decided (« Retrait », already `Withdrawn`) =
     **8,073 admis par défaut**, of which **2,016 diplômés**.
   - ⚠ The one number to read carefully is **Diplômés**. It is per *student*, from his own CNPN, not
     per level, and the row that proves it is **Sixième Année Médecine: 686 admis / 2 diplômés** —
     two students on the six-year text sitting beside 686 on the seven-year one. The level alone
     could never have produced that split. The rest: 6ᵉ Pharmacie 356/356, 7ᵉ Médecine 1 657/1 657,
     Interne CHU 1/1.
4. **Enregistrer les décisions is disabled** until the confirmation checkbox is ticked. Tick it, apply.
5. Re-upload the *same* file. Now everyone carries a verdict, so `admis par défaut` = **0** and
   `déjà décidé(s), inchangé(s)` = the whole year. Nothing changes. That is the re-runnability rule.

### 19b — the guard that matters (do this one)

6. Simulate again with a file naming one student. Note the « admis par défaut » figure. **Before
   applying**, create a registration for a new student in the same year (19d below, or the student
   dossier). Now apply → refused, `Deliberation.DefaultsNotConfirmed`, naming both numbers.
   *This is the whole reason the count is echoed instead of a checkbox.* Re-simulate → applies.
7. Mistype one CNE → the row errors, **Enregistrer is disabled**, and the alert says the import is
   all-or-nothing. Verify **nothing** was written (the untouched students still read « en cours »).
   - **Executed 2026-08-18** with `CNE-QUI-NEXISTE-PAS` and a decision reading « Peut-être »: two
     error rows, correctly diagnosed (*Étudiant inconnu* / *Décision invalide*), the second one still
     naming the student it belongs to so the operator knows whose line to fix. Apply stayed disabled.

### 19c — réinscription, year-wide

8. Pick the destination year (the picker only offers years starting *after* the one being closed; if
   none exists it says so). **Simuler**. Expect `willRegister` ≈ admis + redoublants, a per-target-level
   breakdown, and an « à traiter » count for anyone with no decision.
   - **Executed 2026-08-18, before any verdict existed**: `0 à créer`, `8 077 ignoré(s) sur 8 077`,
     `8 077 à traiter`, every row *Aucune décision — clôturez la promotion d'abord*, attention-rows
     first and the list capped. It refuses to carry anyone over before the year is closed, which is
     the guard, not a failure.
9. Apply, then **Simuler again** → 0 to create, everyone `Déjà inscrit`. Idempotent.
10. Check a created row: `Active`, **no group**, so it lands in « Non réparti ».
   - **Executed on one student** (see 19d): 5ᵉ année → **6ᵉ année Médecine**, `Active`,
     `OutcomeSource` null, no group. Exactly one row created.

### 19d — one student at a time

> **Executed end-to-end 2026-08-18** on one student (5ᵉ année Médecine), then reverted. This is the
> cheapest way to exercise the whole chain — verdict → rollover → undo — without touching 8 000 rows,
> and it is worth re-running that way whenever the chain changes.

11. On a student's dossier, the year card now carries a **Décision** select (the five verdicts) and,
    once set, « Prononcée par la faculté » plus the date. Set one → the réinscription picks it up.
    - Verified: `Status=Validated`, `OutcomeSource=Declared`, `OutcomeRecordedOn` set, and a
      `StatusChange` history row carrying the source. The rollover then offered **1 à créer**.
12. Click the **undo arrow** → « Décision retirée ». If the rollover already created next year's
    registration, the toast says so and **that row is still there**. Confirm it is: deleting it would
    take the student's rotations with it.
    - Verified: back to `Active` with `OutcomeSource` null, and a second history row reading
      `reopened: true, withdrawnOutcome: Validated` — a withdrawal is distinguishable from a verdict on
      the timeline, which is the point of having its own event. The 2026-2027 registration survived.
13. ⚠ **The old defect**: edit a registration's status through the edit form. It must now show
    « Prononcée par la faculté », not a blank source. Before this session the form wrote the status and
    the réinscription still reported « aucune décision enregistrée ».

### 19e — joining a roster after publication

14. Take a student with **no group** (a fresh registration, or one from the rollover) →
    « Affecter à un groupe ». Pick a roster of his own promotion whose schedule is published.
15. The toast names the rotations created **and** the stages already over. Then check his dossier:
    - one `InternshipAssignment` per cohorte of the roster, including the finished stages;
    - `ServicePeriod`s **only** for windows not yet closed;
    - a period on a cell the roster has already started shows as *started*, beginning **today**, not
      on the cell's own start date.
16. Try « Affecter » on a student who already has a group → refused, told to use a transfer.
17. Try a roster of another promotion (via the API — the picker filters them out) → refused,
    `AcademicGroups.TargetGroupInAnotherLevel`.

### 19f — ⚠ what the first real run found: silence must not mean *diplômé*

The default is right for years 1–6 and **wrong for a final year**, and the live data says so plainly:

| | in the final year | of whom, there before |
|---|---|---|
| 7ᵉ année Médecine | 1 657 | **855** (550 twice, 173 three times, 132 four times) |
| 6ᵉ année Pharmacie | 356 | **74** |

The 7ᵉ année is the thesis year: students sit in it until they defend, and PGSH holds no record of a
defence. So « everyone not named is diplômé » would graduate **at least ~930 students who are simply
still enrolled** — and that is a floor, since a first-time final-year student can also fail to defend.

An exceptions file only works where the exception is the rare case. In a final year it is the reverse:
the graduates are the list the faculty actually has (the defence roll), and the lingerers are the
silent majority.

**Settled the same day (PHASES 14.3e): the default promotes and never graduates.** Anyone who may be in
his last year is counted and left untouched, and the faculty names its graduates. Re-verified live
after the change: **6 057 admis par défaut / 2 016 en dernière année**, with *Sixième Année Médecine*
splitting **686 admis / 2 en dernière année* — the two students on the six-year text, which is the
per-student CNPN rule visible on real data.

⚠ **What this step is now for.** The rule is verified by tests and by those numbers; what nobody has
walked is the flow it makes load-bearing. **Upload a defence roll**: a file naming a handful of 7ᵉ année
students « Diplômé ». Expect exactly those to graduate, the rest to stay `Active` with no decision, and
`FinalYearUndecided` to drop by the number you named.

---

## 20 · Le CNPN d'une inscription, et l'entrée en vigueur par niveau (12 min) — session 24

*Admin → Académique → « CNPN (programme) »*, panneau **« Entrée en vigueur par niveau »**, au-dessus
de « Étudiants rattachés à ce CNPN ».

> **Status: 20a, 20b, 20c and 20d executed 2026-08-18** against the live base, and the base was
> **restored afterwards** — 0 effectivity rules, 43 605 registrations all `Backfilled`, 0 divergence
> from the student stamps, student totals unchanged (6 460 / 1 980 / 1 745). **20e was deliberately
> not executed**: applying the rule would have re-stamped 936 real registrations and moved 936
> confirmed student stamps, which is a faculty decision, not a test.
>
> ⚠ **20f is a defect found by this pass and left open** — see the bottom of this section.

> The migration `RegistrationCnpnAndLevelEffectivity` was **already applied** when this ran
> (`__EFMigrationsHistory` confirms it), so no restart was needed.

### 20a — the backfill is exactly what it claims

1. The column exists on every row and agrees with the student's own stamp, everywhere:

```sql
SELECT COALESCE("CnpnSource",'(null)'), count(*) FROM public."Registrations" GROUP BY 1;
-- Backfilled | 43605          ← measured 2026-08-18

SELECT count(*) FROM public."Registrations" r JOIN public."Users" u ON u."Id"=r."StudentId"
WHERE r."CnpnVersionId" IS DISTINCT FROM u."CnpnVersionId";
-- 0                            ← the backfill changed no behaviour, it froze what was computed
```

2. ⚠ The source reads `Backfilled`, **not** `StudentStamp`, and the distinction is the point: nobody
   was asked at the time. There is no `(null)` bucket on this base — every enrolled student now
   carries a stamp, so the ~2 200 unstamped students noted in earlier sessions have since been
   resolved. The null path stays supported anyway; it is not dead code, it is the path a student with
   no recorded text still takes.

### 20b — the real transition is visible in the data

3. This is the situation the whole feature exists for, in 2025-2026 Médecine:

| promotion | inscrits | 2174.18 (7 ans) | 1650.25 (6 ans) |
|---|---|---|---|
| 1ère année | 1 061 | 1 | **1 060** |
| 2ème année | 940 | **19** | 920 |
| 3ème année | 936 | **936** | 0 |
| 4ème année | 852 | 852 | 0 |

   The 19 in the 2nd year are the repeaters who entered before 2024-2025 sitting beside 920 who did
   not — two texts in one (level, year), which is exactly what no year-keyed model can express. The
   3rd year is wholly on the old text, so « la 3ᵉ année de 2026-2027 » is a live decision, not a
   hypothetical.

### 20c — the picker refuses what the server would refuse

4. Open the panel with **Texte comparé = CNPN 2025 (6 ans)** and open **Niveau**. It must offer
   **exactly** Première…Sixième Année Médecine — and nothing else.
   - **Measured 2026-08-18**: six options. No Pharmacie level (another programme), no *Septième Année*
     (beyond a six-year text's span), no *Retrait* (not a promotion), and no level this text already
     takes effect for. All four are server guards, mirrored client-side so no click can only fail.
5. **Ajouter stays disabled** until both a level and a year are chosen, with the reason on the tooltip.
   Confirmed: clicking it with no level selected sends no request at all.

### 20d — authoring a rule, and what « 0 inscriptions régies » means

6. Add **Troisième Année Médecine · à partir de 2025-2026**. The row appears reading
   **« 0 inscriptions régies »**.
   - ⚠ **Zero is correct and is the whole design.** The rule is read as each registration is *created*;
     it does not reach back. A number here only appears once registrations have been stamped under it.
     Anyone reading it as "the rule did nothing" has misread it, which is why the column is labelled
     *régies* and not *concernées*.
7. Press **Rattraper** — the catch-up path, needed only when a rule is authored *after* the
   réinscription has already run. The preview must reconcile against SQL.
   - **Measured 2026-08-18 on the real base: 936 inscription(s) concernées · 936 à re-rattacher ·
     936 étudiant(s) · 0 année close.** Exactly the 936 rows of the table above. The sample is capped
     at 50 rows; the counts stay exact.
   - **Then verify the preview wrote nothing.** It is a dry run over 936 tracked entities, and the
     assertion that matters is not the numbers but that the store is untouched:

```sql
SELECT "CnpnVersionId","CnpnSource",count(*) FROM public."Registrations"
WHERE "LevelId"=3 AND "AcademicYearId"=21 GROUP BY 1,2;
-- 1 | Backfilled | 936        ← unchanged after the preview
```

8. Delete the rule. The toast reads « Règle supprimée » with **no** count clause, because it governed
   nothing. Had it governed rows, the sentence would name them — removing a rule is prospective and
   never un-stamps anybody.

### 20e — applying it (NOT executed, and read this before you do)

9. **Re-rattacher 936 inscription(s)** would re-stamp 936 registrations *and* move 936 confirmed
   student stamps onto the six-year text — which changes how many years those students owe. That is
   the faculty's decision. Take a `pg_dump -Fc` first, and note there is **no undo command**: the way
   back is another rule, or SQL.
10. The guard to exercise deliberately: run the preview, then create a registration in the same
    (level, year) from another tab, then apply → refused with `CnpnEffectivity.MoveCountNotConfirmed`
    naming both numbers. Same shape as the déliberation's `DefaultsNotConfirmed`, same reason.
11. The other guard, which cannot be forced at all: a registration whose year has been pronounced
    counts as **année close** and is skipped. There is no override checkbox — re-open the year.

### 20f — ✅ RESOLVED: a re-entrant dispatch in `loadingMiddleware` (app-wide, pre-existing)

12. **Symptom.** The panel rendered « 0 règle(s) » with a permanent « Actualisation… » on a fresh
    load, while the store held the very same query as `fulfilled`, with 3 rows and 1 subscriber
    attached. Navigating away and back fixed it; reloading did not.

13. **Root cause — not this panel, and not the CNPN feature at all.** `src/app/loadingMiddleware.ts`
    dispatched **before** forwarding the action:

```ts
if (action.type.endsWith("/fulfilled")) api.dispatch(fulfilled());   // ⚠ before next(action)
return next(action);
```

   `api.dispatch` runs the whole reducer chain and notifies every subscriber *while the action in
   flight has not yet been reduced*. On `api/executeQuery/fulfilled` that means subscribers re-render
   reading the query as still `pending`, `data: undefined` — and `useSyncExternalStore` caches that
   stale snapshot.

14. ⚠ **Why it hid for so long, and why it looked like a CNPN bug.** It self-corrects almost
    everywhere: any *later* dispatch notifies again and every component catches up. Only the query
    that settles **last on a page** has nothing after it — so it stays pending forever. On the CNPN
    page that was the effectivity table; the versions table above it, driven by the same slice and
    invalidated by the same mutation, always refreshed. Every "fix" tried on the panel (memoizing the
    argument, dropping it, explicit `refetch()`) changed nothing, correctly — the panel was never the
    problem.

15. **Fix:** forward first, then dispatch. One reorder, no behaviour change to the loader.
    **Verified live 2026-08-18 after a clean restart and re-login**: the table renders 3 rules on a
    fresh load, and a create (3 → 4) and a delete (4 → 3) both refresh immediately with no reload.

16. ⚠ **Re-test other screens for the same class of staleness.** This was app-wide for the whole life
    of the middleware, so any screen whose *last* query settled without a following dispatch has been
    showing stale data — silently, and nobody would have reported it as a bug.

### 20g — the corrective rules, applied for real (executed 2026-08-18)

13. `pg_dump -Fc` taken first and **verified restorable** (19 MB, 34 tables). ⚠ Piping `pg_dump` to a
    file through Git Bash on Windows **corrupts the dump** — "did not find magic string in file
    header". Write it inside the container and `docker cp` it out, with `MSYS_NO_PATHCONV=1` so the
    container path is not rewritten.
14. Three rules authored through the panel, for 1650.25 — the new text rolling one level forward per
    year, which is how it actually reaches a promotion already in the building:

| rule | in scope | déjà à jour | re-rattachées |
|---|---|---|---|
| 1ère année from 2024-2025 | 1 061 | 1 060 | **1** |
| 2ème année from 2025-2026 | 940 | 920 | **20** |
| 3ème année from 2026-2027 | 0 | — | 0 — fires at the réinscription |

15. Result, verified in SQL: 1ère and 2ème années are now **wholly on 1650.25**, the 21 moved rows
    carry `CnpnSource = 'Effectivity'`, and the 1 981 rows that were already correct were **not
    touched** (they still read `Backfilled`). Student stamps moved 2174.18 6 460 → 6 440 and
    1650.25 1 980 → 2 001, which balances exactly.
16. ⚠ **One of the 20 was a data fix, not a repeater.** It was a Médecine registration stamped with
    **`PHARM-LEGACY`, a Pharmacie text** — one of **57 such rows** in the base, a pre-existing defect
    from the original CNPN backfill (not from the per-registration migration, which copied the student
    stamp faithfully). Applying the rule corrected the one row that fell in scope; **56 remain** and
    need a decision. `CreateCnpnEffectivityCommand` refuses this pairing going forward.

```sql
-- the 56 that are left
SELECT v."Code" AS text, v."AcademicProgram" AS text_program, l."AcademicProgram" AS level_program, count(*)
FROM public."Registrations" r
JOIN public."Levels" l ON l."Id"=r."LevelId"
JOIN public."CnpnVersions" v ON v."Id"=r."CnpnVersionId"
WHERE v."AcademicProgram" <> l."AcademicProgram" GROUP BY 1,2,3;
```

17. The 3ème année rule is the one that matters next: at the September rollover every student
    re-registering in the 3rd year — repeaters included — is stamped 1650.25 automatically, while the
    ones who pass into the 4th keep 2174.18. That is the whole cut, and nobody has to remember it.

## 21 · La dernière année ne commence pas sur un stage non validé (10 min) — session 24

> **Status: the rule itself was run against the real base 2026-08-26** — steps 1, 4 and 8, plus the
> bulk route below (§24). The déliberation/réinscription legs (2, 3), the unstamped student (5), the
> dérogation (6, 7) and the revalidation (9) are **still owed**. Migration `FinalYearEntryWaiver`
> creates one table and changes no data.

*Admin → Académique → « Clôture & réinscription »*, plus the student dossier.

1. Find a student in the year **below** his last (7ᵉ under 2174.18, 6ᵉ under 1650.25) who carries an
   unvalidated stage. `GET students/{id}/outstanding-stages` is the same list the gate reads.

```sql
-- candidates: students whose every attempt at some stage came back NonValidé
SELECT r."StudentId", s."Name" AS stage, l."Label" AS owed_in
FROM public."InternshipAssignments" a
JOIN public."Registrations" r ON r."Id" = a."RegistrationId"
JOIN public."Cohorts" c ON c."Id" = a."CurrentCohortId"
JOIN public."Stages" s ON s."Id" = c."StageId"
JOIN public."Levels" l ON l."Id" = r."LevelId"
GROUP BY r."StudentId", c."StageId", s."Name", l."Label"
HAVING bool_and(a."Result" = 'NonValidé')
LIMIT 20;
```

2. Close his year « Admis », then run the réinscription preview for his promotion. He must appear as
   **« Bloqué — dernière année »**, the report must count him under `finalYearBlocked`, and **no
   registration must be created for him** — that is the assertion, not the badge.
3. The control, and do it in the same run: a classmate with no outstanding stage must roll over
   normally. A gate that refuses everybody proves nothing.
4. ⚠ **Check a student one year lower.** The same debt must **not** block him — carrying an
   unvalidated stage forward is legal everywhere except into the final year.
5. ⚠ **Check a student with no CNPN stamp.** He must roll over untouched. This is the case the guard
   got wrong in development: a `Dictionary<Guid,int>` default of 0 made every year his last, so the
   rule fired hardest exactly where it must stand aside.
6. Grant the dérogation (`POST final-year-waivers` with a reason), re-run the preview: he now rolls
   over, and the report counts him under **`finalYearWaived`** rather than silently. Verify the stored
   waiver kept `OutstandingAtGrant` and `OutstandingSummary` — what it excused, as it read that day.
7. Try to grant a second waiver for the same year → refused. Try to grant one to a student who owes
   nothing → refused (`FinalYearWaiver.NotNeeded`). Revoke before the rollover → allowed; revoke
   *after* → refused (`FinalYearWaiver.AlreadyUsed`).
8. ⚠ **The manual path.** Create the same student's next-year registration by hand from the dossier —
   it must be refused identically (`Registrations.FinalYearBlocked`). A rule the rollover enforces and
   the form does not is a rule anyone steps around with the other button.
9. **Revalidation is the other way out**: open the failed stage (`POST registrations/{id}/revalidate`),
   let it come back validated, and the student rolls over with no waiver at all.
---

## 22 · Une colonne se répartit sur tous les services (12 min) — session 25

> **Status: built and unit-tested (14 tests), and the Psychiatrie half was verified live on
> 2026-08-18 — the other stages of the base have not been re-run.** No migration. ⚠ **This step
> carries a data repair**: every cell arranged *before* this session was written by the broken
> indexing, and a wrong plan looks exactly like a right one in the grid.

### 22a — find what the old arranger left behind

Nothing is published anywhere (0 grid-linked `ServicePeriod`s), so re-arranging costs nothing but a
click. The signature of the defect is a column whose cells all sit in one service:

```sql
-- a période in which every cohort landed in the same service
SELECT s."Name" AS stage, l."Label" AS promo, sl."PeriodNumber",
       COUNT(*) AS cells, COUNT(DISTINCT a."ServiceId") AS services
FROM public."CohortSlotAssignments" a
JOIN public."StageSlots" sl ON sl."Id" = a."StageSlotId"
JOIN public."Stages" s      ON s."Id"  = sl."StageId"
JOIN public."Levels" l      ON l."Id"  = s."LevelId"
WHERE sl."AcademicYearId" = <année en cours>
GROUP BY s."Name", l."Label", sl."PeriodNumber"
HAVING COUNT(DISTINCT a."ServiceId") = 1 AND COUNT(*) > 1
ORDER BY cells DESC;
```

⚠ **One row is not proof.** A stage with a single admitted service is legitimately one service per
column — check `Stage.AllowedServices` and the quotas before calling it a defect. What convicts is
the *year-wide* shape: a stage whose every column names the same service while other services of the
same stage carry nothing.

```sql
-- the year's load per (stage, service): the untouched services are the tell
SELECT s."Name" AS stage, sv."Name" AS service, COUNT(*) AS cells
FROM public."CohortSlotAssignments" a
JOIN public."StageSlots" sl ON sl."Id" = a."StageSlotId"
JOIN public."Stages" s      ON s."Id"  = sl."StageId"
JOIN public."Services" sv   ON sv."Id" = a."ServiceId"
WHERE sl."AcademicYearId" = <année en cours>
GROUP BY s."Name", sv."Name"
ORDER BY stage, cells DESC;
```

Re-arrange each stage the first query names, **scoped** — one concurrency block at a time, or via the
macro plan. Then re-run both queries: every column must show more than one service, and the per-stage
totals must be flat (5MED Psychiatrie went from 9 columns in 1 service to 12/12/13/11/12 over five).

**Measured on the live base 2026-08-24, and it is smaller than feared.** The first query names only
5MED Santé Publique, which has exactly **one** allowed service — the legitimate case, not the defect.
The catastrophic form is gone: Psychiatrie was the only stage that had it and it has been re-run.

⚠ **What is left is the milder half, and the first query cannot see it.** Every column is spread
correctly — 6-7 cohorts over 5 services is 2,2,1,1,1, which cannot be improved on — but *which*
services carry the leftover was frozen, so the imbalance accumulates over the year:

| stage | services | cells | per service over the year |
|---|---|---|---|
| Urologie Néphrologie | 5 | 60 | 18 · 15 · 9 · 9 · 9 |
| Ophtalmologie | 3 | 60 | 24 · 18 · 18 |
| ORL | 2 | 60 | 33 · 27 |
| Neurologie | 8 (7 used) | 60 | one service **never used all year** |
| Psychiatrie *(re-run)* | 5 | 60 | 13 · 12 · 12 · 12 · 11 |

Urologie A took the extra cohort in **all nine columns**, Urologie B in six of nine, and the other
three in none — the stable tie-break exactly as predicted, and 2× the load on one service. Use this
query to see it rather than the year totals, which a service named the same as another will merge:

```sql
SELECT a."ServiceId", sv."Name", COUNT(*) AS cells,
       COUNT(DISTINCT CASE WHEN cnt = 2 THEN sl."PeriodNumber" END) AS cols_taking_two
FROM public."CohortSlotAssignments" a
JOIN public."StageSlots" sl ON sl."Id" = a."StageSlotId"
JOIN public."Services" sv   ON sv."Id" = a."ServiceId"
JOIN LATERAL (SELECT COUNT(*) AS cnt FROM public."CohortSlotAssignments" b
              WHERE b."StageSlotId" = a."StageSlotId" AND b."ServiceId" = a."ServiceId") c ON true
WHERE sl."StageId" = <stage> AND sl."AcademicYearId" = <année>
GROUP BY a."ServiceId", sv."Name" ORDER BY cells DESC;
```

`cols_taking_two` equal to the column count is the frozen tie-break. After a re-arrange it must be
spread across the services, and no service may sit at 0 cells while another carries two per column.

**✅ Executed 2026-08-24.** All four re-arranged, unscoped, from the stage's own *Grille de planning*
— which is the safe path here because the other stages already hold every group in 8 of the 9 columns,
so the guard in §22c cannot fire and the arrange has freedom over services only.

| stage | before | after |
|---|---|---|
| Urologie Néphrologie | 18·15·9·9·9 | 13·12·12·12·11 |
| Ophtalmologie | 24·18·18 | 21·20·19 |
| ORL | 33·27 | 30·30 |
| Neurologie | 7 of 8 services, one idle all year | 8 of 8 — 8·8·8·8·7·7·7·7 |

Gynécologie was already flat (39·36·36·36·33 over 180 cells) and was left alone. The two checks that
say the repair moved only what it should:

```sql
-- must be 0: a roster in two services in one column
SELECT COUNT(*) FROM (
  SELECT c."AcademicGroupId", sl."PeriodNumber"
  FROM public."CohortSlotAssignments" a
  JOIN public."StageSlots" sl ON sl."Id" = a."StageSlotId"
  JOIN public."Cohorts" c ON c."Id" = a."CohortId"
  JOIN public."Stages" s ON s."Id" = sl."StageId"
  WHERE sl."AcademicYearId" = <année> AND s."LevelId" = <promotion>
  GROUP BY c."AcademicGroupId", sl."PeriodNumber" HAVING COUNT(*) > 1) x;
```

and the per-stage cell totals, which must be **unchanged** — 60 per stage here. A re-arrange that
moves a total has not rebalanced the year, it has lost or invented a placement.

### 22b — the balance itself

*Admin → Stages → un stage réparti → Répartition*.

1. Pick any période of an arranged stage. Its cells must name **several** services, and the counts
   across a column must differ by at most one. Seven cohorts over five services is 2,2,1,1,1 — that
   multiset cannot be improved on, only rotated.
2. ⚠ **Check which services carry the leftover, column by column.** With all 148 services on the same
   imported capacity the tie-break used to be stable, so the same two services took the pair in every
   column of the year. The remainder must now move between columns.
3. The control: a stage with **one** admitted service still puts everybody there, and reports nothing.

### 22c — the stage that would swallow the year

*Admin → Stages → un stage de la **6ᵉ année** → Répartition → « Auto-répartir »*, **sans** cocher de
période et **sans** choisir de partition.

1. It must be **refused**, naming the stage and its ten périodes, and telling you to establish the
   crossover first. Med6 is the live case: six stages, ten columns, zero cells — the promotion where
   the first button pressed would have decided the year.
2. ⚠ **Nothing must have been written.** Re-open the grid: still empty. The guard runs before the
   stale-cell removal precisely so a refusal cannot leave the grid emptier than it found it.
3. **Three controls, and they are the point** — the guard has to bite narrowly or it is worse than
   the bug:
   - name a partition (« A ») with no période → **allowed**. « A → Médecine P1-P2 » is the faculty's
     own layout, not an accident.
   - name the périodes with no partition → **allowed**, same reason.
   - on a promotion where one stage *is* the whole axis (nothing else declares those windows) →
     **allowed**. A stage nobody competes with starves nobody.
4. Then do it properly: apply the bloc de rotation for the 6ᵉ année (`k = [2,2,2,2,1,1]`, `T = 10`,
   `P = 10`), hand the matrix to the plan macro, and the same button now works because the crossover
   exists.
5. ⚠ **First it needs services.** Measured 2026-08-24: the 6ᵉ année is ready in every other respect —
   ten partitions of ten rosters each, ten slots per stage — but **all six of its stages carry zero
   `StageAllowedServices`**, so nothing can be arranged into them at all. The imported history names
   which services each one actually used, over ~3,550 périodes per stage:

   | stage | services used historically |
   |---|---|
   | GYNECOLOGIE OBSTETRIQUE | 6 |
   | PEDIATRIE | 10 |
   | ANESTHESIE REANIMATION | 11 |
   | URGENCES OU TRAUMATOLOGIE | 11 |
   | MEDECINE | 24 |
   | CHIRURGIE | 28 |

   ```sql
   SELECT s."Name" AS stage, sv."Id", sv."Name", COUNT(*) AS periods
   FROM public."ServicePeriods" sp
   JOIN public."InternshipAssignments" ia ON ia."Id" = sp."InternshipAssignmentId"
   JOIN public."Cohorts" c  ON c."Id"  = ia."CurrentCohortId"
   JOIN public."Stages" s   ON s."Id"  = c."StageId"
   JOIN public."Services" sv ON sv."Id" = sp."ServiceId"
   WHERE s."LevelId" = <6ᵉ année>
   GROUP BY s."Name", sv."Id", sv."Name" ORDER BY s."Name", periods DESC;
   ```

   ⚠ **History is evidence, not authority.** A service the 6ᵉ année used in 2019 may have closed, and
   a long tail of one or two périodes is as likely to be a délocalisation as a standing arrangement.

   ⚠ **…and volume is the wrong filter, which is not obvious.** Ranking by total périodes drops
   exactly the partners that matter: Hôpital Moulay Youssef went 33 → 1 453 → 1 864 périodes over the
   last three years and Lalla Aicha 11 → 648 → 862, so six years of history buries them under
   hospitals that have been there all along. **Recency is the signal.** Hôpital Azzamouri is the
   mirror case — it appears in all six stages, but only ever in 2024-2025, and not at all this year.

   **✅ Authored 2026-08-24: 51 rows**, on the rule *used in the last two academic years, at least ten
   times*. Both halves are needed — recency alone keeps Endocrinologie's single période under
   MEDECINE, and the three Traumatologie services that show 6, 6 and 4 under CHIRURGIE while carrying
   447, 401 and 392 under URGENCES, which is where they belong.

   | stage | services authored |
   |---|---|
   | GYNECOLOGIE OBSTETRIQUE | 5 |
   | ANESTHESIE REANIMATION | 7 |
   | PEDIATRIE | 7 |
   | URGENCES OU TRAUMATOLOGIE | 6 |
   | CHIRURGIE | 13 |
   | MEDECINE | 13 |

   ⚠ **Three judgement calls to review on the Stage page**, all excluded by the rule and all arguable:
   *Pédiatrie CCP* (230 périodes historically, 2 recently — wound down, or mis-sampled?), *Urgences
   (Moulay Youssef)* at 5 recent, on a site that is growing fast, and everything at *Azzamouri*.
   Undo is one statement: `DELETE FROM "StageAllowedServices" WHERE "StageId" IN (15,16,17,18,19,20);`

### 22d — the configuration comes back

*Admin → Planification → Bloc de rotation*.

1. Choose **5ᵉ année médecine**, then reload the page and choose it again. The seven stages must come
   back **in the order they were authored** — Gynécologie first at 3 périodes — with the nine windows
   filled and a banner naming the apply date. The order is not decoration: it is the itinerary
   partition A actually walks.
2. ⚠ **The durations must be flagged when they were not read from an apply.** A block arranged but
   never applied through this screen shows « déduites de la grille » (`Derived`); one with neither
   shows « à ressaisir » (`Unknown`). « 1 période » deduced from an empty grid is not « 1 période »
   entered by somebody.
3. Nudge one stage's P1 by three days on its own grid, come back: that stage must have **left** the
   block and appear on its own. A screen that reports it as still aligned is hiding the drift.
4. The control: a promotion with no axis at all opens an empty form, not an error.
---

## 23 · Poser, corriger et supprimer une année universitaire (8 min) — session 26

> **Status: ✅ executed against the real base 2026-08-25**, except **23d** (needs a second login).
> 24 unit tests behind it (14 handler, 10 endpoint). No migration. ⚠ **This is the step the in-memory
> suite cannot stand in for**: every delete guard here exists to keep the user away from a foreign
> key, and `UseInMemoryDatabase` has none.
>
> **It found one defect the tests could not see** — every refusal printed **two** toasts, « Conflit »
> and « Erreur », with identical text. `errorMiddleware` already surfaces every rejected mutation in
> the server's own words, so the page's own `notify.error` was a second copy of the same sentence.
> Fixed by removing the page-level error toasts; the success ones stay, because they carry what the
> middleware cannot know (the year that stood down, the périodes left outside the span, the rosters
> the cascade took). ⚠ **The same double-toast is pre-existing in `CnpnEffectivityPanel`,
> `CnpnTargetingPanel`, `CnpnVersionsPanel`, `ScheduleGridModal` and `GroupsPage`** — same shape, not
> touched here.
>
> Baseline before, and restored after: **22 years, exactly 1 current, 0 overlapping pairs.**

*Admin → Académique → Années universitaires.*

### 23a — designating « l'année en cours »

1. With 2025-2026 current, designate **2026-2027**. The reply must name what stood down
   (`previousLabel: "2025-2026"`), and the navbar must follow.
2. ⚠ **Check the singleton on the database, not on the screen:**

```sql
SELECT COUNT(*) FROM public."AcademicYears" WHERE "IsCurrent";   -- must be exactly 1, always
```

   Two rows flagged at once means two screens quietly disagreeing about which promotion they show,
   with nothing on either to say so. `IX_AcademicYear_IsCurrent` should make it impossible — this step
   is what proves the index is really there and that the demote precedes the promote.
3. Designate the year that already holds it → refused (`AcademicYears.AlreadyCurrent`), count still 1.
4. Put 2025-2026 back before continuing. **Everything below assumes it is current again.**

**✅ Executed.** The badge followed both moves, `COUNT(*) FILTER (WHERE "IsCurrent")` read **1** after
each, and the reply named the year that stood down. Two `ACADEMIC_YEAR_SET_CURRENT` audit rows.
⚠ **Step 3 was not exercised end-to-end**: the UI *disables* the control on the year that already
holds it (tooltip « Déjà l'année en cours ») rather than letting the call fail — the same choice
`DeleteCnpnVersionCommand` makes. The refusal itself is covered by
`AcademicYearManagementTests.Designating_the_year_that_already_holds_it_is_refused`.

### 23b — deleting

1. Try to delete **2025-2026** (the current one) → refused, `AcademicYears.CannotDeleteCurrent`.
   Deleting the year every unscoped handler resolves through leaves the app with no answer to
   « quelle année ? ».
2. Try to delete a year that holds data — any of the imported ones. It must be refused with
   `AcademicYears.StillInUse` **naming every count at once**, not just the first: « 6 057
   inscription(s), 63 période(s) de stage, … ». One reason at a time sends the user round the loop.
3. ⚠ **The assertion is the year, not the message.** Re-read it afterwards:

```sql
SELECT "Id","Label","IsCurrent" FROM public."AcademicYears" ORDER BY "Id";
```

   A guard ordered after the delete returns the same refusal and passes every handler test. Here it
   would have taken the year's **rosters** with it — `AcademicGroups.AcademicYearId` is `CASCADE`,
   which is the whole reason this step is written.
4. The control: create a throwaway year (« 2099-2100 », 01/09/2099 → 31/08/2100), delete it, and it
   goes. A route that refuses everything satisfies every assertion above and proves nothing.
5. Create a throwaway year, auto-arrange a couple of empty rosters into it, delete it: it succeeds and
   reports `rostersRemoved` — the number is the point, because it is the only thing destroyed and the
   only thing that cannot be read back.

**✅ Executed, steps 2-4.** Deleting **2024-2025** was refused with all four counts in one sentence —
« 4971 inscription(s), 1682 cohorte(s), 1 règle(s) d'entrée en vigueur CNPN, 1 CNPN dont c'est l'année
d'entrée » — and the year, its 4 971 registrations **and its 395 rosters** were all still there
afterwards, which is the assertion: `AcademicGroups` cascades, so a guard ordered after the delete
would have taken them silently. The control passed: a throwaway year created and deleted cleanly,
back to 22.

⚠ **Step 1 was not exercised end-to-end** — the delete control is disabled on the current year, same
as above; covered by `The_current_year_is_never_deleted` and `The_current_year_survives_a_delete`.

⚠ **« 2099-2100 » cannot be entered.** A pre-existing client guard in the create form caps the start
year at *this year + 1* (« L'année de début ne peut pas dépasser 2027 ») and disables « Créer ». Use a
year inside the cap — **2027-2028** was used here. The cap is client-side only; the server has no such
rule, so it is a UX limit, not an invariant.

### 23c — the calendar rule that was never enforced

1. Edit 2026-2027 to start **01/06/2026** → refused, `AcademicYears.OverlapsAnotherYear`, naming
   2025-2026. ⚠ Not tidiness: `ServiceOccupancyCalculator` bounds a year by its **dates**, not by its
   id, so a day belonging to two years counts every slot in the overlap twice against a service — the
   number the publish guard refuses on.
2. Confirm the base satisfied it all along, and keep the query — it is the regression test:

```sql
SELECT a."Label", b."Label" AS overlaps_with
FROM public."AcademicYears" a
JOIN public."AcademicYears" b
  ON a."Id" < b."Id" AND a."StartDate" <= b."EndDate" AND b."StartDate" <= a."EndDate";
-- verified empty 2026-08-24, on 22 years
```

3. Re-save a year completely unchanged → **allowed**. A year must not collide with itself; this is the
   control for the two refusals above and it is the one that breaks first when the guard is rewritten.
4. Rename a year to another year's label → refused, `AcademicYears.DuplicateLabel`.
5. **Narrow** a year that carries `StageSlot`s so some fall outside the new span → **allowed**, and the
   reply must report `slotsOutsideSpan`. Refusing would block the ordinary case (a year corrected while
   its axis is still a draft); saying nothing would hide périodes that no longer sit in their own year.

**✅ Executed, steps 1-4.** Moving 2027-2028's start to 01/06/2027 was refused naming both years
(« chevaucherait « 2026-2027 » »), and the table still read 1 sept. 2027 — nothing written. The
overlap query is empty on all 22 years. Re-saving the same year unchanged went through, so a year
does not collide with itself. Renaming it to « 2025-2026 » was refused as a duplicate.
⚠ **Step 5 not executed** — it needs a year carrying `StageSlot`s that is neither current nor holding
registrations, which the base has none of. Covered by
`Narrowing_a_year_reports_the_periodes_it_leaves_outside` and its endpoint twin.

**The edit form restores correctly**: label and both dates come back, and the « année actuelle »
checkbox is replaced by a line saying the current year is changed from the list — designating is a
distinct act, with its own guard.

### 23d — who may

Every route above, as a **professeur** → 403 `AcademicYears.NotAllowed`, and nothing changed. With no
session at all → 401. The year is the one setting that moves every screen at once.

⚠ **Not executed** — it needs a second Keycloak account, and the admin session was the one under test.
`AcademicYearEndpointTests` covers both through the real pipeline (`Only_the_administrative_side_may_move_the_current_year`,
`An_anonymous_caller_never_reaches_the_handler`), with the role emitted as Keycloak's `realm_access`
so `KeycloakRoleTransformer` is exercised rather than bypassed. Worth doing by hand once a second
account exists.


---

---

## 24 · La porte de dernière année, demandée une fois pour tout un lot (6 min) — session 27

> Run against the live base 2026-08-26. **Everything it writes is deleted at step 4** — the base ended
> the pass exactly as it started it: 0 inscriptions en 2026-2027, 0 dérogations.

The gate used to be asked per student *inside* `CreateManyRegistrationsCommandHandler`'s loop, and each
ask reads that student's whole cursus. This checks that batching the question did not batch the answer.

1. **Find the population.** 686 étudiants de 6ᵉ année Médecine de 2025-2026 sans inscription en
   2026-2027; **60 doivent encore un stage**, 626 non, tous sur 2174.18 — donc la 7ᵉ est leur dernière.
   The query is §21's, with `bool_and(coalesce(a."Result",'') = 'NonValidé')` — `bool_and` skips NULLs,
   so without the `coalesce` a stage nobody marked counts as failed and the candidate list is wrong.
2. **The batch that writes nothing.** `POST /registrations/bulk` with the 60 owing ids,
   `academicYearId` 2026-2027, `levelId` 7ᵉ année → **200, 60 refusals, 0 created**, one call, 470 ms.
   Each refusal names the stages: « La 7ᵉ année est la dernière de ce cursus… Faites-les revalider, ou
   accordez une dérogation nominative. » ⚠ Check `SELECT count(*) … WHERE "AcademicYearId" = <target>`
   is still 0 — a refusal reported *after* the write looks identical from the response.
3. **The control, in the same call.** Two ids — one owing, one clear — into the 7ᵉ année: **1 refused,
   1 created**. A batch that refuses everybody proves nothing.
4. **The same debt, one year lower.** The refused student, alone, into the **6ᵉ** année: **created**.
   Carrying an unvalidated stage forward is legal everywhere except into the last year — and it is the
   narrowing in the wall clock too: 722 ms for the final-year call, **56 ms** for this one, because
   nobody in it is in his final year so neither the cursus nor the waivers are read at all.
5. **Clean up**: delete the two registrations by id (they carry no cohorte, no période, no groupe —
   check before deleting), and confirm the count is back to 0.
6. **The manual path, from the dossier.** *Étudiants → dossier → « Ajouter une inscription »* →
   2026-2027 + 7ᵉ année → « Créer ». Refused with the same sentence, naming 8 stages here. Nothing
   created. ⚠ Two toasts appear — the server's sentence and a page-level « Impossible de créer
   l'inscription ». That is the pre-existing double-toast, and this page is one more offender.

---

## 25 · Le bloc de rotation d'une promotion : le voir, le modifier, le supprimer (8 min) — session 27

*Admin → Formation → Bloc de rotation.* ⚠ **La barre de navigation choisit l'année** que cette page
écrit — vérifiez-la avant tout : 2026-2027 est l'année en cours et ne contient encore ni groupes ni
cohortes, alors que les axes existants sont sur 2025-2026.

1. **Le voir.** Choisir *Sixième Année Médecine* sur 2025-2026 : le bloc en vigueur est restauré depuis
   l'axe sur disque — « 6 stage(s) sur 10 colonne(s), appliqué le 13/08/2026 », les kₛ (2+2+2+2+1+1) et
   les dix fenêtres pré-remplies. La source des kₛ est dite : *authored* (l'apply), *derived* (les
   cellules) ou *unknown*.
2. **Le simuler.** « Simuler » : `T = 10`, multiples de 10 acceptés, 2 partitions simultanées dans les
   quatre stages longs. ⚠ Le tableau « Durée réelle par stage » doit donner **44/44/44/44/22/22 jours
   ouvrables** contre les mêmes chiffres annoncés — un axe posé en jours ouvrables tombe juste, alors
   que l'étendue calendaire varie de 60 à 67 jours.
3. **Le modifier** : corriger une date ou un kₛ, re-simuler, « Appliquer l'axe ». Le toast dit
   « N créneaux écrits, N remplacés » — et, s'il y avait des cellules réparties, combien sont à refaire.
4. **Le supprimer** ⚠ *pas encore fait à la main — voir la note en fin de section* : « Supprimer le
   bloc », dans le bandeau de restauration. La confirmation nomme les
   stages et les colonnes ; le toast, les créneaux et les cellules supprimés. ⚠ Vérifier ensuite que la
   promotion n'a plus de créneaux **pour ces stages seulement** — un autre bloc du même niveau (deux
   semestres) doit rester debout.
5. ⚠ **Le refus.** Sur un bloc dont une cellule est publiée, le bouton est désactivé et dit pourquoi.
   Côté serveur, `CannotDeletePublished` — et il faut le vérifier là aussi : un bouton grisé n'est pas
   une garde.
6. **Le plan macro, et ce qu'il faut vérifier ensuite** (mesuré sur la 6ᵉ année, 2026-08-26 —
   1 000 cellules). Le toast ne suffit pas : relisez la base.
   - chaque roster passe par les 6 stages, `kₛ` colonnes chacun (2·2·2·2·1·1), min = max ;
   - chaque colonne de chaque stage porte exactement `Lₛ` partitions ;
   - **0 roster en double** sur une colonne ;
   - **tous** les services de chaque stage sont utilisés à chaque colonne, écart ≤ 1 roster — c'est la
     correction de la session 25 ; un service qui rafle toute une partition est le défaut d'alors.
   - ⚠ Le dépassement d'effectif reste : 88 des 510 couples (service × colonne), au pire 30 étudiants
     pour 20. Ce n'est pas l'arrangeur — les 148 services portent le même 20 importé et aucun quota
     n'est saisi.
7. ⚠ **L'étape 4 n'a jamais été cliquée par un humain.** Le serveur est couvert : quatre tests de
   pipeline (`RotationCycleEndpointTests`) passent la route de bout en bout — les `stageIds` répétés
   dans la query string, le refus sans stage, le 404 sur un bloc absent, le 401 anonyme — et six tests
   de handler couvrent les gardes. Ce qui reste à voir, c'est le bouton : ouvrir la confirmation,
   lire les nombres qu'elle annonce, et vérifier que l'autre bloc de la promotion tient debout.
   ⚠ **À faire dans l'onglet au premier plan** : la modale de Mantine se monte via `requestAnimationFrame`,
   qui est suspendu dans un onglet en arrière-plan — voir `NOTES.md` (2026-08-26).
8. ⚠ **La garde lit la table de couverture, pas la clé étrangère.** Sous `SingleService` une période
   couvre toute une série et `ServicePeriod.CohortSlotAssignmentId` ne nomme que la **première**
   cellule. Le test `A_published_run_protects_every_cell_it_covers_not_only_the_first` en fait foi ;
   sur base réelle, cela ne se voit pas encore (tous les stages de 6ᵉ sont `PerPeriod`, 0 période liée
   à la grille).

> ⚠ **Si une page reste vide ou un bouton tourne sans fin, regardez Visual Studio avant de suspecter
> les données.** Réglé sur « arrêter quand l'exception est levée », le débogueur fige le processus *à
> l'endroit du throw*, avant que `ExceptionHandlerMiddlewareImpl` ne le transforme en 500 : la requête
> HTTP ne se termine jamais, aucun toast n'apparaît, et l'API ne répond plus à rien — pas même à un
> `GET /api/levels` anonyme. Signature : **CPU à plat, connexions Postgres au repos, 0 réponse**.
> C'est ainsi que le plan macro de la 6ᵉ année a « planté » deux fois le 2026-08-26 ; la deuxième fois
> c'était une vraie exception de traduction SQL, trouvée dans la pile d'appels.

---

## 26 · La clôture 2025-2026 → 2026-2027, exécutée pour de bon (session 28)

**Executed 2026-08-29 against the real base**, whole faculty, both acts. Not a rehearsal: 8 077
inscriptions closed and 5 930 créées. The numbers below are what a correct run prints, so a later
run that disagrees has something to explain.

⚠ **The wrong-year state is what the user actually hit, and the page does say so.** 2026-2027 had been
created *and* designated « actuelle » before the déliberation, so the closure page read
« Les décisions du PV, pour **2026-2027** » and the rollover said « Aucune année postérieure n'existe ».
Downloading the canvas there fails with `PromotionHasNoStudents` — the year holds 0 registrations —
which is exactly why `GetDeliberationTemplateQuery` refuses instead of emitting an empty sheet. The fix
is the **top bar**, not the `IsCurrent` flag: the closure page scopes on the navbar selection
(`YearClosurePage.tsx:97`), and neither act reads `IsCurrent`. Order that works: close 2025-2026 →
create 2026-2027 → réinscription → *then* designate it.

### 26a — the déliberation (1 692-row exceptions file)

| | |
|---|---|
| lignes dans le fichier | 1 692 (619 redoublants, 46 exclus, 30 abandons, 997 diplômés) |
| admis par défaut | **5 369** |
| en dernière année, sans décision | **1 015** |
| déjà décidé / inchangé | 1 (« Retrait ») |
| avec un stage non validé | 2 (signalé, jamais bloquant) |

1 692 + 5 369 + 1 015 + 1 = **8 077** — the identity to check first, because every mis-scoped run
breaks it. `ConfirmedDefaultCount = 5369` is in the audit entry.

⚠ **« Diplômé » is refused on a level that is not the last year of the student's own text, and this
base has 6 registrations that can therefore never be graduated**: 5 in *Septième Année Médecine*
stamped `PHARM-LEGACY` (spans 6) and 1 in *Interne CHU Médecine* (year 8, text spans 7). A file naming
any of them refuses **the whole import** — all-or-nothing — so a generated PV must emit « Diplômé »
only where `level.Year == TotalYears`, never on `>=`. `MayBeAFinalYear` uses `>=` deliberately (they
must not be promoted either), so the two conditions genuinely differ.

### 26b — the réinscription

| | |
|---|---|
| à créer | **5 930** |
| ignorés | 2 147 (997 diplômés + 46 exclus + 31 abandons + 1 015 sans décision + 58 bloqués) |
| à traiter | 1 074 (1 015 sans décision + 1 « Retrait » + **58 dernière année bloquée**) |

**The final-year gate fired for real**: 664 Med6 admis, only **606** rolled into the 7ᵉ année — 58
refused over unvalidated earlier stages. That is the first time `FinalYearBlocked` has been observed
outside a test. Verified in SQL: 58 Med6 `Validated` rows with no 2026-2027 registration.

Post-conditions, all verified: 619 redoublants back on the **same** `LevelId`; **0** students whose
cursus ended rolled over; **5 930 / 5 930** carry a `CnpnVersionId` (the stamper ran on every one);
**0** carry an `AcademicGroupId` — they land in « Non réparti », and `AutoArrangeGroupsCommand` is the
next act. *Première Année Médecine* = 233, all redoublants: PGSH does not invent an intake.

### 26c — ⚠ the apply is a per-registration N+1 after the commit, and it is minutes long

The write itself is one `SaveChanges` and lands in seconds. What the user then waits on is
`ApplicationDbContext.PublishDomainEventsAsync`, which publishes **one event per registration
sequentially**, and `RegistrationYearOutcomeRecordedEventHandler` answers each with a `SELECT`, an
`INSERT` and its **own `SaveChangesAsync`** — on a context whose change tracker grows by one `History`
per event.

Measured: **7 061 timeline rows at ~50/s ≈ 2 min 20 s**, during which the button spins with the data
already committed. A crash in that window leaves the verdicts written and the timeline half-written,
with nothing to resume from. The réinscription has the same shape (5 930 `StudentRegisteredDomainEvent`).

⚠ **Do not "fix" this by making the handler fire-and-forget** — the timeline is the audit surface. The
shape that fits is a bulk path: one `AddRange` of `History` rows and a single `SaveChanges`, or an
`INotificationHandler` over a batched event. Not done here; recorded so the next person does not read
the spinner as a hang. Signature that tells it apart from a real hang: `Histories` count climbing.

---

## 27 · La liste d'un chef de service : quatre tranches, une année, et « À venir » (12 min) — sessions 29-30

**Not yet executed.** No migration. Two reports, one screen: the chef's « Mes Services » loaded every
period he had ever had, and the 4MED 2026-2027 rotations he had just published did not appear at all.

Measured on the live base 2026-08-29, chef of Pédiatrie1 + Pédiatrie2 (services 45 and 46):

```sql
SELECT count(*) FILTER (WHERE NOT sp."IsStarted")                                         AS a_venir,
       count(*) FILTER (WHERE sp."IsStarted" AND NOT sp."IsComplete")                     AS en_cours,
       count(*) FILTER (WHERE sp."IsStarted" AND sp."IsComplete" AND ev."Id" IS NULL)     AS a_evaluer,
       count(*) FILTER (WHERE sp."IsStarted" AND sp."IsComplete" AND ev."Id" IS NOT NULL) AS evalues,
       count(*) AS total
FROM "ServicePeriods" sp
LEFT JOIN "ServiceEvaluation" ev ON ev."ServicePeriodId" = sp."Id"
WHERE sp."ServiceId" IN (45,46);
-- 300 | 0 | 683 | 2237 | 3220        ← 3 220 rows, back to 2019, all fetched and mounted at once
```

The 300 are the 4MED Pédiatrie publication (2 × 150, three windows, `IsStarted = false` on every one)
and were invisible because the worklist only ever returned started rows.

**a. « À venir » shows the publication.** Log in as the chef → *Mes Services*. The card must open on a
slice that has something in it, and the segmented control must read four counts.

⚠ **The counts are the current year's**, so they are *not* the 300 / 0 / 683 / 2 237 above — that
split is the whole history. With 2026-2027 selected expect the 300 under **À venir** and most of the
2 237 gone, replaced by the notice in step **d**. The year-scoped split is:

```sql
SELECT y."Label",
       count(*) FILTER (WHERE NOT sp."IsStarted")                                     AS a_venir,
       count(*) FILTER (WHERE sp."IsStarted" AND NOT sp."IsComplete")                 AS en_cours,
       count(*) FILTER (WHERE sp."IsStarted" AND sp."IsComplete" AND ev."Id" IS NULL) AS a_evaluer,
       count(*) AS total
FROM "ServicePeriods" sp
LEFT JOIN "ServiceEvaluation" ev ON ev."ServicePeriodId" = sp."Id"
JOIN "AcademicYears" y ON y."IsCurrent"
WHERE sp."ServiceId" IN (45,46)
  AND sp."StartDate" <= y."EndDate" AND sp."EndDate" >= y."StartDate"
GROUP BY y."Label";
```
Open **À venir**: 4MED Pédiatrie, three windows (07 sep → 06 nov, 07 nov → 31 déc, 07 jan → 06 mar),
50 students per window per service. Every row says **« À venir » / « Pas encore démarrée »** and
**carries no button** — visible is not actionable.

**b. One row per student, not one per période.** Pédiatrie 4MED is `SingleService`, so the publisher
collapsed each run into a single period: 898 périodes for 898 affectations. In the card, a student
appears **once** per window, and evaluating him once is the whole stage. Check it:

```sql
SELECT max(n) FROM (SELECT count(*) n FROM "ServicePeriods"
  WHERE "CohortSlotAssignmentId" IS NOT NULL GROUP BY "InternshipAssignmentId") x;   -- expect 1
```

**c. Starting is still the administration's act.** As admin → *Stages → Pédiatrie (4ᵉ année)* →
« Démarrer les affectations » for the first window. Back on the chef's page, those rows move from
**À venir** to **En cours**, and the two counts move by the same number. ⚠ Nothing on the chef's page
may start anything: re-loading **À venir** must not change `IsStarted` for a single row.

**d. The year is on every slice — and it says what it hides.** ⚠ This is the step that matters most,
because year scoping blanked chef worklists twice and both times it was *silent*.

The selector sits beside the four tranches on **every** slice and opens on the current year, which
the server chose (the request sends no year at all; check the network tab). Then:

- Open **Terminé**. Because almost the whole 2 237-row archive predates 2026-2027, the list is short
  and a yellow band must appear reading roughly « **2 1xx rotations de cette catégorie en dehors de
  2026-2027** » with a **Toutes les années** button. ⚠ **If the list is short and the band is absent,
  stop — that is the exact regression this design exists to prevent.**
- Press **Toutes les années**. The full archive comes back, paginated 200 at a time; the band
  disappears (nothing is outside a read that spans everything).
- Pick **2021-2022** from the selector: the list narrows to rotations that *ran* in that span — the
  scoping is on the dates, not on the year the registration carries.
- Switch tranches. The year **stays** where you put it: it is an axis of its own, not a property of
  the archive. The band's number changes with the tranche, because it counts what *this* slice is
  missing.
- ⚠ **The year comes from the registration, not from the dates.** With 2026-2027 selected,
  **À évaluer** must not list 6ᵉ année Pédiatrie. Those 41 périodes are *registered* 2025-2026; they
  merely ran 08 jul → 08 sep 2026, and a date predicate filed them under the new year because they
  finished eight days into it — which is how a promotion with no 2026-2027 planning appeared to have
  rotations in it. The rule the screen must follow:

```sql
-- what the screen shows for 2026-2027 (id 22) — the registration is the authority
SELECT count(*) FROM "ServicePeriods" sp
JOIN "InternshipAssignments" ia ON ia."Id" = sp."InternshipAssignmentId"
JOIN "Registrations" r ON r."Id" = ia."RegistrationId"
WHERE sp."ServiceId" IN (45,46) AND r."AcademicYearId" = 22;      -- expect 0

-- how far apart the two rules are, base-wide: 7 030 of 105 626 (6.7%), registration right each time
SELECT count(*) FROM "ServicePeriods" sp
JOIN "InternshipAssignments" ia ON ia."Id" = sp."InternshipAssignmentId"
JOIN "Registrations" r ON r."Id" = ia."RegistrationId"
JOIN "AcademicYears" sy ON sp."StartDate" BETWEEN sy."StartDate" AND sy."EndDate"
WHERE r."AcademicYearId" <> sy."Id";
```

- ⚠ **The escape must survive a stale calendar.** Temporarily narrow the current year in
  *Paramètres → Années universitaires* so that it no longer covers the 4MED windows. « À venir »
  empties — and the band must say « 300 rotations … en dehors de 2026-2027 ». That is the 2026-08
  incident reproduced on purpose, now visible instead of silent. Widen the year back afterwards.

**e. The search reaches past the page.** In **Historique**, type a surname you know is in 2019.
It must be found — the search is a server query now (350 ms debounce, from 2 characters), and the four
counts narrow with it, so the badges answer « où est cet étudiant ? ». ⚠ The old page filtered the
rows it held: pick a student who is *not* on page 1 and confirm he is still found.

**f. The dashboard number is the server's, and it agrees with the list.** *Tableau de bord* →
« Évaluations en attente » must read the **current year's** à-évaluer count for this chef — the same
number the segmented control shows in step **a**, since both default to the same year — and the
network tab must show **no** request returning thousands of rows to compute it (it asks for
`state=AwaitingEvaluation&pageSize=1` and reads the count).

**g. The control.** A chef must still see only his own services, and an anonymous request must still be
refused — `ChefWorklistEndpointTests` covers both, but confirm the page lists exactly Pédiatrie1 and
Pédiatrie2 and nothing else.

⚠ **What this step does not prove.** The 683 « à évaluer » are older rotations nobody marked; the
slice is honest, not small. Most now sit *outside* the default year and are reported by the band
rather than listed — which is the intent, but it means the backlog is still there. If it is not meant
to be evaluated, that is a data question (close-out, or importing the marks), not a bug in this
screen.

---

## 28 · Inscription — les gens que la clôture ne voit pas (10 min) — session 30

**Executed 2026-08-30 against the live base, except f and g** — the Keycloak session expired between
ticking the confirmation and pressing *Inscrire*, so the transfer was never applied. Migration
`PriorEnrolment` is **applied** (confirmed in `__EFMigrationsHistory`; MigrationService picked it up
at startup).

The screen is **Clôture de l'année → 3 · Inscription**, the third card beside déliberation and
réinscription. Steps **a–g** below are that card; **h–j** are the « Un seul étudiant » modal and the
identifier rules, and are quicker to drive from Scalar (`/scalar/v1`) where a raw body is wanted —
both paths hit the same planner, so either proves the rule.

⚠ **This is the only act in PGSH that creates people.** Run it on a promotion you are willing to have
extra students in, or take the dump first. There is no undo: `DELETE` on a `Student` is the only way
back, and it cascades.

### What the run of 2026-08-30 established

Dump taken first: `C:\Users\LEGION\pgsh-20260830-195550.dump` (20.5 MB, `-Fc`). Kept **outside the
repo** — it holds real student data.

| step | result |
|---|---|
| **a** | Promotion select required; both buttons disabled without it, with the reason shown. ⚠ « Retrait » is **absent** from the picker — `getPromotionLevels` passes `promotionsOnly`, so the `NotAPromotion` refusal is unreachable from the UI and only the API can provoke it |
| **b** | 1ʳᵉ année Médecine, 2 rows, no e-mail column → `2 lignes` · `2 à créer` · `2 nouveaux`, and the generated-address panel showing `nour_zaimi@um5.ac.ma` / **`nour_zaimi2@um5.ac.ma`**. Store unchanged by the preview |
| **c** | Applied. `students` 10 204 → **10 206**, `registrations` 49 535 → **49 537** |
| **e** | Same file re-uploaded → `0 à créer` · **`2 déjà inscrit(s), ignoré(s)`** in grey, nothing written |
| **d** | 3ᵉ année → amber warning, report cleared; the same rows → `0 à créer` · `2 erreurs` · « Provenance requise » · « Aucun étudiant n'a été créé », **and no apply button at all**. With the three provenance cells → `1 à créer` · `1 transferts` · `1 équivalence(s)` |
| **h** | Modal opens with **Inscrire** disabled; choosing a 3ᵉ année flips the divider to « Provenance — **obligatoire** », raises the alert and marks « Établissement d'origine » required |
| **i** | A 17-character Apogée at 1ʳᵉ année → **VALEUR ILLISIBLE**, « le code provisoire « SANS-CNE-AP-000000000000001 » … ne serait pas un identifiant enregistrable ». Without that guard the student would have been created and then unsaveable for ever |
| **g-bis** | 2025-2026 → 2026-2027: `1074 à traiter` · **`58 bloqué(s) en dernière année`**, and the DOM holds **1000 rows: 942 « Aucune décision », 58 « Stage antérieur non validé »**. Before the fix those 58 were counted and not listed |
| **f, g** | ⚠ **Not run.** `PriorEnrolments` is still **0 rows** — the one thing that table exists for is the one thing not yet exercised on real data |

The four rules the created rows prove, which no test could:

```
SMOKETEST01 | SANS-APOGEE-SMOKETEST01 | Nour Zaimi | nour_zaimi@um5.ac.ma  | Medecine | Pending | Cnpn 3 / Effectivity
SMOKETEST02 | SANS-APOGEE-SMOKETEST02 | Nour Zaimi | nour_zaimi2@um5.ac.ma | Medecine | Pending | Cnpn 3 / Effectivity
```

the provisional **Apogée** (both identifiers are `NOT NULL UNIQUE`, so each row needed its own), the
suffixed second address for the homonym, `AcademicProgram` read from the **level** and not from a
column, and `CnpnSource = Effectivity` — the stamper ran and a rule governed the 1ʳᵉ année.

### ⚠ Two test students are in the live base

`SMOKETEST01` / `SMOKETEST02`, « Nour Zaimi », Première Année Médecine 2026-2027. Verified to carry
**2 registrations, 0 internship assignments, 0 group, 0 history** — nothing hangs off them, so the
removal is two statements and cascades nowhere:

```sql
DELETE FROM public."Registrations"
 WHERE "StudentId" IN (SELECT "Id" FROM public."Users" WHERE "CNE" LIKE 'SMOKETEST%');
DELETE FROM public."Users" WHERE "CNE" LIKE 'SMOKETEST%';
```

Leave them only if you intend to finish **f/g** first — the same file is re-runnable and will report
them as « déjà inscrit ».

**a. The canvas is cut for its promotion.** `GET /api/inscription/template?levelId=<1MED>` →
`Inscription` sheet with 18 headers, `Mode d'emploi` saying « PROVENANCE — facultative en 1ʳᵉ année ».
Ask for a 3ᵉ année instead: the same call must say « ⚠ PROVENANCE — **OBLIGATOIRE** », and the
provenance block in the header row must be coloured amber rather than grey.

**b. The dry run writes nothing.** Fill two rows (CNE, Nom, Prénom, no e-mail), upload to
`POST /api/inscription/preview?levelId=<1MED>`. Expect `willCreateStudents: 2`, `newEntrants: 2`,
`canApply: true`, and **`generatedEmails: 2`** with each row naming its own `prenom_nom@um5.ac.ma`.
Then check the student count in the base is unchanged — the preview must have written nothing.

⚠ **The generated address is a login.** Put a name in the sheet that already exists in the base
(`SELECT "FirstName","LastName" FROM public."Users" WHERE "Email" LIKE '%@um5.ac.ma' LIMIT 1`) and
confirm the preview offers `prenom_nom2@um5.ac.ma`, not the address the existing person holds.
`SyncUserMiddleware` matches a Keycloak subject on e-mail, so a collision here hands one student
another's account.

**c. The confirmation is a number.** `POST /api/inscription?levelId=<1MED>` with **no**
`confirmedStudentCount` → **409 `Inscription.CreationsNotConfirmed`**, and the student count is
unchanged. Send `confirmedStudentCount=99` → 409 again, unchanged. Send the number the preview
returned → **200**, and exactly that many students appear.

**d. A transfer cannot enter without its équivalence.** Same two rows against a 3ᵉ année →
**400 `Inscription.Rejected`**, rows reporting `OriginRequired`, **nothing created**. Add
« Établissement d'origine », « Dernière année suivie » = 2 and « Référence d'équivalence », re-run →
200, and:

```sql
SELECT "Institution", "LastLevelYearCompleted", "EquivalenceReference"
FROM public."PriorEnrolments";
```

must show the row, joined to the registration that admitted him. ⚠ Fill only two of the three columns
and confirm the row is refused (`InvalidValue`) rather than the équivalence being silently dropped.

**e. The file is re-runnable.** Upload the exact same accepted sheet a second time, with **no**
`confirmedStudentCount`. Expect **200**, `alreadyRegistered` equal to the row count, and the student
and registration counts unchanged. This is the property the déliberation deliberately does *not* have,
and it is what lets scolarité append the late arrivals and re-send.

**f. The returner.** Find a student with a registration in an earlier year and none in the current one:

```sql
SELECT s."CNE", s."FirstName", s."LastName"
FROM public."Users" s
JOIN public."Registrations" r ON r."StudentId" = s."Id"
WHERE NOT EXISTS (SELECT 1 FROM public."Registrations" x
                  WHERE x."StudentId" = s."Id" AND x."AcademicYearId" = <current>)
LIMIT 5;
```

Put his CNE on a one-row sheet. The preview must read `returning: 1`, `willCreateStudents: 0` — **no
second student record**. This is the case the réinscription cannot carry, because he holds no
registration in the closing year for it to read a verdict from.

**g. The réorientation.** Take a Médecine student with no current-year registration and inscribe him
into a **Pharmacie** level. Preview reads `programmeChanges: 1`. After applying:

```sql
SELECT u."AcademicProgram", u."CnpnVersionId", v."AcademicProgram" AS text_programme
FROM public."Users" u
LEFT JOIN public."CnpnVersions" v ON v."Id" = u."CnpnVersionId"
WHERE u."CNE" = '<cne>';
```

⚠ `text_programme` must be **Pharmacie or NULL — never Medecine**. A stamp naming the programme he
has left makes `TotalYears`, and therefore the final-year gate, answer from the wrong arrêté. NULL is
the correct answer when PGSH holds no Pharmacie text applying at or before his entry.

**g-bis. The réinscription's blocked rows are visible now.** Still on this screen, run
**2 · Réinscription → Simuler** for 2025-2026 → 2026-2027. ⚠ The « à traiter » table must now list the
students refused entry to their final year, with a red badge « Stage antérieur non validé » and a red
count beside it. Measured on the live base: **60 of the 686** 6ᵉ année Médecine. Before session 30 the
count appeared and **the table was empty** — the filter was a literal pair that never included them.
If the table is still empty while the count is not, the fix did not land.

**h. One student at a time, without a file.** The November transfer. Either « Un seul étudiant » on
the card, or `POST /api/inscription/student` with a JSON body — no multipart, no preview, no
`confirmedStudentCount`:

```json
{ "levelId": <3MED>, "cne": "T99001", "lastName": "Alaoui", "firstName": "Omar",
  "dateOfBirth": "03/09/2006",
  "originInstitution": "FMP Casablanca", "originLastYearCompleted": "2",
  "equivalenceReference": "Arrêté 12/2026" }
```

Expect **200** and a single row report reading `"action": "TransferIn"`, `"createsStudent": true`,
`"recordsOrigin": true`. ⚠ Send the same body **without** the three provenance fields and expect
**400 `Inscription.OriginRequired`** — note the code names the *field*, not « 1 ligne en erreur »:
that is the whole reason this path is not just a one-line sheet. Check the student count is unchanged
after the refusal.

In the modal, the equivalent check is that the **Inscrire** button stays disabled with the tooltip
naming what is missing, and that the refusal — when it comes from the server — appears *inside* the
modal rather than only as a toast.

⚠ **The form and the sheet must read a date the same way.** `dateOfBirth` above is `03/09/2006`, the
French spelling — confirm `SELECT "DateOfBirth" FROM public."Users" WHERE "CNE" = 'T99001'` reads
**2006-09-03** and not 3 September's American twin. Both paths go through one parser; this is the
check that they still do.

**i. The identifiers PGSH manufactures are the ones it can still save.** Send a row with **no CNE**
and an Apogée of 8 digits: the preview must offer `SANS-CNE-<apogee>`. Send one with no CNE and a
17-character Apogée: **refused** (`InvalidValue`), because `SANS-CNE-` plus that would exceed the 20
characters `StudentIdentifierRules.CnePattern` allows and the student would be read-only in the edit
form for ever. ⚠ Then take a student the first sheet created and **open his file in the app and save
it unchanged** — that is the assertion the pattern check exists for, and it is the one no unit test
makes.

**j. An identifier that belongs to somebody else.** Take a real student's e-mail from the base, put it
on a row with a **brand-new** CNE, and preview. Expect `IdentifierConflict` naming the other student —
**not** a match. Before this check the row silently gave that existing student a registration under
the newcomer's name.

**k. The control, and « Retrait ».** A `levelId` pointing at the withdrawal marker (`Year = 0`) →
**400 `Inscription.NotAPromotion`**. Omitting `levelId` entirely → **400** from model binding. An
anonymous request → **401**; a Professeur → **403 `Inscription.NotAllowed`**. In every case the
student count must be unchanged — `InscriptionEndpointTests` asserts exactly this, but confirm it
against the real base, because a guard ordered after the write passes the handler test.

---

## 29 · Défaire une planification — ce que chaque bouton emporte (10 min) — session 31

**Executed 2026-08-30 against the live base — steps 0, 1, 2, 3, 4, 5, 6 pass; step 7 deliberately not
run** (see below). Dump taken first: `C:\Users\LEGION\pgsh-pre-smoke29.dump` (20.5 MB, `-Fc`), kept
outside the repo.

### What the run established

| step | result |
|---|---|
| **0** | **0 stranded affectations** — the defect never fired on this base. **285 mismatched**, but all 285 share year *and* group number and differ only in level: the `SplitAcademicGroupsPerLevel` signature (2023-2024 « Interne CHU » registrations whose cohortes stayed on the « Sixième Année » shard), **not** the double-affectation bug. That one is provably absent: **0** (registration, stage) pairs carry more than one affectation |
| **1** | « Vider le groupe » on a roster holding 2 planned affectations → **the dialog stayed open**, its text replaced by the server's own sentence — « … n'est pas seulement une liste : ses étudiants tiennent **2 affectation(s)** et **0 période(s)** … » — and the button became **« Vider et supprimer les affectations »**. Counts match SQL. Store unchanged |
| **2** | Confirmed again → roster emptied, **2 affectations + 2 memberships removed, cohorte and roster kept**, and the rest of the base untouched (98 558 → 98 556 affectations, exactly the two) |
| **3** | « Vider le groupe » on *Groupe 29 — Cinquième Année Pharmacie* (2025-2026, 4 affectations / 8 périodes, all started) → refused: « Les rotations de « … » sont engagées : sur **8 période(s), 8 ont démarré** … ». **No second confirmation offered** — it cannot be forced from here, as designed. Store unchanged |
| **4** | « Vider toutes » on 2025-2026 → refused; the 8 077 registrations of the year kept their rosters. No override anywhere |
| **5** | « Supprimer la cohorte » on a cohorte holding 1 planned affectation → deleted, affectation and membership with it, **and the student's registration and roster pointer survived** |
| **6** | « Réinitialiser » on *Psychiatrie* 2025-2026 → refused: « « **Psychiatrie** » est engagé en **2025-2026** : **60 cohorte(s), 706 affectation(s), 706 période(s) dont 706** … ». The old message was « des affectations sont déjà en cours ». ✅ It names **2025-2026**, not the current year — proof the year is resolved from the navbar selection rather than widened |
| **7** | ⚠ **Not run.** The base holds **0 published cells**, so « Supprimer le bloc » would *succeed* and destroy a real axis. That click is already `HANDOFF` item 2, to be done by a human on a block whose loss costs nothing |

**After every refusal the store was compared against the dump and was identical**; at the end of the
run — test rows removed — the base matched it on every table touched: 98 556 affectations, 105 626
périodes, 13 604 cohortes, 87 094 évaluations, 3 797 groupes, 43 605 inscriptions rattachées.

### Two things the run found

1. ⚠ **Every refusal toasted twice** — `errorMiddleware` (« Conflit ») and the page's own
   `notify.error` (« Erreur »), with the *identical* sentence, because this session had just made the
   page-level one print the server's words. `errorMiddleware` already toasts every rejected mutation
   in the server's own words, so the page-level call was redundant: removed from all four teardown
   handlers. This is `HANDOFF` item 4's sweep, done for these paths only.
2. ⚠ **The reset dialog said « pour l'année en cours » while the command targets the year selected in
   the navbar** — routinely a past one. The refusal that came back said « engagé en 2025-2026 »,
   naming a different year from the confirmation the operator had just read. It now names the year.
   *A destructive confirmation must name what it will actually hit.*

Both fixes are type-clean; ⚠ neither was re-driven through the browser afterwards — the year picker
stopped responding to automation near the end of the session.

### ⚠ What this base cannot exercise, and why steps 1/2/5 needed help

**All 105 626 périodes carry `IsStarted = true` and 0 are grid-linked** — they are the imported
history, and the importer marked them all started. So *every* roster that has ever been planned is in
the « underway » state, and the middle state (affectations planned, nothing run) does not occur
anywhere in the base. Steps 1, 2 and 5 were run against a throwaway roster seeded for the purpose
(`ZZ-SMOKE29`, 2026-2027, since removed).

The consequence worth remembering: **on this base « Vider le groupe » is now refused for every roster
that carries history**, which is correct — those students really did stand in those services — and
rosters with no cohortes (« Non réparti », the 2026-2027 rosters) still empty freely.

### The original recipe, for a future run

⚠ **Two of these steps destroy rows on purpose.** Do them on a cohorte whose loss costs nothing, or
take a `pg_dump -Fc` first. The base holds **0 grid-linked périodes**, so almost nothing is currently
in the « underway » state — which means the refusals in steps 3 and 5 have to be *provoked* rather
than stumbled on.

### 0 · What the old behaviour already left behind — run this first

Nothing back-fills it. An affectation whose registration points at no roster is invisible to every
roster screen and visible to every chef:

```sql
-- affectations whose student is in NO roster at all
SELECT COUNT(*)                                   AS stranded_assignments,
       COUNT(DISTINCT a."RegistrationId")         AS students,
       COUNT(DISTINCT c."AcademicGroupId")        AS rosters_still_named
FROM   "InternshipAssignments" a
JOIN   "Registrations" r ON r."Id" = a."RegistrationId"
JOIN   "Cohorts"       c ON c."Id" = a."CurrentCohortId"
WHERE  r."AcademicGroupId" IS NULL;

-- the sharper one: student IS in a roster, but not the roster his affectation's cohorte belongs to
SELECT COUNT(*) AS mismatched_assignments
FROM   "InternshipAssignments" a
JOIN   "Registrations" r ON r."Id" = a."RegistrationId"
JOIN   "Cohorts"       c ON c."Id" = a."CurrentCohortId"
WHERE  r."AcademicGroupId" IS NOT NULL
  AND  r."AcademicGroupId" <> c."AcademicGroupId";
```

The second query is the double-affectation signature (empty → re-cut → re-assign). If either comes
back non-zero, decide what to do with them **before** running the rest of §29 — the new guards stop
more from being made, they do not clean up.

### 1 · « Vider le groupe » on a roster that holds affectations — refused, and it says what it holds

*Groupes* → open any roster of a promotion that has been provisioned (its stages have cohortes) →
**Vider le groupe**.

- The dialog opens with the ordinary wording.
- Confirm → **it does not close.** The message is replaced by the server's own sentence:
  « … ses étudiants tiennent N affectation(s) et M période(s) de service, qui ne partiraient pas avec
  eux… », and the button now reads **« Vider et supprimer les affectations »**.
- ✅ N and M must match:
  ```sql
  SELECT COUNT(*) AS affectations,
         (SELECT COUNT(*) FROM "ServicePeriods" p
          JOIN "InternshipAssignments" a2 ON a2."Id" = p."InternshipAssignmentId"
          JOIN "Cohorts" c2 ON c2."Id" = a2."CurrentCohortId"
          WHERE c2."AcademicGroupId" = <groupId>) AS periodes
  FROM "InternshipAssignments" a
  JOIN "Cohorts" c ON c."Id" = a."CurrentCohortId"
  WHERE c."AcademicGroupId" = <groupId>;
  ```
- **Cancel here.** Nothing must have been written — re-run the query and confirm the roster still has
  its students (`SELECT COUNT(*) FROM "Registrations" WHERE "AcademicGroupId" = <groupId>`).

⚠ This is the assertion the whole step exists for: a guard placed *after* the write returns the same
refusal and looks identical on screen. Only the store tells them apart.

### 2 · The same roster, confirmed twice — it empties and says what it took

Repeat step 1 and press **Vider et supprimer les affectations**.

- ✅ Toast: « N étudiant(s) retiré(s) du groupe — N affectation(s) et M période(s) supprimée(s) ».
- ✅ The **cohortes survive** (they are structural): the stage page still lists them, now with 0
  students each.
- ✅ Both queries from step 0 stay at whatever they were — this path leaves nothing stranded.

### 3 · A roster whose rotation has started — refused outright, no way through

Provoke it: on the stage page, publish a cohorte's planning and press **Démarrer les affectations**,
then go back to that roster and press **Vider le groupe**.

- ✅ The message is the *underway* one — « Les rotations de « … » sont engagées : sur M période(s),
  K ont démarré, … » — and the button stays **« Vider »**: no second confirmation is offered, because
  there is nothing to confirm. It cannot be forced from here.
- ✅ It names « Dépubliez la répartition du stage » as the way forward.
- Follow it: *Stage* → **Dépublier** → read *that* refusal's numbers → force it → come back and empty
  the roster. The chain must work end to end.

### 4 · « Vider toutes » on a year that holds affectations — refused, and offers no override

*Groupes* → **Vider toutes**.

- ✅ Refused with « Les groupes de 2025-2026 portent N affectation(s) et M période(s)… », pointing at
  the per-stage reset.
- ✅ There is **no** second confirmation and no flag anywhere that would let it through. A year's
  affectations are not something a roster button may destroy.

### 5 · « Supprimer la cohorte » — the one that had no guard at all

*Stage* → a cohorte's trash icon.

- On a **planned** cohorte: ✅ toast « Cohorte supprimée — N affectation(s) et M période(s) avec
  elle ». Before this session it answered 204 with no number.
- On a **started** one (use the cohorte from step 3): ✅ refused, « La cohorte « … » est engagée : N
  affectation(s), M période(s) dont K démarrée(s), … », and the périodes are still there afterwards.

### 6 · « Réinitialiser les cohortes » — and the year it must not cross

*Stage* → **Réinitialiser**.

- ✅ On a started stage: refused, naming the **stage**, the **année**, and the five counts. The old
  message was « des affectations sont déjà en cours » — true, and useless.
- ✅ Change the navbar year to a **past** one on a stage that ran then, and check the reset touches
  only that year. Before this session an unresolved year meant *every* year the stage ever ran:
  ```sql
  SELECT g."AcademicYearId", COUNT(*) FROM "Cohorts" c
  JOIN "AcademicGroups" g ON g."Id" = c."AcademicGroupId"
  WHERE c."StageId" = <stageId> GROUP BY 1 ORDER BY 1;
  ```
  Run it before and after: exactly one row's count may change.

### 7 · The rotation block — confirm it was already right

Nothing was changed here; the point is to see that it holds.

- ✅ *Niveau* → **Supprimer le bloc** on a promotion with a **published** cell → refused
  (`RotationCycle.CannotDeletePublished`), naming the cell count.
- ✅ Unpublish, then delete: it goes through, reporting `SlotsRemoved` / `PlannedCellsRemoved`.
- ⚠ Known and deliberate: a cohorte served only by **ad-hoc** périodes (historique importé,
  délocalisation, revalidation) hangs off no cell — it neither blocks the removal nor is destroyed by
  it. Removing slots cascades cells, never périodes.

### What would make this section a pass

Every refusal above reaches the screen **in the server's own words**, the store is unchanged after each
refusal, and the two destructive confirmations name numbers that match the SQL. If any toast reads
« Impossible de … » with no sentence behind it, the failure is in the page's `catch`, not in the guard
— see the `errors[]` / `detail` rule in `CLAUDE.md`.


## 30 · Les deux exports Excel (8 min) — session 32

**Executed 2026-08-31 against the live base — steps 0-7 pass, through the real buttons.** Nothing
here writes, so no dump was taken and none was needed. ⚠ Step 8's second half (a past year through
the navbar) was driven, but the year picker is unreliable under browser automation — see the note at
the end.

### What the run established

| step | result |
|---|---|
| **1** | `GET students/export`, no filters → `etudiants-2026-2027.xlsx`, **5 932 rows**. SQL says 5 932 registrations in the current year, and **every one of the 13 per-level counts matches exactly** (902 / 898 / 895 / 833 / 691 / 606 / 235 / 228 / 207 / 160 / 144 / 98 / 35). ⚠ « 5ᵉ année Médecine » is **833** — the figure `CLAUDE.md` records for the one-`Any` scoping rule, reproduced here independently |
| **2** | `?levelId=` → `etudiants-cinquieme-annee-medecine-…`, accents folded not dashed, `Programme` and `Niveau` columns still present |
| **3** | `CNE`/`Apogée` are text (leading zeros intact), `Date de naissance` is a real date cell, `Statut` reads « En cours », `Origine CNPN` is « Inscription » on all 5 932 (the réinscription stamped every one), `CNPN` splits 2174.18 = 3 028 · 1650.25 = 2 032 · PHARM-LEGACY = 872 = 5 932. **`Groupe` / `Partition` blank on every row** — correct: 2026-2027 was rolled over but no roster has been cut yet |
| **4** | `GET stages/assignments/export?levelId=3` (3ᵉ MED 2025-2026) → **1 872** rows on « Stages » (SQL: 1 872), **3 744** on « Périodes », **2** on « Synthèse » (Chirurgie 936 + Médecine 936 = 1 872). `Réf. stage` matches across the two sheets. `Origine` = « Hors grille » on every période — the base holds 0 grid-linked ones |
| **5** | The multi-service case, on real data: `Service(s)` = « Chirurgie B → Traumatologie1 », `Période(s)` = « 18/03/2026 – 03/05/2026 · 04/05/2026 – 17/07/2026 », `Nb services` = 2, `Découpage` = « Rotation — 2 services, 2 périodes ». Spans and services correspond position by position |
| **5b** | **The single-service multi-période case** — 4ᵉ MED 2018-2019, the only place in the base it occurs: **293 rows** read « Service unique — 2 périodes, 1 interruption(s) », against 293 in SQL. `Service(s)` = « Pédiatrie1 » written **once**, and `Période(s)` = « 22/04/2019 – 31/05/2019 · 25/06/2019 – 12/07/2019 » — **not merged**, because 24 worked days separate the two windows. `Jours ouvrables` = **44** (30 + 14) against `Jours calendaires` 58 and an end-to-end span of 82 days |
| **6** | `onlyEvaluated` — the 2018-2019 file's « Synthèse » carries real verdicts: Cardiologie 556/582 (95,5 % · moyenne 11,29), Dermato-Endocrino 561/582 (96,4 % · 14,23), Pédiatrie 552/582 (94,8 % · 11,06), Pneumologie 554/582 (95,2 % · 13,35), Rhumato-Radio 566/582 (97,3 % · 13,16). `Non évalués` = 0 across the promotion, and 582 × 5 = 2 910 = the Stages sheet |
| **7** | The rattrapage columns are populated (`Niveau` ≠ `Niveau du stage` where they differ) |
| **UI** | ⚠ On the Répartition page the stage-record export is **enabled while « Imprimer / PDF » is disabled** — 4ᵉ MED 2018-2019 shows « Aucune période n'est planifiée » and still exports 2 910 affectations. That is the intended split: the répartition is the plan, this is the record, and every imported year has the second without the first |

### Two things the base cannot show you

- **The contiguous merge has no instance in this base.** Across every year: 91 894 affectations with
  one période, **293** with several in one service (all the 2018-2019 Pédiatrie interruption above),
  6 367 with two services, 1 with three. So the branch that prints « 2 périodes contiguës » as a
  single span is covered by `StagePeriodFolderTests` and by nothing on disk. It will first appear the
  day a `SingleService` stage is published and then edited, or when a second import lands.
- **`Export.TooManyRows` is unreachable.** The biggest year is 15 542 affectations against a cap of
  25 000, and 5 932 registrations against 20 000. The refusal was exercised with a stubbed 400 in the
  browser instead — which is what surfaced the defect below.

### Found during the run

1. ⚠ **Every refusal toasted twice** — `errorMiddleware` (« Données invalides ») and the export
   component's own `notify.error` (« Erreur »), same sentence. Fixed: components no longer toast, and
   the frontend `ARCHITECTURE.md` line that said the middleware does *not* toast 400/422 — the
   sentence that invites exactly this defect — was corrected. Re-verified: one toast, in the server's
   own words.
2. ⚠ **The caption number was locale-wrong**: « 5.932 inscription(s) », because `:N0` used the API
   process's `CurrentCulture`. Now formatted through `ExportLabels.Fr`. **The running API predates
   the fix — restart it and re-check the caption reads « 5 932 ».** Nothing else in the file is
   affected; the cells are typed values, not formatted strings.

### ⚠ On driving this from a browser

The navbar year picker is unreliable under automation (it silently reverts, and it froze the renderer
twice) — the same note session 31b left. Selecting a promotion and clicking the export are fine. To
reach a past year, change it by hand first, then automate the rest.


### 9 · Ce que le document dit de ses propres blancs (added 2026-08-31, session 32c)

⚠ **Needs the API restarted** — the notes were added after the run above.

Re-download the roll for a year whose students are not yet in rosters (2026-2027 today) and read the
lines **above the header**:

1. « Aucune valeur dans cet export pour : Groupe, N° groupe, Partition, Source de la décision,
   Convention. Ces colonnes sont vides parce que la donnée n'existe pas encore, pas parce qu'elles
   n'ont pas été lues. »
2. « Aucune inscription n'est rattachée à un groupe, alors que **90 groupe(s)** existent pour cette
   sélection : le découpage est fait, la répartition des étudiants ne l'est pas encore. »

Then check the **controls**, which are what stop the note becoming noise:

- Re-download the same roll for **2025-2026** (8 077 inscriptions, all in rosters): note 1 must not
  name `Groupe`, and note 2 must be absent entirely.
- ⚠ A **partly**-filled column must never be reported. 2025-2026 has 8 077 rosters pointers but only
  3 351 partitions — `Partition` must therefore **not** appear in note 1 for that year.
- The stage record carries note 1 too: export a promotion with no marks and « Note » should be named.

The numbers to check against:

```sql
-- rosters that exist for the scope vs inscriptions actually attached to one
SELECT (SELECT count(*) FROM "AcademicGroups" WHERE "AcademicYearId"=<year> AND ("LevelId"=<level> OR <level> IS NULL)) rosters,
       count(*) total, count(r."AcademicGroupId") rattachees
FROM "Registrations" r WHERE r."AcademicYearId"=<year>;
```

Measured 2026-08-31: 2026-2027 → **90 rosters · 5 932 inscriptions · 0 rattachées**; 2025-2026 →
8 077 / 8 077. ⚠ Nothing is broken in the first: that promotion is cut and not yet populated, and no
plan can be generated until it is (`HANDOFF` item 0d).

### 10 · La colonne « Étudiants » de la liste des groupes (session 32d)

Académique → Groupes, sans filtre de niveau.

- **2026-2027** : les 90 groupes de la 4ᵉ année Médecine affichent **0**, en orange.
- **2025-2026** : des comptes réels en teal — 12, 15, 13, 3, 7 … — et « Non réparti » à **4 725**.

⚠ C'est le contraste qui compte : avant cette colonne, 90 groupes vides et 90 groupes pleins étaient
indiscernables, et c'est ce qui a fait prendre un export exact pour un export cassé. Un zéro n'est
pas une erreur — c'est l'état normal entre le découpage et la répartition — mais il ne doit jamais
ressembler aux autres lignes.

### Where the buttons are

- **Étudiants → Liste des étudiants** → « Exporter (.xlsx) », top right. Carries the année, the
  programme, la promotion and the search term already on screen.
- **Formation → Répartition annuelle** → « Dossier de stages (.xlsx) », a menu with « État des lieux
  — tout » and « PV — stages évalués uniquement ». Scoped to the promotion and the année.
- **Suivi → Affectations** → « Exporter le dossier (.xlsx) », beside « Importer les notes », scoped
  to the stage in view.

Both routes require an administrative role; a professor gets 403 and an anonymous caller 401. They
can also be driven from **Scalar** (`/scalar/v1` → *Students* → `ExportStudents`,
*InternshipAssignments* → `ExportStageAssignments`).

### The steps, to re-run

### 0 · Le rôle (30 s)

Signed in as scolarité, open `…/api/students/export`. A file downloads.
Sign in as a professor and open the same URL → **403**, `Export.NotAllowed`, « Seule la scolarité peut
exporter… ». That refusal is the reason the rest of the run means anything.

### 1 · La liste des étudiants, sans rien préciser (1 min)

```
GET /api/students/export
```

- The file name carries the scope: `etudiants-<année>.xlsx`.
- Row 1 is a caption: « Étudiants — toutes promotions — **2025-2026** — N inscription(s) ».
- ⚠ **N must be this year's registrations, not the 10 204 students in the base.** Cross-check:

```sql
SELECT count(*) FROM "Registrations" r
JOIN "AcademicYears" y ON y."Id" = r."AcademicYearId"
WHERE y."IsCurrent";
```

- Header row frozen, auto-filter on, one row per registration.
- Spot-check a student who repeated: he appears **once**, under the year's level and group — not once
  per year he has been enrolled.

### 2 · Une promotion (1 min)

```
GET /api/students/export?levelId=<5ᵉ année Médecine>
```

- File name becomes `etudiants-cinquieme-annee-medecine-2025-2026.xlsx` (accents folded, not dashed).
- ⚠ **The `Programme` and `Niveau` columns are still there.** That is the answer to « fichier par
  promotion ou colonne ? » — both, and the row still says where it came from.
- Row count must equal the promotion, measured the right way — **one `Any`, not two**:

```sql
SELECT count(*) FROM "Registrations" r
WHERE r."LevelId" = <levelId> AND r."AcademicYearId" = <yearId>;
```

- Add `&academicGroupId=<id>` and check the count drops to that roster; `&searchTerm=ben` and check it
  matches on nom, prénom, CNE, Apogée and CIN — case-insensitively (try `AP2200A` in lower case).
- `?levelId=4242` → **400**, not a file. An unknown level must never silently widen to the whole year.

### 3 · Ce que les colonnes doivent dire (2 min)

Open the sheet and check, on a handful of rows:

| column | what to verify |
|---|---|
| `CNE`, `Apogée` | left-aligned **text** — a code with leading zeros still has them, and none has become a date |
| `Date de naissance` | a real date cell: sort by it and the order is chronological, not alphabetical |
| `Groupe` / `N° groupe` / `Partition` | match the roster on the Groupes page for the same student |
| `Statut` | French — « Admis », « Redoublant », « En cours » — never `Validated` |
| `Source de la décision` | blank on every legacy year (nobody pronounced), « Déclarée (PV) » after a déliberation |
| `CNPN` / `Origine CNPN` | « Inscription » where the registration carries a stamp, « Étudiant » where it fell back, **blank on both for the ~2 200 unstamped** — blank means « jamais résolu », not « rien dû » |

### 4 · Le dossier de stages, une promotion (2 min)

```
GET /api/stages/assignments/export?levelId=<promotion>
```

Three sheets, in this order: **Stages**, **Périodes**, **Synthèse**.

- **Stages** — one row per affectation. Check the count against:

```sql
SELECT count(*) FROM "InternshipAssignments" a
JOIN "Registrations" r ON r."Id" = a."RegistrationId"
WHERE r."AcademicYearId" = <yearId> AND r."LevelId" = <levelId>;
```

- **Périodes** — one row per période of those affectations. On this base every période is imported
  history, so `Origine` should read **« Hors grille »** on all of them (0 grid-linked périodes in the
  whole base). A row reading « Répartition » means something has been published since.
- **Synthèse** — one row per stage. `Effectif` per stage must sum to the Stages sheet's row count, and
  `Validés + Non validés + Non évalués = Effectif` on every line.
- Pick one student and follow him across the two sheets by **`Réf. stage`** — the same GUID on both.
  It is the only join; if it does not match, the detail sheet is unreadable.

### 5 · La question qui a motivé la fonctionnalité : plusieurs périodes (2 min)

Find a 3ᵉ or 4ᵉ année student whose stage was recorded in more than one période:

```sql
SELECT a."Id", count(*) AS periodes, count(DISTINCT p."ServiceId") AS services
FROM "ServicePeriods" p
JOIN "InternshipAssignments" a ON a."Id" = p."InternshipAssignmentId"
JOIN "Registrations" r ON r."Id" = a."RegistrationId"
WHERE r."AcademicYearId" = <yearId>
GROUP BY a."Id" HAVING count(*) > 1
ORDER BY services DESC, periodes DESC LIMIT 20;
```

Take one row of each shape and check the Stages sheet:

| the data | `Nb périodes` | `Nb services` | `Découpage` | `Période(s)` |
|---|---|---|---|---|
| 2 périodes, 1 service, meeting | 2 | 1 | `Service unique — 2 périodes contiguës` | **one** span, `début – fin` |
| 2 périodes, 1 service, a gap | 2 | 1 | `Service unique — 2 périodes, 1 interruption(s)` | **two** spans joined by « · » |
| 2 périodes, 2 services | 2 | 2 | `Rotation — 2 services, 2 périodes` | two spans; `Service(s)` reads `A → B` in the same order |

⚠ **The three things to actually check, because each is a defect the export was written to avoid:**

1. On the contiguous row, `Nb périodes` still says **2**. A merged span that also erased the count
   would have hidden the multi-période fact entirely.
2. On the gapped row, the span is **not** merged. `début → fin` there would claim the student stood in
   a service on days he was not enrolled.
3. `Jours ouvrables` is the **sum over the périodes**, not `Fin − Début`. On the gapped row it must be
   clearly less than the calendar distance between the first start and the last end.

Weekends are not gaps: a période ending a Friday followed by one starting the Monday is **contiguous**.
So is one separated only by a declared `Holiday`. If either prints as an interruption, the calendar is
not being consulted.

### 6 · PV ou état des lieux (1 min)

```
GET /api/stages/assignments/export?levelId=<promotion>&onlyEvaluated=true
```

- The caption gains « — évaluées uniquement » and the row count drops to the affectations carrying a
  verdict. Without the flag the unmarked rows are **in** the file, reading « Non évalué » — that is
  deliberate: a document whose purpose is « où en est la promotion » has to show the holes.
- `Synthèse`'s `Non évalués` column should be **0** in the filtered file and non-zero in the unfiltered
  one, for the same promotion.

### 7 · Un rattrapage (1 min)

Find a student registered in one promotion holding an affectation on another promotion's stage:

```sql
SELECT r."LevelId" AS inscrit_en, s."LevelId" AS stage_de, count(*)
FROM "InternshipAssignments" a
JOIN "Registrations" r ON r."Id" = a."RegistrationId"
JOIN "Cohorts" c ON c."Id" = a."CurrentCohortId"
JOIN "Stages" s ON s."Id" = c."StageId"
WHERE r."AcademicYearId" = <yearId> AND r."LevelId" <> s."LevelId"
GROUP BY 1, 2;
```

⚠ He must appear on the export of the promotion he is **registered in**, with `Niveau` and
`Niveau du stage` disagreeing — and **not** on the export of the stage's own promotion. That is the
opposite of how the student dossier scopes, on purpose: this file is « la promotion et ce qu'elle a
fait », the dossier is « ce que cet étudiant doit à ce niveau ».

### 8 · L'année, lue et jamais devinée (30 s)

Switch the export to a past year (`&academicYearId=<previous>`) and confirm the caption, the file name
and the rows all move together. Then check the case a date rule gets wrong: a stage registered in
2025-2026 that ran into September 2026 must be in the **2025-2026** file, never in 2026-2027.

```sql
SELECT count(*) FROM "ServicePeriods" p
JOIN "InternshipAssignments" a ON a."Id" = p."InternshipAssignmentId"
JOIN "Registrations" r ON r."Id" = a."RegistrationId"
JOIN "AcademicYears" y ON y."Id" = r."AcademicYearId"
WHERE p."EndDate" > y."EndDate";
```

Measured 2026-08-30 this is **7 030 of 105 626 périodes (6.7 %)**. Every one of them belongs to the
year its registration names.

---

# Rollback

> ⚠ **This section is a per-session recipe, and that is the problem.** The base has been the
> faculty's real data since the 2026-09-01 rebuild, and the only undo is a `pg_dump -Fc` somebody
> remembered to take — it has worked three times because a human typed it each time. A mechanism
> (scheduled dumps, a **named safe point** before each bulk act, a manifest carrying the git sha and
> the last applied migration, a restore that asserts its own row counts in SQL) is `PHASES.md` §18,
> and this section becomes a pointer to it once it ships. Until then: **dump before every bulk act,
> no exceptions.**

Reverse in the order below.

### Database (destructive — take a dump first)

```bash
# 1. Back up
docker exec -e PGPASSWORD='<pw>' postgres-0fae29d8 \
  pg_dump -U postgres -d TodoDatabase -Fc -f /tmp/pre-rollback.dump

# 2. Revert, newest first.
dotnet ef database update HolidayCalendar \
  --project PGSH.Infrastructure --startup-project PGSH.MigrationService   # undoes SplitAcademicGroupsPerLevel
dotnet ef database update RegistrationYearOutcome \
  --project PGSH.Infrastructure --startup-project PGSH.MigrationService   # undoes HolidayCalendar
dotnet ef database update AddServiceLevelCapacityAndLocalization \
  --project PGSH.Infrastructure --startup-project PGSH.Infrastructure   # undoes RegistrationYearOutcome
dotnet ef database update CnpnVersioning \
  --project PGSH.Infrastructure --startup-project PGSH.Infrastructure   # undoes AddServiceLevelCapacityAndLocalization
dotnet ef database update StageSlotAcademicYear \
  --project PGSH.Infrastructure --startup-project PGSH.Infrastructure   # undoes CnpnVersioning
dotnet ef database update CurriculumCnpn \
  --project PGSH.Infrastructure --startup-project PGSH.Infrastructure   # undoes StageSlotAcademicYear
```

⚠ **`SplitAcademicGroupsPerLevel.Down()` merges the rosters back and is lossy in labels only.**
Rehearsed both ways against a clone of your database: down takes 3 707 rosters back to 1 003 with
registrations, cohorts and cells all unchanged. What it cannot restore is which promotion each row was
split for — everything sharing `(year, number)` collapses onto the lowest id, which is the row that was
there before. Re-running `Up` splits them again from the registrations, so nothing is permanently lost.

⚠ **Reverting `RegistrationYearOutcome` drops every verdict.** `OutcomeSource` and
`OutcomeRecordedOn` are the only record that a year was closed by declaration rather than guessed at,
and no other table carries it. Dump first, without exception.

⚠ **`CnpnVersioning.Down()` restores the shape, not the data.** The forward migration *merged* 51
curricula into 9 by union; reverting cannot split them again and points every survivor at the current
year. After reverting, re-run the history reconstruction:

```bash
dotnet run --project PGSH.LegacyImport -- --seed-curricula --connection "<conn>"          # dry run
dotnet run --project PGSH.LegacyImport -- --seed-curricula --connection "<conn>" --apply
```

Or restore the dump, which is cleaner:
`pg_restore -U postgres -d TodoDatabase -c /tmp/pre-rollback.dump`

### Code

Everything through session 25 is **committed** on `cnpn-versioning-and-year-scoping`, nothing is
pushed. The working tree is clean, so `git revert <sha>` is the only lever — `git status` no longer
covers anything:

```bash
git log --oneline -3
#   c603ceb  A service holds who is standing in it, so the balance is per column
#   bdde739  What a student owes is a fact about a registration, not about him
#   9cc1f5b  Stop the capacity override from waiving admissibility
```

⚠ **`bdde739` is five work streams in one commit** — the registration's own CNPN, the effectivity
rules, the déliberation defaults, the single-row outcome, the final-year gate and the group-join
path. They could not be separated at file granularity (`DependencyInjection.cs`,
`RegistrationErrors.cs`, `DeliberationPlanner.cs` and the model snapshot each carry hunks from three
or more of them), so reverting it takes all six back and drops two migrations with it. Revert the
migrations from the database *first*, in the order given above.
The **frontend is a separate repo** and had pre-existing uncommitted work before all of this:

```bash
cd PGSH/PGSH.Frontend && git status --short
```

`git checkout .` there would discard that too. Stash rather than checkout if in doubt.

The backend tree is now clean — everything those sessions added is tracked, so a `git checkout .`
removes nothing of it. The one untracked path left is `cnpn/`, your PDF; keep it.

## 31 · La grille paginée, la publication en un seul refus, et un plan écrit d'un bloc (12 min) — session 33

⚠ **Nothing here has been run in a browser.** The suite is green (1 223) and the SQL was measured
against the live base, but the three screens below were changed and no human has opened them since.
Steps 1-4 write nothing. Steps 5-6 write; **take `pg_dump -Fc` first** if the base matters.

⚠ **Restart the API and reload the frontend first** — the response shape of `GET
stages/{id}/schedule` changed (`cohorts` is now a paginated envelope, and there is a new `summary`),
so an old bundle against a new API, or the reverse, shows an empty grid rather than an error.

### 1 · La grille s'ouvre et se ferme (2 min) — le symptôme d'origine

Stages → **Gynécologie Obstétrique** (2026-2027, la promotion la plus large : 105 cohortes) →
« Grille de planning ».

| attendu | pourquoi |
|---|---|
| la modale s'ouvre en moins d'une seconde | 25 lignes rendues au lieu de 105 |
| **et se ferme instantanément** | c'est la moitié qui prouve où était le coût : fermer n'appelle pas le serveur |
| sous le tableau : « 1–25 sur 105 cohorte(s) » et une pagination | une liste bornée doit dire ce qu'elle ne montre pas |
| en haut : « 105 cohorte(s) », et le cas échéant « N publiée(s) » / « N configurée(s) non publiée(s) » | ces nombres viennent du serveur et décrivent **toute** la sélection |

⚠ **Le piège à vérifier explicitement :** le bouton « Publier tout (N) ». **N doit être le nombre de
la sélection entière, jamais 25.** S'il vaut la taille de page, la publication en publiera bien plus
que ce qu'elle annonce.

### 2 · Paginer et filtrer (3 min)

1. Page 2, page 3 → des cohortes différentes à chaque fois, jamais deux fois la même ligne.
2. Chips de partition : chaque chip porte maintenant **son effectif** (« A (11) »). Cliquer « A » →
   les lignes se réduisent, le compteur passe à « 1–11 sur 11 cohorte(s) — partition A », **et les
   chips B, C… restent affichées.** Si elles disparaissaient, il n'y aurait plus de retour possible.
3. Le filtre doit **remettre la page à 1** : depuis la page 3 de « Toutes », cliquer une partition
   qui n'a qu'une page ne doit pas donner une grille vide.
4. Ouvrir une cellule (le sélecteur de service), en changer une, la vider : inchangé.

### 3 · Le rapport de saturation (2 min)

Le bandeau rouge, puis « Voir le rapport ».

- Le nombre annoncé est celui de **toute la sélection**, pas de la page — changer de page ne doit pas
  le faire varier.
- Un service saturé sur une période apparaît **une fois**, même si dix cohortes y sont : c'est un
  fait sur le couple (créneau, service).
- Si le tiroir affiche « N des M sont détaillées ici », le déficit annoncé est celui des N listées et
  le dit. (M > 100 est improbable sur cette base ; la ligne existe pour ne pas mentir si ça arrive.)

### 4 · « Répartition auto. » — les deux avertissements dérivés (2 min)

Toujours dans la grille :

- Ajouter un créneau vide (« Ajouter créneau »), rouvrir « Répartition auto. » → le bouton
  « **Nouveaux créneaux uniquement (1)** » doit apparaître et ne cibler que la colonne neuve. ⚠ S'il
  proposait aussi des colonnes déjà réparties, la répartition existante serait réécrite.
- Cibler une partition (chip « A ») dont la fenêtre est déjà occupée par B → l'alerte orange
  « Ces créneaux contiennent déjà les affectations de la partition **B** » doit apparaître. ⚠ C'est
  précisément la partition que le filtre vient d'enlever de l'écran : si elle ne s'affiche plus,
  l'avertissement est lu depuis les lignes visibles et il est faux.

### 5 · Publier — un seul refus, qui compte (2 min) · ⚠ écrit

Page du stage → « **Publier toutes** », **sans** cocher « Autoriser le dépassement d'effectif ».

| attendu | pourquoi |
|---|---|
| **un seul toast rouge**, pas une dizaine | c'était le symptôme signalé : une requête par cohorte, donc un toast par cohorte |
| il nomme un **nombre** (« N affectation(s) dépassent… ») et les trois plus lourdes | refuser cellule par cellule sur une base sur-souscrite à 66 % est inactionnable |
| **rien n'est écrit** — le compteur « publiée(s) » ne bouge pas | le garde passe avant l'écriture |
| si un service n'accueille pas la promotion, le message dit « ce refus-là ne peut pas être forcé » | la case ne lève que les effectifs |

Puis recocher la case et recommencer : le refus d'effectif disparaît, un refus d'**admissibilité**
resterait. Le succès annonce « N planning(s) publié(s) — M période(s) ».

### 6 · Générer le plan — long, dit qu'il l'est, et tout ou rien (3 min) · ⚠ écrit

Bloc de rotation → simuler → « Appliquer l'axe » → « **Générer le plan** ».

1. Un panneau bleu apparaît sous les boutons : « Génération du plan en cours… », avec ce qui est
   écrit et « Ne fermez pas l'onglet ».
2. Tenter de fermer l'onglet pendant le run → le navigateur demande confirmation.
3. **Le test qui compte :** relancer, puis fermer l'onglet (ou couper le réseau) au milieu. Rouvrir
   et regarder la promotion : **soit le plan entier est là, soit rien n'a bougé.** Jamais trois
   stages planifiés et quatre vides. ⚠ C'est la seule vérification de l'atomicité qui existe — le
   provider en mémoire n'a pas de transactions, donc la suite de tests ne peut pas la prouver.

```sql
-- avant / après une interruption volontaire, sur la promotion visée
SELECT s."Name", count(DISTINCT c."Id") AS cohortes, count(csa."Id") AS cellules
FROM "Stages" s
LEFT JOIN "Cohorts" c ON c."StageId" = s."Id"
LEFT JOIN "AcademicGroups" g ON g."Id" = c."AcademicGroupId" AND g."AcademicYearId" = 22
LEFT JOIN "CohortSlotAssignments" csa ON csa."CohortId" = c."Id"
WHERE s."LevelId" = :levelId
GROUP BY s."Name" ORDER BY s."Name";
```

Les deux colonnes doivent être **toutes remplies ou toutes vides** — jamais un mélange.

### 7 · Les écrans qui lisent la grille sans être la grille (1 min)

Affectations → choisir un stage → cocher des périodes (P4-P6) : la liste de cohortes doit se
restreindre correctement. ⚠ Cette page lisait « dans quelles colonnes tourne cette cohorte » depuis
la grille ; ce fait est passé sur la cohorte elle-même (`CohortResponse.periodNumbers`). **Si elle
n'affiche plus que quelques cohortes, ou aucune, c'est que la page est restée sur l'ancienne
source** — les cohortes au-delà de la première page se liraient comme ne tournant nulle part.

---

## §32 — L'export des stages : les créneaux d'une période groupée, et le chef de service

⚠ **Redémarrer l'API suffit** — rien n'a changé côté frontend, l'export est un téléchargement. Aucune
migration : `ServicePeriodSlotCoverage` et `ServiceChefAssignment` existent déjà.

**Le cas de référence, mesuré sur la base le 31/08/2026 :** 5ᵉ année Médecine, **Gynécologie
Obstétrique**, `Service unique`, publiée sur **3 créneaux** (P4 08/12→07/01, P5 08/01→07/02,
P6 08/02→07/03) — 833 périodes, une par étudiant, chacune couvrant ces trois colonnes. Les six autres
stages de 5MED sont `Rotation par période` et couvrent un créneau chacun : c'est le témoin.

1. **Étudiants / Stages → « Exporter le dossier de stages »**, promotion **5ᵉ année Médecine**, année
   **2026-2027**. Ouvrir le .xlsx.

2. **Onglet « Stages », ligne Gynécologie Obstétrique.** Les colonnes anciennes n'ont pas bougé —
   c'est voulu, le regroupement n'était pas le défaut :
   - `Découpage` = « Période unique », `Nb périodes` = **1**
   - `Nb services` = 1, `Service(s)` = le service
   
   Les nouvelles, juste à côté :
   - `Nb créneaux` = **3** ⚠ c'est le nombre à vérifier en premier
   - `Créneaux` = « P4-P6 »
   - `Chef(s) de service` = un nom, `Origine du chef` = « Note (import) » sur presque toutes les
     lignes (voir le point 5)
   - `Détail des périodes` se termine par « … · créneaux P4-P6 »

3. **Témoin sur la même feuille :** une ligne **ORL** ou **Psychiatrie** (`PerPeriod`) doit lire
   `Nb périodes` = 1 **et** `Nb créneaux` = 1. Si tout le fichier affiche 3, la lecture est fausse ;
   si tout affiche 1, la couverture n'est pas lue du tout.

4. **Onglet « Périodes », même étudiant.** ⚠ **Toujours une seule ligne** — une période notée une fois
   reste une ligne, sinon la note est comptée trois fois dans le premier tableau croisé venu. Sur
   cette ligne :
   - `Nb créneaux` = 3, `Créneaux` = « P4-P6 »
   - `Détail des créneaux` = trois lignes dans la cellule, chacune avec **sa** fenêtre et ses jours
     ouvrables : « P4 · 08/12/2026 – 07/01/2027 · 22 j.o. » etc. Élargir la ligne si Excel la tronque.

5. **Chef de service — les deux moitiés.**
   - Sur **toutes** les lignes de 2026-2027 : un nom + `Origine du chef` = « **Note (import)** ».
     Vérifié sur la base — les 29 services que touche cette année n'ont que la note. C'est la
     vérité — 140 des 148 services ne nomment leur professeur que dans une note de l'ancienne base,
     **sans date**. Le nom est imprimé (sur 95 % du document c'est le seul disponible) et la colonne
     d'à côté dit d'où il vient.
   - ⚠ **Cette deuxième moitié est suspendue depuis le 03/09/2026** — voir §42. Rattacher un chef
     dans Personnel puis ré-exporter ne fait **plus** passer la ligne à « Affectation » : les
     documents ne lisent que la note (`ServiceChefPolicy.InForce` = `SourceNoteOnly`), parce
     que les 2 affectations enregistrées sont des liens de test. La colonne reste « Note (import) »,
     et un service que **seule** une affectation nomme sort **sans chef**, avec la phrase qui le dit
     sous la légende de la feuille. Rétabli en une ligne le jour où les vrais chefs sont saisis.

6. **Une période hors grille ne ment pas.** Filtrer l'onglet « Périodes » sur `Origine` =
   « Hors grille » (l'historique Access, les délocalisations, les revalidations) : `Nb créneaux` doit
   être **vide**, pas `0`. Un `0` se lirait comme un décompte qui a échoué ; ces périodes ne viennent
   d'aucune grille.

7. **La note de bas de feuille.** Si `Nb créneaux` est vide sur *toutes* les lignes du fichier — un
   export d'une promotion dont rien n'est publié — la légende sous le titre doit le dire
   (« Aucune valeur dans cet export pour : … »). Une colonne vide partout sans explication est
   exactement ce qui a fait remonter le premier export comme cassé.

8. **La répartition n'a pas bougé.** Niveaux → 5ᵉ année Médecine → « Répartition » : les noms de chefs
   imprimés doivent être identiques à avant. La règle de résolution a déménagé (elle est partagée avec
   l'export) mais n'a pas changé — c'est le contrôle du refactoring.

## §33 — La page Stages cesse d'affirmer un chiffre qu'aucun CNPN n'énonce (4 min) — session 36

⚠ **Redémarrer l'API et recharger le frontend.** Aucune migration : les trois de la 3ᵉ année (1650.25)
sont **déjà appliquées** — vérifié sur la base le 01/09/2026, `Cnpn1650Med3CatalogueAlignment` n'a pas
levé d'exception, donc aucune période publiée depuis la grille ne verrouillait le changement de mode.

**Le cas de référence, mesuré sur la base :** MED3 **Chirurgie** et **Médecine** portent au catalogue
`coefficient 3` et `30 j.o.`, tandis que l'arrêté **1650.25** en dit `coefficient 1` et que
**2174.18** en dit `66 j.o.` — les deux sont justes, chacun *de son texte*. Un étudiant de 5ᵉ année qui
revalide un crédit de 3ᵉ année reste régi par 2174.18 : c'est pour cela que la migration a enregistré
66 **avant** d'écraser le catalogue.

1. **Stages → filtrer sur « 3ème année » (Médecine).** Les en-têtes lisent désormais
   « Durée (catalogue) » et « Coefficient (catalogue) ».

2. **Ligne Chirurgie.** Un **petit triangle orange** doit apparaître à côté du `3` du coefficient
   *et* à côté du `30j` de la durée. Survoler : l'infobulle donne « Valeur du catalogue : … » puis une
   ligne par texte — « 2174.18 (3ème année) : 66j », « 1650.25 (3ème année) : 30j » — et la phrase
   disant qu'aucune des deux n'est fausse.

3. ⚠ **Le témoin, et c'est le point le plus important de cette section.** Une ligne dont les textes
   sont d'accord — ou qu'aucun CNPN ne mentionne — ne doit porter **aucun marqueur**. Si le triangle
   apparaît partout, l'indicateur ne signale plus rien : un marqueur qui s'allume quoi que disent les
   données est du bruit, le bruit se fait ignorer, et c'est le vrai cas qui devient invisible. Même
   règle que les notes d'export (§30.7).

4. **Les valeurs affichées n'ont pas changé.** La cellule montre toujours le chiffre du catalogue —
   c'est celui que le formulaire d'édition réécrit. Ouvrir « Modifier » sur Chirurgie : le
   coefficient proposé doit être **3**, pas 1. Le correctif nomme la provenance, il ne déplace aucune
   valeur.

5. **Pagination.** Passer à la page 2 du catalogue complet (sans filtre) : les marqueurs doivent
   suivre les lignes affichées. Les figures sont lues par une seconde requête plate sur les ids de la
   page — si la page 2 n'en affiche jamais, la clé de regroupement est fausse.

# §34 — Bout en bout : planifier la 3ᵉ année sous le nouveau texte, puis solder une dette sous l'ancien (60-75 min) — session 36

> ⚠ **Sur la base réelle, avec des données réelles.** Cette section écrit : elle crée des rosters, des
> cohortes, des affectations, des périodes, et elle déplace des étudiants nommés. **Prendre un
> `pg_dump -Fc` avant de commencer** — c'est la seule vraie annulation de la partie A.
>
> Ce qui est réversible sans restauration est dit à chaque étape. **Les parties D/E touchent une
> promotion publiée (5MED)** — rien n'y est démarré, donc rien n'est détruit, mais la partie I remet
> tout en place.

**Pourquoi cette section existe.** Chaque section précédente teste un acte. Celle-ci teste ce qui ne se
voit qu'en les enchaînant : **le même `Stage` — MED3 Chirurgie, `Id = 2` — est dû par deux populations
sous deux textes différents**, et aucune page isolée ne le montre.

| | régi par | durée du texte | coefficient |
|---|---|---|---|
| les 895 inscrits en 3ᵉ année 2026-2027 | **1650.25** (`CnpnSource = Effectivity`) | 30 j.o. | 1 |
| les 92 en 6ᵉ année qui la doivent encore | **2174.18** | **66 j.o.** | 3 |
| le catalogue (`Stage`), ce que la page Stages affichait seule | — | 30 j.o. | 3 |

---

## 34.0 — L'état de départ, mesuré le 01/09/2026

À vérifier **avant** de toucher à quoi que ce soit : si la base ne dit pas cela, elle a dérivé et les
nombres cités plus bas ne tomberont pas juste.

| fait | valeur attendue |
|---|---|
| 3ᵉ MED 2026-2027 — inscriptions | **895** |
| 3ᵉ MED 2026-2027 — rosters / cohortes / créneaux | **0 / 0 / 0** — jamais planifiée |
| stamps CNPN de ces 895 | **1650.25**, `CnpnSource = Effectivity`, 895/895 |
| règles d'effectivité | 1650.25 → niveau 1 dès 2024-2025, niveau 2 dès 2025-2026, **niveau 3 dès 2026-2027** |
| stages 3ᵉ MED | 6, tous **30 j.o. / Service unique** |
| services autorisés (3ᵉ MED) | Cardio **3**, Chirurgie **12**, Dermato-Endoc **4**, Médecine **14**, Pneumo **3**, Rhumato-Radio **7** |
| 5ᵉ MED 2026-2027 | 105 rosters, 9 partitions, 833 inscrits, **5 831 périodes issues de la grille, 0 démarrée** |
| jours fériés enregistrés | 24 |

⚠ **Les 17 lignes `AllowedServices` des quatre nouveaux stages ont été copiées depuis leurs homologues
de 4ᵉ année le 01/09/2026** (Cardiologie 3, Dermato-Endoc 4, Pneumo 3, Rhumato-Radio 7). Sans elles
`RotationArranger` refuse d'emblée et **rien de la partie A n'est possible** — c'est la première chose
à revérifier si l'auto-répartition échoue.

---

## Partie A — planifier la 3ᵉ année de 2026-2027 (25 min)

Cette promotion n'a **jamais** été planifiée. C'est le seul endroit de la base où l'on peut voir la
chaîne complète partir de zéro.

1. **Étudiants → filtre promotion « 3ème année Médecine », année 2026-2027.** Le compteur doit dire
   **895**. ⚠ Ce nombre est le contrôle du piège des deux `Any` indépendants : demandé de travers il
   remonterait à quelques milliers, en comptant tous ceux qui sont *passés* par la 3ᵉ année.

2. **Groupes → 3ᵉ année Médecine.** Attendu : **aucun roster**, et 895 inscriptions dans
   « Non réparti ». C'est le seul écran d'où l'on voit le vivier.

3. **« Répartir automatiquement »** sur la promotion.
   - ⚠ **Les groupes sont homogènes par CNPN.** Les 895 sont tous sur 1650.25, donc **un seul bucket**
     et aucun groupe « CNPN à confirmer ». S'il en apparaît un, des inscriptions ont perdu leur stamp —
     arrêter et chercher pourquoi avant de continuer.
   - La numérotation **repart à 1** pour cette promotion. Elle ne continue pas après les 105 de la
     5ᵉ année : un numéro sans sa promotion n'identifie rien.

4. **Découper en partitions.** Le nouveau texte organise la 3ᵉ année en **deux semestres de trois
   stages**, pas un bloc de six. Donc `T = 3` par bloc, et `P` doit être un **multiple de 3**.
   - Prendre `P = 9` (comme la 5ᵉ année), ou 6.
   - ⚠ Demander **7** exprès : le refus doit être `PartitionCountIncompatible` **et nommer le multiple
     qui marche**. « Mauvais nombre » sans « voici ce qui va » ne sert à rien.

5. **Rotation cycle → 3ᵉ année Médecine → premier bloc**, trois stages, `k = 1` chacun, unité
   **`WorkingDays`**, 30 jours ouvrables par colonne.
   - `T = Σkₛ = 3` colonnes. `Lₛ = P·kₛ/T = 3` partitions simultanées par stage si `P = 9`.
   - **`DurationChecks` doit tomber juste** : 30 j.o. demandés contre 30 j.o. annoncés au catalogue.
     C'est le premier endroit où le catalogue aligné sur 1650.25 se voit.
   - ⚠ **`CalendarIsEmpty` ne doit pas apparaître** — 24 fériés sont enregistrés. S'il apparaît,
     « jours ouvrables » veut dire « moins les week-ends » et les dates de fin seront fausses.
   - ⚠ **`MissingReligious`** peut légitimement apparaître : les dates lunaires sont annoncées par
     décret et ne se calculent pas. C'est un rapport, pas une erreur.

6. **Appliquer**, puis **deuxième bloc** avec les trois autres stages. Les deux blocs coexistent sur le
   même niveau — c'est le cas pour lequel le remplacement « scopé aux stages nommés » existe.

7. **Générer le plan macro.** Il écrit cohortes, affectations et cellules **dans une seule
   transaction**. La page annonce ce qui s'écrit et dit qu'interrompre coûte la passe sans rien abîmer —
   ce qui n'est vrai *que* grâce à cette transaction.
   - ⚠ **`NotRequiredByCnpn` doit être à 0.** 1650.25 exige les six stages de la 3ᵉ année ; un refus ici
     veut dire que le jeu d'exigences n'est pas celui qu'on croit.

8. **Répartition → 3ᵉ année Médecine.** Le document doit sortir rempli. Deux causes d'un tableau vide,
   et elles appellent des gestes opposés — `DeclaredSlotCount` les sépare :
   - 0 créneau déclaré → l'axe n'a pas été écrit (retour au point 5)
   - des créneaux déclarés mais 0 ligne → personne n'a été réparti (retour au point 7)

9. **Publier.** ⚠ S'attendre à `PublishRefusedByIntake` : la base est structurellement sur-souscrite
   (233 cellules sur 353 dépassaient déjà la capacité, pire cas 85 pour 20). Le refus doit être
   **unique**, nommer le total, dire **combien relèvent de la moitié non forçable**
   (`LevelNotAdmitted`) et citer les trois plus lourds. Un refus par cellule est le défaut corrigé en
   session 33.
   - Cocher « autoriser le dépassement d'effectif » et republier. La moitié **admissibilité** doit
     continuer à refuser — aucune case ne la rend vraie.

---

## Partie B — le même stage, l'autre texte (5 min)

10. **Stages → filtre 3ème année.** Sur **Chirurgie** et **Médecine**, un triangle orange à côté du
    coefficient **et** de la durée (§33). L'infobulle doit lire :
    - « 2174.18 (3ème année) : 66j » · « 1650.25 (3ème année) : 30j »
    - coefficient : catalogue **3**, 1650.25 **1**
    - ⚠ **Témoin :** les quatre stages descendus de la 4ᵉ année ne portent **aucun** marqueur — un seul
      texte les mentionne, et il est d'accord avec le catalogue.

11. **Curriculum → comparer 2174.18 et 1650.25 au niveau 3.** Les deux lignes qui changent sont
    Chirurgie et Médecine (66→30, coef 3→1) ; les quatre autres apparaissent comme ajoutées.

---

## Partie C — la revalidation, et ce qu'elle ne fait pas (15 min)

> ✅ **Il y a désormais un écran** (session 36) : **Étudiants → l'étudiant → onglet Inscriptions →
> « Revalider un stage »**, sur la carte de l'inscription qu'il détient *aujourd'hui*. Le bouton est
> sur chaque carte parce que le rattrapage se raccroche toujours à l'inscription courante, jamais à
> l'année de l'échec.
>
> ⚠ **Redémarrer l'API avant cette partie.** L'écran lit
> `GET registrations/{id}/revalidation-context`, ajouté en même temps ; un processus antérieur
> répond **404** et la boîte reste vide. Le contrôle qui distingue les deux cas : `/api/stages`
> répond **401** à un appel non authentifié (la route existe) tandis que la route absente répond
> **404**.

**Le cas, réel :** *Abdallah Jad*, CNE `2136598214`, inscrit en **6ᵉ année 2026-2027**, stampé
**2174.18**. Il a échoué MED3 Chirurgie en **2023-2024**, servie en **Chirurgie Vasculaire**
(`serviceId = 43`) du **18/03/2024 au 14/06/2024**.

12. **Compter les jours réellement servis** sur cette fenêtre : **65 jours ouvrables** (hors week-ends
    et fériés enregistrés).
    - ⚠ **C'est la preuve chiffrée de toute la section.** 65 ≈ **66**, la durée que *2174.18* énonce.
      Le catalogue dit aujourd'hui **30**. Pour cet étudiant, le chiffre du catalogue est faux, et il
      l'était en silence avant §33.

13. **Dossier de l'étudiant → niveau 3.** Chirurgie doit apparaître **à revalider** (toutes les
    tentatives `NonValidé`).
    - ⚠ **Témoin :** un stage `NonÉvalué` ne doit **pas** y figurer. Non noté n'est pas échoué, et la
      base n'a presque aucune note — le compter bloquerait toute la faculté sur une donnée absente.

14. **Ouvrir la revalidation.** Par l'écran : choisir **Chirurgie — Troisième Année Médecine** dans
    « Stage à revalider ». La boîte doit alors afficher, *avant* toute écriture :
    - le texte qui le régit — **2174.18**, avec le badge « inscription » (le stamp est lu sur
      l'inscription, pas sur l'étudiant) — et **66 j.o.**
    - un bandeau orange : « Le catalogue annonce **30 j.o.**, son texte **66 j.o.** » et la phrase
      disant qu'aucune des deux n'est fausse
    - la fenêtre proposée, **laid sur 66 jours ouvrables**, jamais sur 30
    - l'échec : 2023-2024, **Chirurgie Vasculaire**, 18/03/2024 → 14/06/2024, **65 j.o. réellement
      servis** — le seul chiffre de l'écran qui ne vienne ni du catalogue ni d'un texte
    ⚠ **Si la fenêtre proposée fait 30 jours, la règle est cassée** : c'est exactement le défaut que
    cet écran existe pour empêcher.

    Le même acte en direct, si besoin — `POST stages/revalidate`, corps :

```json
{
  "registrationId": "0e9872a1-f7e4-4665-8ced-53017b630471",
  "stageId": 2,
  "cohortId": 0,
  "startDate": "2026-10-05",
  "endDate": "2027-01-08",
  "reason": "Rattrapage 3e annee - arrete 2174.18"
}
```

- Remplacer `cohortId` par une cohorte **MED3 Chirurgie créée en partie A**.
- ⚠ **`cohortId` est obligatoire ici** : cet étudiant n'a pas de roster en 2026-2027, donc le repli
  « la cohorte de son propre groupe » n'a rien à trouver. Sans lui : `NoGroupForRevalidation`.
- **Laisser `serviceId` absent.** La règle est « servi là où il a échoué » : le service **43** doit
  être repris tout seul. S'il faut le donner à la main, `OriginalServiceId` ne se résout pas.
- La période créée porte **`CohortSlotAssignmentId = null`** — hors grille, comme une délocalisation.
  Ce n'est pas une cellule de la rotation d'un groupe.

15. **Ce qui a changé, et ce qui n'a pas changé.** La fenêtre est désormais *proposée* depuis le
    texte de l'inscription ; elle n'est pas *imposée*. Le champ reste modifiable et le serveur écrit
    ce qui lui est envoyé — un rattrapage écourté d'un commun accord reste possible.
    - ⚠ **Aucune autre lecture n'applique de durée.** Le dossier, la progression et l'export
      n'en lisent toujours aucune. Ce qui a été fermé, c'est le seul endroit où une durée était
      *demandée* à l'opérateur sans que rien ne lui dise laquelle.
    - **Témoin :** choisir un stage qu'aucun texte de cet étudiant ne mentionne. Aucune fenêtre ne
      doit être proposée, et la boîte doit le dire. Rien n'est déduit du catalogue — ce serait une
      valeur qu'aucun texte n'affirme, et elle serait indiscernable d'une valeur saisie.

16. **Les garde-fous** (chacun doit refuser) :

| essai | refus attendu |
|---|---|
| relancer le même appel | `AlreadyAssignedForStage` |
| un stage qu'il a validé | `StageAlreadyValidated` |
| un stage jamais tenté | `NothingToRevalidate` |
| connecté en professeur | `RevalidationNotAllowed` |

17. **Réinscription — la porte de la dernière année.** Simuler la clôture 2026-2027 → 7ᵉ année.
    - Sous **2174.18** (`TotalYears = 7`), la 6ᵉ n'est **pas** sa dernière année : il y entre.
    - La **7ᵉ** l'est, et il doit être **`FinalYearBlocked`** tant que Chirurgie n'est pas validée.
    - ⚠ **Témoin :** un 6ᵉ année stampé **1650.25** (`TotalYears = 6`) est, lui, **déjà** en dernière
      année — le test est par étudiant, jamais par niveau. C'est exactement pourquoi le niveau seul ne
      peut pas répondre « est-ce sa dernière année ? ».
    - ⚠ Un étudiant **sans stamp** ne doit **pas** être bloqué (`TryGetValue`, jamais
      `GetValueOrDefault` : 0 lu comme « son texte dure 0 an » rendait toute année finale).

---

## Partie D — transfert temporaire (10 min)

Terrain : **5ᵉ MED 2026-2027**, publiée, **rien de démarré**. Rien n'est détruit ; la partie I remet en
place.

**Le cas :** *Aazou Zakaria*, CNE `J131520156`, inscription `e5ec92fa-a932-43f7-9377-e0fd9de04695`,
roster **3799** (Groupe 1, partition **A**).

18. **Groupes → Groupe 1 (5ᵉ MED) → l'étudiant → « Transférer ».** Type **Temporaire**, **un seul**
    stage (le sélecteur ne liste que ses affectations encore déplaçables), roster cible **3811**
    (Groupe 13, partition **B**).
    - ⚠ **Un temporaire sans stage doit être refusé.** « Temporaire » veut dire « pour ce stage-là » ;
      sans stage nommé il n'a pas de portée et ne saurait pas quand se terminer.

19. **Vérifier la portée.** Une seule affectation a bougé. **Les six autres stages sont restés dans le
    Groupe 1** — c'est toute la différence avec le définitif, et c'est ce qu'il faut regarder en
    premier.

20. **Le retour automatique.** Démarrer puis clôturer les périodes de ce stage-là (Exécution).
    - À la clôture, `EndTemporaryTransferIfAny` ferme l'adhésion temporaire et lève
      `TemporaryTransferEndedDomainEvent`. L'historique d'adhésion doit montrer **A → B → A**.
    - ⚠ **Le retour est déclenché par l'achèvement du stage, pas par une date.** Rejoindre un groupe
      dont le stage est *déjà* fini clôt le prêt immédiatement — même chemin.

---

## Partie E — transfert définitif (10 min)

21. **Même écran, un autre étudiant du Groupe 1**, type **Définitif**, cible **3823** (Groupe 25,
    partition **C**). Pas de stage : un définitif porte sur l'année.

22. **Vérifier la cascade.** *Toutes* ses affectations actives suivent vers les cohortes du groupe
    cible. `Registration.AcademicGroupId` change. **Aucun retour** n'est programmé.

23. **Les refus qui protègent l'identité du roster** — un index rend les rosters distinguables, il
    n'empêche pas de les mélanger :

| cible | refus attendu |
|---|---|
| un roster d'une **autre promotion** (ex. un roster 4MED) | refus (`AcademicGroupErrors`) |
| un roster d'une **autre année** | refus |
| « Non réparti » | refus |

⚠ Sans ces refus, l'étudiant est affecté à des stages qu'il ne doit pas et compté sur le quota d'une
autre promotion — exactement ce que `SplitAcademicGroupsPerLevel` a dû réparer sur 1 003 lignes.

24. **Le cas « il vient d'arriver ».** Prendre une inscription **sans groupe** et essayer de la
    transférer : refus. Le bon geste est **« Affecter à un groupe »** (`POST groups/assign-student`),
    qui matérialise les fenêtres **non encore closes** et rapporte `StagesAlreadyOver` pour les autres.
    ⚠ Le transfert filtre sur des affectations que le nouvel arrivant n'a pas : il « réussissait » en
    ne faisant rien, et l'étudiant se retrouvait dans un groupe correct sans aucune période.

---

## Partie F — le croisement, qui est le vrai test de charge (10 min)

25. **Transférer un étudiant qui porte une revalidation.** Reprendre Abdallah Jad (partie C), lui donner
    un roster 6MED, puis le transférer **définitivement**.
    - ⚠ **La période de revalidation ne doit pas bouger.** Elle est **hors grille**
      (`CohortSlotAssignmentId = null`) : la cascade déplace les affectations issues du plan, pas une
      réparation ad-hoc. Si elle suit, le rattrapage a été traité comme une cellule de rotation.

26. **Revalider un étudiant déjà transféré temporairement.** Les deux adhésions coexistent : le prêt
    porte sur un stage, la revalidation crée une **affectation neuve**. Vérifier que la clôture du
    stage prêté ne referme pas la revalidation.

27. **« Vider le groupe » sur un roster qui porte des affectations.**
    - Rien de planifié → vide en silence.
    - Affectations seulement planifiées → **refus nommant le compte** (`RosterHasAffectations`) ;
      `DropAffectations: true` est le fait d'avoir lu la phrase.
    - Quoi que ce soit d'engagé → **refus non forçable** (`RosterAffectationsUnderway`).
    - ⚠ **C'est délibérément non forçable.** L'acte qui détruit notes et présences est « Dépublier »,
      qui annonce son coût et demande deux fois. Un bouton côté roster ne doit jamais devenir le
      contournement.
    - ⚠ **Et remettre les étudiants ne défait rien** : un re-découpage les envoie vers d'*autres*
      cohortes, et la déduplication porte sur (inscription, cohorte) — chacun revient avec une
      **seconde** affectation pour le même stage.

---

## Partie G — les inscriptions (10 min)

28. **Inscription d'un seul étudiant** (`POST inscription/student`, ou l'écran) en **3ᵉ année
    2026-2027**, un `NewEntrant`.
    - ⚠ Il doit être stampé **1650.25** par la **règle d'effectivité**, pas par son année d'entrée —
      `CnpnSource = Effectivity`. C'est la règle lue **une fois**, à la création.
    - Sans CNE ou sans Apogée : l'identifiant manquant est fabriqué (`SANS-CNE-…` / `SANS-APOGEE-…`) et
      **la ligne le dit**. Sans e-mail : une adresse est générée et **comptée** (`GeneratedEmails`) — un
      e-mail est un identifiant de connexion.

29. **Le fichier.** `GET inscription/template` → remplir → `POST inscription/preview` → `POST inscription`.
    - ⚠ **`ConfirmedStudentCount` est un nombre, jamais une case.** Modifier le fichier entre la
      simulation et l'application : le refus est attendu. Cet acte **crée des identités**.
    - **`AlreadyRegistered` est un saut, pas une erreur** — le fichier doit survivre à un renvoi avec les
      retardataires ajoutés. Tout le reste refuse le fichier entier.
    - **Deux lignes pour une même personne** — une avec le CNE, l'autre avec l'Apogée — doivent être
      détectées. Sur une seule colonne, elles passent et `IX_Registration_Student_Year` rend un 500.
    - Un e-mail appartenant à quelqu'un d'autre avec un CNE inconnu : **`IdentifierConflict`**. Ni un
      nouveau, ni cette personne-là.

30. **Un `TransferIn` en 5ᵉ année sans `PriorEnrolment`** → **`OriginRequired`**. Les trois champs
    (établissement, dernière année validée, référence d'équivalence) vont **ensemble** ; deux sur trois
    refuse.

31. **Déliberation 2026-2027, 3ᵉ année.** Le canevas est une **liste d'exceptions** : les non-nommés
    sont **Admis**.
    - ⚠ **`ConfirmedDefaultCount`** doit refuser si une inscription est créée entre la simulation et
      l'application — c'est précisément l'étudiant que personne n'a nommé.
    - ⚠ Le défaut **promeut, ne diplôme jamais** : « est-ce sa dernière année ? » se demande par
      étudiant depuis son propre `TotalYears`. Sur la 6ᵉ MED, les 1650.25 sont en dernière année et les
      2174.18 non — **dans la même promotion**.

---

## Partie H — la charge, et les pièges de volume (10 min)

32. **Pagination.** Aucun écran ne doit tout charger :
    - « Non réparti » 3ᵉ MED avant la partie A : ~895 inscriptions dans **un** roster.
    - Grille de planification d'un gros stage : **une page de lignes + un `Summary`**. ⚠ Le
      « Publier tout (N) » doit annoncer le **total du stage**, pas les 25 lignes visibles.
    - Liste d'un chef de service : bornée par l'**état**, narrowée par l'**année**, et
      **`OutsideYearCount` doit dire ce que l'année a caché**. C'est ce qui rend le filtre sûr.
    - ⚠ `?pageSize=0` ne doit pas rendre 1 ligne en silence.

33. **Recherche serveur.** Chercher un étudiant par Apogée **en minuscules** : il doit être trouvé.
    Puis le chercher depuis la **page 3** d'une liste — le filtrage est côté serveur, donc il est
    trouvé ; filtré côté client il répondrait « aucun étudiant ».

34. **Occupation des services.** Services → un service de Chirurgie → occupation.
    - ⚠ Le pic vit dans le **chevauchement** des fenêtres, pas dans une ligne par créneau.
    - ⚠ **3ᵉ et 4ᵉ année listent désormais les mêmes services** (les 17 lignes copiées). Là où leurs
      fenêtres se recouvrent, la charge est la **somme des deux promotions** — 895 + 898. Aucun quota
      n'est écrit, donc rien ne refuse : c'est une question de calendrier, à décaler ou à contraindre
      par des `ServiceLevelCapacity`.

35. **Exports.** Rôle 3ᵉ MED + dossier de stages 3ᵉ MED.
    - Les colonnes vides doivent être **nommées** (« aucune valeur dans cet export »). Une colonne vide
      sans explication est ce qui a fait remonter le premier export comme cassé.
    - `Nb créneaux` et `Créneaux` doivent être remplis pour les stages `Service unique` publiés en
      partie A : une période, plusieurs créneaux.

---

## Partie I — remise en état

36. **Dans cet ordre, chaque étape refusant tant que la précédente n'est pas faite :** dépublier
    (annonce ce que ça coûte) → réinitialiser les cohortes du stage → vider les groupes → supprimer les
    groupes / supprimer le bloc de rotation.

37. **Les transferts des parties D/E se défont à la main** — retransférer vers le roster d'origine. Il
    n'y a pas d'annulation ; l'historique d'adhésion garde la trace des deux mouvements, ce qui est le
    comportement voulu.

38. **La revalidation de la partie C** : supprimer l'`InternshipAssignment` créé. ⚠ Sa période
    **cascade** avec lui.

39. ⚠ **Si quoi que ce soit a mal tourné en partie A, restaurer le dump.** La 3ᵉ année 2026-2027 est une
    promotion entière ; la remonter à la main n'est pas une opération de rattrapage.

---

# §37 — Reconstruire la base, puis appliquer le fichier de réinscription (session 37)

> **La partie A a été exécutée le 01/09/2026** et les chiffres ci-dessous sont ceux qu'elle a
> réellement produits. Elle est conservée telle quelle : c'est la procédure à rejouer, et le relevé de
> ce qu'un run correct affiche.

## Partie A — la reconstruction · **exécutée 01/09/2026**

Script : `rebuild.ps1`. Dump conservé : `pgsh-avant-reimport-20260901-223756.dump`.
Ordre : migrer jusqu'à `PriorEnrolment` → importer → `--seed-curricula` → migrer le reste →
restaurer la moitié saisie → `--stamp-cnpn`.

**Ce qu'il a produit, et ce qu'il faut retrouver en le rejouant :**

| contrôle | attendu |
|---|---|
| CNE `LEGACY-%` | **0** |
| étudiants sans CNE | **4 695** |
| étudiants / inscriptions | 10 203 / 43 605 |
| périodes / évaluations | 105 626 / 87 092 |
| étudiants rattachés à un CNPN | **10 185** (dont 2 769 par entrée déduite), **0 non résolu** |
| inscriptions rattachées | **43 605** |
| étudiants non rattachés | **18** — exactement les 18 sans aucune inscription |
| jours fériés / services autorisés / règles d'effectivité / chefs | 24 / 146 / 3 / 2 |
| rosters sans niveau | **0** |
| inscriptions 2026-2027 | **0** — la répartition de test a disparu |
| année courante | **2026-2027** |

⚠ **Quatre pièges silencieux ont été trouvés en l'exécutant, et chacun a maintenant sa garde.** Si
vous rejouez la partie A, ce sont les lignes à surveiller — le détail est dans `NOTES.md`
« The rebuild, run for real ».

1. **Le `DROP DATABASE` peut ne rien faire, et sortir en code 0.** PowerShell retire les guillemets
   des arguments d'une commande native, donc `"TodoDatabase"` arrive non quoté, Postgres le replie en
   minuscules et `IF EXISTS` en fait un simple avis. ⚠ L'étape suivante,
   `dotnet ef database update <cible>`, réagit à une base **peuplée** en *défaisant* les migrations —
   elle a commencé à démonter les migrations CNPN de la base vivante avant d'échouer sur une clé
   étrangère. Rien n'a été perdu (EF encapsule chaque migration dans une transaction). Le SQL est
   désormais dans un **fichier**, et la vacuité est **affirmée en SQL** avant de migrer.
2. **Les textes CNPN perdent leur année d'entrée.** `CnpnVersioning` la lit dans `AcademicYears`, vide
   quand la chaîne tourne avant l'import. ⚠ Un texte sans année d'entrée n'est pas invalide : il est
   *conservé pour citation*, donc rien ne proteste — **10 185 étudiants sur 10 185 non résolus, 0
   rattaché, et le passage a retourné un succès.** Corrigé par `CnpnIntakeYearsBackfill`, et
   `--stamp-cnpn` **refuse** désormais s'il ne rattache personne.
3. **Les noms de service ne sont pas uniques** — 25 sont partagés entre hôpitaux, « Urologie »
   apparaît deux fois dans le même hôpital — donc une restauration par nom a transformé 146
   `StageAllowedServices` en **178**. La restauration est maintenant par identifiant (l'import est
   déterministe, vérifié : 148/148 services identiques) et **vérifie ses propres comptes**.
4. **Les deux employés de démonstration n'existent pas** après une reconstruction : c'est
   `PGSH.MigrationService` qui les crée au démarrage d'Aspire. Les affectations de chef pointaient
   donc dans le vide et ont restauré **0 sur 2**, sans erreur.

**Après la reconstruction : redémarrer la stack.** L'API tourne contre une base supprimée puis
recréée sous elle, et il lui faut de toute façon les deux nouvelles migrations et la route
`reinscription/sheet`.

## Partie B — le fichier de réinscription 2026-2027 (20 min)

> Le fichier de la faculté, pas un canevas PGSH. Une ligne par étudiant, son étape actuelle et son
> étape de l'an prochain. ⚠ **Le silence n'y vaut décision que dans une chose** — voir l'étape 12.

9. **Clôture & réinscription** → la carte **« Réinscription par fichier »**, tout en haut. Elle est
   volontairement hors de la numérotation 1/2/3 : ce n'est pas une quatrième étape, c'est 1 et 2.

10. Année de destination : **2026-2027**. Déposer `Réinscriptions 26-27 VF.xlsx`. La simulation part
    toute seule.

11. **Lire les compteurs. Attendu :**

    | badge | attendu |
    |---|---|
    | lignes dans le fichier | **6 862** |
    | à réinscrire en 2026-2027 | **≈ 6 813** |
    | décisions sur 2025-2026 | **≈ 6 009** — *volontairement plus petit* |
    | diplômés déduits | **≈ 1 218** |
    | hors périmètre (masters) | **23** |
    | étudiants inconnus | **26** |
    | sans inscription source | **3** |
    | erreurs | **0** |
    | non couvertes | **≈ 1 267** |

    ⚠ **Premier test : l'écart entre « à réinscrire » et « décisions ».** Ce sont les 804 étudiants de
    dernière année qui se réinscrivent au même niveau : la thèse n'est pas soutenue, ce n'est pas un
    redoublement, et enregistrer `Failed` **annulerait les stages de leur année**. L'encart mauve doit
    l'expliquer. **Si les deux nombres sont égaux, ne pas appliquer** — la règle ne mord pas.

12. ⚠ **Deuxième test : les diplômés.** L'encart bleu doit décomposer les non couvertes en
    *diplômés / à examiner / déjà décidés*, et un second encart mauve doit annoncer les ~1 218 qui
    seront enregistrés **« Diplômé » sans être nommés dans le fichier** — ils sont absents **et** en
    dernière année de leur propre CNPN. **La case à cocher est obligatoire** : le bouton Appliquer
    reste désactivé tant qu'elle ne l'est pas, et le nombre repart au serveur, qui refuse s'il a
    bougé depuis la simulation.

    La décision est enregistrée **déduite**, pas déclarée : une liste de soutenances déposée plus tard
    par la déliberation la corrigera d'elle-même.

13. **Le tableau « Absent du fichier »** doit lister **~47** étudiants absents qui ne sont *pas* en
    dernière année, en orange. Ceux-là ne sont pas touchés : rien dans le fichier ne distingue un
    abandon d'une exclusion. Plus le tableau « à examiner » avec les 26 inconnus et les 3 sans
    inscription source.

14. **Appliquer.** ⚠ **Compter en minutes, pas en secondes** : ~14 000 événements de domaine se
    publient un par un après le commit. `SELECT count(*) FROM "Histories"` qui monte = ça avance.

15. **Après :**
    ```sql
    SELECT count(*) FROM "Registrations" r
      JOIN "AcademicYears" y ON y."Id"=r."AcademicYearId" WHERE y."Label"='2026-2027';   -- ≈ 6 813

    -- les décisions déclarées, portées par le fichier
    SELECT count(*) FROM "Registrations" r
      JOIN "AcademicYears" y ON y."Id"=r."AcademicYearId"
     WHERE y."Label"='2025-2026' AND r."OutcomeSource"='Declared';                        -- ≈ 6 009

    -- les diplômes déduits de l'absence
    SELECT count(*) FROM "Registrations" r
      JOIN "AcademicYears" y ON y."Id"=r."AcademicYearId"
     WHERE y."Label"='2025-2026' AND r."Status"='Graduated'
       AND r."OutcomeSource"='Inferred';                                                  -- ≈ 1 218

    -- ⚠ le contrôle qui compte : aucune 7ᵉ année ne doit être passée en Failed
    SELECT count(*) FROM "Registrations" r
      JOIN "AcademicYears" y ON y."Id"=r."AcademicYearId"
      JOIN "Levels" l ON l."Id"=r."LevelId"
     WHERE y."Label"='2025-2026' AND l."Year"=7 AND r."Status"='Failed';                  -- 0

    -- ⚠ et aucun diplômé en dessous d'une dernière année
    SELECT count(*) FROM "Registrations" r
      JOIN "AcademicYears" y ON y."Id"=r."AcademicYearId"
      JOIN "Levels" l ON l."Id"=r."LevelId"
      LEFT JOIN "CnpnVersions" v ON v."Id" = COALESCE(r."CnpnVersionId",
               (SELECT u."CnpnVersionId" FROM "Users" u WHERE u."Id"=r."StudentId"))
     WHERE y."Label"='2025-2026' AND r."Status"='Graduated' AND l."Year" <> v."TotalYears";  -- 0
    ```

16. **Re-déposer le même fichier.** Tout doit basculer en **« déjà inscrit »**, 0 création, et
    **0 diplômé** — les absents portent désormais une décision, donc ils comptent en « déjà décidés ».

17. **Un refus, pour vérifier qu'il refuse.** Dupliquer une ligne dans une copie du fichier, déposer :
    la simulation doit afficher **1 erreur rouge** « Doublon » et le bouton Appliquer doit disparaître.
    ⚠ Vérifier ensuite que **rien n'a été écrit**.

## Partie C — remise en état

18. Si la partie B a mal tourné : les inscriptions de 2026-2027 se suppriment par année, mais **les
    décisions portées sur 2025-2026 se rouvrent une par une**
    (`POST registrations/{id}/outcome/reopen`) — diplômes déduits compris. Il n'y a pas d'annulation
    en masse. ⚠ **À ce volume, restaurer le dump est la bonne réponse.**

---

## Partie D — les signalements · **exécutée 02/09/2026**

> ⚠ **La partie B ci-dessus décrit le comportement d'avant le 02/09/2026.** Le fichier ne refuse plus
> personne : les **60** qu'il ignorait sont désormais **créés et gelés**, et **les 1 267 absents sont
> gelés eux aussi**, diplômés déduits compris. Les compteurs attendus changent en conséquence —
> « bloqués » disparaît, « réinscrit(s) signalé(s) » et « absent(s) gelé(s) » apparaissent.

19. **Avant d'appliquer** : sur la carte « Réinscription par fichier », cliquer **« Exporter le
    rapport »**. Un .xlsx à trois feuilles doit se télécharger. ⚠ Vérifier que la feuille **Lignes**
    contient bien **6 862** lignes et non 1 000 : l'écran plafonne, le document non — c'est la
    raison d'être du bouton.

20. Vérifier que le bouton fonctionne **aussi sur un fichier refusé** : reprendre la copie avec le
    doublon de l'étape 17. L'export doit produire le même classeur, feuille « Lignes » en tête avec
    les erreurs. Un refus qui ne nomme que la première ligne fautive ne répond pas à « donne-moi la
    liste ».

21. Appliquer. Puis **Académique → Signalements**. Attendu, sur 2025-2026 :

    | filtre | attendu |
    |---|---|
    | Encore gelés, motif « Absent du fichier » | **≈ 1 267** |
    | Encore gelés, motif « Stages antérieurs » (année 2026-2027) | **60** |

    ⚠ Le sélecteur d'année de la navbar pilote la page : les 60 sont sur **2026-2027** (l'inscription
    créée) et les 1 267 sur **2025-2026** (l'inscription qui se ferme). C'est voulu — l'année est
    celle de *l'inscription*, pas celle du signalement.

22. **Le gel doit mordre.** Prendre un étudiant gelé de 2026-2027, aller sur **Groupes** et lancer
    l'auto-répartition de sa promotion. Attendu : il **n'est pas** rattaché à un groupe, et il est
    **nommé** dans le compte-rendu de l'opération avec le motif. ⚠ Un découpage qui l'omet en silence
    ressemble exactement à une promotion de cette taille-là — c'est le défaut que le signalement
    supprime.

23. **Lever un signalement.** Bouton « Lever », saisir un motif (le bouton reste désactivé sans
    motif), valider. Attendu : la notification dit « participe de nouveau à la planification » si
    plus rien ne le gèle, ou « il reste N signalement(s) » sinon. Relancer l'auto-répartition : il
    doit maintenant être rattaché.

24. **La ligne survit à sa levée.** Basculer le filtre sur **« Levés »** : la ligne doit y être, avec
    le constat d'origine *et* le motif de levée. C'est la moitié du dossier qu'un audit demande.

25. **Le fichier est rejouable.** Redéposer le même fichier et appliquer (en confirmant **0**
    diplômé — ils portent déjà une décision). Attendu : aucun signalement en double sur un étudiant,
    et le constat d'origine **inchangé** — pas réécrit.

### Résultat de l'exécution du 02/09/2026

Le fichier a été **appliqué** pour de bon (sauvegarde `pgsh-avant-reinscription-20260902-140434.dump`
prise avant). Tout ce que la simulation annonçait s'est écrit, au chiffre près :

| | attendu | écrit |
|---|---|---|
| inscriptions 2026-2027 créées | 6 813 | **6 813** |
| décisions portées sur 2025-2026 | 6 015 + 1 217 | **7 232** |
| « Diplômé » déduits | 1 217 | **1 217** |
| signalements posés | 60 + 1 267 | **1 327** |

**Le gel mord, mesuré sur la promotion réelle.** Découpage de la 7ᵉ année Médecine 2026-2027 :
**65 groupes, 1 281 étudiants placés, 60 non placés — et les 60 sont exactement les signalés**
(0 signalé placé). Après la levée d'un signalement : 1 282 placés, 59 non placés, et l'étudiante
levée est dans « Groupe 66 ». La ligne du signalement survit à sa levée, avec le constat d'origine
**et** le motif de levée, l'auteur horodaté.

⚠ **Les 1 267 absents apparaissent « dans un groupe » et c'est normal** : ce sont leurs inscriptions
de **2025-2026**, rattachées à un groupe depuis l'import. Le signalement empêche de construire du
**nouveau**, il ne déloge personne rétroactivement — c'est la règle énoncée, pas une fuite. Sur
2026-2027, aucun signalé n'est dans un groupe.

### Trois défauts trouvés en exécutant cette partie — tous corrigés le jour même

1. **Le découpage ne disait pas *pourquoi*.** Il annonçait « 60 étudiant(s) non assigné(s) » sans un
   nom ni un motif, alors que le serveur envoie une erreur par étudiant portant le constat du
   signalement. Un décompte sans raison ne vaut guère mieux que le découpage silencieux que ce
   rapport remplace. `GroupsPage` liste désormais les motifs et renvoie vers **Signalements**.
2. **`BulkItemResult.error` était typé `ApiError`** — l'enveloppe problem-details — alors que le
   serveur y sérialise le `Error` du domaine (`code` / `description`). Défaut **préexistant** : la
   conséquence est que l'erreur d'un item pouvait être testée mais jamais lue. Typé `DomainError`.
3. **Le panneau de levée était rendu *après* le tableau.** Avec 60 lignes, cliquer « Lever » sur la
   première ne montrait rien : le bouton passait pour cassé. ⚠ Un `Modal` Mantine a été essayé et
   **refuse de se monter sur cette page** (la racine `mantine-Modal-root` apparaît vide) — non
   diagnostiqué, contourné : le panneau est inline, **au-dessus** du tableau, sans portail ni
   transition. À reprendre si un autre écran rencontre le même refus.


---

## §40 — « Charge des services », the status filter, and the student file's Stages tab (session 39)

⚠ **Restart the AppHost first.** Two of the three need backend code the running process predates:
`GET /services/occupancy-report` 404s, and `?status=` on `/students` binds to nothing — the filter
*appears to do nothing* rather than erroring, which is the confusing failure.

### A · Le filtre « décision » sur la liste des étudiants — `/admin/students`
1. Année 2026-2027 dans la barre du haut. Ouvrir « Toutes les décisions ».
   → Diplômée · Admise · Redoublée · Exclue · Abandon · Active · En attente. ✅ **vérifié 02/09**
2. Choisir **Diplômée**. → Le total tombe à ~1 217 (les Diplômé déduits par le rouleau).
   ⚠ **Le test qui compte** : basculer l'année sur **2025-2026** et regarder ce total. Les 1 217
   verdicts sont portés par les inscriptions **2025-2026**, donc c'est là qu'ils doivent apparaître —
   et *pas* sur 2026-2027, où les mêmes personnes sont réinscrites et « en cours ». Si les deux
   années donnent le même nombre, le statut n'est pas résolu sur la même inscription que l'année et
   c'est exactement le défaut que `StudentStatusFilterTests` couvre.
3. Cliquer « Exporter (.xlsx) » avec le filtre actif. → Le titre de la feuille doit nommer la
   décision (« … — diplômée ») et le fichier doit contenir le même nombre de lignes que la liste.
4. Vider le filtre. → Le total revient à 6 839.

### B · L'onglet « Stages » du dossier étudiant
✅ **Vérifié 02/09 sur Houda Aamoud** (7ᵉ MED, `J137479812`) : 21 stages / 21 validés, 6ᵉ 6/6,
5ᵉ 7/7, 4ᵉ 5/5 « COMPLET », **3ᵉ 2/6 dont quatre « JAMAIS TENTÉ »** et le bandeau vert « aucun stage
en attente de revalidation » — la distinction que l'onglet existe pour montrer. « Septième Année
Médecine » affiche « aucun stage n'est inscrit au catalogue de ce niveau », ce qui est exact (7MED a
0 stage). L'axe année par année affiche dates, rotations, notes et groupes.

Ce qui **reste** à conduire :
1. Un étudiant qui **doit** vraiment un stage (toutes tentatives `NonValidé`). → Bandeau rouge,
   badges par stage, bouton « Ouvrir une revalidation ». Le bandeau doit rappeler qu'il peut
   *poursuivre* sa dernière année, seulement pas la *commencer*.
2. Un **redoublant** dont une tentative a été validée dans une année ensuite redoublée. → Badge
   barré, gris, contour — et l'infobulle « l'année a été redoublée, donc cette tentative n'établit
   rien ». C'est la ligne qu'on ne doit jamais lire comme un « validé » ordinaire.
3. Un **rattrapage** : stage d'un niveau antérieur servi sur l'inscription courante. → Dans « Par
   promotion » il figure sous **son** niveau ; dans « Année par année » il porte le badge violet du
   niveau du stage.

### C · La charge des services — `/admin/charge-services`
⚠ **Attendu aujourd'hui : un rapport vide, et c'est le cas à vérifier en premier.** La base ne
contient **0 créneau et 0 cellule sur les 22 années**.
1. Ouvrir la page. → Le document doit dire, en toutes lettres, qu'aucune cellule n'existe et
   qu'il faut passer par « Bloc de rotation » — **pas** afficher « 0 étudiant » comme si les services
   étaient vides. Les graphiques et la bande annuelle doivent être **absents**, pas vides.
2. La note sur la capacité uniforme (20) doit apparaître : tous les services portent la valeur par
   défaut de l'import.
3. « Télécharger (.html) » → ouvrir le fichier **hors de l'application**. Il doit être complet et
   autonome : titre, portée, notes, tableaux, sans rien à charger. « Imprimer / PDF » ouvre un onglet
   qui s'imprime seul.
4. **Après avoir posé un axe et réparti** (c'est la vraie recette) :
   - Le graphe mensuel montre le **pic** du mois, pas une moyenne.
   - La bande annuelle place chaque intervalle à ses vraies dates ; lire une colonne verticale donne
     les services pleins la même quinzaine.
   - Filtrer sur une promotion : ⚠ **le pic d'un service partagé ne doit pas bouger** — seule la
     part attribuée change. Un service au-dessus de sa limite reste au-dessus.
   - « Services saturés uniquement » ne doit laisser que des lignes à jours > 0.
   - Un stage dont tous les groupes tombent dans un seul service doit apparaître avec
     **inutilisés > 0**, et les services vides comptés dans « jamais utilisés ».

---

## §41 — Les sauvegardes et le point de restauration (session 40)

⚠ **À conduire sur la base vivante, mais rien ici n'écrit dans la base** — sauf l'entrée d'audit de
chaque acte. Un `pg_dump` est une lecture ; c'est la *restauration* qui est dangereuse, et elle n'est
pas déclenchable depuis l'application, par construction.

**Prérequis :** Docker démarré (c'est là que tourne la base) et l'AppHost relancé — les routes
`/api/backups*` n'existent pas dans un processus antérieur à cette session. ⚠ Le contrôle qui
distingue « route absente » de « non authentifié » : `/api/backups/safe-point` répond **404** sur un
vieux processus et **401** sans jeton.

### A · L'état, avant tout point
1. `/admin/sauvegardes`. → Le bandeau doit dire **« Aucune sauvegarde »** en rouge, et la carte
   doit nommer le dossier (`%LOCALAPPDATA%/PGSH/backups`), la migration en cours, le sha, et
   « prochaine sauvegarde automatique » avec une heure réelle.
2. ⚠ **Le cas qui compte le plus : arrêter Docker Desktop, recharger.** Le bandeau doit devenir
   **« Service de sauvegarde indisponible »** avec la raison en clair — **jamais** « aucune
   sauvegarde ». Ce sont deux gestes opposés (réparer le runner / prendre un point) et un seul écran
   vide pour les deux est précisément le défaut que cette page existe pour supprimer. Redémarrer
   Docker, recharger, l'état revient.
3. L'encart orange sur **Keycloak** doit être présent : le realm vit dans son propre volume et n'est
   pas sauvegardé avec la base.

### B · Prendre un point
4. Libellé vide → le bouton « Créer le point » est **désactivé** (pré-vol : le validateur serveur
   exige un libellé).
5. Libellé « Test §41 », note libre → **Créer le point**. Attendre : c'est un vrai `pg_dump -Fc` de
   ~10⁵ lignes. → La ligne apparaît, type **Manuel**, schéma **Compatible**, relecture **Jamais
   relue**, « Par » = votre nom.
6. Vérifier le fichier hors application : deux fichiers dans le dossier, `<id>.dump` et
   `<id>.manifest.json`. Ouvrir le manifeste → il porte la migration, le sha et les effectifs
   (`Students`, `Registrations`, `ServicePeriods`, `ServiceEvaluations`…). ⚠ **Comparer ces
   effectifs à la base** : ce sont eux qui chiffreront le coût d'une restauration.
7. **Relire l'archive** (icône liste). → Le badge passe à **« Archive relue »**. C'est
   `pg_restore -l` : il prouve que l'archive n'est ni tronquée ni corrompue — la panne exacte qu'un
   `pg_dump` redirigé par un tube a produite ici une fois — et **rien de plus**.
8. Le bandeau passe au **vert**, « il y a N min, même schéma ».

### C · Le plan de restauration — ⚠ **lire, ne pas exécuter**
9. Icône « Plan de restauration » sur le point pris. → Table par table : au point, aujourd'hui,
   effacées, rétablies. Juste après la prise, tout doit être à **0 effacée / 0 rétablie**.
10. Créer une donnée quelconque (un jour férié de test, par exemple), rouvrir le plan. → La ligne
    `Holidays` doit afficher **1 effacée**, et le total en tête doit le dire. Supprimer le jour férié
    ensuite.
11. La commande affichée doit être complète et précédée de « AppHost arrêté ». ⚠ Elle porte
    `PGPASSWORD=<mot de passe>` **en gabarit**, avec au-dessus la ligne qui le relève
    (`docker exec … printenv POSTGRES_PASSWORD`) : mesuré 03/09/2026, la socket locale du conteneur
    est en `scram-sha-256` et non en `trust`, donc une commande sans mot de passe échoue — et un
    identifiant affiché sur une page web est un identifiant dans une capture d'écran.
    ⚠ **Ne pas la lancer sur la base vivante.** Si vous voulez l'éprouver, faites-le contre une base
    de rebut (`createdb pgsh_restore_test`, puis `-d pgsh_restore_test`) — c'est ce qui manque
    encore (§18.2) et c'est le seul moyen de faire passer un point de « relue » à « restaurée ».

### C-bis · La découverte du conteneur — ⚠ le piège de cette machine
11-bis. `docker ps --format "{{.Names}}	{{.Image}}"`. → Un **seul** conteneur dont l'image commence
    par « postgres » doit tourner. S'il y en a plusieurs (cette machine héberge d'autres projets), la
    page doit dire **« plusieurs conteneurs PostgreSQL tournent (…) : renseignez
    Backups:ContainerName »** — et surtout **pas** en choisir un. Un dump de la *mauvaise* base,
    classé et étiqueté comme point de restauration de celle-ci, est exactement la panne silencieuse
    que cette phase existe pour supprimer. pgAdmin est exclu par son image, pas par son nom.

### D · Les garde-fous
12. **Supprimer le point le plus récent** → l'icône est **désactivée**, avec l'infobulle disant
    pourquoi : c'est celui que lisent les confirmations des actes en masse.
13. Prendre un second point, puis supprimer le premier → autorisé, **et seulement en `SuperUser`**.
    Connecté en `Scolarite`, l'appel doit répondre **403** et le point rester en place.

### E · La bannière dans les actes en masse — c'est *ça*, la fonctionnalité
14. `/admin/year-closure`, charger un fichier de déliberation (ou de réinscription). → La bannière
    apparaît **au-dessus du résumé**, avec le libellé pré-rempli « Avant clôture … ».
15. ⚠ **Sans point exploitable** (aucun, ou pris sous une autre migration) : le bouton
    « Enregistrer les décisions » est **désactivé** et l'infobulle dit « Créez un point de
    sauvegarde, ou confirmez de continuer sans ». Cocher la case → le bouton redevient actif. **Ce
    n'est pas un blocage** : le jour où Docker est en panne, la faculté doit pouvoir clôturer.
16. « Créer un point maintenant » **depuis la bannière**, sans quitter l'écran. → Le fichier chargé
    et le rapport affiché doivent **survivre** à la prise ; la bannière passe au vert et le bouton
    d'application se débloque de lui-même.
17. Même vérification sur **« Bloc de rotation » → Appliquer l'axe** et sur la section
    **« Réinscription par fichier »**.

### F · La planification
18. Laisser tourner une heure (ou baisser `Backups:Schedule:IntervalMinutes`, minimum **5**). → Un
    point **Automatique** apparaît seul. ⚠ Vérifier ensuite que la rotation **n'a pas** touché aux
    points *Manuel* / *Avant un acte* : elle ne purge que les automatiques.
19. `Backups:Schedule:Enabled = false` → « prochaine sauvegarde automatique : aucune planification
    active », en toutes lettres plutôt qu'une case vide.

### Remise en état
20. Supprimer les points « Test §41 » (en `SuperUser`, le plus récent en dernier — ou en prendre un
    nouveau d'abord). Les entrées d'audit `BACKUP_POINT_*` restent, et c'est voulu.

## §42 — « Quel groupe va déjà là ? » et la faisabilité d'un hôpital (session 41)

**Pourquoi cette passe.** Trois demandes nominatives réelles ont motivé deux lectures neuves. Rien
n'écrit dans la base — ce sont deux `GET` — donc cette section est **sans risque** et peut être
déroulée sur la base vivante telle quelle. ⚠ **L'AppHost doit être relancé** : un processus antérieur
à cette session répond **404** sur `/api/groups/placements` alors que `/api/groups` répond 401 sans
jeton, et c'est exactement le contrôle qui distingue « route absente » de « non authentifié ».

⚠ **Ce que la base va répondre aujourd'hui, et il faut le savoir avant de crier au bug** : elle tient
**0 cellule de planification sur toutes les années sauf la 3ᵉ MED 2026-2027** (posée en session 39).
Donc `Summary.PlacedRosters` vaudra **0** sur presque toutes les promotions, et c'est *la bonne
réponse* : « rien n'est encore réparti », pas « personne ne va au HMIMV ». Toute la §42 consiste à
vérifier que les deux se distinguent.

### a — la route existe, et elle est protégée

1. Sans jeton : `GET /api/groups/placements?levelId=<3ᵉ MED>` → **401**.
   ⚠ Si elle répond **404**, l'API n'a pas été relancée ; rien d'autre dans §42 n'a de sens.
2. Avec jeton, même URL → **200**, avec `rosters.items`, `rosters.totalCount` et `summary`.

### b — le blanc qui veut dire deux choses

3. Sur une promotion **non répartie** (par ex. 6ᵉ MED 2026-2027), demander
   `?levelId=<promo>&hospitalId=<HMIMV>&match=Exclusively`.
   Attendu : `items` **vide**, `summary.promotionRosters` > 0 et **`summary.placedRosters` = 0**.
   ⚠ C'est *le* point de la section : la liste vide et le chiffre à 0 disent ensemble « allez répartir
   la promotion », là où la liste vide seule se lirait « cet hôpital ne prend personne ».
4. Sur la **3ᵉ MED 2026-2027**, qui *est* répartie, la même requête sans `hospitalId` :
   `placedRosters` doit être **> 0**. C'est le témoin — sans lui, une route qui renvoie toujours 0
   passerait l'étape 3.

### c — « exclusivement » ne doit jamais ramener un roster que personne n'a réparti

5. `?levelId=<3ᵉ MED>&hospitalId=<HMIMV>&match=Exclusively` puis la même chose avec
   `match=Anywhere`.
   Attendu : le résultat d'`Exclusively` est **inclus** dans celui d'`Anywhere`, et dans la réponse
   `Anywhere` les rosters portant `hospitalPlacement = "Entire"` sont **exactement** ceux
   qu'`Exclusively` renvoie.
   ⚠ Aucun roster ne doit porter `hospitalPlacement = "Unplaced"` dans une réponse filtrée par
   hôpital : c'est la marque du bug à vide.
6. Prendre un roster de la réponse et vérifier à l'écran, sur la grille du stage, qu'il est bien dans
   les services annoncés, aux créneaux annoncés (`services[].periodNumbers`).

### d — la faisabilité, sur les deux cas réels

7. `GET /api/hospitals/<HMIMV>/stage-coverage?levelId=<6ᵉ MED>` →
   `stageCount = 6`, `coveredStageCount = 6`, `unauthoredStageCount = 0`.
   **« Tout au militaire » est possible en 6ᵉ année.**
8. La même chose pour la **5ᵉ MED** → `coveredStageCount = 6` sur `stageCount = 7`, et **Santé
   Publique** doit ressortir `coverage = "NotAtThisHospital"` avec `allowedServiceCount = 1`.
   ⚠ C'est la ligne qui, jusqu'ici, se découvrait à la sixième cellule après la promesse.
9. Une promotion tenant un stage d'immersion (1ʳᵉ ou 2ᵉ année) → au moins un stage à
   `coverage = "NoServicesAuthored"` avec `allowedServiceCount = 0`.
   ⚠ Vérifier que l'écran/la réponse ne le compte **pas** comme « non couvert » : une liste vide n'est
   pas appliquée, donc le stage est ouvert à tout. Le confondre enverrait changer d'hôpital au lieu de
   saisir la liste.
10. Hôpital inconnu → **404** `Hospitals.NotFound`. `levelId` omis → **400**.

### e — les refus qui ne viennent que du pipeline

11. `?levelId=<promo>&serviceId=<S>&hospitalId=<H>` → **400** (un service appartient déjà à un hôpital).
12. `?levelId=<promo>&match=Exclusively` sans service ni hôpital → **400**.
13. `?hospitalId=<H>` sans `levelId` → **400**.
    ⚠ Ces trois règles vivent **uniquement** dans `ValidationPipelineBehavior` : un test appelant le
    handler les traverse sans les voir. C'est le même angle mort qui avait rendu tout le catalogue de
    stages non enregistrable.

### f — le geste complet, sur une vraie demande

14. Prendre une demande réelle (« X doit passer tous ses stages au HMIMV »). Poser d'abord **d**
    (faisabilité), puis **c** (quel groupe y va déjà), puis transférer l'étudiant vers ce groupe
    depuis la fiche étudiant. Vérifier sur son dossier qu'il a bien repris les cohortes du roster.
15. ⚠ **Ne pas lancer « auto-répartir ce stage » ensuite** si une cellule a été posée à la main :
    `RotationArranger` la supprime et la réécrit sans rien dire. Tant que `PHASES.md` §19.2 n'est pas
    livré, publier cohorte par cohorte.

**Non exécutée à ce jour.** Elle ne demande aucune sauvegarde préalable — les deux routes lisent.

## §42 — le chef de service vient de la note d'import, et de rien d'autre (03/09/2026)

Bâti session 42. **Aucune migration.** Deux documents concernés : l'export Excel du dossier de stages
(Répartition annuelle → « Exporter le dossier de stages ») **et** la répartition annuelle elle-même.

⚠ **Le pourquoi, à relire avant de « corriger » ce qui suit :** la base ne contient que **2**
`ServiceChefAssignment`, et ce sont des liens de test. Un document qui les résout imprime le nom d'un
compte de test à côté d'étudiants réels. L'ordre d'autorité (affectation datée → chef en poste →
note) reste la règle et reste testé ; `ServiceChefPolicy.InForce` dit seulement quelle part en
est appliquée.

1. **Export Excel, onglet « Stages » ou « Périodes ».** Sur **toutes** les lignes :
   `Origine du chef` = « **Note (import)** ». Une seule ligne en « Affectation » signifie que la
   constante a été remise à `Authority`.

2. **Sous la légende des deux feuilles** (et **pas** sur « Synthèse », qui ne nomme aucun chef) :
   « Les chefs de service sont repris de la fiche du service (note d'import) uniquement… ». ⚠ C'est le
   contrôle qui distingue « politique choisie » de « colonne codée en dur » — une colonne qui ne varie
   jamais est le miroir d'une colonne vide.

3. **Le coût, et il est voulu.** Prendre un service qui a un chef rattaché dans Personnel *et* aucune
   note dans sa description (Hôpitaux → le service → onglet Chef). Une ligne de l'export qui passe par
   ce service doit sortir **`Chef de service` vide et `Origine du chef` vide** — un blanc dit moins
   faux qu'un mauvais nom, et rien ne prétend une source pour un nom non imprimé.

4. **Répartition annuelle** (télécharger le document) : le nom imprimé après le service est celui de
   la note, sur toutes les lignes. Survoler le nom → l'infobulle « Nom repris de la fiche du service
   (import)… ». ⚠ Elle apparaît maintenant partout, ce qui est exact : la note est indatée quelles que
   soient les sources autorisées. Rien de visible n'a été ajouté au document imprimé.

5. **Le témoin.** Sur un service **sans** chef rattaché et **avec** une note, le nom doit s'afficher
   exactement comme avant la session — sinon ce n'est pas la politique qui a changé, c'est la lecture
   de la note qui est cassée.

6. **La fiche du service dit maintenant la même chose que les documents** — c'est le défaut trouvé
   après le premier rechargement. **Hôpitaux → Pédiatrie1** (ou Pédiatrie2 : ce sont les **deux**
   seuls services de la base portant une affectation, *Youssef Alaoui*, ouverte depuis le
   29/08/2026) :
   - Le nom en tête doit être celui de la **note** — « Pr.N.Elhafidi » pour Pédiatrie1,
     « Pr.A.Mdaghri Alaoui » pour Pédiatrie2 — avec le badge « **note (import)** ». ⚠ C'est
     exactement le nom que l'export imprime pour ce service : si les deux diffèrent, la page
     re-classe les sources de son côté, ce qui est le défaut.
   - Juste en dessous, l'encadré jaune doit dire « … un chef est pourtant **rattaché** à ce service
     (voir l'historique), mais les documents ne lisent que la note pour l'instant : les seules
     affectations enregistrées sont des liens de test ». ⚠ Il **ne** doit **pas** dire « Désignez un
     chef de service » — le service en a un.
   - Sous « Historique » : « Youssef Alaoui · *son grade* · en cours depuis 2026-08-29 ».
   - ⚠ **Un chef rattaché par la clé du service** (`ServiceChefId`, et non par une affectation datée)
     doit apparaître sur une ligne « **rattaché** » sous l'encadré — pas seulement dans
     « Historique », qui ne liste que les affectations datées. Aucun service de la base n'est dans
     cet état aujourd'hui (0 sur 148), donc c'est un contrôle à faire en rattachant un chef depuis
     le formulaire du service : le nom doit rester visible même si ce n'est pas celui qui est
     imprimé.

7. **Le témoin, sur un service ordinaire** (n'importe lequel des 140 autres, p. ex. **Pédiatrie3**) :
   même nom en tête + badge « note (import) », mais l'encadré dit « **Désignez un chef de service**
   pour que l'attribution soit datée… » — la phrase inverse. Les deux états appellent des gestes
   opposés et ne doivent jamais partager la même phrase.

8. **Contrôle « l'API est à jour »** : si la fiche affiche « Chef de service non communiqué par
   l'API », le processus est antérieur à `chefAttribution` — **relancer l'AppHost**. ⚠ La page ne
   devine **pas** le nom dans ce cas : le deviner reviendrait à remettre un second ordre de
   résolution côté client, c'est-à-dire le défaut lui-même.

**Pour revenir en arrière** (le jour où les vrais chefs sont saisis) : une ligne,
`ServiceChefPolicy.InForce` = `ServiceChefSourcePolicy.Authority`. Les deux documents, **la
fiche du service** et la phrase sous la légende suivent la constante. ⚠ Vérifié en la basculant :
**5 tests de handler tombent, 0 test de `ServiceChefDirectoryTests`** — l'ordre d'autorité est
couvert là où il vit.

⚠ **Ce que la session n'a pas corrigé, et qui se voit encore :** la **liste** des services
(Infrastructure) affiche « — » dans la colonne Chef pour les 148 services, et la fiche service du
**portail étudiant** dit « aucun chef » — les deux lisent la clé `ServiceChefId` seule, jamais la
note. `HANDOFF.md` item `0ag`.
