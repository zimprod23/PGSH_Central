# Smoke test — sessions 11 → 23

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
and it carries a data repair: every cell arranged before it needs re-running).
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
| `PartitionScopeAndIndexGaps` | 🔲 **pending** — apply before step **17**. Adds `IX_AcademicYear_IsCurrent` (unique, filtered on `"IsCurrent"`) and demotes any extra current year first, keeping the highest `Id`. It should touch 0 rows: `CreateAcademicYear` already demotes the others. No table or column is added |
| `GroupLabelPerPromotion` | ✅ applied — verified 2026-08-14: `IX_AcademicGroup_Year_Level_Label` and `IX_AcademicGroup_Year_Level_Number` both present, the two year-only indexes gone. A pure relaxation: `IX_AcademicGroup_Year_Label` → `IX_AcademicGroup_Year_Level_Label` (`NULLS NOT DISTINCT`). The old key is a superset of the new one, so no existing row can collide; see step **12l** |

Timings below are the real figures from your data — if you see a different number, that is the bug.

---

## 0 · Sanity (2 min)

```bash
rm -rf PGSH.Tests/bin PGSH.Tests/obj          # ⚠ see below
dotnet test PGSH.Tests/PGSH.Tests.csproj      # expect: 965 passed, 0 failed, ~35 s
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

> **Status: built and unit-tested (11 tests), never run against the real base.** Migration
> `FinalYearEntryWaiver` creates one table and changes no data.

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

# Rollback

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
