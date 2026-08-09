# Smoke test — sessions 11 → 14

Covers the year-scoping lockdown (11), CNPN versioning + targeting + text editing (12–13), per-level
service quotas and the répartition annuelle (14), and the déliberation / réinscription flow (14).
**Rollback is at the bottom** — read it before you start, not after.

Prerequisites: `dotnet run --project PGSH.AppHost`, log in as an admin (Scolarité).

| Migration | Applied? |
|---|---|
| `StageSlotAcademicYear`, `CnpnVersioning` | ✅ already in your dev database |
| `AddServiceLevelCapacityAndLocalization` | ✅ already in your dev database |
| `RegistrationYearOutcome` | ⚠ generated, **not yet applied** — restart `PGSH.AppHost` and `MigrationService` applies it |

Timings below are the real figures from your data — if you see a different number, that is the bug.

---

## 0 · Sanity (2 min)

```bash
dotnet test PGSH.Tests/PGSH.Tests.csproj      # expect: 660 passed, 0 failed
```

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

## 13 · Closing a year: the déliberation canvas (8 min)

*Admin → Étudiants → une promotion → « Clôturer l'année »*

> **Restart the API first.** `levels/{id}/deliberation*` and `levels/{id}/reinscription*` are new.

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

# Rollback

Three migrations have been applied to `TodoDatabase`. Reverse in the order below.

### Database (destructive — take a dump first)

```bash
# 1. Back up
docker exec -e PGPASSWORD='<pw>' postgres-0fae29d8 \
  pg_dump -U postgres -d TodoDatabase -Fc -f /tmp/pre-rollback.dump

# 2. Revert, newest first.
dotnet ef database update AddServiceLevelCapacityAndLocalization \
  --project PGSH.Infrastructure --startup-project PGSH.Infrastructure   # undoes RegistrationYearOutcome
dotnet ef database update CnpnVersioning \
  --project PGSH.Infrastructure --startup-project PGSH.Infrastructure   # undoes AddServiceLevelCapacityAndLocalization
dotnet ef database update StageSlotAcademicYear \
  --project PGSH.Infrastructure --startup-project PGSH.Infrastructure   # undoes CnpnVersioning
dotnet ef database update CurriculumCnpn \
  --project PGSH.Infrastructure --startup-project PGSH.Infrastructure   # undoes StageSlotAcademicYear
```

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

Sessions 11–13 are **committed** on `cnpn-versioning-and-year-scoping`, nothing is pushed:

```bash
git log --oneline -3
#   008fd37  Per-level service quotas, the répartition annuelle, and a CNE that admits real students
#   f9875e4  Scope everything by academic year, and version the CNPN
```

So `git revert 008fd37` / `git reset --hard f9875e4` are the levers for those two, and `git status`
covers whatever is still in the tree (the déliberation / réinscription work, at time of writing).
The **frontend is a separate repo** and had pre-existing uncommitted work before all of this:

```bash
cd PGSH/PGSH.Frontend && git status --short
```

`git checkout .` there would discard that too. Stash rather than checkout if in doubt.

Untracked backend files added since `008fd37` (checkout will not remove them):

```
PGSH.Domain/Registrations/RegistrationOutcomeSource.cs
PGSH.Domain/Registrations/RegistrationYearOutcomeRecordedDomainEvent.cs
PGSH.Application/Students/Registrations/Deliberation/
PGSH.Application/Students/Registrations/Reinscription/
PGSH.Application/Students/Registrations/RegistrationYearOutcomeRecordedEventHandler.cs
PGSH.Infrastructure/Registrations/ClosedXmlDeliberationSheetParser.cs
PGSH.API/Endpoints/Registrations/Deliberation.cs
PGSH.API/Endpoints/Registrations/Reinscription.cs
PGSH.Tests/Application/DeliberationTests.cs
PGSH.Tests/Application/ReinscriptionTests.cs
cnpn/                                    ← your PDF, keep this
```
