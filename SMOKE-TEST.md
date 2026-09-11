# Smoke test — the manual verification pass

One section per piece of work, oldest first, numbered by section rather than by session. Each says
what to click, what the correct numbers are on **this** base, and what a wrong number would mean.

⚠ **Timings and counts below are the real figures from your data — a different number is the bug.**

⚠ **The base is the faculty's real data.** Take a `pg_dump -Fc` before any section that writes, and
read **Rollback**, at the bottom, before you start rather than after.

Sections **§0 – §19** cover sessions 11 → 23 and are all executed; they have moved to
[`SMOKE-TEST-ARCHIVE.md`](SMOKE-TEST-ARCHIVE.md).

Prerequisites: `dotnet run --project PGSH.AppHost`, log in as an admin (Scolarité).

## Migrations

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

---

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
18. ⚠ **La cadence est désormais quotidienne** (`IntervalMinutes` = 1440 depuis le 04/09/2026) :
    ne pas attendre le tour suivant. Baisser `Backups:Schedule:IntervalMinutes` (minimum **5**)
    le temps de l'essai, puis le remettre à 1440. → Un
    point **Automatique** apparaît seul. ⚠ Vérifier ensuite que la rotation **n'a pas** touché aux
    points *Manuel* / *Avant un acte* : elle ne purge que les automatiques.
19. `Backups:Schedule:Enabled = false` → « prochaine sauvegarde automatique : aucune planification
    active », en toutes lettres plutôt qu'une case vide.

### Remise en état
20. Supprimer les points « Test §41 » (en `SuperUser`, le plus récent en dernier — ou en prendre un
    nouveau d'abord). Les entrées d'audit `BACKUP_POINT_*` restent, et c'est voulu.

## §42 — « Quel groupe va déjà là ? » et la faisabilité d'un hôpital · **exécutée 04/09/2026**

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

### g — l'écran, une fois l'AppHost relancé (session 41)

`/admin/placements` — **Académique → Placements**. Rien n'y écrit&nbsp;: les deux appels sont des
`GET`, donc cette partie se déroule sans sauvegarde préalable.

16. **Arriver sans promotion** → l'écran demande d'en choisir une et **n'appelle pas** le serveur
    (la requête est `skip`ée). Vérifier dans l'onglet réseau qu'aucun `/groups/placements` ne part.
17. **Choisir une promotion sans lieu** → tous les rosters, chacun avec son effectif, sa partition,
    et pour chaque stage ses services + créneaux. Aucun badge de placement (aucun lieu n'a été
    demandé), et « x / y stage(s) au lieu demandé » absent.
18. **Choisir l'Hôpital Militaire Mohammed V** → le panneau de faisabilité apparaît au-dessus de la
    liste. En **6ᵉ MED** il doit dire « toute la rotation peut se faire ici » (6/6). En **5ᵉ MED**,
    « impossible pour cette promotion », en nommant **Santé Publique**.
19. **Cocher « Exclusivement »** → la liste se restreint. ⚠ Sur une promotion non répartie elle doit
    devenir **vide**, avec l'encadré orange « rien n'est encore réparti » — *pas* le gris « aucun
    groupe ne correspond ». C'est la distinction que toute la page existe pour tenir.
20. **Choisir un service alors qu'un hôpital est sélectionné** → l'hôpital doit **se vider tout
    seul**, et l'inverse aussi. La combinaison interdite ne doit pas être atteignable (le serveur la
    refuserait en 400).
21. **Sans lieu, le commutateur « Exclusivement » est désactivé**, et son infobulle dit pourquoi.
22. **Recharger la page** → les filtres reviennent (ils sont dans l'URL), et l'URL est partageable.
23. **Changer l'année dans la barre du haut** → la liste se recharge (l'année est dans la clé de
    cache), et l'effectif des rosters change avec.
24. Sur un stage qu'un roster ne fait pas encore, la ligne doit lire « reste à répartir » — pas
    disparaître.

⚠ **Le contrôle qui distingue « route absente » de « non authentifié »**&nbsp;: sur un processus API
antérieur à la session 41, `/api/groups/placements` répond **404** tandis que `/api/groups` répond
**401** sans jeton. Si la page reste vide avec une erreur, vérifier cela avant tout le reste.

### Ce que la passe a donné, 04/09/2026 — **deux défauts, tous deux invisibles autrement**

`tsc` propre, `npm run lint` propre, 1 474 tests backend verts, et **deux défauts trouvés au premier
pilotage**. Les deux sont de la famille « la donnée est juste, la phrase est fausse ».

1. ⚠ **Le panneau de faisabilité survivait au `skip`.** Lu sur `data` au lieu de `currentData`, il
   affichait encore « Faisabilité — Hôpital Militaire Mohammed V » après que le choix d'un service
   d'un *autre* hôpital eut vidé le filtre hôpital. Corrigé, et la règle générale est écrite dans
   `PGSH.Frontend/CLAUDE.md` §1i : **un panneau qui nomme son sujet se lit sur `currentData`**.
2. ⚠ **La réponse vide avait trois causes, pas deux.** La 6ᵉ MED 2026-2027 n'a **aucun groupe**, et
   le message écrit pour « des groupes existent mais rien n'est réparti » disait « Les 0 groupe(s)
   de cette promotion ne tiennent aucune cellule » en renvoyant vers la répartition — alors que le
   premier geste est de **découper**. Trois états, trois phrases, trois couleurs.

**Relevé à l'écran, sur la base vivante :**

| étape | attendu | obtenu |
|---|---|---|
| 16 · arrivée sans promotion | aucun `/groups/placements` | ✅ seuls `levels`, `hospitals`, `services`, `academic-years` partent |
| 17 · 3ᵉ MED 2026-2027 | la rotation lisible | ✅ 134 groupes ; partition A = Cardio P1 → Chirurgie P2 → Endocrino P3 → Médecine P4 → Pneumo P5 → Rhumato P6 |
| — · 4ᵉ MED 2026-2027 | le pliage `SingleService` | ✅ **Pédiatrie · P1, P2** — une entrée, deux créneaux |
| 18 · faisabilité 6ᵉ MED | 6/6, vert | ✅ « Toute la rotation peut se faire ici » |
| 18 · faisabilité 5ᵉ MED | impossible, *Santé Publique* | ✅ « 1 stage(s) sur 7 n'ont aucun service autorisé ici : Santé Publique » |
| 19 · Exclusivement, promotion répartie | **gris** | ✅ 5ᵉ MED : « Aucun groupe ne correspond », 121 répartis |
| 19 · Exclusivement, promotion non répartie | **orange** | ✅ 6ᵉ MED 2025-2026 : « Rien n'est encore réparti », 100 groupes / 0 répartis, « reste à répartir » par stage |
| — · promotion sans aucun groupe | orange, autre phrase | ✅ 6ᵉ MED 2026-2027 : « Cette promotion n'a aucun groupe » *(après correction)* |
| 20 · hôpital puis service | l'hôpital se vide | ✅ l'URL perd `hospital=2` |
| 21 · Exclusivement sans lieu | désactivé + raison | ✅ |
| 22 · rechargement | filtres conservés | ✅ ils sont dans l'URL |
| 23 · changement d'année | la liste se recharge | ✅ 6ᵉ MED : 0 groupe en 2026-2027, 100 en 2025-2026 ; les filtres de lieu survivent |

**Non exécuté :** la pagination au-delà de la page 1, et le lien vers la fiche d'un groupe.

⚠ **L'automatisation du navigateur reste fragile ici** — le viewport se redimensionne sous les clics
et `Page.captureScreenshot` expire régulièrement. Passer par les `ref` des éléments plutôt que par
des coordonnées. Même constat que l'item 0ac.

Elle ne demande aucune sauvegarde préalable — les deux routes lisent.

## §42b — le chef de service vient de la note d'import, et de rien d'autre (03/09/2026)

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

## §43 — Le journal des actions (session 42)

⚠ **L'AppHost doit être relancé** : `/api/audit-log` n'existe pas dans un processus antérieur à
cette session. Le contrôle qui distingue « route absente » de « non authentifié » est que
`audit-log` répond **404** sur l'ancien processus tandis que `/api/academic-years` répond **200** —
et depuis cette session la page le **dit** au lieu de rester vide.

Rien n'écrit ici : la lecture est un `GET`. Les étapes qui *provoquent* une entrée sont signalées.

### a — la page, et ce qu'un vide veut dire

1. **Système → Journal des actions** (`/admin/journal`). Sur une base neuve : « Le journal est vide »,
   avec la phrase disant que les actes s'enregistrent à mesure. ⚠ Ce n'est **pas** le même message
   que « Aucune entrée pour ce filtre » — vérifier les deux (poser un filtre de date sur une année
   sans activité donne le second, avec « le journal contient N entrée(s) »).
2. Le bandeau bleu doit rappeler que le journal est daté d'une **horloge** : changer l'année dans la
   barre du haut ne doit **rien** changer à la liste.

### b — l'acte qui a motivé la fonctionnalité

3. **Découper une promotion** (Groupes → répartition automatique, ou Bloc de rotation → partitions).
   ⚠ Ceci **écrit** dans la base : le faire sur une promotion qu'on veut réellement découper, ou
   prendre un point de sauvegarde d'abord.
4. Revenir au journal → une ligne **« Découpage en partitions »** (ou « Découpage en groupes »),
   datée, avec **votre nom** dans la colonne Auteur et les critères en puces
   (`levelId`, `partitionCount`, `strategy`, `reassign`).
   ⚠ C'est exactement la question du 02/09/2026 : avant, cette ligne n'existait pas.

### c — ce que le journal n'enregistre pas

5. Provoquer un **refus** : découper « Retrait » (le marqueur de retrait, année 0) → 400.
6. Revenir au journal → **aucune ligne** pour cette tentative. Le registre est la liste de ce qui a
   eu lieu, pas des essais.

### d — les filtres

7. Choisir un **type d'acte** → la liste se restreint, et la ligne au-dessus dit « N sur M au total
   dans le journal ». ⚠ Le sélecteur doit **continuer** à proposer les autres types avec leurs
   effectifs — comptés sur tout le journal, sinon il n'y a pas de retour en arrière.
8. **Du / Au** : poser la même date des deux côtés sur un jour où quelque chose s'est passé. ⚠ La
   borne haute est **inclusive** : « du 2 au 2 » doit rendre les entrées du 2, pas zéro.
9. Recharger → les filtres survivent (ils sont dans l'URL).

### e — l'auteur

10. Une entrée écrite par une tâche planifiée (`BACKUP_POINT_CREATED` du minuteur) doit afficher
    « **système** » et non un blanc. Une entrée dont le compte n'existe plus doit afficher
    « **non résolu** » avec l'identifiant brut dans l'infobulle. ⚠ Les deux sont des faits
    différents, et un blanc les confondrait.
11. Un acte destructeur (« Partitions supprimées », « Groupe vidé ») doit ressortir en **rouge**.

### Relevé du 04/09/2026 — partiellement exécutée

| étape | résultat |
|---|---|
| 1-2 · la page, l'année sans effet | ✅ 11 entrées, ordre décroissant, libellés français, actes destructeurs en rouge |
| — · l'auteur | ✅ résolu sur les 11 lignes |
| 7 · filtre par type | ✅ « 1–1 sur 1 entrée(s) · **11 au total dans le journal** », les autres types restent proposés avec leurs effectifs |
| — · les deux vides | ✅ « Le journal est vide » ≠ « Aucune entrée pour ce filtre » |
| 9 · rechargement | ✅ les filtres sont dans l'URL |
| 8 · **filtre de dates** | ✅ **vérifié après redémarrage** — « du 03/09 au 03/09 » rend bien **3** entrées, dont celle affichée « 03/09/2026 00:16 » |

**Deux défauts trouvés, tous deux corrigés :**

1. ⚠ **`stages : [object Object],[object Object]…`** sur les cinq lignes de bloc de rotation — une
   métadonnée imbriquée rendue par `String()`. Corrigé : JSON sérialisé, tronqué, valeur entière en
   infobulle (vérifié à l'écran).
2. ⚠ **Le filtre de dates comparait en UTC ce que l'écran affiche en heure locale.** Trois entrées
   lues « 03/09/2026 », un filtre « du 3 au 3 » n'en rendait que **deux** : celle de 22:16 UTC,
   affichée « 03/09 00:16 », tombait hors de la fenêtre. Corrigé — les bornes sont des instants et
   c'est le client qui définit la journée. `PHASES.md` §20.1.

✅ **Étape 8 exécutée après redémarrage le 04/09/2026** : « du 03/09 au 03/09 » rend **3** entrées,
dont celle affichée « 03/09/2026 00:16 » — celle-là même que les bornes UTC faisaient disparaître.

**Étapes 3-6 (l'acte enregistré, le refus non enregistré) non exécutées** — elles écrivent dans la
base et sont couvertes par `AuditLogEndpointTests`.

---

## §44 — L'ordre des services d'un stage (session 43)

⚠ **Redémarrer l'AppHost d'abord.** La route `PUT /stages/{id}/allowed-services/order` n'existe pas
dans un processus antérieur à cette session, et la migration `StageAllowedServiceRank` doit être
appliquée. Le contrôle qui distingue « route absente » de « non authentifié » : `order` répond
**404** sur l'ancien processus et **401** sans jeton.

⚠ **Prendre un point de sauvegarde avant l'étape 5** — c'est la seule qui écrit des cellules.

**Ce qu'on vérifie** : que le 1ᵉʳ service de la liste reçoit bien les premiers groupes de la 1ʳᵉ
période, et que la répartition imprimée reste en **plages propres** (c'est tout l'intérêt par rapport
à une cellule retouchée à la main).

| # | Geste | Attendu |
|---|---|---|
| 1 | Fiche d'un stage réparti (3ᵉ MED 2026-2027 en a six) → carte « Services autorisés » | La liste est **numérotée 1, 2, 3…**, la pastille du 1ᵉʳ est pleine, et la phrase « Ordre de rotation : le 1ᵉʳ service reçoit les premiers groupes de la 1ʳᵉ période » est au-dessus. ⚠ L'ordre n'est **plus alphabétique** — il l'était avant, ce qui était un quatrième ordre ne correspondant à rien. |
| 2 | Comparer avec la grille de planning du stage, période 1 | Les groupes de plus petit numéro sont dans le service n° 1. C'est l'affirmation sur laquelle tout repose. |
| 3 | Flèche ↑ sur le service n° 3 | Il passe n° 2 immédiatement (optimiste), et le reste après le refetch. ⚠ **Aucune cellule ne bouge** : l'ordre est lu par la *prochaine* répartition. |
| 4 | Journal des actions → filtrer `STAGE_SERVICE_ORDER_SET` | Une ligne, avec l'auteur, la date, `serviceCount` et l'ordre envoyé. |
| 5 | Grille du stage → « Répartition auto. » sur la période 1 | Les premiers groupes sont maintenant dans le service que vous avez monté en tête. |
| 6 | Répartition annuelle de la promotion (téléchargement) | Les cellules restent des **plages entières** (« 21-27 »), jamais « 21-23, 25-27 ». C'est la différence avec une retouche de cellule, et c'est le point de la fonctionnalité. |
| 7 | Retirer le service n° 2 de la liste | Les numéros se **referment** — 1, 2, 3 sans trou. Pas de « 1, 3, 4 ». |
| 8 | Ajouter un service depuis la recherche | Il arrive **en dernier**, jamais en tête. |

**Le contrôle qui doit échouer** — sinon les précédents ne prouvent rien :

| # | Geste | Attendu |
|---|---|---|
| 9 | Ouvrir la fiche dans deux onglets, autoriser un service dans l'onglet A, puis réordonner dans l'onglet B (dont la liste est périmée) | **409** avec « … 1 service(s) autorisé(s) n'y figurent pas. Rechargez la fiche du stage… ». ⚠ Et **l'ordre sur disque est inchangé** — vérifier en rechargeant : une liste partielle n'est jamais complétée en silence. |

**Non piloté à la livraison** : tout ce tableau. Le serveur est couvert par 19 tests (dont 5 par le
vrai pipeline HTTP) et la morsure a été vérifiée en cassant l'ordre (2 échecs) puis la garde de
permutation (3 échecs). Ce qui n'a pas été fait, c'est le navigateur.

### §44 — **piloté pour de vrai le 05/09/2026**, fenêtre visible, clics réels

L'API a été redémarrée entre-temps, donc le correctif de la trace d'audit est en vigueur.

| # | Geste | Résultat |
|---|---|---|
| 1 | Fiche du stage « Médecine » (3ᵉ MED) | Liste **numérotée 1→14 dans l'ordre de rotation**, pastille pleine sur le 1ᵉʳ, phrase « le 1ᵉʳ service reçoit les premiers groupes de la 1ʳᵉ période » présente. ↑ **désactivée** sur la ligne 1 et ↓ sur la ligne 14 — lu sur le DOM (`disabled: true`), pas déduit de l'apparence. ✅ |
| 2 | Comparer à la grille P1 | Acquis par mesure (04/09) : pour chacun des six stages de la 3ᵉ MED, le service de rang 1 tient les plus petits numéros de groupe, en plages contiguës — Cardiologie rang 1 → groupes 1-8, rang 2 → 9-16, rang 3 → 17-23. ✅ |
| 3 | ↑ sur *Médecine C* (rang 3) | Passe rang 2 à l'écran **et sur la base** ; *Médecine B* descend en 3. Rangs contigus 1..14. ✅ |
| 4 | Journal des actions | **Deux lignes** `STAGE_SERVICE_ORDER_SET`, avec auteur, date, objet *Stage #1*, et l'ordre complet + `serviceCount : 14` en critères. ✅ ⚠ **Cette étape avait d'abord échoué** — voir le défaut ci-dessous. |
| — | ↓ pour revenir | Ordre **restauré à l'identique** : `2,4,5,8,23,28,29,81,117,123,125,131,132,133`, c'est-à-dire exactement l'état relevé avant de toucher à quoi que ce soit. |

⚠ **Le défaut trouvé, et il n'était visible que comme ça.** Au premier passage, le
réordonnancement a écrit ses rangs et **aucune entrée de journal**.
`AuditLogPipelineBehavior` met la ligne en attente **avant** le handler — c'est ce qui fait qu'un
acte refusé n'écrit rien et qu'un acte réussi s'enregistre dans la même unité de travail — et
`ExecuteAtomicallyAsync` ouvrait chaque tentative par `ChangeTracker.Clear()`, qui la mangeait.
Corrigé au niveau du helper (nettoyage à partir de la **deuxième** tentative, et ré-inscription de ce
qui venait d'amont), donc **tout `IAuditableCommand` transactionnel** est couvert. Épinglé par
`The_act_records_itself_even_though_the_write_opens_its_own_transaction`, morsure vérifiée en
remettant les deux lignes d'origine.

**Corrigé aussi en pilotant** : l'acte s'affichait `STAGE_SERVICE_ORDER_SET` en brut au milieu de
libellés français — `auditActions.ts` le nomme désormais « Ordre des services modifié ».

**Non piloté, et pourquoi :**

- **Étapes 7 et 8** (retirer / ajouter un service) — mesuré d'abord : **les 14 services du stage
  portent des cellules**, dont 6 sur le dernier. Retirer une autorisation laisserait des cellules
  publiées pointant vers un service que le stage n'autorise plus. Les deux chemins (`RemoveAsync`,
  `AppendAsync`) sont couverts par les tests de handler ; les jouer sur une promotion publiée n'en
  valait pas le prix.
- **Étape 5** (répartition auto.) — écrit des cellules pour toute la promotion. Point de sauvegarde
  d'abord, et c'est le clic de l'utilisateur.
- **Étape 9** (ordre périmé → 409) — couverte par un test d'intégration qui passe par le vrai
  pipeline HTTP ; la rejouer au navigateur demandait de manipuler le jeton, ce qui n'apprend rien de
  plus.

---

## §45 — La grille de planning dit ce qu'elle montre (session 44)

⚠ **Redémarrer l'AppHost d'abord.** Les trois champs (`declaredSlotCount`, `servedPeriodCount`,
`emptyGridNote`) et le drapeau `isPublished` par cellule n'existent pas dans un processus antérieur à
cette session. **Aucune migration** — la lecture seule change. Le contrôle : sur l'ancien processus
la réponse de `GET /stages/{id}/schedule` ne porte pas `summary.emptyGridNote` du tout (et non pas
`null`), donc l'écran se comporte exactement comme avant.

**Rien ici n'écrit** — c'est une lecture de bout en bout. Aucun point de sauvegarde n'est requis.

**Ce qu'on vérifie** : qu'un tableau vide dit *laquelle* des situations il montre, et qu'une cellule
publiée se voit, y compris celles qu'aucune clé étrangère ne nomme.

| # | Geste | Attendu |
|---|---|---|
| 1 | Barre de navigation → année **2024-2025**, puis n'importe quel stage → « Grille de planning » | Le tableau est vide **et la phrase le dit** : « … alors que N période(s) y ont été servies : ces rotations viennent de l'historique importé… ». ⚠ C'est le symptôme rapporté. La phrase doit **déconseiller** de poser un axe, pas l'inviter. |
| 2 | Même année, un stage qui n'a jamais rien servi | La phrase change : « Aucun créneau n'est posé… c'est un axe (bloc de rotation) qui les crée ». Aucune mention d'historique. |
| 3 | Année **2026-2027** → 6ᵉ MED (aucun roster à ce jour) → grille d'un de ses stages | « … ce stage n'a aucune cohorte sur cette année : la promotion doit être découpée et ses cohortes provisionnées ». ⚠ Surtout **pas** « lancez la répartition » — c'est le second geste. |
| 4 | 4ᵉ MED 2026-2027 → grille de *Pédiatrie* (répartie) | **Aucune phrase.** La note est muette dès qu'une cellule existe : une alerte qui s'affiche quoi qu'il arrive est du bruit, et le bruit se congédie. |
| 5 | Sur cette même grille, filtrer sur une partition qui n'a aucune cellule | Toujours **aucune phrase** : le vide à l'écran est le fait du filtre, et le compteur « 0 cohorte(s) » le dit déjà. |
| 6 | 3ᵉ MED 2026-2027 → grille d'un stage **publié** | Chaque cellule publiée porte une **petite fusée verte** avant le nom du service, et l'infobulle native dit « Cellule publiée : des périodes ont été matérialisées à partir d'elle. » |
| 7 | Passer la souris sur la croix rouge d'une de ces cellules | Elle est **désactivée**, avec « Cellule publiée : dépubliez la cohorte avant de la retirer. » ⚠ Le serveur refusait déjà ; ce qui change est qu'on ne l'apprend plus par un toast rouge. |
| 8 | **Le cas qui compte** — un stage `SingleService` publié sur plusieurs colonnes (Gynécologie Obstétrique 2026-2027 : 3 colonnes par cohorte) | Les **trois** cellules du pli portent la fusée, pas seulement la première. C'est exactement ce que la FK ne pouvait pas dire : 363 cellules couvertes, 121 nommées par la clé. |

**Le contrôle qui doit rester vrai** — sinon l'étape 8 ne prouve rien :

| # | Geste | Attendu |
|---|---|---|
| 9 | Sur la même grille, une cohorte non publiée | Ses cellules n'ont **pas** de fusée et leur croix reste active. Un marqueur qui s'allume partout ne distingue rien. |

**Non piloté à la livraison** : tout ce tableau. Le serveur est couvert par sept tests plus un cas de
traduction, et la morsure a été vérifiée dans les deux sens — le drapeau relu depuis la FK fait
tomber le cas de la cellule de queue et lui seul ; la note lue sur la sélection filtrée fait tomber
le cas du filtre et lui seul. Ce qui n'a pas été fait, c'est le navigateur.

### §45 — **piloté le 05/09/2026**, fenêtre visible, DOM lu

**Ce qui a été fait tourner, et sur quoi.**

| # | Écran | Mesuré |
|---|---|---|
| 1 | **CHIRURGIE (6ᵉ MED), année 2024-2025** — 103 cohortes, 0 créneau | La phrase s'affiche au-dessus du tableau : « Aucun créneau n'est posé pour ce stage sur cette année, alors que **627** période(s) y ont été servies : ces rotations viennent de l'historique importé… poser un axe sur une année déjà servie ne reconstituerait pas ce qui a eu lieu. » ⚠ **627 est exactement le compte en base** pour (stage 16, 2024-2025). C'est le symptôme rapporté, nommé. |
| 4 | **Gynécologie Obstétrique 2026-2027** (363 cellules) | **Aucune alerte** dans le DOM. La note se tait dès qu'une cellule existe. |
| 6 | idem | **75 cellules sur 75** portent la fusée et l'infobulle « Cellule publiée : des périodes ont été matérialisées à partir d'elle. » (25 lignes × 3 colonnes = la page entière). |
| 7 | idem | **75 croix sur 75 désactivées**, avec « Cellule publiée : dépubliez la cohorte avant de la retirer. » |
| 8 | idem — **le cas qui compte** | Les **trois** cellules de chaque pli sont marquées. ⚠ Contrôle SQL sur *cette page précise* : **75 cellules, 75 couvertes, 25 nommées par la FK**. Un marqueur bâti sur la clé aurait montré 25 cellules publiées et **laissé 50 cellules publiées passer pour libres**, sur un seul écran. |
| 8b | idem, **page 5** (Groupe 101+, dernière page) | 63 cellules, **63 marquées**, 0 croix active. Le drapeau n'est pas un artefact de la première page — 4 × 75 + 63 = **363**, le compte de couverture en base. |
| 9 | **Pédiatrie (4ᵉ MED) 2026-2027** — répartie, non publiée | **50 cellules, 0 marquée, 50 croix actives**, aucune alerte. Le marqueur distingue ; il ne s'allume pas partout. Vérifié aussi en page 2 (Groupe 26+) : même résultat. |

**Trois lignes du tableau n'ont pas pu être pilotées, et ce n'est pas un oubli — l'état n'existe pas
dans cette base :**

- **Étape 2** (aucun créneau *et* rien de servi) : **aucun couple (stage, année) de la base** n'a des
  cohortes, zéro créneau et zéro période — mesuré, 0 ligne. Tout stage qui a des cohortes une année-là
  a aussi les rotations importées de cette année.
- **Étape 3** (un axe posé sur une promotion **sans cohorte**) : ⚠ **le bouton « Grille de planning »
  n'est rendu que si le stage a des cohortes cette année-là** (`yearCohorts.length > 0`), donc la
  grille est inatteignable dans cet état depuis cet écran. Ce n'est pas grave — la carte « Cohortes »
  de la fiche affiche déjà « Aucune cohorte pour ce stage », c'est-à-dire la même réponse au même
  endroit — mais la phrase serveur, elle, reste juste pour tout autre appelant.
- **Étape 5** (filtrer sur une partition sans cellule) : **aucune partition de 2026-2027** n'a de
  cohortes sans cellules — mesuré, 0 ligne. La règle « la note parle du stage, pas du filtre » est
  donc épinglée par le test et par lui seul.

**Rien n'a été écrit** : sept ouvertures de grille, deux changements d'année, deux paginations. Aucun
`POST`, aucun point de sauvegarde nécessaire.

---

## §46 — Le chef nommé sur la liste des services et dans le portail étudiant (session 44)

⚠ **Redémarrer l'AppHost d'abord.** `ServiceSummaryResponse.ChefAttribution` n'existe pas dans un
processus antérieur. Le contrôle : sur l'ancien processus la colonne « Chef de service » affiche
**« ? »** (repli voulu — *inconnu* n'est pas *personne*), et non un nom ni un tiret.
**Aucune migration**, **aucune écriture** : deux lectures.

**Ce qu'on vérifie** : que les cinq écrans qui nomment un chef nomment **le même**, et que les trois
façons de ne nommer personne restent distinctes.

| # | Geste | Attendu |
|---|---|---|
| 1 | Infrastructure → onglet **Services** | La colonne « Chef de service » n'est plus « — » partout : ~140 des 148 services portent un nom, suivi d'une pastille jaune **« note »**. C'est le défaut rapporté, à l'envers. |
| 2 | Survoler la pastille « note » | « Nom repris de la note d'import — non daté. Désignez un chef de service pour que l'attribution soit datée. » |
| 3 | Ouvrir la fiche d'un de ces services (clic sur le nom) | ⚠ **Le nom en tête de fiche est exactement celui de la ligne.** C'est l'affirmation que toute cette phase existe pour tenir : deux écrans d'une même faculté ne doivent pas nommer deux personnes. |
| 4 | Chercher **Pédiatrie1** (ou Pédiatrie2) — les deux seuls services portant une affectation | La ligne affiche le nom de la note + « note », et la fiche ajoute « Un chef est pourtant **rattaché** à ce service ». La liste ne le répète pas : la place manque, et l'infobulle de la pastille le dit. |
| 5 | Un service sans note **et** sans rattachement | « — ». Pas « ? », pas « rattaché, non nommé ». |
| 6 | **Portail étudiant** → un stage → le service → carte « Chef de service » | Le nom s'affiche, avec **« D'après la fiche du service »** en dessous. ⚠ Pas de « Dr. », pas d'avatar à initiales, pas de grade : il n'y a aucun employé derrière ce nom. |
| 7 | Comparer avec la répartition annuelle que l'étudiant peut télécharger | **Même nom.** C'était le symptôme : la page disait « aucun chef de service désigné » pour un service dont le document nommait le chef. |

**Le contrôle qui doit rester vrai** :

| # | Geste | Attendu |
|---|---|---|
| 8 | Un service dont la fiche ne porte **aucune** note, côté étudiant | « Aucun chef de service désigné » — et **« Non communiqué »** si un chef y est rattaché. Les deux phrases sont différentes parce que les deux situations le sont. |

**Non piloté à la livraison** : tout ce tableau. Trois tests couvrent le serveur, dont celui qui
compare la ligne de liste à la réponse de la fiche ; la morsure a été vérifiée (la liste remise à
nommer la FK fait tomber 3 tests). Ce qui n'a pas été fait, c'est le navigateur.

### §46 — **piloté le 05/09/2026**, DOM lu, API redémarrée

| # | Mesuré |
|---|---|
| 1 | Page 1 des services : **14 lignes sur 15 nomment un chef**, chacune suivie de la pastille **NOTE** ; zéro « ? », donc le champ est bien servi. La 15ᵉ (« Chirurgie », Hôpital Azzamouri) affiche « — » : ni note ni rattachement. C'est le défaut rapporté, à l'envers — la colonne lisait « — » sur les 148 lignes. |
| 3 | ⚠ **L'affirmation centrale.** « Cardiologie » (HMIMV) affiche **Pr.A.Benyass** sur la ligne ; sa fiche affiche **Pr.A.Benyass**. Deux écrans, un nom. |
| 4 | **Pédiatrie1 → Pr.N.Elhafidi**, **Pédiatrie2 → Pr.A.Mdaghri Alaoui** — les noms de la note, *pas* l'affectation. La fiche de Pédiatrie1 ajoute « Un chef est pourtant rattaché à ce service… » et l'Historique montre la tenure **en cours de Youssef Alaoui**, le compte de test. La liste ne répète pas la phrase : l'infobulle de la pastille la porte. |
| 5 | « Pédiatrie » (Hôpital Azzamouri) : « — ». Pas « ? », pas « rattaché, non nommé ». |
| — | **Page 5** de la liste : 15 lignes, 14 nommées, 0 « ? ». La résolution est bien par page et pas seulement sur la première. |

**Le repli a été vérifié *avant* le redémarrage** : sur le processus antérieur au champ, la colonne
affichait **« ? »** sur chaque ligne — inconnu, et non « personne ». C'est le contrôle que la page ne
retombe pas silencieusement sur l'ancienne lecture.

**Portail étudiant — piloté le même jour**, sous une vraie session `Student` (« Wail », 5ᵉ MED
2026-2027), par le chemin réel : Tableau de bord → *Gynécologie Obstétrique* → son affectation.

| # | Mesuré |
|---|---|
| 6 | **Gynécologie Obs A** (Hôpital des Orangers, service 53) : la carte affiche **Pr.M.H.Alami** et, dessous, **« D'après la fiche du service »**. Pas de « Dr. », pas d'avatar à initiales, pas de grade — il n'y a aucun `Employee` derrière ce nom. Le bandeau « CHEF DE SERVICE ASSIGNÉ » revient, lui aussi lu sur l'attribution. Avant, cette page disait « aucun chef de service désigné ». |
| 7 | La description du service en base est « Responsable (source) : Pr.M.H.Alami » — **le nom imprimé est exactement celui que la répartition et l'export tirent du même annuaire**. |
| 8 | **Chirurgie (Hôpital Azzamouri, service 139)**, sans note ni rattachement : aucun bandeau, et la carte dit **« Aucun chef de service désigné »**. Le marqueur distingue. |
| 8b | ⚠ **Le cas qui compte pour un étudiant — Pédiatrie1 (service 45)** : la page nomme **Pr.N.Elhafidi**, et **« Youssef Alaoui » n'apparaît nulle part**. Le compte de test rattaché ne fuit pas vers l'écran de l'étudiant, ce qui est précisément la raison d'être de `ServiceChefPolicy.SourceNoteOnly`. |

⚠ **« Non communiqué » reste impilotable** : il faudrait un service **rattaché et sans note**, et il
n'en existe aucun dans la base — les deux seuls services rattachés portent tous deux une note. Cette
branche ne tient que par le test.

**Rien n'a été écrit** : une recherche, deux ouvertures de fiche, une pagination.

---

## §47 — Changement de groupe et échange, sans trace (session 45)

⚠ **À faire après avoir pris un point de sauvegarde** (« Sauvegardes » → « Prendre un point »). L'acte
est irréversible au sens qui compte : le groupe d'origine n'est écrit nulle part sur le dossier après
coup, seul le journal des actions le garde. Ce n'est pas un acte de masse, mais c'est le premier essai
sur des données réelles.

**Non piloté à la livraison.** La session ouverte était une session **étudiant** et l'espace admin
répond 403 comme il doit ; la base étant celle de la faculté, l'acte n'a pas été exécuté pour
vérifier. Tout ce qui suit reste à faire.

### Où

`Admin → Groupes → (un groupe de 2026-2027) → ligne d'un étudiant`. Deux icônes **orange** nouvelles à
côté du transfert (bleu) et de la délocalisation (turquoise) :

| icône | action |
|---|---|
| `IconUserEdit` | **Changer de groupe** — correction sans trace |
| `IconArrowsExchange` | **Échanger** — deux étudiants permutent leurs groupes |

### 1 · Ce que la fenêtre doit dire avant tout

Ouvrir « Changer de groupe ». L'encart orange doit expliquer que **ce n'est pas un transfert**, que
rien ne restera sur le dossier, que le groupe d'origine ne sera plus indiqué nulle part *sauf dans le
journal*, et que l'action sera refusée si les rotations ont commencé. Le bouton reste **désactivé**
tant que la case « Je comprends… » n'est pas cochée.

### 2 · La liste des groupes proposés

⚠ Elle doit contenir **uniquement des groupes de la même promotion**, jamais le groupe courant et
jamais « Non réparti ». C'est la vérification la plus utile de l'écran : le serveur refuse les trois
cas, mais une option qui ne peut produire qu'un refus est une option qu'il faut essayer pour
apprendre qu'elle n'en est pas une. Chaque ligne indique l'effectif du groupe, ce qui est le chiffre
sur lequel on choisit.

### 3 · Le changement lui-même

Choisir un groupe, cocher, valider. Le message de succès doit nommer **le groupe d'origine et celui
d'arrivée**, le nombre d'affectations déplacées, le nombre de périodes reconstruites, et — s'il y en a
— les périodes hors grille conservées. ⚠ C'est le seul endroit de l'application où le groupe d'origine
sera affiché ; après cela, seul le journal.

À vérifier ensuite :

- **la fiche du groupe de départ** ne le liste plus, **celle du groupe d'arrivée** le liste ;
- **le dossier de l'étudiant** (`Admin → Étudiants → …`) ne montre **aucune** ligne « transfert de
  groupe » dans son historique — c'est toute la fonctionnalité ;
- **ses stages** nomment les services du **nouveau** groupe, pas de l'ancien ;
- **le journal des actions** (`Admin → Journal`) contient une entrée `STUDENT_GROUP_CHANGED` à son
  nom, avec l'auteur et l'heure.

### 4 · Le refus qui doit tomber

Refaire la manœuvre sur un étudiant de **2025-2026** (année entièrement démarrée et close : 17 752
périodes, toutes démarrées). Le refus doit nommer les quatre chiffres — périodes, démarrées,
évaluations, journées de présence — et **désigner le transfert**. Il n'y a volontairement aucune case
« forcer ».

⚠ Vérifier ensuite que **rien n'a bougé** : l'étudiant est toujours dans son groupe, et le journal ne
contient **aucune** entrée pour cette tentative (un acte refusé n'écrit rien).

### 5 · L'échange

Sur un groupe de 2026-2027, icône « Échanger » : choisir un **groupe**, puis un **étudiant** de ce
groupe (la liste se charge à l'ouverture ; la recherche se déclenche à partir de 2 caractères).
Valider. Les deux fiches de groupe doivent avoir **échangé** exactement un étudiant chacune, et les
effectifs doivent être **inchangés** — c'est la raison d'être de l'échange.

⚠ Le cas qui compte : tenter un échange où **l'un des deux** a une rotation démarrée. Le refus doit
tomber et **aucun des deux** ne doit avoir bougé — pas même celui dont la moitié était valide.

### 6 · Ce qui n'est pas pilotable par les données

- **« Le groupe d'arrivée ne fait pas ce stage »** : tous les rosters d'une promotion portent
  exactement les mêmes cohortes (mesuré le 06/09/2026), donc ce refus ne peut pas être atteint sans
  fabriquer la situation. Il ne tient que par le test.
- **« Il tient déjà une affectation dans la cohorte d'arrivée »** : il faudrait une revalidation posée
  à la main dans le groupe cible.

### §47 — **piloté pour de vrai le 06/09/2026**, session admin, quatre actes exécutés puis annulés

Point de sauvegarde pris **avant** (« Avant essai changement de groupe », 21.4 Mo, schéma
**COMPATIBLE**, 06/09/2026 11:03) — et la bannière disait justement, avant lui, que le dernier point
avait été pris sous une autre migration. C'est le `SchemaChanged` de la Phase 18 sur données réelles.

**Le sujet : Wail Aabaybou** (CNE E137200311), *Groupe 1 — Cinquième Année Médecine* 2026-2027, dont
l'état a été relevé en base **avant** l'essai : 7 affectations, 7 périodes, 7 memberships, 1 ligne
d'historique (`StatusChange` du 02/09), et — le cas qui compte — sa période de *Gynécologie
Obstétrique* est un run `SingleService` **couvrant 3 cellules**.

| étape | résultat |
|---|---|
| 1 · la fenêtre | encart orange complet, bouton **désactivé** tant que la case n'est pas cochée ✓ |
| 2 · la liste des groupes | commence à « Groupe 2 », **toutes** Cinquième Année Médecine, chacune avec son effectif. Rechercher « Non r » → **aucune option** ; « Troisi » → **aucune option** ✓ |
| 3 · le changement | `POST` 200. En base : pointeur 4121 → **4122**, les 7 cohortes passées à leurs homologues du Groupe 2 (15183→15184, 15197→15198, …), les 7 cellules idem, **couverture Gynéco toujours à 3** — le run n'a pas éclaté en trois périodes ✓ |
| — la trace | `Histories` **toujours à 1**, **7** memberships (pas 14), toutes `EndDate` nulle, `StartDate` inchangée au 02/09, aucun motif ✓ |
| — le journal | `STUDENT_GROUP_CHANGED`, objet = son inscription, critères `targetGroupId : 4122` ✓ |
| 4 · le refus | sur *Groupe 1 — Sixième Année Médecine* **2025-2026** : « sur 6 période(s), 6 ont démarré, 0 portent une évaluation et 0 journée(s) de présence… Utilisez un transfert ». Effectifs inchangés (7/7) et le journal reste à **2** entrées — l'acte refusé n'écrit rien ✓ |
| 5 · l'échange | Wail ↔ Aya Acharai (Groupe 3). Les deux ont permuté, **les deux effectifs sont restés à 7**, chacun garde 7/7/7 et 1 ligne d'historique, et le journal porte **une seule** ligne `STUDENT_GROUPS_SWAPPED` avec la seconde inscription en critère ✓ |
| — retour | tout a été remis en place par les actes inverses. Vérifié en base : Wail retrouve **exactement** ses cohortes et cellules d'origine (15183/1501/couv=3, …), et sur toute l'année 2026-2027 il y a **0 membership close et 0 motif de transfert** ✓ |
| 6 · le dossier | la fiche affiche « GROUPE 1 — CINQUIÈME ANNÉE MÉDECINE » et **aucune ligne de transfert** ✓ |
| 7 · le journal | les quatre actes nommés en français, avec auteur et critères ; les tentatives refusées absentes ✓ |

⚠ **Un défaut trouvé à l'écran, et lui seul pouvait le trouver.** Le premier changement a répondu 200,
l'étudiant avait bougé en base, et **la page depuis laquelle l'acte venait d'être lancé continuait de
le lister**. `getGroupById` fournit le tag `group-<id>`, différent de celui de la liste, et la mutation
ne l'invalidait pas. Confirmé par le journal réseau : un refetch de `/api/groups`, **aucun** de
`/api/groups/4121`. Corrigé (les deux pages de groupe sont nommées, l'origine passée en champ
client-only), puis **revérifié sur l'acte suivant** : la liste est passée de 8 à 7 immédiatement, avec
le toast nommant le groupe d'origine. `PGSH.Frontend/CLAUDE.md` §1j.

⚠ **Second manque, même famille** : `STUDENT_GROUP_CHANGED` et `STUDENT_GROUPS_SWAPPED` n'avaient pas
de libellé dans `auditActions.ts` et se seraient lus en `SCREAMING_SNAKE` — sur les deux seuls actes
de l'application dont le journal est l'*unique* trace. Ajoutés.

**Reste non piloté** : « le groupe d'arrivée ne fait pas ce stage » et « il tient déjà une affectation
dans la cohorte d'arrivée » — aucun des deux états n'existe dans cette base (tous les rosters d'une
promotion portent les mêmes cohortes). Ces deux branches ne tiennent que par les tests.

---

## §48 — Un service qui refuse le dépassement d'effectif (session 46)

⚠ **La migration `ServiceOverCapacityPolicy` doit être appliquée**, c'est-à-dire la stack redémarrée
une fois avec le nouveau code. Sans elle l'API interroge une colonne qui n'existe pas et la liste des
services répond 500.

⚠ Rien ici n'est destructeur (le drapeau ne touche aucune ligne publiée), mais l'étape 4 **publie** :
elle se fait sur une cohorte qu'on accepte de publier pour de bon, ou pas du tout. Le résultat du
premier passage est en fin de section.

### 1 · L'état par défaut, qui est le plus important

`Admin → Infrastructure → Services`. Sur les **148** lignes :

- la colonne « Capacité » ne montre **aucun cadenas**. C'est ce qui est attendu : le drapeau vaut
  `true` partout, et un marqueur qui s'affiche sur toutes les lignes ne dit rien.
- Ouvrir n'importe quel service → la carte « Limite en vigueur » porte maintenant une ligne
  **« Dépassement d'effectif autorisé »** avec sa conséquence écrite : un administrateur peut publier
  au-delà en cochant la case.

### 2 · Le rendre ferme

Fiche d'un service → « Modifier le service et ses quotas ». Sous « Capacité totale », un interrupteur
**« Autoriser le dépassement d'effectif »**, coché.

- Le **décocher** : la description change et dit que la publication sera refusée au-delà, et une ligne
  orange rappelle que **les plannings déjà publiés ne sont pas touchés**.
- Enregistrer. La fiche doit afficher **« Dépassement d'effectif refusé »** (cadenas orange), et la
  ligne de la liste porter un cadenas dans la colonne « Capacité ».
- ⚠ Rouvrir le formulaire : l'interrupteur doit être **décoché**. S'il revient coché, le formulaire
  n'envoie pas le champ et l'a silencieusement rouvert — le défaut exact que la commande
  (`= true` par défaut) rend possible si le client se tait.

### 3 · Ce que la grille en dit, avant tout clic

Choisir un service **réellement saturé** (la 4ᵉ MED 2026-2027 en offre 138 : toutes ses paires
service × créneau dépassent, la pire à 196 contre 20) et le rendre ferme. Puis
`Stage → Grille de planning` :

- le bandeau rouge de saturation porte un badge orange **« dont N non forçable(s) »** ;
- « Voir le rapport » → les lignes du service ferme portent **« non forçable »** sous leur motif, et
  elles sont **en tête** du tableau ;
- « Publier tout » → la description de la case nomme les services concernés : *« ⚠ N affectation(s)
  portent sur 1 service(s) qui refusent le dépassement (…) : elles seront refusées quoi qu'il
  arrive. »*

### 4 · Le refus lui-même

Cocher « autoriser le dépassement d'effectif » et publier.

- **Un seul service ferme en cause** → un refus nommant la période, le service, les chiffres, et la
  phrase *« Ce service n'autorise pas le dépassement d'effectif : la case « autoriser le dépassement »
  ne lève pas ce refus. »*
- **Plusieurs cellules** → le refus agrégé, qui compte les deux moitiés séparément (« dont N sur un
  service qui n'accueille pas cette promotion **et** M sur un service qui n'autorise pas le
  dépassement ») et **ne propose plus la case** s'il ne reste rien de franchissable.
- **Rien ne doit être écrit** : le nombre de périodes de la promotion est inchangé.

### 5 · Le contrôle, sans lequel les étapes ci-dessus ne prouvent rien

- Rouvrir le service, **recocher** l'interrupteur, republier : la publication passe (avec la case
  cochée), ce qui montre que le refus venait bien du drapeau et non d'autre chose.
- Sur un service ferme **dans** son effectif, publier doit réussir **sans** cocher quoi que ce soit :
  un service ferme n'est pas un service fermé.

### ⚠ Ce qui n'est pas couvert par cet écran

« Charge des services » ne dit rien du drapeau — c'est délibéré (voir `PHASES.md` §24). Un service
ferme et saturé y apparaît comme n'importe quel autre service saturé.

### §48 — **piloté le 06/09/2026**, session admin, sur *Cardiologie B* (Maternité Souissi)

Migration appliquée au redémarrage et vérifiée en base : **148 services sur 148 à `true`** — la
valeur par défaut a bien porté, personne n'est devenu ferme sans l'avoir demandé.

**Le sujet.** *Cardiologie B* (Maternité Souissi, service 25, capacité 20) était le meilleur cas
possible : sa fiche annonce un pic de **118 étudiants** venant de **trois promotions à la fois**
(3ᵉ MED 56 · 4ᵉ MED 56 · 5ᵉ Pharmacie 6), et il est **premier dans l'ordre de rotation** de la
Cardiologie de 4ᵉ MED, promotion répartie mais **non publiée** (0 période).

| étape | résultat |
|---|---|
| 1 · la liste | **aucun cadenas** sur les 15 lignes de la page. C'est l'attendu : le marqueur ne dessine que l'état rare ✓ |
| 2 · la fiche | « **Dépassement d'effectif autorisé** », avec sa conséquence écrite ✓ |
| 3 · le formulaire | l'interrupteur bascule, la description passe au refus, l'encart orange « les plannings déjà publiés ne sont pas touchés » apparaît ✓ |
| — enregistrer | fiche : « **Dépassement d'effectif refusé** ». En base : **exactement 1 ligne** à `false`, nom / capacité / description intacts ✓ |
| — rouvrir | l'interrupteur revient **décoché** — le formulaire envoie donc bien le champ et ne rouvre pas le service en silence ✓ |
| — la liste | **un seul cadenas**, sur *Cardiologie B (Maternité Souissi)* ; l'autre *Cardiologie B (Ibn Sina)* reste nu ✓ |
| 4 · la grille | « 18 AFFECTATIONS SATURÉES » + badge « **DONT 6 NON FORÇABLE(S)** » ✓ |
| — le rapport | les **6** colonnes de *Cardiologie B* portent « **NON FORÇABLE** » et sont **en tête**, au-dessus de lignes au **même** dépassement (+98) qui, elles, sont forçables — l'ordre suit donc la forçabilité, pas les chiffres ✓ |
| — le dialogue | « ⚠ 6 affectation(s) portent sur 1 service(s) qui refusent le dépassement (**Cardiologie B**) : elles seront refusées quoi qu'il arrive. » ✓ |
| 5 · le refus | publication d'une cohorte dont l'unique cellule est ce service, **case cochée** : « La période 3 ne peut pas être publiée : le service « Cardiologie B » (09/11/2026 – 09/12/2026) accueillerait 118 étudiant(s) pour une capacité de 20. **Ce service n'autorise pas le dépassement d'effectif** : la case « autoriser le dépassement » ne lève pas ce refus. » ✓ |
| — rien écrit | 0 période sur la cohorte, **0 période sur toute la 4ᵉ MED** avant comme après ✓ |
| 6 · le contrôle | drapeau remis à `true` → **mêmes 18 saturations, badge disparu**. Le marqueur suit le drapeau et non les chiffres ✓ |
| — retour | **148/148 à `true`**, service 25 identique à l'état initial ✓ |

**Non piloté, délibérément** : la publication qui *réussit*. Elle écrirait 4 625 périodes réelles sur
une promotion dont la publication est l'item `0d` — c'est un clic de l'utilisateur, pas une
vérification. Le contrôle « le drapeau est bien la cause » a été fait autrement, ci-dessus : en le
remettant et en regardant le marqueur disparaître sur des saturations inchangées.

### ⚠ Deux défauts trouvés au clic, tous deux corrigés

**① Le mien, et l'écran seul pouvait le voir.** Basculer l'interrupteur faisait tomber la fenêtre dans
l'ErrorBoundary — « Un problème est survenu », rien d'autre. Cause :
`onChange={(e) => setForm((p) => ({ …, e.currentTarget.checked }))}` — React remet `currentTarget` à
`null` une fois l'événement propagé, et l'updater fonctionnel s'exécute au rendu **suivant**, donc il
lit `null`. Invisible au type-check (la propriété est typée non-nullable), au lint et aux tests.

**② Le même motif ailleurs, préexistant — et il cassait une page.** Cherché après coup : **cinq**
autres occurrences, trois champs de `CnpnVersionsPanel` et deux de `HolidaysPage`. Vérifié en
cliquant : « Date confirmée » faisait tomber la fenêtre « Ajouter un jour férié », c'est-à-dire que
**saisir un jour férié était impossible** — sur la page où les fêtes lunaires *ne peuvent qu'être*
saisies à la main. Les six sites sont corrigés et la règle est écrite dans
`PGSH.Frontend/CLAUDE.md` §1l.

**Au passage** : le refus s'affichait **deux fois** (« Conflit » puis « Erreur »), `errorMiddleware`
et le `catch` du composant disant la même phrase. C'est l'item 4 de `HANDOFF.md`, qui nomme
justement `ScheduleGridModal` ; corrigé sur ce chemin-là.

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

---

## §49 — Un stage fait hors faculté, pour toute une promotion (session 48)

⚠ **La migration `ExternalServices` doit être appliquée** — la stack redémarrée une fois avec ce code.
Sans elle l'API interroge une colonne `IsExternal` qui n'existe pas et la liste des services répond
500.

⚠ **L'étape 5 écrit sur de vraies inscriptions** : elle supprime les rotations planifiées des étudiants
qu'elle nomme et clôt leur stage. **Point de sauvegarde avant** (`pg_dump -Fc`, ou la bannière). Le
retour en arrière existe désormais (étape 7) mais il ne restaure pas les périodes : il rend les
étudiants à la répartition, qu'il faut republier.

⚠ **Aucun service externe n'existe dans la base.** Tout ce qui suit commence par en créer un ; tant
que personne ne l'a fait, rien de cette section n'est visible nulle part et c'est normal.

L'interface est faite (session 48) mais **n'a jamais été cliquée** : `tsc`, `eslint` et `npm run build`
sont propres, ce qui ne dit rien de ce que fait un clic — les deux défauts du 06/09 (l'`ErrorBoundary`
sur un interrupteur, et « Ajouter un jour férié » impossible) étaient invisibles aux trois.

### 1 · Créer le service hors faculté

`Admin → Infrastructure → Services → Ajouter`. Nom : **« Stage hors CHU — Kénitra »**, rattaché à
l'hôpital de votre choix (le rattachement ne sert qu'à l'arborescence).

- Dans le formulaire, l'interrupteur **« Service hors faculté »** doit exister et être **décoché** par
  défaut. Le cocher : la description doit dire que le service ne pourra pas être utilisé dans une
  rotation et que les étudiants n'y arrivent que par une délocalisation.
- Enregistrer, puis **rouvrir le formulaire** : l'interrupteur doit être **coché**. S'il revient
  décoché, le formulaire ne renvoie pas le champ — mais côté serveur l'omission vaut « inchangé », donc
  le service reste externe ; c'est l'écran qui ment, pas la base.

### 2 · Ce qu'il ne peut plus faire

- `Admin → Stages →` un stage `→ Services autorisés`. Le service Kénitra doit être **refusé** si on
  l'ajoute : *« Ce service est hors faculté : il ne peut pas figurer dans la rotation d'un stage. »*
- La grille de planning d'un stage → poser une cellule à la main sur Kénitra : **même refus**. ⚠ C'est
  l'étape qui compte le plus : 25 stages sur 27 n'autorisent aucun service, donc sur eux la liste
  blanche ne garde rien du tout et ce refus-ci est le seul.
- `Charge des services` : Kénitra ne doit **pas** y apparaître avec un taux d'occupation. Il n'a aucune
  cellule, donc il n'a aucune charge.

### 3 · Un étudiant, à la main

Fiche d'un étudiant → son stage → « Délocaliser ».

- **Sans motif** : refusé, et le message doit être *« Un motif est requis pour la délocalisation. »*
  (et non « Erreur lors de l'enregistrement »).
- **Sans dates**, sur un stage qui a des créneaux cette année : accepté, et la période enregistrée doit
  porter **les dates du stage** (première date du premier créneau → dernière du dernier).
- **Sans dates**, sur un stage sans créneaux (toutes les années importées) : refusé, et le message doit
  **nommer le stage et l'année** et demander les dates.
- **Avec une note /20** : la fiche de validation doit afficher la note, pas « validé ».

### 4 · L'aperçu de l'acte de masse

`Admin → Stages →` un stage `→ Délocaliser en masse` (le bouton n'apparaît qu'une fois la promotion
découpée en cohortes).

⚠ **Vérifier d'abord la liste déroulante « Groupes concernés »** : elle ne doit contenir que les
groupes de la **promotion de ce stage**, et **pas** « Non réparti ». Même contrôle sur « Transférer »,
« Changer de groupe » et « Échanger » depuis la fiche d'un groupe. C'est un ajustement du 06/09 et les
trois écrans croyaient déjà le faire — ils lisaient la promotion dans une liste plafonnée à 200
rosters sur 1 003.

Sélectionner **un roster entier**, ajouter deux ou trois noms, et coller une liste de CNE/Apogée dont
**un est faux** et **un appartient à une autre année**.

- L'aperçu doit distinguer les deux erreurs : *« Aucun étudiant ne porte ce CNE ni cet Apogée »* et
  *« Étudiant connu, mais sans inscription sur l'année sélectionnée »*. ⚠ Si les deux disent la même
  chose, la correction à faire n'est pas la même et l'écran ne le dit pas.
- Un étudiant nommé deux fois (par son nom **et** par son Apogée) ne doit produire **qu'une ligne**.
- **Rien ne doit être écrit.** Vérifier le compteur de périodes délocalisées avant/après :
  `SELECT count(*) FROM public."ServicePeriods" WHERE "IsDelocalized";`

### 5 · L'appliquer

- Le bouton doit porter **le nombre** de l'aperçu, pas le nombre de lignes sélectionnées.
- ⚠ **Le contrôle qui compte** : lancer l'aperçu, puis (dans un autre onglet) inscrire un étudiant de
  plus dans le roster, puis appliquer. L'acte doit être **refusé** avec les deux nombres nommés, et
  **rien écrit**. C'est la seule garde qui protège un acte tombant sur des gens dont personne n'a tapé
  le nom.
- Après l'application : les étudiants concernés ont **une seule** période, sur Kénitra, marquée
  « délocalisé », et leur stage est clos.

### 6 · Ce que la grille et la charge disent maintenant

- La ligne du roster affiche son effectif **et** « dont N hors CHU ». ⚠ Sans cette mention, un effectif
  plein devant des cellules qui ne chargent rien se lit comme un bug — et la personne suivante
  réaffecte tout le monde dans le CHU.
- `Charge des services` : le service **quitté** doit avoir baissé d'exactement N sur les créneaux
  concernés. C'est le chiffre pour lequel toute l'opération existe ; s'il n'a pas bougé, la
  délocalisation n'a rien soulagé.

### 7 · Le retour en arrière

- Sur un étudiant délocalisé **sans verdict** : « Annuler la délocalisation ». Sa période disparaît,
  son stage repasse « Planifié ». Republier la cohorte doit lui rendre sa rotation.
- Sur un étudiant délocalisé **avec** son verdict saisi : l'annulation doit être **refusée** —
  *« … annuler la délocalisation supprimerait la seule trace du stage. »*

### 8 · Les validations papier, en masse

`Admin → Stages →` le stage `→ Import des évaluations`, portée **« stage entier »**.

- Le canevas téléchargé doit **contenir** les étudiants délocalisés. (La portée « P1/P2 » ne les
  contient pas, et c'est voulu : une délocalisation ne suit aucun créneau.)
- Remplir seulement les lignes rentrées, importer : l'aperçu doit dire « Nouvelle note » pour elles et
  ne rien reprocher aux lignes laissées vides.
- Renvoyer le même fichier corrigé : les lignes déjà notées doivent dire **« Remplace la note déjà
  enregistrée »**.
- ⚠ Le mode « validation par objectif » n'est **pas** disponible à l'import ; il se saisit étudiant par
  étudiant.

---

## §50 — Une semaine d'examens appartient à une promotion (session 49)

> ✅ **Toutes les étapes exécutées au navigateur le 06/09/2026** sur la base vivante, en trois passages
> — voir `HANDOFF.md`, session 49, pour tous les chiffres. La base est revenue à son état d'origine à
> chaque fois (36 créneaux, 804 cellules, 5 598 couvertures, P4 = 18/01 → 26/02, 0 fenêtre).
>
> ⚠ **Et §50.5 a répondu autre chose que ce qui était écrit** : sur une promotion **publiée**, reposer
> l'axe est **refusé**. Voir l'étape ci-dessous, réécrite.

> **Phase 17.** Aucune étape ici n'est destructrice **sauf la 5**, qui repose un axe et remplace donc
> les créneaux et les cellules d'une promotion. Point de sauvegarde avant celle-là, et faites-la sur
> une promotion dont la grille peut être reposée.
>
> ⚠ **Le point à ne pas manquer : déclarer une fenêtre ne déplace *rien* par elle-même.** Ce n'est pas
> un oubli, c'est la décision de la phase — c'est l'axe posé *ensuite* qui l'enjambe, en jours
> ouvrables. Une fenêtre déclarée sur une grille déjà posée laisse chaque créneau où il est, et
> l'aperçu compte exactement ce qu'ils y perdent.

### 1 · La fenêtre existe et se déclare

`Admin → Calendrier` (« Jours fériés et fermetures »). Le panneau **« Suspensions de promotion »**
apparaît sous la table des jours fériés.

- Sur un processus antérieur à cette session la liste répond **404** : c'est le contrôle qui distingue
  « route absente » de « non authentifié » (sans jeton, elle répondrait **401**).
- « Déclarer une suspension » → le sélecteur de promotion ne doit **pas** proposer « Retrait ».
- Choisir la **3ᵉ année Médecine 2026-2027**, une fenêtre du **lundi au vendredi** d'une semaine que la
  promotion traverse, motif « Examens du premier semestre ».

### 2 · L'aperçu, avant d'écrire

Cliquer **« Aperçu »** dans la fenêtre de saisie.

- « Ouvrables perdus » doit valoir **5** (lundi → vendredi). Sur une fenêtre samedi–dimanche il doit
  valoir **0** — la fenêtre est réelle et ne coûte aucun jour de stage.
- « Créneaux traversés » doit être non nul si la promotion a une grille : l'axe 3MED est posé
  (item 0ac), donc une semaine de janvier en traverse **6** (un par stage).
- Le tableau par stage doit dire, pour chacun, combien de jours ouvrables la colonne perd et ce qu'il
  lui reste — à comparer avec la durée annoncée au catalogue, affichée à côté et **jamais** comme un
  verdict.
- ⚠ **Rien ne doit être écrit.** Fermer sans enregistrer, recharger la page : la liste est toujours
  vide.
- Sur une promotion **sans grille** (la 6ᵉ MED, item 0ai) l'aperçu doit dire en toutes lettres
  *« Aucun créneau ni aucune rotation ne traverse cette fenêtre »* — et non pas afficher un 0 nu. Les
  deux zéros veulent dire des choses opposées.

### 3 · Déclarer, et ce que le message dit

Enregistrer.

- Le message de succès nomme les dates et le nombre de jours ouvrables.
- Un second message doit apparaître **seulement s'il y a des créneaux** : *« N créneau(x) traversent
  cette fenêtre et gardent leurs dates… Reposez l'axe. »* Sur une promotion sans grille il ne doit
  **pas** apparaître.
- La ligne apparaît dans la table avec sa promotion, ses dates et « ouvrables perdus ».
- `Admin → Journal des actions` doit porter **`PROMOTION_PAUSE_DECLARED`**.

### 4 · Ce qu'elle ne fait pas — et le contrôle qui compte

- `Admin → Stages →` un stage de la 3ᵉ MED **→ Grille de planning** : les colonnes n'ont **pas
  bougé**. C'est voulu.
- `Admin → Rotation` sur la **4ᵉ** année : « Générer les fenêtres » avec la même date de départ et la
  même durée doit donner **exactement** les mêmes colonnes qu'avant. Une promotion voisine ne partage
  pas les examens de l'autre — c'est la raison d'être de toute la phase.

### 5 · L'axe posé ensuite l'enjambe — et ce qu'une promotion **publiée** peut réellement faire

`Admin → Rotation`, promotion **3ᵉ MED**, unité **« jours ouvrables »**.

- ⚠ Le bouton « Générer les N fenêtre(s) » doit être **désactivé tant qu'aucune promotion n'est
  choisie** : les colonnes sont posées sur *son* calendrier.
- Générer (**une lecture, n'écrit rien**) : la colonne qui contenait la semaine d'examens doit
  **enjamber** celle-ci — sur la 3ᵉ MED, C4 passe du 18/01 au **25/01** — en gardant le **même nombre
  de jours ouvrables** (30). Toutes les colonnes suivantes se décalent d'autant.
- Regénérer **sans rien changer** : dates **identiques**. Une fenêtre est *dérivée*, jamais *ajoutée*.
- Retirer la suspension et regénérer : l'axe doit revenir **exactement** sur l'axe stocké.

#### ⚠ Et le résultat qui a corrigé la phase — mesuré le 06/09/2026

Cliquer **« Simuler »** sur la 3ᵉ MED. Le bandeau dit « 804 créneau(x) déjà publiés — ce bloc ne peut
plus être redéfini » et **« Appliquer l'axe » est désactivé** : `ApplyRotationCycleCommand` refuse sur
`PublishedCells > 0`.

- **Donc « reposez l'axe » n'est pas un geste disponible sur une promotion publiée** — c'est-à-dire
  exactement celle qu'une fenêtre déclarée tardivement pénalise. Le rapport ne le prescrit plus : il
  affiche « Cellules publiées » et, quand ce nombre est non nul, dit que les jours sont perdus et que
  déplacer une colonne publiée n'existe pas encore (`PHASES.md` §17.1).
- **Le contrôle qui le prouve sans rien écrire** : « Durée réelle par stage » passe de « 30 – 30 » à
  **« 25 – 30 »** jours ouvrables dès que la fenêtre est déclarée. La grille garde ses dates et perd
  bien les 5 jours ; c'est le manque, affiché.
- **Vérifier ensuite que rien n'a été écrit** : 36 créneaux, 804 cellules, 5 598 couvertures, P4
  toujours 18/01 → 26/02.
- ⚠ **Reposer l'axe pour de vrai reste non exécuté**, et ne peut l'être que sur une promotion **non
  publiée** (dépublier d'abord détruirait notes et présences). Sur une telle promotion, l'acte écrit
  des cellules : **point de sauvegarde avant**.

### 6 · Corriger

Crayon sur la ligne → décaler la fenêtre d'un jour.

- Le message doit dire que les dates ont changé et nommer les créneaux couvrant **l'ancienne ou la
  nouvelle** période, comptés une seule fois.
- Rouvrir, ne changer que « Dates confirmées », enregistrer : **aucun** message sur les créneaux. Une
  case cochée sur des dates déjà justes ne coûte aucun jour.
- La promotion n'est **pas** modifiable en correction : une fenêtre appartient à celle qui l'a
  déclarée.

### 7 · Les refus

- Déclarer une **seconde** fenêtre pour la même promotion qui **touche** la première ne serait-ce que
  d'un jour → refus nommant l'existante. Un jour plus loin → accepté.
- La **même** fenêtre pour une **autre** promotion → acceptée. C'est le contrôle.
- Une fenêtre qui sort des dates de l'année universitaire → refus nommant l'année.
- Motif vide → refus, et **rien n'est écrit** (recharger pour le vérifier).

### 8 · Retirer

Corbeille sur la ligne.

- La confirmation doit dire que le retrait est **prospectif**.
- Si la fenêtre avait déjà commencé, un message le dit après coup : rien n'est rattrapé.
- `Journal des actions` doit porter **`PROMOTION_PAUSE_REVOKED`**.
- Regénérer l'axe de la promotion : les colonnes doivent revenir où elles étaient.

## §51 — « Vider » se limite à la promotion affichée (session 50)

> **Aucune étape ici n'est destructrice au sens des marques ou des présences** : vider ne touche que
> `Registration.AcademicGroupId` — le pointeur, jamais l'affectation, qui pend à la cohorte. C'est
> précisément pourquoi l'acte est **refusé** tant que la portée demandée porte des affectations : les
> laisser en place derrière des groupes affichés vides est le défaut que la garde existe pour empêcher.
>
> ⚠ **Redémarrage de l'AppHost obligatoire.** Un processus antérieur à cette session ignore
> silencieusement `levelId` — un paramètre de requête inconnu ne se lie à rien — et l'acte reste
> annuel, donc toujours refusé. Le contrôle qui distingue les deux : filtrer sur la **4ᵉ MED** et lire
> le libellé du bouton (voir §51.1).

**État de la base au 07/09/2026**, mesuré avant la correction — les nombres attendus ci-dessous :

| promotion 2026-2027 | rosters | inscrits | cohortes | affectations | périodes |
|---|---|---|---|---|---|
| **4ᵉ Médecine** | 116 | 925 | **0** | **0** | **0** |
| 3ᵉ Médecine | 134 | 933 | 804 | 5 598 | 4 665 |
| 5ᵉ Médecine | 121 | 842 | 847 | 5 894 | 5 894 |
| 5ᵉ Pharmacie | 71 | 212 | 142 | 424 | 848 |

### 1 · Le bouton dit sa portée

`Admin → Gestion des groupes → Liste des groupes`, année **2026-2027**.

- **Sans** filtre Niveau : le bouton orange dit **« Vider toute l'année »**, et son infobulle dit
  « toutes promotions confondues ».
- Filtre **Niveau = Quatrième Année Médecine** : il devient **« Vider la promotion »**, et l'infobulle
  nomme la promotion.
- ⚠ Si le libellé ne change pas, l'API tourne encore sur l'ancien build — rien de ce qui suit ne veut
  dire quoi que ce soit.

### 2 · L'année entière reste refusée, et c'est correct

Sans filtre, cliquer « Vider toute l'année » → confirmer.

- Refus **`AcademicGroups.YearRostersHaveAffectations`**, nommant **11 916 affectations** et
  **11 407 périodes**.
- Recharger : les effectifs des groupes sont inchangés. La garde ne doit **rien** écrire.

### 3 · La promotion planifiée est refusée en son nom

Filtre **Niveau = Troisième Année Médecine** → « Vider la promotion » → confirmer.

- Refus **`AcademicGroups.PromotionRostersHaveAffectations`**, nommant **« Troisième Année Médecine »**,
  **5 598 affectations** et **4 665 périodes** — *ses* nombres, pas ceux de l'année.
- ⚠ C'est l'assertion qui compte : un refus qui cite 11 916 ici voudrait dire que la portée du refus
  n'a pas suivi celle de l'acte, ce qui est exactement le défaut corrigé.

### 4 · La promotion libre se vide

Filtre **Niveau = Quatrième Année Médecine** → « Vider la promotion » → confirmer.

- Succès, et le message dit **925 étudiants retirés de leurs groupes**.
- La liste montre 116 groupes à **0 étudiant** ; les groupes eux-mêmes sont conservés.
- Les autres promotions sont intactes : refiltrer sur la 3ᵉ MED, les effectifs n'ont pas bougé.
- `Journal des actions` porte **`PROMOTION_GROUPS_EMPTIED`** (et **non** `YEAR_GROUPS_EMPTIED`), avec
  `levelId` en métadonnée.

### 5 · Re-découper, qui est la raison de tout ceci

Onglet **Répartition** (ou « Répartir automatiquement »), 4ᵉ MED, taille de groupe voulue.

- Les **925** inscriptions sont ramassées : `AutoArrangeGroupsCommand` ne prend que celles dont le
  groupe est nul, donc un seul étudiant resté rattaché serait un étudiant manquant du découpage.
- `Journal des actions` porte **`GROUPS_AUTO_ARRANGED`**.

### 6 · Le contrôle négatif

- Filtrer sur une promotion **sans aucun groupe** dans l'année → le bouton ne s'affiche pas
  (`totalCount > 0` le conditionne). Ce n'est pas un refus, c'est qu'il n'y a rien à vider.
- ⚠ Ne pas conclure de §51.4 que « vider marche » : ce que cette étape prouve, c'est que la garde a
  laissé passer. C'est §51.2 et §51.3 qui prouvent qu'elle mord encore, et il faut les deux.

## §52 — Le CNPN accepte enfin un nouveau stage, et le refus se lit (session 51)

⚠ **Prérequis : redémarrer l'AppHost.** Le processus en cours porte encore le plafond de page à 100 ;
le contrôle qui distingue « ancien processus » de « corrigé » est l'étape 1.

⚠ **Aucune étape ci-dessous n'écrit dans la planification.** Enregistrer un CNPN n'écrit que
`Curriculums` / `CurriculumStages`. L'étape 6 est une **décision**, pas un clic.

### 1 · Le contrôle qui dit sur quel processus on est

`Admin → CNPN`, programme **Médecine**, niveau **Troisième Année Médecine**, texte **1650.25**,
« Modifier le texte ». Onglet réseau ouvert.

- `GET /api/stages?levelId=3&pageSize=200` → **200**, huit stages dans `items`.
- Sur l'ancien processus il répond **400** avec
  « Page size must be between 1 and 100 » — et c'est exactement le symptôme d'origine.

### 2 · Le sélecteur offre les deux stages créés le 07/09

Dans « Ajouter un stage ».

- Le champ est **actif**, et la liste propose **Santé Publique** et **Simulation Médicale**.
- ⚠ Si le champ est grisé en disant « Tous les stages du niveau sont listés », c'est l'étape 1 qui a
  échoué — pas le CNPN. La phrase ne peut plus signifier « la requête a été refusée », mais elle
  reste vraie quand les huit stages sont déjà dans le tableau.

### 3 · Les ajouter, et vérifier ce que ça change

Ajouter les deux (coefficient et durée pré-remplis depuis le catalogue : **1** et **15 j.**),
« Enregistrer ».

- Succès, le bandeau du texte passe de **6** à **8 stage(s)**.
- ⚠ **Ne pas cliquer si l'étape 6 n'est pas tranchée** — lire la ligne 0ap de `HANDOFF.md` d'abord.

### 4 · Le repère du catalogue nomme le texte qui diverge

`Admin → Stages`, filtre **Troisième Année Médecine**, survoler le triangle orange sur **Chirurgie**.

- Colonne **Durée** : la bulle ouvre sur **« 2174.18 — durée différente du catalogue »**, puis liste
  `= 1650.25 (3ᵉ) : 30j` et `≠ 2174.18 (3ᵉ) : 66j`.
- Colonne **Coefficient** : la bulle ouvre sur **« 1650.25 — coefficient différent du catalogue »**,
  et liste `≠ 1650.25 : 1`, `= 2174.18 : 3`.
- ⚠ **C'est l'assertion de la session.** Le signalement était « je l'ai alignée dans le même CNPN et
  l'avertissement reste » : il restait parce qu'il désignait l'*autre* texte et l'*autre* chiffre.
  Une bulle qui ne nommerait aucun code veut dire que l'ancien composant est encore servi.
- **Santé Publique** et **Simulation Médicale** ne portent **aucun** triangle avant l'étape 3, et
  aucun après non plus (le texte reprend les chiffres du catalogue). Un triangle sur ces lignes-là
  serait un vrai désaccord à lire.

### 5 · Le repère disparaît sans rechargement de page

Toujours sur 1650.25, mettre le coefficient de **Chirurgie** à **3** dans le texte, enregistrer, puis
revenir sur `Admin → Stages` **sans recharger le navigateur** (navigation interne).

- Le triangle de la colonne **Coefficient** de Chirurgie a **disparu**.
- ⚠ Ce qui est testé est l'invalidation du cache, pas la valeur : avant la correction la page
  réaffichait les chiffres d'avant l'édition et le repère survivait à la modification qui le
  résolvait. Remettre **1** ensuite si l'on veut laisser le texte tel que la faculté l'a écrit.

### 6 · Ce qu'il faut décider avant d'aller plus loin (aucun clic)

L'axe de la 3ᵉ MED 2026-2027 porte **6 stages × 6 colonnes** de 30 jours ouvrables et **804 cellules
publiées**. Les deux nouveaux stages n'ont **aucun créneau**, et les durées du catalogue disent
maintenant 30 j. pour deux stages et 15 j. pour six.

- « Bloc de rotation » → « Appliquer l'axe » est **désactivé**, et
  `ApplyRotationCycleCommand` refuse sur `PublishedCells > 0`. C'est correct.
- Donc : soit la promotion finit l'année sur l'axe qu'elle a — chaque colonne de 30 j. sert un stage
  qui n'en annonce plus que 15, ce qui est du **mou** et non un manque (l'aperçu n'avertit que sur un
  déficit, et il a raison de rester muet) — soit c'est un démontage complet,
  [`docs/planning-rosters.md`](docs/planning-rosters.md), « Repartir de zéro ».
- ⚠ Un stage exigé par le texte et sans créneau est **dû et jamais servi**. C'est la décision de la
  faculté ; la noter ici plutôt que la prendre.

### 7 · Le refus lisible — ce qui est vérifiable ici, et ce qui ne l'est pas

✅ **Le contrôle qui compte est passif, et il est déjà donné par l'étape 1** : ouvrir l'éditeur du CNPN
ne fait plus apparaître **aucun** bandeau. C'était l'endroit exact où
« Données invalides · One or more validation errors occurred » se déclenchait, à chaque ouverture.

⚠ **Ne pas essayer de provoquer un 400 depuis un formulaire — les deux le bloquent avant l'envoi**,
et c'est voulu (garde côté client, cf. la règle maison sur les pré-contrôles) :

- `StagesPage.handleSave` **filtre** les objectifs sans libellé (`.filter((o) => o.label.trim())`),
  donc « ajouter un objectif au libellé vide » ne part jamais et ne peut rien refuser. *(Recette
  corrigée le 07/09/2026 : elle avait été écrite sans vérifier ce filtre.)*
- `CurriculumEditor` désactive « Enregistrer » tant qu'un coefficient < 1 ou une durée < 1 subsiste
  (`blockedReason`).

**Le chemin est donc couvert côté serveur**, où il est démontrable :
`StageEndpointTests.A_refusal_carries_a_message` affirme que le document de problème porte un
`errors[]` **au premier niveau** — c'est cette forme-là que le type client lisait au mauvais endroit.
La régression se verrait ici sous la forme d'un bandeau générique là où l'écran nomme aujourd'hui la
règle.

## §53 — « Supprimer les groupes » se limite à la promotion affichée (session 52)

⚠ **Prérequis : redémarrer l'AppHost.** Le processus en cours ne connaît pas `levelId` sur cette
route, et un paramètre de requête inconnu **ne se lie à rien** : l'acte resterait annuel sans que
rien ne le dise.

⚠ **C'est un acte destructeur** — il supprime les rosters **et leurs cohortes**. `pg_dump -Fc` avant,
même sur une base sans planification.

⚠ **Les nombres ci-dessous dépendent de ce qui est découpé au moment du test.** Mesuré le 07/09/2026
après la remise à zéro, 2026-2027 portait **0 roster** : il faut donc **découper deux promotions**
d'abord, sinon il n'y a rien à supprimer et le bouton ne s'affiche même pas (`totalCount > 0` le
conditionne).

### 1 · Préparer deux promotions, dont une seule habitée

`Admin → Groupes`, année **2026-2027**.

- Découper la **3ᵉ MED** et la **4ᵉ MED** (« Répartir automatiquement »).
- Puis, filtre **Niveau = Quatrième Année Médecine**, cliquer **« Vider la promotion »** → confirmer.
- La 4ᵉ MED affiche ses groupes à **0 étudiant** ; la 3ᵉ MED garde les siens.

### 2 · Le bouton dit sa portée

- Filtre **Niveau** posé → le bouton lit **« Supprimer la promotion »**, et son info-bulle nomme la
  promotion.
- Filtre **effacé** → il lit **« Tout supprimer »**, et l'info-bulle dit « toutes promotions
  confondues » et invite à filtrer.
- ⚠ Un bouton qui lit « Tout supprimer » avec un filtre posé veut dire que l'écran est l'ancien.

### 3 · La promotion habitée est refusée **en son nom, avec ses nombres**

Filtre **Niveau = Troisième Année Médecine** → « Supprimer la promotion » → confirmer.

- Refus **`AcademicGroups.HasStudents`**, dont la phrase nomme **« Troisième Année Médecine
  (2026-2027) »**, le nombre de groupes et le nombre d'étudiants qui y sont encore.
- ⚠ **C'est l'assertion de la session.** L'ancien message était
  *« One or more groups in this year have students assigned »* — l'année, pas la promotion, et aucun
  nombre. Un refus qui cite l'année ici veut dire que la portée de la garde n'a pas suivi celle de
  l'acte, ce qui est exactement le défaut corrigé.
- Recharger : les groupes des deux promotions sont intacts. La garde ne doit **rien** supprimer.

### 4 · La promotion vidée se supprime, et l'autre survit

Filtre **Niveau = Quatrième Année Médecine** → « Supprimer la promotion » → confirmer.

- Succès, le message dit combien de groupes ont été supprimés.
- Refiltrer sur la **3ᵉ MED** : ses groupes et ses effectifs sont **inchangés**. C'est le contrôle qui
  compte — c'est lui qui dit que la suppression n'a pas débordé.
- `Journal des actions` porte **`PROMOTION_GROUPS_DELETED`** (et **non** `YEAR_GROUPS_DELETED`), avec
  `levelId` en métadonnée. ⚠ L'acte n'écrivait **rien** au registre avant cette session : une ligne
  absente veut dire l'ancien processus.

### 5 · L'acte annuel existe toujours, et refuse toujours

Effacer le filtre → « Tout supprimer » → confirmer.

- Refus **`AcademicGroups.HasStudents`** nommant cette fois **l'année**, puisque la 3ᵉ MED est encore
  habitée. C'est correct : à portée annuelle, la garde doit voir toute l'année.

### 6 · Le contrôle négatif

- Vider **aussi** la 3ᵉ MED, puis « Tout supprimer » sans filtre → succès, et il ne reste aucun
  groupe sur l'année.
- ⚠ Ne pas conclure de §53.4 que « supprimer marche » : ce que cette étape prouve, c'est que la garde
  a laissé passer **au bon endroit**. C'est §53.3 qui prouve qu'elle mord encore, et il faut les deux.
- ⚠ **Non vérifiable ici** : que le `DELETE` final porte bien sur les rosters sélectionnés et non sur
  l'année. Le fournisseur en mémoire refuse `ExecuteDelete`, donc aucun test ne couvre cette ligne —
  c'est §53.4 (« la 3ᵉ MED est inchangée ») qui en est la seule preuve, et elle est manuelle.

---

## §54 — Le placement nominatif : la liste, le groupe, le service réservé (session 54)

⚠ **Prérequis : redémarrer l'AppHost.** La migration `NominativePlacement` ajoute trois colonnes, et
les routes `POST /groups/assign/bulk*` et `PUT …/placement-mode` n'existent dans aucun processus
antérieur à cette session.

⚠ **Le contrôle qui distingue « ancien processus » de « corrigé »** : `POST /api/groups/assign/bulk/preview`
répond **404** sur l'ancien, **401** sans jeton, **400** avec un corps vide sur le nouveau.

⚠ **Point de sauvegarde avant l'étape 4** — c'est la seule étape destructrice, et elle déplace des
inscriptions.

### 1 · Créer le groupe volontaire, avec son motif

`Admin → Groupes`, promotion visée, « Nouveau groupe ».

- Libellé « Kénitra 1 », **motif** « Volontaires Kénitra (GST) — formulaire du 12/09 ».
- ⚠ **Le numéro est attribué à la suite du plus haut existant.** Si plusieurs groupes volontaires sont
  créés, les créer **d'affilée** : `GroupNumberRanges` replie des numéros de roster, donc 48-60
  imprime « 48-60 » et les mêmes treize éparpillés impriment une pluie de nombres isolés.
- Rouvrir la fiche du groupe : le motif est **relu tel quel**. ⚠ Le vérifier explicitement — un
  résumé qui alimente un formulaire d'édition doit porter tout ce que ce formulaire réécrit, sans quoi
  l'édition suivante l'efface (c'est ce qui avait effacé la description de chaque hôpital).

### 2 · L'aperçu, avant tout écrit

Sur le groupe, « Affectation nominative ». Coller une liste mêlant **CNE et Apogée**, en minuscules,
plus une ligne inventée.

- L'aperçu montre une ligne par étudiant, **refus en tête**.
- La ligne inventée est **`Introuvable`** et porte l'identifiant tapé — c'est ce qui permet de la
  retrouver dans le fichier.
- ⚠ **Un étudiant d'une autre promotion est `Mauvaise promotion`, pas `Introuvable`** : ce sont deux
  corrections différentes. Un étudiant d'une autre année est `Autre année`.
- ⚠ **Rien n'est écrit.** Recharger la page des groupes : l'effectif du groupe cible n'a pas bougé.
  Un aperçu qui déplacerait quelqu'un serait une application sous un autre nom.

### 3 · Le refus qui compte

Prendre un étudiant dont une rotation a **commencé** et l'ajouter à la liste.

- Sa ligne est **`Déjà engagé`**, elle chiffre les périodes / commencées / évaluées / journées de
  présence, et elle **nomme le transfert** comme l'acte qui peut le faire.
- Le compte « seront affectés » **ne l'inclut pas**.

### 4 · Appliquer, et la garde qui n'est pas une case à cocher

⚠ **Point de sauvegarde pris.** Lancer l'aperçu, puis **depuis un autre onglet** ajouter un étudiant
de plus au groupe source, puis appliquer.

- Doit **refuser** en nommant les deux nombres (`RosterAssignment.CountMismatch`), et n'écrire
  **rien** — vérifier que l'effectif du groupe cible est inchangé.
- Relancer l'aperçu, appliquer : les étudiants applicables sont dans le groupe, les refusés sont
  restés où ils étaient.
- ⚠ **Un étudiant sans groupe reçoit ses affectations** (il *rejoint*) ; un étudiant venant d'un autre
  groupe **garde les siennes**, re-pointées, et son adhésion est **réécrite sur place** — donc son
  dossier ne montre aucun déplacement. C'est voulu : « changement de groupe » est une correction.
- `Journal des actions` porte **`STUDENTS_ASSIGNED_TO_ROSTER`**.

### 5 · Réserver les services de Kénitra

`Admin → Stages`, le stage concerné, ses services autorisés.

- Autoriser le service de Kénitra s'il ne l'est pas, puis le passer en **« Réservé »**.
- ⚠ Le tenter sur un service **non autorisé** doit refuser en disant de l'autoriser d'abord — pas
  « service introuvable », qui est l'autre cause.
- `Journal des actions` porte **`STAGE_SERVICE_PLACEMENT_MODE_SET`**.
- ⚠ **Rien de déjà placé ne bouge.** C'est la répartition suivante qui lit le mode, exactement comme
  l'ordre des services.

### 6 · Épingler, puis relancer la répartition — l'assertion de la session

Sur la grille du stage, placer à la main la cohorte du groupe volontaire dans le service de Kénitra,
pour les colonnes voulues. Puis « Répartir » **sur tout le stage, sans rien décocher**.

- Les cellules épinglées sont **toujours là**, sur le même service.
- Le retour annonce **« N cellule(s) épinglée(s) conservée(s) »**. ⚠ **C'est l'assertion.** Avant cette
  session la répartition les supprimait et réécrivait, en annonçant un `Assigned = N` parfaitement
  normal — aucun refus, aucun compte, rien à l'écran.
- **Aucune autre cohorte n'est placée à Kénitra.** ⚠ Sans l'étape 5 elles le seraient : autoriser un
  service est exactement ce qui le met dans le vivier.
- Le retour annonce aussi **« N service(s) réservé(s) »** : la capacité retenue quitte le plafond avec
  eux, donc « il manque N places » se mesure contre un plafond plus petit, **volontairement**.

### 7 · Le cas qu'il faut voir refuser

Réserver **tous** les services autorisés du stage, puis « Répartir ».

- Doit refuser en disant que **tous sont réservés** (`Schedule.AllServicesReserved`), et **non**
  « aucun service n'accueille cette promotion ». ⚠ Les deux envoient l'opérateur à deux écrans
  différents : le second lui ferait élargir des quotas qui n'ont jamais été l'obstacle.
- Remettre un service en « Rotation » ensuite.

## §55 — La fenêtre d'une délocalisation est celle du groupe (session 57)

> ⚠ **À dérouler sur une promotion dont l'axe est croisé** — c'est-à-dire dont deux partitions
> traversent le même stage à des périodes différentes. Sur un stage qui tourne sur une seule fenêtre
> pour toute la promotion, l'ancien calcul et le nouveau donnent le **même** nombre et l'écran ne
> prouve rien.
>
> Prérequis : un axe posé, une répartition arrangée (des cellules dans la grille), et un service
> externe au catalogue. Point de sauvegarde avant, comme pour tout acte de masse.

### 1 · Lire l'axe, pour savoir ce qu'on attend

Sur la grille du stage, noter pour **deux** groupes de partitions différentes la colonne qu'ils
occupent et ses dates. Exemple mesuré : partition A en **P3, 16/11 → 16/12** ; l'axe entier, lui, va
du **14/09 au 25/03**.

### 2 · L'aperçu, sans saisir de dates

« Délocaliser en masse », choisir le stage et le service externe, sélectionner **les deux groupes**, et
**laisser les dates vides** — c'est le chemin où PGSH les déduit, et le seul où le défaut vivait.

- La colonne **« Période »** porte, pour chaque ligne, **les dates de son propre groupe**. ⚠ **C'est
  l'assertion de la session.** Avant, chaque ligne portait l'axe entier — quatre mois pour un stage
  d'un mois.
- L'en-tête ne dit plus « du … au … » mais **« n périodes différentes selon le groupe »**.
- Un seul groupe sélectionné : l'en-tête **redonne** une paire de dates, et c'est le passage de ce
  groupe.

### 3 · Le cas qui doit prévenir

Sélectionner un groupe **sans cellule dans la grille** (non encore réparti).

- Sa ligne porte les dates de **tout l'axe** et un badge **« tout le stage »**.
- Un bandeau jaune compte les lignes concernées et dit quoi faire : répartir d'abord, ou saisir les
  dates réelles.
- ⚠ **Il ne doit pas refuser.** Délocaliser un stage que personne n'a planifié reste un cas soutenu ;
  ce qui a changé, c'est qu'il ne fait plus passer quatre mois pour une mesure.

### 4 · Les dates saisies gagnent

Refaire l'aperçu en saisissant des dates.

- **Toutes** les lignes les portent, l'en-tête aussi, et le bandeau jaune disparaît. La scolarité
  énonce ce que l'hôpital a fait ; PGSH ne déduit que faute de mieux.

### 5 · Appliquer, puis lire le dossier

Appliquer sur les deux groupes, **sans dates**.

- Sur la fiche d'un étudiant de chaque groupe, la période hors CHU porte **les dates de son groupe**,
  différentes de l'autre.
- ⚠ **Et sur le calendrier de l'année**, la bande de la partition ne dépasse plus sa dernière colonne
  — c'est le symptôme par lequel le défaut avait été signalé.
- ⚠ Aucune période de l'étudiant n'en chevauche une autre. C'est ce que l'axe entier fabriquait avec
  **chaque** autre stage de son année dès la publication.

### 6 · Le refus qui reste

Sur un stage **sans aucun créneau** (toute année importée), l'aperçu sans dates doit refuser en
nommant `Delocalizations.NoWindow` et demander les dates — jamais inventer une paire.

### §55 — **piloté le 10/09/2026**, session admin, sur un axe croisé monté pour l'occasion

⚠ **L'année 2026-2027 était vide** (0 roster, 0 cohorte, 0 créneau) et **aucun service externe
n'existait** : il a donc fallu monter le décor. Monté au plus petit — deux rosters de 3 étudiants, un
stage, deux colonnes — plutôt que de partitionner une promotion, puis **entièrement démonté** (voir
plus bas).

**Le décor** — 4ᵉ MED / Cardiologie, l'axe même où le défaut avait été mesuré :

| | |
|---|---|
| service externe | « Stage hors CHU — Kénitra (SMOKE 55) », créé par l'écran, bascule « hors faculté » |
| rosters | SMOKE55-A (partition **A**) et SMOKE55-B (partition **B**), 3 étudiants chacun |
| axe | **P1 16/11 → 13/12/2026** · **P2 22/02 → 25/03/2027** — soit un axe de **130 jours** |
| cellules | A en **P1 seulement**, B en **P2 seulement** — le croisement, sans lequel l'écran ne prouve rien |

⚠ **Les créneaux et les cellules ont été posés par l'API, pas par la souris** : le sélecteur de dates
ne répondait pas aux clics de l'automatisation (coordonnées décalées, cf. l'avertissement de §54).
C'est du **décor**, pas l'objet du test ; tout ce qui suit a été lu à l'écran.

**Ce que l'écran a dit** — aperçu sur les deux groupes, **dates laissées vides** :

- L'en-tête : « Stage hors CHU — Kénitra (SMOKE 55) · 2026-2027 · **2 périodes différentes selon le
  groupe (voir la colonne Période)** ». Il n'affiche plus **aucune** paire de dates.
- La colonne **Période**, ligne par ligne : les trois A à **2026-11-16 → 2026-12-13**, les trois B à
  **2027-02-22 → 2027-03-25**. ⚠ **C'est l'assertion.** Avant le correctif les six lisaient
  *16/11/2026 → 25/03/2027* — l'axe entier, quatre mois pour un stage d'un mois.
- **Un seul groupe sélectionné** : l'en-tête **redonne** une paire — « du 2027-02-22 au 2027-03-25 »,
  le passage de ce groupe.

**Le cas qui prévient** (cellule de A retirée le temps du contrôle, puis remise) :

- Bandeau jaune : « **3 étudiant(s)** sont datés par **tout le stage** et non par le passage de leur
  groupe : ces groupes n'ont pas encore de cellule dans le planning. Répartissez-les d'abord, ou
  saisissez les dates réelles ci-dessus. »
- Les trois lignes de A portent **2026-11-16 → 2027-03-25** + le badge **« TOUT LE STAGE »** ; celles
  de B gardent leurs quatre semaines, **sans badge**. Les deux réponses côte à côte, distinguables.
- ⚠ **Et le bandeau disparaît** dès que les deux groupes ont une cellule : il ne se déclenche pas
  quoi qu'il arrive, donc ce n'est pas du bruit.

**L'application, relue en SQL** (aucune écriture de vérification — lecture seule) :

| | écrit | l'axe faisait |
|---|---|---|
| les 3 de A | 16/11 → 13/12/2026, **28 jours** | 130 jours |
| les 3 de B | 22/02 → 25/03/2027, **32 jours** | 130 jours |

**Paires de périodes qui se chevauchent pour un même étudiant : 0.**

**Le calendrier** (`/academic-years/22/timeline`) : bande **A = 16/11 → 13/12/2026**, bande
**B = 22/02 → 25/03/2027** — chacune s'arrête à sa propre colonne. ⚠ **C'est le symptôme par lequel le
défaut avait été signalé** (« pourquoi Cardio va du 16/11/2026 au 25/03/2027 ? ») et il a disparu.

**Les dates saisies gagnent** : avec 05/04 → 02/05/2027, `distinctWindowCount` = **1**, l'en-tête
porte la paire, chaque ligne est `Named`, et le bandeau jaune est absent.

**Le refus tient** : sur *Pneumologie* (0 créneau), l'aperçu sans dates répond **409
`Delocalizations.NoWindow`** — « Aucun créneau n'est défini pour « Pneumologie » en 2026-2027 […]
Indiquez les dates de la délocalisation, ou créez d'abord les périodes du stage. » Il n'invente pas.

#### ⚠ Un défaut trouvé au pilotage, et corrigé

Le libellé sous « Période enregistrée » disait encore **« Laissez vide pour reprendre les dates
officielles du stage pour cette promotion »** — c'est-à-dire la description exacte de l'ancien
comportement, restée en place. Une phrase qui réapprend le défaut à l'opérateur. Remplacée par ce qui
est réellement calculé (le passage du groupe, et le repli sur tout le stage quand il n'y a pas de
cellule). ⚠ **Ni `tsc`, ni `eslint`, ni les 1 750 tests ne pouvaient le voir** : c'est du texte juste
au regard du compilateur.

#### Le démontage, et ce qu'il laisse volontairement

Tout le décor a été retiré : 6 délocalisations annulées (⚠ **une par une — c'est exactement l'item
`0aw`**), 4 cohortes réinitialisées, 2 créneaux supprimés, 2 groupes vidés puis supprimés, service
externe supprimé. Le balayage de résidu (11 contrôles) est **à 0**, l'année est revenue à
**0 roster / 0 cohorte / 0 cellule / 0 créneau**, les 6 839 inscriptions toutes détachées, et les
années passées intactes (105 626 périodes, 13 793 cohortes).

⚠ **Ce qui reste, par construction et non par oubli** : **15 entrées de registre** (`AuditLogs`) et
**18 lignes de dossier** (`Histories`) sur les 6 étudiants — 6 `Delocalization`, 6
`DelocalizationCancelled`, 6 `GroupTransfer`. Le registre est délibérément permanent ; et une
annulation **n'efface pas** la délocalisation du dossier, c'est écrit dans
[`docs/delocalization.md`](docs/delocalization.md) (« le dossier se lit comme une suite de choses qui
ont eu lieu »). Les effacer demanderait du SQL sur la base vivante et ferait **diverger le dossier du
registre** : c'est une décision de l'utilisateur, pas un nettoyage.


## ✅ §58 — La coupe, dans les deux unités (session 60, déroulé le 11/09/2026)

**Quatre points sur cinq vérifiés à l'écran ; le cinquième attend un redémarrage de l'AppHost.**
Décor monté sur trois promotions (4ᵉ Pharmacie 232, 1ʳᵉ Médecine 44, 7ᵉ Médecine 1 347) puis
démonté ; état final revérifié en base : **0 roster, 0 rattachement, 6 839 inscriptions**.
`pg_dump` avant : `backups/manual/20260911-pre-smoke58.dump`.

| # | ce qui a été vu |
|---|---|
| **1 ✅** | Par taille 20 sur 232 : « GROUPES CRÉÉS **12**, ÉTUDIANTS ASSIGNÉS **232**, ÉCHECS 0 » et, sous le résumé, « Répartition : **4 × 20, 8 × 19** ». L'avorton de 12 mesuré le 10/09 a disparu, à nombre de groupes inchangé. |
| **2 ✅** | Par nombre 12 sur la même promotion : **12 groupes, 232 étudiants, même forme**. La même coupe, demandée dans l'autre unité. |
| **3 ⚠** | *Le refus nommant les deux chiffres* — **non vérifiable dans cette session**. Il est revenu en « **Erreur 500 — Une erreur serveur est survenue** » : `MoreGroupsThanStudents` était construit avec `Error.Problem`, le seul membre que `CustomResults.GetStatusCode` ne nomme pas. Corrigé en `Error.Conflict` (409) et **pinné par `AutoArrangeUnitEndpointTests`** ; à rejouer **après redémarrage de l'AppHost**, l'API tournante portant encore l'ancien code. |
| **4 ✅** | La liste se remplit **sans rechargement** : 12 lignes, tailles `20,20,20,20,19,19,19,19,19,19,19,19`, et les deux boutons destructeurs présents. (C'est §57, revu par le même écran.) |
| **5 ✅** | 7ᵉ Médecine : `assignés 1 288, échecs 59, traités 1 347`, forme « 4 × 108, 8 × 107 » — et **chaque refus porte sa preuve** : « *Inscription signalée — stages antérieurs non validés : … Cardiologie (Quatrième Année Médecine), Pneumologie (Quatrième Année Médecine). Faites-les revalider, ou accordez une dérogation nominative.* » |

⚠ **Correction à une note de briefing** : sur les **86** signalements que porte 2026-2027, seuls
**59** écartent du découpage — tous des `OutstandingPriorStages`, tous sur la 7ᵉ Médecine. Les **27**
autres sont des `IncompleteStudentFile`, que `RegistrationHoldPolicy` classe **consultatifs** : ces
inscriptions-là sont découpées normalement. « 86 seront écartés » était faux ; le chiffre à lire est
celui du résumé, promotion par promotion.

**Le registre a tenu, et c'est la vérification de `A3`.** Les deux actes de démontage ont écrit, avec
leur ampleur : `YEAR_GROUPS_EMPTIED` → `{"rostersInScope": 36, "registrationsDetached": 1564}`,
`YEAR_GROUPS_DELETED` → `{"cohortsDeleted": 0, "rostersDeleted": 36}`. Avant la session 58, ces deux
actes n'écrivaient **rien du tout**. Et `GROUPS_AUTO_ARRANGED` distingue désormais `"askedBy": "size"`
de `"askedBy": "count"` — l'unité demandée est au journal, pas seulement son résultat.

### Les étapes, pour la prochaine promotion

**À voir avant de découper pour de vrai.** ⚠ Point de sauvegarde d'abord — c'est le premier acte qui
écrit sur 2026-2027.

1. **Par taille, sans avorton.** *Répartition automatique* → « Par taille de groupe » → 20 sur une
   promotion de 232. Le résumé doit annoncer **12 groupes** et, sous lui, « Répartition : **4 × 20,
   8 × 19** ». ⚠ Avant, c'était 11 × 20 et un groupe de 12 — et rien à l'écran ne le disait.
2. **Par nombre.** « Par nombre de groupes » → 12 sur la même promotion : mêmes 12 groupes, même
   forme. C'est la même coupe demandée dans l'autre unité.
3. **Un nombre impossible est refusé en nommant les deux chiffres.** Demander 400 groupes sur une
   promotion de 232 : un bandeau qui dit **400** et **232**, et **aucun groupe créé**.
4. **La liste se remplit sans rechargement** (c'est §57, et elle passe par le même écran).
5. **Les signalements restent écartés nommément** : sur une promotion qui en porte, le résumé compte
   les échecs et la liste dessous donne la preuve de chaque signalement.

## ✅ §57 — Le cache des rosters et le bandeau unique (session 59, déroulé le 11/09/2026)

⚠ **Quatre points sur sept vérifiés à l'écran** ; les trois autres ne sont pas des échecs, ils sont
**inatteignables tant que 2026-2027 n'a ni cohorte ni grille**. Décor monté et démonté sur la 4ᵉ
Pharmacie ; état final revérifié en base : **0 roster, 0 cohorte, 6 839 inscriptions**.

| # | ce qui a été vu |
|---|---|
| **1 ✅** | Après « Lancer la répartition », **sans recharger**, l'onglet *Groupes* affiche les 12 rosters à 20 étudiants **et** les deux boutons destructeurs. C'est le défaut mesuré le 10/09, qui affichait « Aucun groupe pour cette année » et faisait disparaître les boutons. |
| **2 ✅** | Transfert définitif d'un étudiant : l'en-tête de la page de départ passe de **20 à 19 étudiant(s)** sans rechargement, et l'étudiant disparaît de la liste. La liste des groupes montre au même instant **19** et **21**. |
| **3 ⚠** | *Rattachement à un groupe* — non joué. |
| **4 ⚠** | *Affectation en masse* — non joué. La moitié serveur (`sourceGroupIds`) est couverte par un test qui **mord**. |
| **5 ⚠** | *Délocalisation* — **impossible aujourd'hui** : l'année ne porte ni cohorte ni créneau, donc il n'y a rien à délocaliser. À reprendre après la pose des axes. |
| **6 ✅** | Refus de « Tout supprimer » sur des rosters habités : **un seul bandeau**, « Conflit », celui du serveur, nommant les 12 rosters et les 232 étudiants. Compté par observateur DOM : **un titre distinct**. |
| **7 ✅** | *Journal des actions* : **aucun code brut en SCREAMING_SNAKE** sur la page. « Transfert d'un étudiant » et les autres s'affichent en français. |

**Bonus vu au passage** : « Vider toute l'année » remet les douze compteurs à **0** dans la liste sans
rechargement — la même invalidation, sur un troisième acte.

⚠ **Résidu, une ligne** : le transfert définitif a laissé un `GroupTransfer` au dossier d'un étudiant
réel (Hiba Abadi, Groupe 1 → Groupe 2). **C'est la règle, pas un oubli** — un transfert garde sa
trace, c'est ce qui le distingue du « changement de groupe ». L'effacer demanderait du SQL sur la base
vivante : **décision de l'utilisateur**.

### Les étapes, pour la prochaine promotion

**À voir au prochain démarrage.** ⚠ **Ce sont deux défauts que seul l'écran peut confirmer** : ni
`tsc`, ni `eslint`, ni les 1 762 tests ne voient un cache périmé ou un bandeau de trop.

1. **Le découpage se voit tout de suite.** Onglet *Répartition automatique* → « Lancer la
   répartition » sur une promotion, puis **sans recharger** l'onglet *Groupes*. Les groupes doivent
   être là, avec « Vider » et « Supprimer ». ⚠ Avant, l'écran disait « Aucun groupe pour cette année,
   lancez d'abord la répartition automatique » **et** faisait disparaître les deux boutons.
2. **Le transfert vide la fiche de départ.** Depuis un groupe, transférer un étudiant vers un autre :
   le compte de la page **d'où l'on vient** doit baisser d'un, sans rechargement.
3. **Le rattachement remplit la fiche d'arrivée**, même chose depuis « affecter à un groupe ».
4. **L'affectation en masse vide *tous* les groupes d'origine** — pas seulement celui de la cible.
   ⚠ C'est le cas que le client ne pouvait pas deviner : les sources viennent du rapport du serveur.
5. **La délocalisation change la grille.** Délocaliser un étudiant, puis regarder la saturation du
   stage : elle doit baisser sans rechargement. Idem à l'annulation.
6. ⚠ **Un refus n'affiche qu'un seul bandeau.** Provoquer n'importe quel refus (« Tout supprimer » sur
   des groupes habités, par exemple) : **un** bandeau, celui du serveur. Avant : deux.
7. **Le journal n'affiche plus de SCREAMING_SNAKE.** Ouvrir *Journal des actions* : tous les codes
   doivent porter un libellé français, les destructeurs teintés.

⚠ **Vérifier 1 à 5 avec le journal réseau ouvert**, pas à l'œil : une page qui se rafraîchit *parce
qu'on y revient* masque exactement ce défaut. Ce qui le prouve est le refetch après le `POST`.

## ✅ §56 — Les actes de planification écrivent enfin au registre (session 58, déroulé le 10/09/2026)

⚠ **Déroulé sur la base vivante le soir même**, sauvegarde prise d'abord. **552 → 559 entrées :
sept actes, sept lignes, et le refus n'en écrit aucune.** Le détail des métadonnées, ce que la
passe n'a pas pu montrer, et les deux défauts trouvés au clic (`autoArrangeGroups` qui n'invalide
pas la liste — item 0at ; le découpage 11×20 + 1×12 — item 0az) sont dans `HANDOFF.md`, session 58.
**Les étapes ci-dessous restent la marche à suivre pour la prochaine promotion.**

**Ce qui a changé.** Dix actes destructeurs ou structurants de la planification écrivent maintenant
une ligne, et cette ligne dit **combien** — et surtout : deux actes qui se disaient audités depuis la
phase 20 n'écrivaient **rien du tout**.

⚠ **Point de sauvegarde avant.** Tout ce qui suit est destructeur, sur la base vivante.
⚠ **AppHost à redémarrer** — c'est du code serveur.

### Ce qu'il faut avoir sous la main
Une promotion de 2026-2027 portant au moins un axe et quelques cohortes. Si l'année est encore vierge
(0 roster, 0 cohorte), monter le décor minimal : découper une promotion, poser un axe sur un stage,
générer les cohortes. Le journal se lit sur **Administration → Journal des actions**.

### Les six choses à voir

1. **« Supprimer les groupes » laisse une ligne.** Sur une promotion vidée, cliquer « Supprimer les
   groupes », puis ouvrir le journal filtré sur `PROMOTION_GROUPS_DELETED`.
   - ⚠ **C'est le test qui compte** : avant cette session l'acte détruisait les rosters et le journal
     restait **vide**. Une ligne doit apparaître, avec un auteur nommé (pas un GUID) et une
     métadonnée portant `rostersDeleted` et `cohortsDeleted`.
   - Même chose pour « Vider les groupes » → `PROMOTION_GROUPS_EMPTIED`, avec `rostersInScope` et
     `registrationsDetached`.

2. **« Réinitialiser les cohortes » dit ce qu'elle a emporté.** Sur un stage, `STAGE_COHORTS_RESET` :
   la métadonnée porte `academicYearId`, `cohortsRemoved`, `affectationsRemoved`, `periodsRemoved`.
   - ⚠ **`academicYearId` est le point** : la requête ne l'envoie pas (« omise » veut dire « l'année
     en cours »), et c'est le serveur qui l'a résolue. Vérifier qu'il nomme bien **2026-2027** et pas
     `null`.

3. **Un acte sans effet s'enregistre quand même.** Rejouer « Réinitialiser les cohortes » sur le même
   stage : une **seconde** ligne, avec `cohortsRemoved: 0`.
   - ⚠ Sans elle, l'absence de ligne recouvrirait « personne ne l'a joué » et « joué sur une
     promotion déjà vide ».

4. **Un acte refusé n'écrit rien.** Tenter de supprimer un créneau couvert par une période publiée :
   le refus s'affiche, et **aucune** ligne `STAGE_SLOT_DELETED` n'apparaît. Puis supprimer un créneau
   libre — le témoin — qui doit, lui, écrire sa ligne avec `periodNumber` et ses dates.

5. **Épingler nomme celui qui a choisi.** Poser une cellule à la main dans la grille →
   `COHORT_SLOT_PINNED`, avec `serviceId`, `replacedServiceId` et `wasPinned`.
   - ⚠ `wasPinned` sépare « écraser ce que la rotation avait placé » de « écraser le choix d'un
     collègue ». Le vérifier en épinglant deux fois la même cellule : la seconde doit dire `true`.

6. **Déplacer un créneau garde les dates d'avant.** Changer les dates d'un créneau →
   `STAGE_SLOT_UPDATED`, avec `fromStartDate`/`fromEndDate` **et** `toStartDate`/`toEndDate`.
   - ⚠ Les dates d'avant ne survivent nulle part ailleurs : la ligne du créneau a été écrasée.

### Ce que cette passe ne peut pas montrer
- **L'atomicité.** Ces actes enchaînent jusqu'à six suppressions **hors transaction** ; une coupure à
  mi-parcours laisse une destruction à moitié faite. Rien à l'écran ne le révèle — item **0bc**.
- **Le reste des ~60 actes audités.** Seuls ceux qui n'appelaient *jamais* `SaveChanges` ont été
  balayés ; un `SaveChanges` **conditionnel** produit le même silence — item **0bd**.


## 59 · Un refus qui s'explique, et une publication qui laisse une trace (8 min) — session 61

⚠ **Redémarrer l'AppHost d'abord.** Le processus qui tourne porte l'ancien code : « aucune année
courante » y répond encore **500**, et « Publier » n'y écrit encore **rien** au registre. Le contrôle
qui distingue « ancien processus » de « défaut » est l'étape 1 — sur l'ancien, elle donne un 500.

⚠ **Rien ici n'est destructeur sauf l'étape 3**, qui publie. La jouer sur un stage dont la
publication est voulue, ou être prêt à dépublier derrière.

### A — Le refus arrive avec sa phrase

1. **Aucune année courante → « Conflit », pas « Erreur 500 ».** Le plus simple sans toucher à la base :
   ouvrir la grille d'un stage en forçant une année qui n'existe pas —
   `/api/stages/{id}/schedule?academicYearId=99999` — le refus doit être **404**, jamais 500.
   - ⚠ **Le vrai cas**, s'il se présente un jour : une base sans `IsCurrent` faisait répondre 500 à
     **tous** les écrans, `AcademicYearResolver` étant le repli de tout handler qui omet l'année. Le
     refus est désormais un **409** portant « Aucune année universitaire courante n'est définie —
     sélectionnez une année ». Ne pas fabriquer cet état sur la base vivante pour le voir : c'est
     `ErrorStatusMappingEndpointTests` qui le tient.

2. **Plus de groupes que d'étudiants.** *Admin → Groupes*, promotion de 232, demander **400** groupes
   par nombre. Le bandeau doit être **orange « Conflit »** et nommer **les deux** chiffres — 400 et
   232 — et non le rouge « Erreur 500 · Une erreur serveur est survenue » mesuré le 11/09.
   - ⚠ C'est le point le plus visible du lot : la phrase existait déjà côté serveur et voyageait dans
     `detail`, mais le client la **jette** au-dessus de 500.

### B — La publication laisse sa ligne

3. **Publier, puis lire le registre.** Publier un stage (ou une cohorte), puis *Admin → Journal* :
   - `STAGE_SCHEDULE_PUBLISHED` (entité `Stage`) ou `COHORT_SCHEDULE_PUBLISHED` (entité `Cohort`).
   - La métadonnée porte `periodsCreated` — **le comparer au nombre de périodes réellement créées**.
     Sur le stage, aussi `academicYearId` (résolu par le serveur, jamais `null`), `cohortsPublished`,
     `cohortsSkipped`, `assignmentsAlreadyServed`.
   - ⚠ Avant cette session **aucune de ces lignes n'existait** : le registre tenait « Dépublier » sans
     « Publier ».

4. **Rejouer « Publier » sur le même stage écrit une *seconde* ligne**, avec `cohortsPublished: 0` et
   `cohortsSkipped` égal au nombre de cohortes déjà publiées.
   - ⚠ **C'est l'étape qui mord.** Le publisher n'enregistre que s'il a des périodes à poser : avec un
     `SaveChanges` conditionnel, cette seconde ligne n'existerait pas, et « joué sans effet » serait
     indiscernable de « jamais joué ».

5. **Une publication forcée se lit comme telle.** Sur un service qui refuse d'être dépassé, publier
   avec le dépassement autorisé → la métadonnée porte `allowOverCapacity: true`.
   - ⚠ Sans lui, forcer contre le refus d'un service et publier normalement écrivent la même ligne.

### C — Ce que cette passe ne peut pas montrer
- **L'atomicité du découpage et de la désignation d'année.** Les deux sont désormais enveloppés dans
  une transaction ; le prouver demanderait de couper la connexion au bon millième de seconde. Aucun
  test de ce dépôt ne le prouve non plus — le fournisseur *in-memory* n'honore aucune transaction.
- **`0bc`** reste ouvert : les trois actes destructeurs par `ExecuteDelete` sont toujours hors
  transaction, parce que l'enveloppe ne se compose pas avec `IAuditTrail`.

## 60 · Une coupe dont toutes les colonnes pèsent pareil (5 min) — session 62

⚠ **Redémarrer l'AppHost d'abord** : le correctif est dans `RosterCut`, donc le processus qui tourne
découpe encore « les gros d'abord ».

⚠ **Ne pas jouer ceci sur la 3ᵉ MED.** Elle est **publiée** — ses colonnes sont figées à
100/100/100/93/90×6 et c'est voulu : le correctif ne vaut que pour les coupes à venir, et un
re-découpage sous une cellule publiée est refusé de toute façon. Prendre une promotion **non encore
découpée** (4ᵉ MED, 5ᵉ MED, 6ᵉ MED…), ou une promotion de rebut.

1. **Découper.** *Admin → Groupes*, choisir la promotion, « Répartition automatique », demander un
   **nombre** de groupes qui ne divise pas l'effectif — p. ex. la 4ᵉ MED (925) en **100 groupes**
   (925 = 25 × 10 + 75 × 9).
   - Attendu : **100 groupes**, tailles **10 et 9** seulement, total **925**.
   - ⚠ **Le point du test** : les groupes de 10 doivent être **dispersés** dans la numérotation, pas
     être les numéros 1 à 25. Repère rapide à l'écran — le groupe **nº 1** et le groupe **nº 100**
     doivent pouvoir différer d'au plus un étudiant ; avant le correctif, nº 1 valait 10 et nº 100
     valait 9 **systématiquement**, et les 25 grands étaient les 25 premiers.

2. **Partitionner.** « Assigner les partitions », stratégie **Contiguë** (celle de la faculté), le
   nombre de colonnes de la promotion.
   - Attendu : chaque partition affiche sa plage de numéros (`A : 1-10`, `B : 11-20`…) — inchangé.
   - ⚠ **Le contrôle qui compte** : les effectifs **par partition** ne doivent pas s'étager. Sur la
     4ᵉ MED en 100 groupes / 10 colonnes, attendre **92 ou 93 partout**, jamais `100, 100, …, 90`.

3. **Le témoin.** Refaire l'étape 1 avec un effectif **divisible** (p. ex. 6ᵉ MED 701 en… non : prendre
   une promotion où le nombre divise, ou 100 en 10). Toutes les tailles doivent être **égales** — le
   correctif ne doit rien changer quand il n'y a pas de reste à répartir.

**Ce que ceci ne prouve pas.** Que les colonnes **tiennent** dans les services : c'est l'item `0bh`, et
c'est de la capacité, pas du découpage. Une promotion parfaitement équilibrée peut dépasser sur tous
ses services à la fois.

## 61 · Supprimer un service dit pourquoi il ne peut pas (4 min) — session 63

⚠ **Redémarrer l'AppHost d'abord** : la garde est côté serveur, donc le processus qui tourne supprime
encore sans rien demander.

⚠ **Ne supprimer aucun service réel.** Les deux premières étapes sont des refus — elles ne détruisent
rien, c'est tout leur intérêt. La troisième crée un service jetable pour avoir un témoin.

1. **Un service que la grille utilise.** *Admin → Infrastructure → Services*, prendre un service qui
   apparaît dans un planning publié (p. ex. l'une des deux « Dermatologie » de la 3ᵉ MED), « Supprimer »,
   confirmer.
   - Attendu : la suppression est **refusée** avec une phrase lisible — « … ne peut pas être supprimé :
     N cellule(s) de planning, N période(s) de stage déjà enregistrée(s)… ».
   - ⚠ **Le point du test** : ce n'est **pas** « Une erreur serveur est survenue ». Avant, c'était un
     500 dont le seul contenu était le nom d'une contrainte PostgreSQL.
   - Attendu aussi : la phrase dit que les périodes **ne se retirent pas** et renvoie vers les listes de
     services autorisés — et non « retirez ces rattachements d'abord », qui n'aurait rien voulu dire ici.

2. **Un service que seul un stage autorise.** Prendre un service sans planning mais présent dans
   *Admin → Stages → (un stage) → Services autorisés*.
   - Attendu : refus **nommant le stage**, et cette fois « Retirez ces rattachements d'abord ».
   - Le retirer de la liste du stage, réessayer : il part.

3. **Le témoin.** Créer un service bidon (« ZZ Test »), ne le rattacher à rien, le supprimer.
   - Attendu : **204**, il disparaît de la liste.
   - Puis *Admin → Journal* : une ligne **« Service supprimé du catalogue »**, teintée destructrice,
     dont les métadonnées portent `serviceName`, `quotasRemoved`, `chefTenuresRemoved`,
     `staffDetached`. ⚠ Vérifier aussi qu'**aucune** ligne n'a été écrite par les refus des étapes 1
     et 2 : le registre enregistre les actes, pas les tentatives.

**Ce que cette passe ne peut pas montrer.** Que la base aurait refusé toute seule : les tests tournent
sur un magasin *in-memory* qui ne tient aucune clé étrangère, et ici c'est la **garde** qui refuse,
avant tout `SaveChanges`. Ni ce qui arrive à un service *encore* supprimable et qui porte des quotas :
la cascade les emporte, et seule la ligne du registre pourra encore dire combien.

## 62 · « Cette promotion tient-elle ? » (5 min) — session 64

⚠ **Redémarrer l'AppHost d'abord** : la route `/api/services/promotion-fit` n'existe pas dans un
processus antérieur à cette session, et la page répondrait 404. Le contrôle qui distingue « route
absente » de « non authentifié » est le même que d'habitude : sur l'ancien processus la page affiche
son bandeau d'erreur, jamais un tableau.

Aucune écriture nulle part : la page est une **lecture** de bout en bout.

1. **Ouvrir** *Admin → Infrastructure → Faisabilité des promotions*, année **2026-2027**, sans filtre.
   - Attendu : une ligne par promotion ayant des inscrits, chacune avec son état — « Tient »,
     « En dépassement », « Impossible à placer », « Aucun stage au catalogue ».
   - ⚠ **Le point du test** : les promotions qu'on n'a **pas encore découpées** doivent afficher des
     nombres. C'est toute la différence avec « Charge des services », qui affiche zéro partout tant
     qu'aucune cellule n'est posée — ouvrir les deux pages côte à côte le montre en un coup d'œil.

2. **La 3ᵉ MED** (déjà publiée, donc vérifiable contre la réalité) : déplier sa ligne.
   - Attendu : **10 colonnes de 15 j**, et sur `Dermatologie - Endocrinologie` une marge de
     **−14** pour 94 à placer et 80 places. C'est le nombre relevé à la main le 11/09 sur la base.
   - Attendu : `Santé Publique` et `Simulation Médicale` à **+6**.
   - Si ces trois nombres ne tombent pas, ce n'est **pas** un défaut d'affichage : c'est que le
     catalogue a changé depuis (un service autorisé ajouté, une capacité relevée) — ce qui est
     précisément l'acte que la page demande.

3. **Les états « impossible »** : la 2ᵉ MED, dont les deux stages n'ont aucun service autorisé.
   - Attendu : « Impossible à placer », et sur chaque stage le badge **« Aucun service autorisé »** —
     pas « 0 place ». Le message doit dire de saisir la liste, pas de trouver des lits.
   - La 7ᵉ MED (1 347 inscrits, aucun stage) doit lire **« Aucun stage au catalogue »**, jamais
     « Tient ».

4. **Le partage entre promotions.** Filtrer sur la **4ᵉ MED**, déplier `Dermatologie`.
   - Attendu : un badge **« N partagé(s) »** dont l'infobulle nomme la 3ᵉ MED.
   - ⚠ **Le contrôle qui compte** : le filtre ne doit **pas** faire disparaître ce badge. Un service
     est partagé, et cacher l'autre prétendant ferait lire « confortable » là où deux promotions se
     disputent les mêmes lits.

**Ce que cette passe ne peut pas montrer.** Que la répartition tiendra compte du chiffre : elle ne le
fera pas. `RotationArranger` pondère par la capacité et ne lit jamais l'occupation vivante — la page
prévient, elle ne place pas mieux. La suite reste : lire le nombre, corriger le catalogue, répartir.
