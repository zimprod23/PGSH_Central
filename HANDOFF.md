# HANDOFF.md

> ## ▶ Start here — next session
>
> Everything below is history, newest first. What is actually waiting:
>
> | # | Do this | Why it is not done |
> |---|---|---|
> | 0z | ~~Run the rebuild~~ — **DONE 2026-09-01.** ⚠ **RESTART the AppHost**: the API is running against a database that was dropped and recreated under it, and it needs the two new migrations and the `reinscription/sheet` route either way. | 10 203 students · 43 605 registrations · 105 626 périodes · 87 092 évaluations · **0 `LEGACY-` CNEs** (4 695 null) · **10 185 stamped, 0 unresolved** · 43 605 registrations backfilled · 146 allowed services · 3 effectivity rules · 2 chef tenures · 2026-2027 present, current, **0 registrations**. Dump kept: `pgsh-avant-reimport-20260901-223756.dump`. Four *silent* traps found on the way — see `NOTES.md` « The rebuild, run for real ». |
> | 0y | ~~Apply `Réinscriptions 26-27 VF.xlsx`~~ — **DONE 02/09/2026**, through the UI, against the live base. 6 813 inscriptions créées · 7 232 décisions · 1 217 Diplômé déduits · **1 327 signalements** (60 dette de dernière année sur 2026-2027, 1 267 absents sur 2025-2026). Sauvegarde préalable : `pgsh-avant-reinscription-20260902-140434.dump`. | Le gel a été vérifié sur la promotion réelle : découpage 7ᵉ MED 2026-2027 = 65 groupes, 1 281 placés, **60 non placés et ce sont exactement les signalés**. Levée d'un signalement → 1 282 placés, l'étudiante dans « Groupe 66 », la ligne conservée avec constat + motif + auteur. Trois défauts trouvés et corrigés en chemin — voir `SMOKE-TEST.md` Partie D. |
> | 0ab | ~~`CohortProvisioner.GroupTextsQuery` was missing the hold exclusion~~ — **fixed 02/09/2026.** Documented for three reads, applied to two. A held registration's CNPN text was deciding which cohortes its roster gets, while the same student is excluded from the cut and from affectation. Caught by `SqlTranslationTests`' one assertion that reads *what the SQL says* rather than *that there is SQL*. | Latent only because the base holds 0 cells — the 1 327 signalements are real, and the first « Générer le plan » would have read them. See `NOTES.md`. |
> | 0ac | **Répartir la 3ᵉ MED 2026-2027** — l'axe est posé, les cohortes ne le sont pas. Depuis « Bloc de rotation » ou la grille d'un stage : auto-répartir, puis publier. C'est le geste qui remplit « Charge des services ». | Fait 02/09/2026 : 6 partitions (A-F, 94 rosters, 933 inscrits, 0 non placé), **4 fêtes lunaires estimées** (non confirmées) et **36 créneaux** — 6 stages × 6 colonnes de **30 jours ouvrables exactement**, coupure de 2 jours, 01/09/2026 → 31/05/2027. ⚠ **Écrits en SQL, donc sans entrée d'audit** : `HolidayCommands` et `ApplyRotationCycleCommand` sont tous deux `IAuditableCommand` et l'automatisation du navigateur n'a pas tenu (le viewport se redimensionnait sous les clics). Même angle mort que l'item 0e. Sauvegarde : `pgsh-avant-axe-3med.dump` dans le conteneur. |
> | 0ad | **Relancer l'AppHost** pour le pic corrigé et les barres empilées par promotion. Sans ça le document affiche encore « pic du 07/09 au 06/10 » (un mois pour un plateau de six) et des barres grises au lieu de la répartition 3ᵉ/4ᵉ année. Le repli est en place, donc la page fonctionne — elle est seulement moins juste. | Corrigé côté serveur : `PeakStart/PeakEnd` sont l'**enveloppe** du pic et non le premier intervalle qui l'atteint, `PeakDays` dit le temps réellement passé à ce niveau (les intervalles au pic ne sont pas contigus — l'axe 3MED a des coupures de 2 jours), et `Months[].Levels` porte le découpage par promotion lu sur l'intervalle de pic du mois. 1 407 tests verts. |
> | 0aa | **RESTART the AppHost, then drive the two new backend-dependent screens.** The API process predates this session's build, so `GET /services/occupancy-report` 404s and `?status=` on `/students` is silently ignored (an unknown query param binds to nothing — the filter appears to do nothing rather than erroring). | Built and green (1 405 tests) but only one of the three features is verified in a browser: the student file's **Stages** tab reads endpoints that already existed and was walked end to end on Houda Aamoud — 21/21 stages, 3ᵉ année 2/6 with the other four « jamais tenté » and *not* « à revalider », which is the distinction the tab exists to draw. The status filter's dropdown renders with the right five verdicts; the **filtering** and the whole **Charge des services** report are unverified against a running API. |
> | 0 | **Finish `SMOKE-TEST.md` §28 f/g**, then remove `SMOKETEST01`/`SMOKETEST02` from the base (SQL is in §28). | The session expired mid-step on 2026-08-30. **`PriorEnrolments` is still 0 rows**, so the équivalence — the whole reason the table exists, and the row a future widening of « ce qu'il doit » will depend on — has never been written outside a test. Everything else in §28 passes. |
> | 0d | **Fill the 90 empty 4ᵉ MED rosters of 2026-2027, or re-cut them** — auto-arrange for that promotion. | Measured 2026-08-31: the year holds 90 rosters carrying partition labels A-F and **0 inscriptions rattachées, 0 cohortes**, plus 0 affectations and 0 slots. Nothing is broken — the promotion is cut and unpopulated — but no plan can be generated until the students are in the rosters. |
> | 0e | **Audit roster creation, rotation-group assignment and « vider les groupes ».** ⚠ **This bit for real on 02/09/2026.** A smoke test created 66 rosters on 7ᵉ MED 2026-2027; the user then asked why groups existed for a promotion he had never partitioned, and **the audit log could not answer** — it held two entries for the whole session (the roll apply, one hold release) and nothing about 66 rosters appearing. The constructive act leaves no trace while the destructive one does, which is the wrong way round, and it cost a real moment of « where did this come from » on live data. | `PARTITIONS_CLEARED` is recorded and the acts that *create* a cut are not, so « qui a découpé cette promotion, et quand ? » is unanswerable — which is exactly the question the empty 2026-2027 rosters raised, and it could not be answered. The destructive act leaves a trace and the constructive one does not: the wrong way round. |
> | 0c | **Restart the API and re-check one caption** — the exports' « N inscription(s) » printed « 5.932 » because `:N0` used the host's `CurrentCulture`. Fixed to `ExportLabels.Fr`, but the running process predates the fix. | Cosmetic and confined to the caption row — the cells are typed values, not formatted strings — but it is a document that leaves the system, and « 5.932 » reads as five-point-nine-three-two. |
> | 0b | **Click « Supprimer le bloc » once** (same as item 2), and re-check the two cosmetic fixes from §29 in a browser. | `SMOKE-TEST.md` §29 ran 2026-08-30: steps 0-6 **pass** against the live base, store provably unchanged after every refusal. Step 7 was **not** run — the base holds 0 published cells, so it would *succeed* and destroy a real axis. The double-toast removal and the « nomme l'année » fix are type-clean but were not re-driven through the UI (the year picker stopped responding to automation). |
> | 1c | ~~Author `AllowedServices` for the four new 3ᵉ année stages~~ — **done 2026-09-01, 17 rows.** | Copied from their 4ᵉ année counterparts on the user's explicit authorisation (Cardiologie 3, Dermatologie - Endocrinologie 4, Pneumologie 3, Rhumatologie - Radiologie 7). `AddAllowedServiceCommand`'s admissibility guard was replicated in the script rather than bypassed — no service in the base carries a `ServiceLevelCapacity` row, so none refused. ⚠ **Consequence to watch:** MED3 (895) and MED4 (898) now list the same services, so wherever their windows overlap the load is the sum of both. Nothing refuses it — a calendar question, or the first real reason to author quotas. **The 3ᵉ année 2026-2027 is now plannable for the first time**, which is what `SMOKE-TEST.md` §34 part A walks. |
> | 1b | ~~Apply `Cnpn1650Med3CatalogueAlignment`~~ — **applied and verified 2026-09-01.** | Applied by `MigrationService` on Aspire startup; it did **not** raise, i.e. no grid-published période locked the mode change. Verified on the base: all six MED3 stages now read **30 j.o. / `SingleService`**, and 2174.18's own 3ᵉ année requirement set preserves Chirurgie and Médecine at **66 j.o., coefficient 3** — that curriculum already existed (« Reconstitué depuis les stages effectivement servis »), so only the two missing rows were inserted and no authored figure was overwritten, which is the path the migration was written for. |
> | 1a | ~~Apply migration `Cnpn1650Med3Stages` to the live base~~ — **applied and verified 2026-09-01.** | The six 3ᵉ année stages and the 1650.25 / MED3 requirement set are on disk. ⚠ **Coefficient 1 throughout is still the documented placeholder**, and it is now a *live* disagreement rather than a predicted one: the catalogue says Chirurgie 3 and Médecine 3 while 1650.25's requirement set says 1, and the Stages page renders the catalogue value. That is `PHASES.md` §15.1's second bullet arriving on real rows — see the note under it. |
> | 1 | **Enter 1650.25's requirement sets — the stage list per level — *before* opening 2026-2027 for registrations.** ⚠ **Awaiting the list from the faculty.** | `RegistrationCnpnStamper` reads the effectivity rule once, at the creation of a registration. Open the year first and every 3ᵉ année of 2026-2027 gets a stamp pointing at a text that requires nothing — `CohortProvisioner` then stands aside silently and the promotion plans as if it owed no stage. `PHASES.md` §15.2. |
> | 2 | **Click « Supprimer le bloc » once, in a foreground tab** — `SMOKE-TEST.md` §25 step 7. | The only thing built in session 27 that no human path has exercised. Server side is covered by ten tests; what is unverified is the confirmation dialog, its counts, and that the promotion's other block survives. ⚠ Do it on a block whose loss costs nothing, or be ready to re-apply and re-plan — Med6's 1 000 cells are one *Générer le plan* away, but they are not free. |
> | 3 | **Decide whether `LateArrivalScheduler` should materialise périodes for an *unpublished* grid.** | It materialises every open cell of the roster whether or not the répartition was published, so a newcomer can hold périodes for a plan nobody published — and `SchedulePublisher` will then skip his assignment as `SkippedAlreadyServed`. The coverage half of this was fixed in session 26; this half is a design question, not a bug. |
> | 4 | **Sweep the pre-existing double-toast** in `CnpnEffectivityPanel`, `CnpnTargetingPanel`, `CnpnVersionsPanel`, `ScheduleGridModal`, `GroupsPage` and the student dossier's « Ajouter une inscription » (seen 2026-08-26). | `errorMiddleware` already toasts every rejected mutation in the server's own words, so each page-level `notify.error` beside it prints the same sentence twice. Found running §23; fixed there, untouched elsewhere. |
> | 5 | **Review three 6MED service calls** on the Stage page — *Pédiatrie CCP*, *Urgences (Moulay Youssef)*, and everything at *Azzamouri*. | All three were excluded by the recency rule and all three are arguable. `SMOKE-TEST.md` §22c.5 names them and the one-line undo. |
> | 6 | **Close 2025-2026 for real** — Clôture & réinscription, exceptions canvas, confirm, apply. | 6 057 verdicts with no undo but a restore. It is the user's click, not ours. **Take a `pg_dump -Fc` first.** |
> | 7 | **Walk the defence roll** — name a handful of 7ᵉ année students « Diplômé » and check they graduate while the rest stay put. | The 14.3e rule is verified by tests and by the preview's numbers; nobody has used the flow it now depends on. |
> | 8 | ~~Phase 16 — the Access re-import~~ — **16.1 and 16.2 closed, session 37.** What remains is executing the rebuild itself (item 0z). | `Student.CNE` is optional, the importer manufactures nothing, and 16.2 is answered with a measurement: **no** source row carries a `NO_ORDRE` in its CNE column (0 identical, 0 cross-matching, 1 of 5 508 with the eight-digit shape). ⚠ Session 37 also found what 16.3/16.4 did not name — the CNPN data migrations refuse to run against an empty base, and the CNPN *attribution* was two one-off `UPDATE`s inside migrations that would be marked applied without stamping anybody. See `PHASES.md` §16.5. |
> | 9 | **Testcontainers.** | Carried since session 22. ✅ The macro-plan path is now swept for *translation* (session 28, twelve cases, all compile, no second defect), so what is left is strictly what no amount of compiling can answer: whether the SQL returns the right **rows**, plus FK behaviour, unique indexes (`NULLS NOT DISTINCT` on the roster keys) and `OnDelete` — which is what every delete guard in the system is written against. |
> | 10 | **Sweep other screens for stale data** — `loadingMiddleware`'s re-entrant dispatch (fixed, `SMOKE-TEST.md` §20f) silently staled whichever query settled *last* on any page, for the whole life of the middleware. | The bug is fixed; nobody has checked what else it was quietly breaking. |
> | 11 | **Decide on the 56 programme-mismatched stamps** — Médecine registrations governed by `PHARM-LEGACY`. `SMOKE-TEST.md` §20g has the query. | Pre-existing, from the original CNPN backfill. One of the 57 was corrected incidentally by the 2ème année rule. |
> | 12 | **Finish the final-year gate's walk-through** — `SMOKE-TEST.md` §21 steps 2, 3, 5, 6, 7, 9. | The rule itself was run against the real base 2026-08-26 (§24): 60 of the 686 6ᵉ année Médecine owe a stage and all 60 are refused entry to the 7ᵉ. What nobody has exercised on real data is the déliberation/réinscription legs, the unstamped student, the dérogation and the revalidation. |
> | 13 | **Close the revalidation flexibility hole** — no way to hand a student a stage he never attempted, and no generic "assign this student to this cohort". | Identified in session 24. `RevalidateStageCommand` needs a prior *failed* attempt; every other creation path is bulk or specific. |
> | 14 | ~~The Inscriptions screen~~ — **done, session 30**, backend and UI. What is left of this item is the **year-segregation audit** and **académic-year update/delete**, which were always separate concerns filed under one number. | Session 24 was scoped to the CNPN alone, by agreement. |
> | 15 | **Give RTK Query a request timeout.** | A hung API is today indistinguishable from an empty year: `fetchBaseQuery` sets no `timeout`, so a request that never answers leaves every screen on a skeleton with no error — `errorMiddleware` never fires, because nothing rejects. Seen 2026-08-26, when the API sat paused on a breakpoint and the frontend showed nothing at all. ⚠ Not a blanket value: `stages/macro-plan` legitimately runs long, and aborting a mutation client-side does not stop the server writing — though since session 33 it is at least *atomic*, so an abort costs the run rather than leaving half a plan. A generous global default with explicit per-endpoint overrides for the heavy writes. |
> | 16 | **Fold `ReinscriptionPlanner`'s own copy of the final-year decision into `FinalYearGuard`.** ⚠ Session 37 extracted the *other* half — « peut-être sa dernière année ? » is now `FinalYearTest`, shared by the déliberation and the réinscription roll — so what is left under this number is strictly the predicate-scoped debt lookup. | The planner builds its `FinalYearGate` from three lookups of its own rather than from the guard, so one rule now has two implementations. Deliberate for now: the planner is scoped by the *predicate* that selects the promotion — 8 077 registrations — and the guard's batch takes a list of ids, which is exactly what must not be shipped down for a promotion. Folding them means teaching the guard to take a predicate. |
> | 17 | ~~Wire the two exports into the frontend~~ — **done, session 32b**, and smoke-tested through the real buttons against the live base (`SMOKE-TEST.md` §30). | — |
> | 19 | **Give « Dépublier toutes » a single command**, the way « Publier tout » now has one. | It still loops one `UnpublishCohortSchedule` per cohorte, so a stage where several rotations have begun answers with one red toast per cohorte. Not a copy of the publish fix: each refusal names what *that* cohorte would lose — périodes démarrées, notes entrées, jours de présence — and an aggregate has to be designed before it can replace them. Session 33. |
> | 18 | **The pre-validation export.** | Agreed with the user as a second document: the same population without the note/verdict columns, showing where everyone *is going*. `onlyEvaluated` is already the switch and `ExportWorkbook` already the shape, so it is a column set and a caption, not a second pipeline. Deferred deliberately — the post-validation one was the ask. |
>
> | 1d | ~~`POST stages/revalidate` has no frontend caller~~ — **done, session 36.** | `RevalidateStageModal`, reached from « Revalider un stage » on each registration card of the admin student page — on *each* card because the retake always hangs off the inscription the student holds now, never off the year he failed in. Backed by a new read, `GET registrations/{id}/revalidation-context`. ⚠ **The API must be restarted** for that route to exist; a process predating it answers 404 while `/api/stages` answers 401, which is the control that tells « route absent » from « not authenticated ». |
> | 1e | ~~Nothing applies a stage's duration~~ — **closed where it was asked for, session 36.** | The revalidation dialog now proposes a window laid from `r.CnpnVersionId ?? r.Student.CnpnVersionId`'s own `CurriculumStage`, never from the catalogue, and shows both figures side by side when they disagree. Proposed, not imposed — the field stays editable, since a retake shortened by agreement is legitimate. ⚠ **The other readers still apply no duration at all**: the dossier, the progression and the export read none. What was closed is the one place a duration was *asked of* an operator with nothing telling him which one. |
>
> **▶ SESSION 37 — the canvas the faculty actually sends, and a CNE that was never an identifier.**
>
> Backend suite **1 342 green** (+45), frontend `tsc --noEmit` and `npm run lint` clean.
>
> **1 · `Student.CNE` is optional.** Phase 16.1, done. The Access base records a code for 5 510 of
> 10 203 students and the import manufactured `LEGACY-{NO_ORDRE}` for the other 4 693 — a value
> indistinguishable, in every list, export, canvas and identifier match, from a code somebody holds.
> ⚠ **The column was already nullable** (`Users` is TPH, an `Employee` has no CNE); the requirement
> lived in EF's model and the validators, and the only real constraint was an *unfiltered* unique
> index that Postgres already let any number of NULLs through. Migration `StudentCneOptional` filters
> the index and clears the placeholders.
>
> ⚠ Two things found on the way. `UpdateStudentCommandValidator` required `int.TryParse` on the
> Apogée, which no `SANS-APOGEE-…` value satisfies — **third instance** of a validator making
> existing rows read-only, after the CNE regex and `Objectives.NotEmpty()`. And `null == null` is
> true in memory and false in SQL, so an unguarded uniqueness check on an optional identifier reports
> a phantom conflict in the suite and none in production.
>
> **Phase 16.2 is answered: no.** No source row carries a `NO_ORDRE` in its CNE column — 0 identical
> to its own, 0 matching another's, 1 of 5 508 with the eight-digit shape. Nothing to move.
>
> **2 · Réinscription par fichier.** The faculty does not use the canvases PGSH generates; for
> 2026-2027 it sent 6 862 rows of `Code · NOM · PRENOM · Etape 25-26 · Etape 2026/2027`. Those two
> étapes carry the verdict, so one upload closes the year and opens the next.
>
> ⚠ **804 of those rows are final-year students re-registering in the same year**, and reading them
> as « redoublant » would have been wrong twice: it is not a failure, and
> `RegistrationStatus.AnnulsItsStages` would have wiped a year of stage record for all 804. Nothing
> is written for them, nor for a réorientation, nor where the closing year holds no registration.
>
> ⚠ **Silence inverts between the two canvases.** The déliberation is a list of exceptions, so an
> unnamed student is admis. This is the roll of who *is* coming back, so an unnamed student is not —
> 1 216 of them, 999 in 7ᵉ MED and 211 in 6ᵉ PHARM, i.e. the thesis years. Left untouched, counted,
> and that is the population the defence roll should then be run over.
>
> Measured before designing: 6 813 of 6 862 codes match a student, none is duplicated, and **0 of the
> 6 810 checkable rows disagree with the registration on record about the level** — which is what
> makes a level disagreement safe to treat as a refusal of the whole file rather than a skip.
>
> Two things it needed that did not exist: **`FacultyLevelCodes`** (the code table promoted out of
> `LegacyImport.LevelMapper`, plus `MDME3`/`MPHAR3` which appear in no legacy row, plus the codes
> PGSH knowingly does *not* manage) and **`FinalYearTest`** (lifted out of `DeliberationPlanner`).
>
> **3 · The rebuild is not « migrate, then import ».** Three CNPN data migrations `RAISE EXCEPTION`
> against an empty base — loud. And the CNPN *attribution* was two one-off `UPDATE`s inside
> migrations: replayed in the corrected order they stamp nobody, are marked applied, and the base
> ends with 10 200 students carrying a null text that **every reader tolerates gracefully**. Closed
> by `CnpnHistoryAttributor` / `--stamp-cnpn`. The authored half (24 holidays, 146 allowed services,
> 3 effectivity rules, 2 chef tenures, the 2026-2027 year) is dumped on **natural** keys — an
> id-keyed restore lands rows on the wrong rows, and does not announce it.
>
> ⚠ `.gitignore` now covers `*.xlsx` — the faculty's roll names real students and was untracked but
> not ignored, unlike `Medecine.mdb`.
>
---

## Session 38 — 2026-09-02 · the roll is applied whole, and the disagreement is recorded

**The user's correction, on the report session 37 produced from the real file.** The rollover was
right about the rules and wrong about what to do with the answer: it *refused* the rows PGSH's own
record disagreed with, and the faculty's document is the more authoritative of the two.

> « the excel should create registration, even tho some conditions are not fulfilled — why, because
> in most cases they already validated / revalidated everything but we did not add the evaluations
> yet, so we can just flag the students so we can later come back to them. »

**What the refusal cost, measured on the real roll:** 182 of the 651 7ᵉ MED it re-registers were
skipped *before session 37's « entrer » fix* — ⚠ **with that fix in place the live preview holds 60**,
verified in the browser 2026-09-02. 182 is the motivation, 60 is today's count. The refusal — a quarter of the promotion, every one named by the faculty as coming back — and in most of
them the stage was served and only the évaluation is missing.

### Built

- **`RegistrationHold`** (`Domain/Registrations/`, table `RegistrationHolds`). A registration created
  but withdrawn from planning: no roster cut, no cohort affectation, no published période. It keeps
  its status, its verdict and everything already published under it — taking those away is
  `UnpublishCohortScheduleCommand`'s act, which names what it costs and asks twice.
  - Two reasons: `OutstandingPriorStages` (60 live) and `AbsentFromReinscriptionRoll` (**all 1 267**
    absentees, the 1 217 inferred graduations included).
  - Evidence is a **snapshot**, never re-derived on read — same bargain as `FinalYearEntryWaiver`.
  - **Released by hand, with a required note**; the row survives its release so the file can say who
    cleared him. Idempotent per reason, because the roll is re-runnable. No bulk release.
- **`RegistrationHoldPolicy`** — one expression, delegates compiled from it, composed into
  `AutoArrangeGroupsCommandHandler`, `StudentAffectationService` and `CohortProvisioner`. Pinned by
  `SqlTranslationTests`: in a predicate it is an `EXISTS`, which translates.
- **`POST reinscription/sheet/export`** — Synthèse · Lignes · Absents, **uncapped**, written from the
  plan rather than the capped report. Writes nothing, so it is offered before the confirmation and on
  a roll the apply would refuse.
- **Signalements page** (`/admin/signalements`) + `GET registrations/holds` and
  `POST registrations/holds/{id}/release`.

### Decided, and worth re-reading before changing

- ⚠ **The 1 217 graduations are held too**, at the user's explicit instruction — and the reasoning is
  better than the original proposal to hold only the 50 undecidable ones. The graduation is *our
  inference*, read off a blank cell. A partial roll would end the cursus of people still enrolled with
  nothing saying a human had looked; holding costs a real graduate nothing, and it catches what an
  absence most often is — a réinscription that has not arrived.
- **Errors still refuse the whole file** (duplicate code, unknown level code, level mismatch, level
  regression, « Retrait »). Those say the *file* is mistaken, not that our data is behind.
- **The manual registration paths still refuse**, with the waiver as the override. The roll is the
  faculty's own document; a hand-typed form is not, and per-student ceremony does not scale to 182.
- **Holds need no confirmed count**, unlike `WillGraduate`: a hold is released in a click, a
  graduation ends a cursus. Confirm what cannot be undone.

### Renamed — the frontend was updated with it

`ReinscriptionSheetRowStatus.FinalYearBlocked` → `WillRegisterHeld`; the report's `finalYearBlocked`
→ `willRegisterHeld`, plus a new `absenteesHeld`. ⚠ The **other** réinscription path
(`ReinscriptionAction.FinalYearBlocked`, the déliberation-derived rollover) still refuses and keeps
its name — do not "tidy" the two into one.

### Left undone, deliberately

- **The *examens cliniques* are not modelled.** They are real — they open once a student's stages are
  done, he can fail them, and he is re-registered to sit them again — so PGSH cannot today tell
  « still finishing stages » from « stages done, waiting on the exams ». The user said the logic is
  complex and did not describe it; inventing it would put a state on screen nobody can act on.
- **When a 1650.25 student starts revalidating** — parked at the user's request. `FinalYearTest` asks
  `level.Year == TotalYears` per student, which reproduces the old text's behaviour on the new one.
  Nothing hard-codes 7. Not urgent: the first 1650.25 promotion is in its 3ᵉ année.

### Ce que l'exécution a trouvé (02/09/2026)

Le backend est passé au premier essai — tous les chiffres écrits sont ceux de la simulation. Les trois
défauts trouvés étaient **côté écran**, et deux d'entre eux sont de la même famille : *une information
que le serveur envoie et que la page jette*.

- ⚠ **Le découpage annonçait « 60 non assigné(s) » sans un motif**, alors que `BulkResponse.items`
  porte une erreur par étudiant avec le constat du signalement. Corrigé : `GroupsPage` liste les
  motifs et renvoie vers Signalements.
- ⚠ **`BulkItemResult.error` était typé `ApiError`** au lieu du `Error` du domaine — **défaut
  préexistant**, qui rendait toute erreur d'item illisible. Typé `DomainError`.
- ⚠ **`Modal` de Mantine refuse de se monter sur la page Signalements** (racine rendue vide). Non
  diagnostiqué. Contourné par un panneau inline au-dessus du tableau — ce qui règle aussi le défaut
  d'origine : rendu *sous* 60 lignes, le formulaire de levée était invisible.
- Et une double-notification (`CLAUDE.md` §1e) retirée du handler de levée.

### 15.16b — the 26 the file names and PGSH never held (same session)

The roll now **creates** them, from the Apogée and the name, flagged `IncompleteStudentFile`.

- ⚠ **That flag is advisory.** The user wanted them flagged *and* partitioned with everyone else, and
  a signalement freezes by construction — so blocking became a property of the **reason**
  (`RegistrationHoldReasonExtensions.Blocking`). `Plannable` = no *blocking* hold; `Flagged` = any
  hold. The page marks « Gelé » vs « Planifié — à compléter ».
- Only the **e-mail** is invented (NOT NULL UNIQUE, and a login): allocated in the planner against the
  store, printed on the row. **No CNE** — the row has an Apogée and CNE is optional. `BacYear` left
  empty rather than guessed.
- ⚠ Two defects the compiler could not see: `Students.Add(student)` left the registration **untracked**
  (add the registration — the graph is whole only from that end), and `ReleaseHold` on an unsaved hold
  lifted an arbitrary one, because store-generated keys are `Guid.Empty` until saved. Both fixed, both
  covered.

### ⚠ The smoke test of 15.16b found a destructive idempotency bug — fixed, needs a restart

Re-uploading the roll (which is now the normal path, since it is how the 26 newcomers get created)
previewed **8 077 signalements and 791 « Diplômé » déduits**, against 1 267 and 1 217 on the first
pass. Cause: `Skip()` dropped the source registration id, so a row skipped as « déjà inscrit » stopped
counting as *covered by the file*; `ReadAbsence` read that as « ne revient pas » and inferred a
soutenance. It would have ended the cursus of 791 students the same file had re-registered minutes
before.

- **Pre-existing** — `Skip` has always passed null — but invisible while the roll had only ever been
  applied once.
- Fixed: the already-registered branch carries its closing-year registration. Covered by
  `Re_running_does_not_turn_already_registered_students_into_absentees`, **proved to bite** (reverted,
  failed, restored).
- ⚠ **The running API still has the old code.** Restart the stack before touching « Réinscription par
  fichier » — a re-upload on the current process still offers those 791 graduations, and nothing on
  screen says the number is wrong.

### ⚠ The apply found a second defect: duplicated absentee flags

The re-run raised a **second** `AbsentFromReinscriptionRoll` on all 1 267 absentees (2 534 rows).
`PlaceOnHold` is idempotent per reason by reading `Registration.Holds`, and the closing-year query did
not `Include` it — an un-Included collection is indistinguishable from an empty one. ⚠ **The
in-memory suite cannot see this**: it fixes navigations up from the change tracker, so the idempotency
test passed throughout.

- Fixed with the `Include` **and** a unique index, `IX_RegistrationHold_Registration_Reason_Active`
  (migration `RegistrationHoldUniquePerReason`), so the next omission is a constraint violation.
- **The 1 267 duplicates were deleted from the live base** inside a transaction that asserted the
  invariant before committing.
- ⚠ **The migration has not been applied** — it lands on the next Aspire startup. The base is already
  deduplicated, so it will create cleanly.

**Tests: 1 391 green** (was 1 342). New: `RegistrationHoldTests` (domain, incl. two Theory sets),
`RegistrationHoldWorkflowTests` (7), 3 in `ReinscriptionSheetTests`, 2 translation cases. The roster
freeze was proven to bite — guard removed, test failed, guard restored.


> **▶ SESSION 36c — `npm run lint` green, and the guide that was prescribing the defect.**
>
> 15 problems to 0, frontend only. No behaviour intended to change; three fixes remove a real
> defect on the way. `npm run build` clean, backend suite unchanged at **1 299 green**.
>
> ⚠ **Eight of the errors were one pattern, and `PGSH.Frontend/CLAUDE.md` §2 was teaching it.**
> The guide prescribed `useEffect(() => setPage(1), [debouncedSearch])` and five screens carried it.
> An effect runs *after* the render that changed the filter, so that render commits with the **old**
> page still in the query arguments and RTK fires a request for page 3 of the new filter which is
> then superseded. `usePagedFilters` adjusts the page **during** render — what React documents for
> this case. §2 now teaches the hook and says why the effect was wrong; `useListParams` stays the
> answer where the filters belong in the URL.
>
> **The three that were not just lint:**
> - `AcademicYearContext` stored `currentYearId` and back-filled it by an effect once the years
>   arrived, committing a render where every consumer read `null`. A handler that omits the year
>   resolves it to the *current* one server-side — so that render was not empty, it was a request
>   for a **different promotion**, on every first paint of the admin shell. Now derived, and the
>   context value memoised.
> - `EmployeesPage` populated its edit form from an async fetch in an effect, so the fields showed
>   the **previous employee** for one render.
> - `GroupsPage`'s student auto-pick listed `matches` — a fresh array every render — so it re-ran
>   continuously and would overwrite a choice the user had just made.
>
> **Verified in the browser, not only by the linter:** the shell resolves 2026-2027, Infrastructure
> pages and filters, paging to page 3 then searching lands on **page 1**, and both employees
> populate their form correctly one after the other — the case the `EmployeesPage` change could
> have broken.
>
> ⚠ **A correction to session 36b:** the note about `RegistrationCard` state resetting mid-
> interaction was **wrong**. Its key is `reg.id` and it does not remount; the modal closing during
> automation was synthetic clicks racing the overlay, not a defect. Nothing was changed for it.
>
> **Left alone deliberately:** the 500 kB bundle warning from `npm run build` — real, but it is a
> code-splitting decision, not a lint fix.
>> **▶ SESSION 36b — driven in Chrome against the running stack, which found two defects tests could not.**
>
> Suite **1 299 green**. The API was restarted by the user, so `GET registrations/{id}/
> revalidation-context` went from 404 to a real 200 — and the whole point was verified on the live
> base, authenticated as the real admin:
>
> ```
> governingText : 2174.18, fromRegistration true, durationInDays 66, coefficient 3
> catalogue     : 30 j.o., coefficient 3
> lastFailure   : 2023-2024, Chirurgie Vasculaire, 18/03→14/06/2024, 65 j.o. servis
> proposedWindow: 2026-09-01 → 2026-12-03, 66 jours ouvrables, 94 calendaires
> ```
>
> **66, not 30.** The dialog prints it beside « Le catalogue annonce 30 j.o., son texte 66 j.o. »
> and « 65 j.o. réellement servis ».
>
> ⚠ **Two defects the whole test suite was blind to, both found in the browser:**
> 1. **The stage picker was silently empty.** `useGetStagesQuery` was called with `pageSize: 200`
>    while `GetStagesQueryValidator` caps it at **100** — a 400, and a rejected lookup shows up only
>    as a select that will not open. Nothing in 1 297 green tests could see it: the validator runs in
>    the pipeline, and no test drives the component. Same family as the `Objectives.NotEmpty()` and
>    CNE-regex incidents — a validator rule is covered from the outside or not at all.
> 2. **The button offered an act that could only fail.** « Aucune cohorte pour ce stage cette
>    année » was displayed *and* « Rouvrir le stage » was enabled, on exactly the student the dialog
>    exists for: a 6ᵉ année revalidating a 3ᵉ année stage sits in a roster that runs 6ᵉ année stages,
>    so the command's fallback resolves to nothing and it answers `NoGroupForRevalidation`. The
>    context now carries **`FallbackCohortId`**, read through the *same* query the command falls back
>    on (`RevalidationPlanner.OwnCohortQuery`), so the dialog can tell « leave it empty » from « this
>    cannot succeed ». Two tests cover it.
>
> ⚠ **Honest limit:** `FallbackCohortId` and its UI guard are proven by tests and by type-checking,
> **not** re-verified in the browser — the live capture above predates the field, and by then every
> RTK query was cached so no fresh token could be sniffed. The 66-day proposal, the picker fix and
> the full dialog were all seen rendered.
>
> **Also seen working live:** the §33 catalogue markers on MED3 Chirurgie and Médecine, absent on
> the two control rows, tooltip reading « 2174.18 (Troisième Année Médecine) : 66j ».
>> **▶ SESSION 36 — the migrations verified on the base, and the number the Stages page was asserting.**
>
> Suite **1 289 green** (was 1 284). One commit of the whole backlog (`b879e5f`, 199 files). No
> schema change; one response gains a field.
>
> **The three migrations were already applied** — `MigrationService` ran them on the Aspire startup,
> so the warning carried out of session 35 was stale. Verified on the live base rather than assumed:
> `Cnpn1650Med3CatalogueAlignment` did **not** raise, i.e. no grid-published période locked the mode
> change; all six MED3 stages read 30 j.o. / `SingleService`; and 2174.18's own 3ᵉ année set
> preserves Chirurgie and Médecine at 66 j.o., coefficient 3. That curriculum already existed, so
> only the two missing rows were inserted and no authored figure was overwritten — the path the
> migration was written for.
>
> ⚠ **Item 1c's premise was wrong, and the mistake is worth naming.** The row said the 4ᵉ année
> counterparts « are probably empty too, so this is fresh entry rather than a copy ». They are not:
> all four carry service lists — 17 rows. « 25 of 27 stages carry none » was measured over the
> *catalogue as a whole* and then read onto a subset of four. Measuring a population and applying the
> answer to a subset is how the two independent `Any`s bug reads, in prose.
>
> **`Stage.Coefficient` / `DurationInDays` stopped being a prediction and became live data**, so the
> annotation half of `PHASES.md` §15.1 was done: `StageSummaryResponse.TextFigures` +
> `StageCatalogueFigure`. The page kept rendering the catalogue number alone while 1650.25 says
> something else, so it asserted a figure no CNPN necessarily states.
> - **Silent when the texts agree, and silent when no text mentions the stage.** A marker that fires
>   whatever the data says is noise, and noise is dismissed — which puts the real one out of sight.
>   Same rule as `ExportNotes`, and « aucun texte ne le mentionne » is not « un texte dit 0 ».
> - ⚠ **A second flat query keyed on the page's stage ids**, not a collection in the row projection —
>   that element carries no key and is the shape that killed the macro plan. Pinned by
>   `SqlTranslationTests`, and the four handler tests were proven to bite.
> - **The substantive half is untouched**: which of the two numbers is authoritative cannot be
>   settled before `Stage.LevelId` is advisory, since a stage on two levels has no single catalogue
>   row to carry a figure for either.
>
> ✅ **The lint error is fixed** — the `_key` destructure-to-omit became an explicit map to
> `StageObjectiveRequest`, which is what proves the React row identity never reaches the wire.
> ⚠ `npm run lint` is still red for **13 other pre-existing reasons** across the app (a
> `set-state-in-effect`, an `any` in `main.tsx`, a fast-refresh export rule). None was touched.
>
> **Then, on request: the 17 `AllowedServices` rows, and `SMOKE-TEST.md` §34** — a 60-75 minute
> end-to-end pass built around one fact no single screen shows: **the same `Stage` (MED3 Chirurgie,
> `Id = 2`) is owed by two populations under two texts.** The 895 inscribed in 3ᵉ année 2026-2027 are
> stamped 1650.25 by the effectivity rule (30 j.o., coef 1); the 92 in 6ᵉ année who still owe it are
> on 2174.18 (66 j.o., coef 3); the catalogue says 30 / coef 3.
>
> ⚠ **The section had to be written around what the app does not do.** The original expectation was
> that a revalidation would carry the old text's 66 days. It does not — see item 1e. Rather than
> assert something false, §34 part C makes the reader measure the served window by hand and states
> the gap. The evidence is exact: the one such window on record (18/03/2024 → 14/06/2024) is **65
> jours ouvrables**, which matches 2174.18's 66 and not the catalogue's 30.

> **▶ SESSION 35 — the CNPN area audited against clean code / clean architecture / DDD, and fixed.**
>
> Suite **1 278 green** (was 1 257). No migration, no schema change, no API change — routes and
> response shapes are identical. The live base was **not** touched.
>
> **Findings that were real defects, not style:**
> 1. `CnpnAssignment.ResolveAsync` had **no production callers** (7 tests kept it alive) and its
>    `asOfAcademicYearId` was never read in the body, while the doc comment described what it
>    anchored. Deleted; its cases moved onto `SelectVersionAsync` + the new `EntryYearDeduction`.
> 2. The entry walk-back — the single assumption the ~2,200-student backfill rests on — was written
>    **twice**, with a duplicated `YearRef` in each file. Now one pure type.
> 3. `RegistrationCnpnStamper.StampAsync` returned `Result<StampReport>` with **no failure path**, so
>    `if (stamp.IsFailure)` in five callers was unreachable.
> 4. Two stale `<see cref="CnpnResolver"/>` references to a type that does not exist (renamed long
>    ago). Silent because `GenerateDocumentationFile` is off.
> 5. `CnpnEffectivityPlanner` loaded display detail for **every** in-scope registration (~900 for one
>    promotion) when the preview prints at most 50, keyed by an `IN (…)` of Guids over a *described*
>    set. Now: classify first, then read detail for the sampled ids only.
> 6. `CnpnTargetPlanner` materialised a whole promotion's ids and used them in three `Contains`.
>    Now one reusable predicate composed as a subquery.
> 7. `ExecutionAuthorizer` — a cross-cutting rule — lived in `Application/Employees/MyServices/` with
>    **48 files outside `Employees/` importing it**. Moved to `Abstractions/Authorization/`.
>
> **The DDD half.** `CnpnVersion` was a property bag with public setters while `Curriculum` in the
> same namespace was a proper aggregate. It is now `Entity` + `init` accessors (the `AcademicYear`
> shape) with `Correct` / `DeclareEffectivity` / `WithdrawEffectivity`, two new domain events, and
> the four rules a text can decide alone moved off the handlers — including the span rule that was
> being stated twice, from opposite sides, in two different files.
>
> ⚠ **Two things worth carrying forward, both measured this session:**
> - **`AsNoTracking()` on a shared subquery reaches its host query.** Reusing the targeting selector
>   made the *apply* stamp detached students and write nothing — preview right, apply reporting
>   success, zero rows moved. Caught by two tests. A shared query must state no tracking behaviour.
> - **A forgotten `Include` cannot be caught by this suite.** Deleting the `Include` from
>   `UpdateCnpnVersionCommandHandler` left all 23 of its tests green: the in-memory provider fixes
>   navigations up from the change tracker, while PostgreSQL would hand back an empty collection.
>   That is why `Correct` takes a `CnpnSpanFloor` read from the store instead of counting its own
>   children — the guard has no unique index behind it, so the failure would be silent data loss.
>
> **Then the adjacent item, on request.** `Student.registrations` / `Student.history` — lowercase
> public `ICollection` with open setters, on an aggregate whose CNPN fields are correctly
> `private set` — became `Registrations` / `HistoryEntries` with `private set`. Nothing ever
> assigned them, and `UserConfiguration` already declared `PropertyAccessMode.Field`, so the intent
> had been recorded and the naming had simply never followed. `HistoryEntries` rather than
> `History`: a property carrying its own element type's name compiles but shadows the type inside
> the class. Also removed a commented-out `//public Guid Id`.
>
> ⚠ **No migration, but the model snapshot did need a hand.** Navigations are CLR metadata, not
> database objects, so the rename generates no SQL — but
> `ApplicationDbContextModelSnapshot` records navigation *names*, and a stale snapshot means the
> next `migrations add` silently carries the rename inside an unrelated migration. The four strings
> were updated and `dotnet ef migrations has-pending-model-changes` now answers **« No changes have
> been made to the model since the last migration »**. The per-migration `*.Designer.cs` snapshots
> are history and were deliberately left alone.
>
> **No API change from this either**: both handlers project the navigation into `StudentResponse` /
> `StudentRegistrationSummary`, so no JSON field moved. (Noted in passing, not fixed: those two
> handlers still call `.Include(...)` on a query that then projects, which EF ignores.)
>
> **Then the CNPN of the new text, dictated by the faculty during the session.** Three migrations,
> **none applied** — items 1a/1b/1c above:
> - `Cnpn1650Med3Stages` — the 3ᵉ année is **six** stages: Chirurgie and Médecine (already at that
>   level, reused) plus four brought down from the 4ᵉ année, two of which are pairings the new text
>   makes — *Dermatologie - Endocrinologie* and *Rhumatologie - Radiologie*. 30 j.o. (six weeks),
>   coefficient 1, `SingleService`. It refuses if the resulting requirement set is not exactly 6.
> - `Cnpn1650ImmersionStages` — 1ʳᵉ année *Stage d'immersion*, 2ᵉ année *Stage d'immersion soins
>   infirmiers*, 15 j.o. Those two promotions had never had a stage on record.
> - `Cnpn1650Med3CatalogueAlignment` — Chirurgie and Médecine still read **66 j.o. / PerPeriod** from
>   the legacy catalogue. It preserves those figures into 2174.18's own 3ᵉ année set *first*, then
>   aligns the catalogue. It raises on the same condition the UI refuses on.
>
> ⚠ **Why new rows and not shared ones.** In 2026-2027 both promotions run this material at once —
> the 3ᵉ année under 1650.25 and the 4ᵉ année still under 2174.18, whose Pédiatrie rotation is already
> published for that year. Sharing one `Stage` row is refused in **four** places
> (`SaveCurriculumCommandHandler`, `CreateCohortCommandHandler`, `SlotOverlapGuard`, and `StageSlot`
> carrying no level at all). Repointing the old rows would have changed the meaning of 4MED's
> published history. This is `Stage.LevelId → advisory` (Phase 15.1) becoming **due** rather than
> deferred: it will recur for every level the new text lands on.
>
> **And the rule the faculty settled while we were in there:** a failed year is annulled, stages
> included — `RegistrationStatus.AnnulsItsStages()`, read by both `OutstandingStageFinder` and the
> dossier so the two cannot disagree. Six tests, guard proven to bite. The frontend states it on the
> parcours; the level-dossier endpoint carries it explicitly and still has no consumer.
>
> **Left open on purpose:** `AllowedServices` for the four new stages is faculty data and cannot be
> inferred — item 1c, and it blocks MED3 auto-arrange.
>
> ---

> **▶ SESSION 34 — the export was faithful and unreadable in two places.**
> Both reported against a file that was correct in every cell, which is the pattern this project
> keeps hitting: *a true statement that leaves out the fact the reader came for.*
>
> 1. **« On ne voit qu'une période alors qu'on en a trois. »** Under `SingleService`
>    `SchedulePublisher` folds the `kₛ` cells of a run into **one** `ServicePeriod` — right, the
>    student stands in one service and is marked once — so the Périodes sheet showed one row where
>    the grid authored three columns. The fold stays; the document now states what it folded:
>    `Nb créneaux` (numeric), `Créneaux` (« P1-P3 ») and `Détail des créneaux` giving each column its
>    own window and worked-day count. Read through `ServicePeriodSlotCoverage`, never the lead-cell
>    FK. New: `CoveredSlotFolder` (pure), `StageAssignmentExportQueries.SlotCoverageQuery`.
>    ⚠ Still **one row per période** — a run marked once, repeated across three columns, is a note
>    counted three times by the first pivot anybody builds.
> 2. **No chef anywhere in the file.** The resolution order existed but lived as a private method in
>    `GetLevelRepartitionQueryHandler`. Extracted to `Application/Hospitals/Chefs/` —
>    `ServiceChefDirectory` (pure) + `ServiceChefProvider`, the `WorkingDayCalendar` /
>    `WorkingDayProvider` split — and both sheets now carry `Chef de service` with
>    `Origine du chef` beside it. ⚠ That second column is not decoration: 140 of 148 services name
>    their professor only in an **undated** legacy note, and printing it unqualified claims the
>    student served under a chef the base cannot date. Linking professors in Personnel upgrades those
>    rows with no code change — that path is already the top of the order.
>
> ⚠ **The as-of date is per période, not per file.** A year of rotations spans months; one date for
> the whole document prints the wrong name on half of it. That is why the tenure trail is loaded
> whole rather than filtered in SQL.
>
> Measured on the live base: **5 831 grid-linked périodes over 7 497 cells**; 833 of them (5MED
> Gynécologie Obstétrique) cover **3** créneaux each. 1 253 tests green (+22); three guards proved to
> bite by breaking them — the coverage read narrowed to the lead cell, the source-note flag forced
> false, and the consecutive-only merge removed.

> **▶ SESSION 33 — three slow screens, and every one of them was a correctness bug underneath.**
>
> Reported as performance: the planning grid takes seconds to open **and to close**, « Publier » without
> the over-capacity checkbox produces *dozens* of errors that will not stop, and « Générer le plan »
> takes a long time with no feedback — during which a lost connection or a closed tab could leave
> damaged data. All three were real, and none of them was only about speed.
>
> **1 — The grid. Closing was slow, and closing does no server work.** That is what settled where the
> cost was: the SQL behind the grid measures ~40 ms on the live base, while the response carried
> **105 cohortes × 10 colonnes = ~1 000 cells**, and the browser was mounting and unmounting a
> thousand cell components. `GetStageScheduleQuery` now returns **a page of 25 rows plus a
> `StageScheduleSummary`**, the partition filter runs server-side, and the modal drops its exit
> transition.
>
> ⚠ **Paging a matrix creates a new failure mode, and closing it was most of the work.** Every number
> on that screen drives an act on the *whole* selection — « Publier tout (N) » sends one stage-wide
> call — so an N counted from the visible rows would promise 25 and publish 90. The summary carries
> the counts, the deduplicated saturation report (bounded by columns × services, not by cohortes),
> `OccupiedSlotIds` for « nouveaux créneaux uniquement », and `PartitionUsage` **read across the whole
> stage** for the « la partition B est déjà là » warning — a question about exactly the rows the
> filter has removed. `AssignmentsPage` needed the same treatment: it folded « which periods does this
> cohorte run in » out of the grid's cells, so past page 1 every cohorte would have read as running in
> no period at all. That fact moved onto `CohortResponse.PeriodNumbers`.
>
> **2 — The « dozens of errors » was a client-side loop.** `StageDetailPage.handlePublishAll` fired one
> `PublishCohortSchedule` per cohorte, sequentially; `errorMiddleware` toasts every rejected mutation,
> so an over-capacity plan produced one red toast per cohorte, arriving one at a time as the loop
> ground on. One `PublishStageSchedule` now, like the grid modal already did — **and that one call had
> to stop refusing on the first cell**: `EnsureIntakeAsync` collects every breach and returns a single
> `Schedule.PublishRefusedByIntake` naming the count, the unforceable admissibility half, and the
> heaviest three. A single breach still gets its own specific sentence.
>
> **3 — « Générer le plan » was ~700 round trips, and it was not atomic.**
> `StudentAffectationService` read the eligible registrations **once per cohorte**, and the macro plan
> walks that loop once per concurrency block: 105 rosters × 7 stages. One read now, keyed on
> **(roster, niveau) together** — the pair is the guard, not an optimisation detail, and there is a
> test that fails when it is broken. Measured server-side: 92 ms of loop against 4 ms batched, for one
> stage, before EF and the network.
>
> ⚠ **The damaged-data worry was correct and is now closed.** The handler committed after every step —
> cohortes, then affectations and cells stage by stage — so cancelling the request between two commits
> left a plan built for the first three stages and nothing for the rest. Indistinguishable from a plan
> somebody meant. `IApplicationDbContext.ExecuteAtomicallyAsync` wraps the whole matrix in one
> transaction, through `CreateExecutionStrategy()` because Aspire enables retry-on-failure and a
> retrying strategy refuses user-initiated transactions outright. The rotation-cycle page now says
> what is being written and that interrupting costs the run but damages nothing — which is only
> truthful because of that transaction.
>
> **Tests:** 1 223 green (17 new). `PlanningPerformanceTests` covers the paged grid, the whole-selection
> counts, the unfiltered partition list and usage, the deduplicated saturation, the aggregated publish
> refusal (asserting the store is untouched after it), the admissibility half surviving the override,
> the affectation pair, and `CohortResponse.PeriodNumbers`. Four of them were **proved to bite** by
> breaking the guard and watching them fail. Three new `SqlTranslationTests` cases: two `Distinct()`
> over computed elements and a nullable FK dereferenced in a projection, all on the macro-plan or grid
> path.
>
> ⚠ **What was deliberately not done: « Dépublier toutes » is still a client-side loop** with the same
> toast-storm shape. Each of its per-cohorte refusals *names what that cohorte would lose* — périodes
> démarrées, notes, jours de présence — and an aggregate would have to be designed before it can
> replace them. It is item 19 below.
>
> ⚠ **Not verified in a browser.** Everything here is type-clean, the suite is green and the SQL was
> measured against the live base, but no human has opened the grid, paged it, or pressed « Publier
> tout » since the change. `SMOKE-TEST.md` §31 is the walk-through.

> **▶ SESSION 32d — « je ne vois toujours pas les groupes » : the export was never the problem, the roster list was.**
>
> Followed the report into the app instead of arguing from queries, and it settled in two clicks:
> **Groupe 1 — Quatrième Année Médecine, 2026-2027 → « 0 étudiant(s) · Aucun étudiant dans ce
> groupe. »** All 90 rosters of that promotion are empty, and the export was right the whole time.
>
> ⚠ **The defect was one screen earlier.** `GroupsPage` listed the 90 rosters with N°, Libellé,
> Niveau, Rotation, Année — **and no population**. So « la 4ᵉ année a des groupes » read as true, and
> the only way to learn otherwise was to open all 90 one at a time. `AcademicGroupResponse` has
> carried `studentCount` since it was written, with a comment saying it was there « so the list can
> show it without loading any student ». The list never rendered it.
>
> One column added, orange at 0 and teal above. Verified live: 2026-2027 → every row **0**;
> 2025-2026 → 12, 15, 13, 3, 7 … and « Non réparti » at **4 725**, the figure `CLAUDE.md` already
> records for that bucket. The contrast is immediate, which is the whole point.
>
> **The rule, now in `PGSH.Frontend/CLAUDE.md` §1b-bis:** *a list of containers must show how full
> each one is.* A roster, cohorte, service or partition list without a count cannot distinguish
> « découpé et rempli » from « découpé et vide », and those call for opposite next acts. Zero is a
> state, not an error — colour it, never hide it.
>
> **Worth noticing about this session and the two before it:** the same failure was found three times
> in three places — the export's blank column (32c), the roster list's missing count (32d), and the
> `RepartitionSummary.DeclaredSlotCount` / `OutsideYearCount` pair that already existed to prevent
> exactly it. **An absence has to announce itself.** It is written down twice in the backend
> `CLAUDE.md` and it was still not applied to either new surface.
>
> No backend change. Frontend only: `GroupsPage` + `CLAUDE.md` §1b-bis + `PHASES.md` 3.7.
> **`NOTES.md`, `PLANNING.md`, `SCHEMA.md`, backend `PHASES.md` unchanged — nothing moved on the
> server.** `SMOKE-TEST.md` §30 gains the roster-count check.

> **▶ SESSION 32c — the first user remark on the export, and it was right about the symptom and wrong about the cause.**
>
> « Le 4MED a des groupes et je ne les vois pas dans l'export 2026-2027 — il manque peut-être d'autres
> choses. » Audited column by column against the live base: **the file was faithful in every cell.**
> `Groupe` / `N° groupe` / `Partition` / `Source de la décision` / `Convention` are blank on all 5 932
> lines because **0 inscriptions carry a roster pointer**, nobody has deliberated a year that has just
> opened, and `AgreementType` is `None` for all 10 206 students in the base. `CIN` 5 904/5 932, `Sexe`
> 5 005/5 932 (927 rows carry `Gender = None`), `Date de naissance` 5 930/5 932 — all true.
>
> **The real state of 2026-2027:** 90 `AcademicGroup` rows for the 4ᵉ année Médecine, numbered 1-90
> with partition labels A-F interleaved, **all empty** — 0 inscriptions, 0 cohortes — and 0
> affectations, 0 slots for the whole year. The Groupes page shows the 90 rosters and *0 étudiants* on
> each (it counts `g.Registrations.Count`), so it and the export agree. Queue item 0d.
>
> ⚠ **`AutoArrangeGroupsCommandHandler` cannot have produced this** — it refuses when nothing is
> unassigned and sets `reg.AcademicGroupId` in the same unit of work. The state is reachable by
> cutting and then emptying, but **the audit trail cannot say**: roster creation, rotation-group
> assignment and « vider les groupes » are all unaudited while `PARTITIONS_CLEARED` is recorded. The
> destructive act leaves a trace and the constructive one does not. Queue item 0e.
>
> **What was actually wrong was the document, not the reads.** A column empty on every row is
> indistinguishable from a column the export forgot — the same « one state standing in for two » that
> `RepartitionSummary.DeclaredSlotCount` and `OutsideYearCount` already exist to prevent, and the
> export was built without applying it. `ExportNotes` now prints above every sheet's header:
> - the columns carrying no value in **any** row, computed from the exported rows so a column added
>   later is covered without anyone remembering;
> - and when the roster columns are among them, **which of the two causes it is** — « aucun groupe
>   n'existe encore » (découper) or « 90 groupe(s) existent mais aucune inscription n'y est rattachée »
>   (répartir). The roster count is queried only when it will be printed.
>
> ⚠ **A note that fires whatever the data says is noise, and noise is dismissed** — which puts the real
> one back out of sight. That control is a test (`A_column_that_carries_a_value_somewhere_is_not_
> reported_as_empty`), and the empty-column guard was proven to bite. **1 206 tests green.**
>
> ⚠ **Restart the API to see any of this** — and it also carries the fr-FR caption fix from item 0c.
>
> Docs: `CLAUDE.md`, `NOTES.md`, `SMOKE-TEST.md` §30, `PHASES.md`, this file. **`PLANNING.md` and
> `SCHEMA.md` unchanged — no planning arithmetic moved and there is still no migration.**

> **▶ SESSION 32b — the exports get their buttons, and the smoke test finds the defect the docs invited.**
>
> **Frontend built** (separate repo), type-clean, lint unchanged from baseline (16 pre-existing
> problems before and after).
>
> - `common/utils/downloadBlob.ts` — `fileNameFromDisposition` + `downloadBlob`. ⚠ The file name comes
>   from `Content-Disposition`, never rebuilt on the client: the server names the file after the scope
>   it actually *resolved*, and an omitted year resolves to the current one.
> - `common/utils/problemMessage.ts` — reads `errors[]` **and** `detail`. It replaced **four** private
>   copies of `detailOf`, none of which read `errors[]`, so every validation refusal had been showing
>   its useless half in `InscriptionSection`, `AdminStudentDetailPage`, `YearClosurePage` and
>   `EvaluationImportModal`.
> - `common/components/ExportButton.tsx` and `features/admin/components/StageRecordExportMenu.tsx`.
>   Two named menu items rather than a checkbox, because « état des lieux » vs « PV » is what the
>   document *is*, not a filter on it.
> - Wired into **three** screens, each passing its own scope: `StudentListPage`, `RepartitionPage`
>   (per promotion) and `AssignmentsPage` (per stage, beside « Importer les notes » — one puts the
>   verdicts in, the other takes the record out).
> - RTK: two lazy queries whose `responseHandler` **branches on `response.ok`**. Reading `.blob()`
>   unconditionally turns a problem-details refusal into an opaque Blob and loses every message. ⚠ The
>   three older template endpoints still have the unguarded shape — worth fixing when next touched.
>
> **Smoke test §30 executed against the live base** — full results in `SMOKE-TEST.md`. The headlines:
> 5 932 registrations and **all 13 per-level counts exact**; 5ᵉ MED 2026-2027 = **833**, reproducing
> the figure the one-`Any` rule was measured at; 3ᵉ MED 2025-2026 = 1 872 affectations / 3 744
> périodes / 936+936; and the multi-période rule proved on the only 293 rows in the base that exercise
> it (4ᵉ MED 2018-2019 Pédiatrie): **two spans, not one**, because 24 worked days separate the windows,
> with `Jours ouvrables` 44 against a calendar span of 82.
>
> ⚠ **Two things the base cannot show.** The *contiguous* merge has **no instance** anywhere (91 894
> single-période, 293 single-service-multi, 6 367 two-service, 1 three-plus), so that branch rests on
> `StagePeriodFolderTests` alone. And `Export.TooManyRows` is unreachable — the biggest year is 15 542
> against a cap of 25 000 — so it was exercised with a stubbed 400 in the browser instead.
>
> **The defect that stub found, and why it is worth a rule.** Every refusal toasted **twice**:
> `errorMiddleware` in orange, the component's own `notify.error` in red, same sentence. It is item 4
> of this queue and session 31b already removed it from four teardown handlers — and I wrote it again
> the same day. The reason is in the docs: `PGSH.Frontend/ARCHITECTURE.md` said 400/422 were
> « handled at the form level, **not** by the global middleware », which is the opposite of what
> `errorMiddleware` does. Corrected there, and the rule is now `CLAUDE.md` §1e: **a component must not
> toast a rejected request**, the single exception being a 404 on a *query*, which the middleware
> deliberately swallows and which a download button has no empty state to render for
> (`isReportedByErrorMiddleware`). Re-verified in the browser: one toast, in the server's words.
>
> ⚠ **The year picker is unreliable under automation** — it reverts silently and froze the renderer
> twice. Same note session 31b left. Change the year by hand, then automate the rest.
>
> Docs: frontend `CLAUDE.md` (§1e, §1f), `ARCHITECTURE.md` (the corrected error table), `API.md` (the
> two routes, with a banner that the file is otherwise stale), `PHASES.md` (Phase 3.6); backend
> `SMOKE-TEST.md` (§30 executed) and this file. **Backend `CLAUDE.md`, `NOTES.md`, `PHASES.md`,
> `PLANNING.md` were written in session 32 and need no change — the contracts did not move.
> `SCHEMA.md` still untouched: no migration.**

> **▶ SESSION 32 — two Excel exports, and the rule that a merged date span must not lie.**
>
> **Built.** Two .xlsx downloads, backend complete, 31 new tests, whole suite green at **1 201**.
>
> - `GET students/export` — the roll. One sheet, one row per **registration** of the resolved year.
>   Nom · Prénom · CNE · Apogée · CIN · Sexe · Date de naissance · E-mail · Programme · Niveau ·
>   Année · Groupe · N° groupe · Partition · Statut · Source de la décision · CNPN · Origine CNPN ·
>   Convention. Filters: `academicYearId` (omitted ⇒ current), `levelId`, `program`,
>   `academicGroupId`, `searchTerm`.
> - `GET stages/assignments/export` — the post-validation stage record. Three sheets: **Stages** (one
>   row per attempt, with note and verdict), **Périodes** (one row per période, joined by
>   `Réf. stage`), **Synthèse** (verdict counts and the class average per stage). Filters:
>   `academicYearId`, `levelId`, `stageId`, `academicGroupId`, `onlyEvaluated`.
>
> **The two design questions the user asked, and the answers.**
>
> 1. *Un fichier par (programme, niveau), ou une colonne ?* — **A column, and the filter still cuts the
>    per-promotion file.** The columns cost nothing and make a row self-describing when two exports are
>    merged or one is opened a year later; `?levelId=` produces exactly the per-promotion document with
>    the columns intact. It was a false choice. The real correction underneath it: the export is of
>    **registrations**, not of students — `Groupe`, `Niveau` and `Statut` are facts about a year, and
>    2 635 students in this base have sat in more than one.
> 2. *Comment afficher plusieurs périodes ?* — **Merge on the service, never on the dates.** A *stay*
>    is a maximal run of périodes in the same service with **no worked day between them**. 01/01→01/02
>    then 02/02→02/03 in one service is one stay and prints `01/01/2025 – 02/03/2025`; the same two
>    windows with a real hole print `01/01/2025 – 01/02/2025 · 17/02/2025 – 02/03/2025`; two services
>    print `Cardiologie → Pneumologie` against the two spans, position by position. **And the
>    multi-période fact is never carried by the string alone** — `Nb périodes`, `Nb services` and a
>    `Découpage` label are their own columns, so it is a filter rather than a reading exercise.
>    `StagePeriodFolder`, pure, ten cases.
>
> ⚠ **A gap is measured in *worked* days.** A calendar-day test calls every Friday → Monday hand-over an
> interruption, which is exactly how one column follows another. A declared holiday between two windows
> is not a hole either — the calendar is already the single answer to « how long is this really ».
>
> ⚠ **Both break conditions matter, and each alone is a different defect.** Breaking only on the service
> change — which is what `SchedulePublisher.BuildStays` does, correctly, since it works from contiguous
> grid columns — swallows a real interruption inside one printed span. Breaking only on the gap merges
> S1 → S2 into one line and loses the second service.
>
> **The other decisions worth keeping.**
> - The stage export is scoped by the **registration's** level, not the stage's: a 6ᵉ année student
>   revalidating a 3ᵉ année stage belongs on his own promotion's document. Both levels are printed, so
>   the row reads as a rattrapage instead of appearing in somebody else's file. `GetStudentLevelDossier`
>   scopes the other way on purpose — different question.
> - The year is read from `Registration.AcademicYearId`, never approximated from the périodes' dates,
>   and an omitted year is the current one.
> - Unmarked attempts are **in** the document by default: a file whose purpose is « où en est la
>   promotion » must show the holes. `onlyEvaluated=true` is the caller saying it is a PV — and it is
>   the switch the **pre-validation** export will reuse rather than a second pipeline.
> - Both exports are capped (`ExportErrors.TooManyRows`) and the refusal names the count *and* the axis
>   that narrows it. An export is the one read deliberately exempt from pagination.
> - Three flat queries, joined in memory, with the scope defined once and reached through
>   `IN (subquery)`. Pinned by two new `SqlTranslationTests` cases — the périodes of an attempt and the
>   objective scores of an évaluation are both collections, i.e. the family that killed the macro plan.
> - Nothing recomputed: `StageScoring` for the mark and the verdict, `ServicePeriodLifecycle` for the
>   state, `WorkingDayCalendar` for the durations.
>
> **Both new guards were proven to bite** — the year predicate widened to `>= 0` fails
> `An_omitted_year_exports_the_current_year_only`; the gap condition removed fails
> `A_real_gap_in_one_service_prints_two_spans_and_says_so`. Restored, green.
>
> ⚠ **No frontend.** Two routes, no buttons — see queue items 17 and 18. Verify meanwhile through
> Scalar (`/scalar/v1`) or a browser hitting the URL directly.
>
> Docs: `CLAUDE.md` (new « An export is a document » section), `NOTES.md`, `PHASES.md` (Phase 15.3),
> `PLANNING.md` (§8), `SMOKE-TEST.md` (§30). **`SCHEMA.md` untouched — no migration, no new table, no
> column: both exports are reads over the schema as it stands.**

> **▶ SESSION 31b — §29 run against the live base: the guards hold, and the base cannot reach the middle state.**
>
> Dump first: `C:\Users\LEGION\pgsh-pre-smoke29.dump` (20.5 MB, `-Fc`, outside the repo).
>
> **Steps 0-6 pass.** Every refusal reached the screen **in the server's own words with true counts** —
> « sur 8 période(s), 8 ont démarré », « Psychiatrie est engagé en 2025-2026 : 60 cohorte(s), 706
> affectation(s)… » — and after each one the store was compared against the dump and was **identical**.
> At the end, test rows removed, the base matched the dump on every table touched.
>
> ⚠ **Step 0 found 285 mismatched affectations, and they are not this defect.** All 285 share year *and*
> group number and differ only in level — the `SplitAcademicGroupsPerLevel` signature (2023-2024
> « Interne CHU » rows whose cohortes stayed on the « Sixième Année » shard). The double-affectation
> signature is provably absent: **0** (registration, stage) pairs carry more than one affectation, and
> **0** affectations belong to a student in no roster. Worth deciding what to do with the 285; they are
> pre-existing and harmless to the new guards.
>
> ⚠ **The base cannot reach the state the `DropAffectations` flow exists for.** All 105 626 périodes
> carry `IsStarted = true` and 0 are grid-linked — imported history, marked started by the importer —
> so *every* planned roster is « underway » and the middle state occurs nowhere. Steps 1, 2 and 5 were
> run against a throwaway roster seeded for it (`ZZ-SMOKE29`, since removed). Consequence: **« Vider le
> groupe » is now refused for every roster carrying history**, which is right, and rosters with no
> cohortes still empty freely.
>
> **Two defects found and fixed during the run:**
> 1. Every refusal toasted **twice** — `errorMiddleware` and the page's own `notify.error`, identical
>    sentence, because this session had just taught the page-level one to print the server's words.
>    The middleware already toasts every rejected mutation; the page-level call is removed from all
>    four teardown handlers (item 4's sweep, for these paths).
> 2. The reset dialog said « pour **l'année en cours** » while the command targets the **navbar** year —
>    routinely a past one. The refusal then came back saying « engagé en 2025-2026 », naming a different
>    year from the confirmation just read. It names the year now. A destructive confirmation must name
>    what it will actually hit.
>
> ⚠ Both fixes are type-clean and were **not** re-driven through the UI: the year picker stopped
> responding to automation near the end of the session.
>
> **Step 7 (rotation block) deliberately not run** — 0 published cells means « Supprimer le bloc » would
> succeed and destroy a real axis. It stays a human click, as item 2 already said.
>
> **▶ SESSION 31 — « Vider le groupe » left the whole plan behind, and « Supprimer la cohorte » had no guard at all.**
>
> Asked what happens when a roster is emptied while affectations exist, and whether a started stage
> should block the cohorte/bloc reset. Read the four teardown paths; three of them were wrong.
>
> **1. `EmptyGroupCommand` cleared `Registration.AcademicGroupId` and nothing else.** An
> `InternshipAssignment` is (inscription × **cohorte**), so every affectation and every `ServicePeriod`
> survived the act: still on the chefs' worklists, still in `ServiceOccupancyCalculator`'s counts,
> still in the printed répartition — against a roster page reading 0 étudiants, with nothing anywhere
> saying the two disagreed. ⚠ And it is not undone by putting the students back: a re-découpage sends
> them to *other* rosters and `StudentAffectationService` dedupes on (inscription, **cohorte**), so
> each one returns with a **second** affectation for the same stage.
>
> **2. `DeleteCohortCommand` had no guard whatsoever**, while its bulk twin refused as soon as one
> affectation left `Planned`. The safe act was the one touching a hundred cohortes; the unguarded one
> was the button beside each line. It deleted every `ServicePeriod` — « plan-generated *and* ad-hoc »,
> its own comment — and `ServiceEvaluation`, `AttendanceRecord`, `PeriodPause` and `Delocalization`
> cascade from those. A chef's marks and a term of attendance on one click, answered with 204 and no
> number.
>
> **3. `DeleteAllCohortsCommand`'s year was optional and unresolved** — a null meant "every year this
> stage ever ran", on the one command in the area that deletes rows, against a stage that keeps 563
> cohortes across six years. Now resolved through `AcademicYearResolver` like everything else.
>
> **4. The rotation block was already right.** `RotationCycleContext` counts published cells through
> `PublishedCells` (coverage table, not the FK), so apply and delete are both refused while anything
> on the axis is published — and a started rotation is published by construction. Nothing added.
>
> **Built:** `AffectationToll` / `AffectationTollReader` (`Stages/Planning/`) — one reader for
> "what is this about to destroy", over three scopes (cohortes, roster, year's rosters), so the four
> refusals cannot describe the same rows differently. Its counts are the same four the unpublish
> refusal already names.
>
> **The rule now:** nothing planned → empties silently · merely planned → refused with the count named
> (`DropAffectations: true` is the caller having read that sentence) · anything underway → refused
> outright, **not forceable**. The act that destroys marks is « Dépublier », which names its cost and
> asks twice; a roster-side button must not become the way round it. `EmptyAllYearGroupsCommand` gets
> no `DropAffectations` at all — a year's affectations are the whole faculty's planning.
>
> **Order for taking a promotion apart:** dépublier → réinitialiser les cohortes du stage → vider les
> groupes → supprimer les groupes / le bloc de rotation. Each step refuses until the one before it is
> done, and says why in a sentence naming numbers.
>
> **Tests:** `RosterTeardownGuardTests` (10) + 3 `SqlTranslationTests` cases. **1 170 green.** Both new
> `IsUnderway` guards were broken on purpose and the three refusal tests failed, then restored. ⚠ The
> success path of the two bulk handlers stays uncovered — `ExecuteUpdate`/`ExecuteDelete` are
> unsupported in-memory; their refusals are pure reads and *are* covered, which is the half that had
> no guard.
>
> **Frontend:** `emptyGroup` takes `dropAffectations` and the modal stays open showing the server's
> own sentence before the second confirmation — the shape `StageDetailPage`'s unpublish warning
> already had. Every teardown toast now prints the refusal's `detail` instead of a generic « Impossible
> de… ». `tsc --noEmit` clean; `npm run lint` adds nothing new.
>
> ⚠ **Not verified from a running frontend** — `SMOKE-TEST.md` §29 is the recipe.
>
> ⚠ **Rows already stranded by the old behaviour are still stranded.** Nothing back-fills them: an
> affectation whose registration points at no roster is invisible to every roster screen and visible
> to every chef. §29 step 0 has the query that counts them.
>
> **▶ SESSION 30b — §28 run against the live base, and the two rows it left behind.**
>
> Dump first: `C:\Users\LEGION\pgsh-20260830-195550.dump` (20.5 MB, `-Fc`, outside the repo).
> Migration `PriorEnrolment` was already **applied** — MigrationService took it at startup.
>
> **Passes: a, b, c, d, e, h, i, g-bis.** The preview steps wrote nothing; the applies moved
> `students` 10 204 → 10 206 and `registrations` 49 535 → 49 537, exactly the two rows the
> confirmation named. What the created rows prove that no test could: the provisional **Apogée**
> (`SANS-APOGEE-…`, because both identifiers are NOT NULL UNIQUE), the suffixed second address for the
> homonym, `AcademicProgram` read from the level, and `CnpnSource = Effectivity`. Re-uploading the
> same file gave `0 à créer` · `2 déjà inscrit(s)` — the idempotence September depends on.
>
> The CNE-length guard added after the audit fired on the real stack: a 17-character Apogée is refused
> with « le code provisoire « SANS-CNE-… » ne serait pas un identifiant enregistrable ». Without it
> that student would have been created and then unsaveable for ever.
>
> **g-bis confirmed the réinscription fix on real data**: `58 bloqué(s) en dernière année`, and the
> DOM holds 1000 rows — 942 « Aucune décision », **58 « Stage antérieur non validé »**. Counted *and*
> listed. ⚠ I first reported them missing, off a text capture that truncated at 335 rows; the DOM is
> the authority when a table is capped at 1000.
>
> ⚠ **Not run: f and g.** The Keycloak session expired between ticking the confirmation and pressing
> *Inscrire*, so the transfer was never applied and **`PriorEnrolments` is still 0 rows** — the one
> thing that table exists for is the one thing unexercised on real data. Nothing is half-written:
> counts verified after the fact.
>
> ⚠ **Two test students are in the live base** — `SMOKETEST01`/`SMOKETEST02`, Première Année Médecine
> 2026-2027, carrying 2 registrations and nothing else (0 assignments, 0 group, 0 history).
> `SMOKE-TEST.md` §28 has the two-statement removal.
>
> **▶ SESSION 30 — the third act of the year, and three defects my own tests did not catch.**
>
> Suite **1 157 green**. One migration scaffolded and **not applied**: `PriorEnrolment` — one new
> table, additive, nothing existing touched.
>
> Raised by the user: *« la déliberation et la réinscription ne concernent que les étudiants qu'on a
> déjà dans la base — pour les nouveaux 1MED, ou ceux transférés d'une autre faculté, ça ne marchera
> pas »*. **Structural, not an edge case.** `DeliberationPlanner` opens with
> `Registrations.Where(r => r.AcademicYearId == yearId)`; `ReinscriptionPlanner` starts from the
> closing year's verdicts. Both are right — you cannot deliberate on somebody who was not there — and
> both are therefore blind to anyone holding no registration. Nor was there anywhere else to go:
> `PGSH.Application/Students/CreateMany/` was an **empty directory**.
>
> Built `Students/Registrations/Inscription/` — the same shape as the déliberation because it is the
> same kind of act. Four writing actions partitioning on two questions (known to PGSH? same
> programme?): `NewEntrant` · `TransferIn` · `Returning` · `ProgrammeChange`, plus `AlreadyRegistered`
> as a **skip** so the file survives being re-sent with the late arrivals appended. `PriorEnrolment`
> records a transfer's équivalence. `InscribeStudentCommand` is the single-row way in.
>
> ⚠ **Three defects found by re-reading the code, not by a test — 1 144 were green at the time.**
> - **The e-mail was treated as identifying.** A newcomer whose address cell was mistyped to an
>   existing student's resolved to *that student*, silently giving him a registration under somebody
>   else's name. CNE and Apogée identify; CIN and e-mail corroborate.
> - **In-file duplication was keyed on `cne ?? appogee`.** One person on two lines, written once with
>   his CNE and once with his Apogée, passed — and `IX_Registration_Student_Year` is unique, so that
>   is a raw constraint violation at `SaveChanges`.
> - **A manufactured CNE could be one the edit form can never save.** `SANS-CNE-` costs 9 of the 20
>   characters `CnePattern` allows. Third instance of the validator-vs-existing-rows failure, after
>   the old CNE regex and `Objectives.NotEmpty()`.
>
> ⚠ **Two latent defects, both predating this work.**
> - **`RegistrationCnpnStamper.Fallback` was programme-blind.** A `CnpnVersion` belongs to exactly one
>   `AcademicProgram`, so any réorientation — including one done months ago through the ordinary
>   registration form — stamped the new registration with the text of the cursus the student had just
>   left, and `TotalYears` read from it answers « est-ce sa dernière année ? » from the wrong arrêté.
>   Where nothing of the new programme resolves, `Student.ClearCnpnVersion()` removes the stamp: null
>   means « never resolved », and keeping a false one is worse than saying less.
> - **`Students.Appogee` is NOT NULL UNIQUE, and `SCHEMA.md` said otherwise.** `IX_Student_Appogee`
>   carries a `"Appogee" IS NOT NULL` filter that reads as though absence were allowed; the column is
>   required, so the filter can never be false and `""` is a value the second student without an
>   Apogée collides on. The doc line has been corrected — it is what I wrote the import against.
>
> ⚠ **And the two address generators had already drifted.** `LegacyIdentityMapper` keeps ASCII
> **letters**; the copy written here kept letters *and digits*. « Mohamed2 Alaoui » would have been
> `mohamed_alaoui` on the 10 204 imported rows and `mohamed2_alaoui` on every new one — two namespaces
> for one faculty, and the Phase 16 re-import would have renumbered people who already log in. The
> rule now lives once, in `StudentIdentifierRules`, and states the behaviour already on disk.
>
> **All five new guards proven to bite** (disabled, confirmed red, restored).
>
> **The screen shipped too** — a third card on `YearClosurePage`, so the three acts of the year sit on
> one page, plus a « Un seul étudiant » modal. `tsc` clean, `npm run build` clean, **no new lint
> error**. ⚠ And it turned up one more: `ReinscriptionAction` was missing `FinalYearBlocked`, added to
> the backend with the final-year gate and never mirrored — its badge rendered blank and the « à
> traiter » table filtered on a literal pair, so those rows **never appeared at all**. The count said
> N and the list showed nothing, on **60 of the 686** 6ᵉ année Médecine. Backlog item 14 is closed.
>
> Docs updated: `CLAUDE.md`, `SCHEMA.md`, `PHASES.md` §15.11, `NOTES.md`, `SMOKE-TEST.md` §28,
> `PGSH.Frontend/PHASES.md` §3.5, `PGSH.Frontend/API.md` (staleness banner — it documents none of the
> last ~15 sessions of endpoints, which is how a built feature gets written twice).
>
> ⚠ **Session 29 (the chef-de-service worklist) has no entry here.** Its work sits uncommitted in the
> tree beside this one and is documented in `CLAUDE.md` and `SMOKE-TEST.md` §27; I did not write it
> and will not invent its history.
>
> **▶ SESSION 28 — the macro-plan path compiles, and one of its ends had never run.**
>
> Suite **1 017 green** (1 006 + 11 translation cases). No production behaviour changed: twelve queries
> were lifted out of the private methods that built them into named
> `internal static IQueryable<T> …Query(IApplicationDbContext, …)` methods beside their callers — the
> shape `CohortProvisioner.GroupTextsQuery` established when it was fixed — because a query built
> inside a private `async` method cannot be compiled without being executed.
>
> **Swept:** `CohortProvisioner` → `StudentAffectationService` → `RotationArranger` (with
> `GroupScheduleConflictGuard` and `ServiceOccupancyCalculator`) → `SchedulePublisher`.
> **Every one compiles. The sweep found no second defect** — which is only knowable by running it,
> and the alternative was finding out on the first publication.
>
> ⚠ **`SchedulePublisher` had never executed against PostgreSQL at all.** Not "untested" — never run:
> the Med6 rehearsal was `publish: false` and the base holds 0 grid-linked périodes, so the first real
> publication would have been the first execution of every one of its queries — the per-cohort
> publish included, which shares nothing with the stage-wide one but the class. The heaviest carries
> four navigation hops, a null-coalesce over `"niveau " + LevelId` and an enum stored as a string; it
> becomes `COALESCE(l."Label", 'niveau ' || s."LevelId"::text)`.
>
> ⚠ **A projection is not a predicate** — found while proving the new cases bite. A client-side method
> call in the final `Select` does *not* fail these tests: EF evaluates the top-level projection on the
> client by design. The same call in a `Where` throws, and both cases on that query went red. So this
> file catches a query the provider **refuses**; one that quietly client-evaluates is a performance
> question and still needs a database. `NOTES.md` (2026-08-26, session 28) has it.
>
> Docs updated: `CLAUDE.md`, `PHASES.md` §11.4, `NOTES.md`.
>
> **▶ SESSION 27e — the frontend backlog is committed, and one click is owed.**
>
> The frontend repo had been carrying sessions 23-26 uncommitted: the year-closure screen, the CNPN
> effectivity panel, academic-year management, the dossier's outcome recording and group join, the
> `loadingMiddleware` ordering fix and the verdict vocabulary. Reviewed and committed as **eight**
> commits, `tsc` clean, every screen loaded against the live API before committing.
>
> ⚠ **Two things left exactly as found**, because changing them while committing somebody's pending
> work is the wrong move: an eslint `react-hooks/set-state-in-effect` error in
> `AdminStudentDetailPage` (verified present at HEAD too, so the backlog did not introduce it), and
> that page's `notify.error` double-toast, already queued.
>
> ⚠ **`« Supprimer le bloc »` has never been clicked by a human.** The server side is covered — four
> pipeline tests and six handler tests — but the UI path is not. An hour went into what looked like a
> broken modal and was **browser automation driving a background tab**: Mantine's `Modal` mounts
> through a transition scheduled on `requestAnimationFrame`, which is paused when the tab is hidden,
> so the dialog's root renders with zero children and no error anywhere. A control modal known to work
> failed identically in the same tab, which is what settled it. `NOTES.md` (2026-08-26) has the
> diagnostic; `SMOKE-TEST.md` §25 step 7 has what is owed.
>
> **▶ SESSION 27d — the 6ᵉ année has its crossover.**
>
> Run on the real base 2026-08-26, once the translation bug was out of the way. **1 000 cells**, and
> every invariant of the block holds when read back out of the database:
>
> | | measured |
> |---|---|
> | rosters × stages | 100 rosters, each visiting all 6 stages |
> | columns per roster per stage | exactly `kₛ` — 2·2·2·2·1·1, min = max |
> | partitions concurrently in a stage | exactly `Lₛ` — 2·2·2·2·1·1 in **every** column |
> | rosters double-booked | **0** |
> | services used per column | all of them: 13/13, 5/5, 13/13, 7/7, 7/7, 6/6 |
> | spread inside a column | ≤ 1 roster (Gynéco is exactly 4·4·4·4·4) |
>
> The printed document comes out at **51 rows over 10 dated columns** (01/09/2025 → 17/06/2026),
> 0 empty cells, 510 document cells over the 1 000 roster placements — a document cell merges the
> rosters sharing a service.
>
> **The service balance is the session-25 fix holding on real data**: every service used in every
> column, nothing dumped into one. What remains is the structural over-subscription — **88 of 510
> (service × column) pairs exceed capacity, worst 30 students against 20** — which is not the
> arranger's doing: all 148 services carry the same imported default of 20 and not one quota is
> authored. It is the soft half of the rule, and it is what `AllowOverCapacity` exists for.
>
> ⚠ **Nothing is published** (`publish: false`): 0 grid-linked périodes, and the 4 128 assignments are
> the imported history plus the single one the plan found missing. This was a **rehearsal on a year
> that ends 31 August** — it proves the block end to end; it is not a plan anyone will follow.
>
> **▶ SESSION 27c — the query that never ran on Postgres.**
>
> Suite **1 006 green** (1 004 + 2 translation tests).
>
> **The finding, and the user found it in the debugger's call stack.** The Med6 macro plan hung — no
> CPU, no DB activity, no response — and it was not a breakpoint: `CohortProvisioner` was throwing
> `InvalidOperationException` « Unable to translate a collection subquery in a projection… ». It
> projected, *inside* `Select(g => new { … })`:
>
> ```
> CnpnVersionIds = g.Registrations
>     .Where(r => r.CnpnVersionId != null || r.Student.CnpnVersionId != null)
>     .Select(r => r.CnpnVersionId ?? r.Student.CnpnVersionId!.Value)
>     .Distinct().ToList()
> ```
>
> The element is a computed value with no key in it, so Npgsql cannot correlate the subquery to its
> parent. Written in session 24, when the CNPN moved onto the registration — before that it was
> `r.Student.CnpnVersionId`, a plain property access, which translates. **1 004 tests were green the
> whole time**: `UseInMemoryDatabase` runs LINQ against objects and never translates anything.
>
> **The fix** is a flat top-level query keyed on the roster (`CohortProvisioner.GroupTextsQuery`),
> folded in memory — and cheaper than the subquery it replaces.
>
> ⚠ **Why nothing surfaced.** Visual Studio was set to break on thrown CLR exceptions, so it paused
> the process *at the throw*, before `ExceptionHandlerMiddlewareImpl` — which is right there in the
> stack — could turn it into a 500. The request never completed, so the UI spun forever with no error.
> Without a debugger attached the same bug is a fast, visible 500. **This is almost certainly the
> earlier « rien ne s'affiche jusqu'à ce que je relance la stack »**: same freeze, different query.
>
> **`SqlTranslationTests` + `TestHarness.NewNpgsqlContext()`** close half of the blind spot with no
> database at all: translation happens at compile time, so a context on the Npgsql provider pointing
> nowhere answers "does this become SQL?" through `ToQueryString()`. Two cases — the fixed query
> compiles, and the shape that broke it still does not. It is not Testcontainers (nothing here proves
> the SQL returns the right rows) but it is the half that costs a 500.
>
> **▶ SESSION 27b — a block you can take back, and the guard that was reading the wrong table.**
>
> Suite **1 004 green** (993 + 11: 7 handler tests, 4 endpoint tests). The coverage test is proven to
> bite: put the guard back on the foreign key and exactly one test fails.
>
> ⚠ The endpoint tests earn their place on the binding alone: the stages reach the route as a repeated
> query parameter (`?stageIds=40&stageIds=41`) bound to an `int[]`, and an empty array there is not a
> harmless no-op — it is « supprimer le bloc » resolving to no stages. The frontend's own
> `paramsSerializer` emits the repeated form; RTK's default (`stageIds=40,41`) does not bind. The
> control mattered too: with the wrong URL every one of them 404s, and « le bloc n'existe pas → 404 »
> passes for exactly the wrong reason.
>
> **What the user asked for.** On « Bloc de rotation », selecting a promotion that already has one
> should show it, with a way to update it *and to remove it*. The first two existed (session 25's
> `GetRotationCycleQuery` restores the block and « Appliquer l'axe » replaces it — confirmed live on
> Med6, which restored « 6 stage(s) sur 10 colonne(s), appliqué le 13/08/2026 » and re-applied as
> « 60 écrits, 60 remplacés »). **Removing did not exist at all**: replacing an axis is not undoing
> one, so a block entered by mistake could only be written over.
>
> **What was built.** `DeleteRotationCycleCommand` + `DELETE levels/{id}/rotation-cycle?stageIds=…`,
> scoped to the stages of the block (a promotion holds several — the 3ᵉ année is two semesters),
> refused while published, reporting `SlotsRemoved` and `PlannedCellsRemoved`. On the page: a
> « Supprimer le bloc » control inside the restore banner, disabled with a reason while anything is
> published, behind a confirmation that names what cascades.
>
> ⚠ **The defect building it turned up.** `RotationCycleContext` — the guard the apply *and* the new
> delete stand on — counted published cells through `ServicePeriod.CohortSlotAssignmentId`, the FK that
> names only the **first** cell of a run. Under `SingleService` the trailing columns of a published run
> read as free, so the axis could have been rewritten or deleted out from under students standing in
> it. `GetRotationCycleQuery` already asked the coverage table; the read was right and the write guard
> was wrong, which is the dangerous way round. Latent today (every 6ᵉ année stage is `PerPeriod`, 0
> grid-linked periods in the base) and now the fifth caller of `PublishedCells`.
>
> **Also:** the apply and the preview now say how many planned cells the replacement destroys — they
> cascade, and the number was nowhere on screen. `TestHarness.SeedCoverage` exists so no future test
> can claim a cell is published by setting the FK alone.
>
> **▶ SESSION 27 — the gate is asked once for the batch.**
>
> Suite **993 green** (990 + 3). All three new tests proven to bite: skipping the batched gate fails the
> two refusal tests, reading a missing text as 0 years fails the stand-aside test, and letting the
> student's stamp outrank his registration's text fails the third.
>
> **The finding** (from the session-26 review). `CreateManyRegistrationsCommandHandler` called
> `EnsureMayEnterAsync` per student *inside* the loop, and each call is four round-trips — the level's
> year, his text, his whole cursus through `OutstandingStageFinder.ForStudentAsync`, and his waiver.
> Enrolling a promotion of 700 by hand was ~2 800 queries, fifteen lines above a `StampAsync` that had
> already learned to take the batch in one pass.
>
> **What was built.** `FinalYearGuard.EnsureMayEnterManyAsync` — the batch is now the implementation and
> the single-student call delegates to it, so the two cannot drift. Only the refused students appear in
> the result; an absent student may enter. Supporting halves: `OutstandingStageFinder.ForStudentsAsync`
> (with `ForStudentAsync` delegating) and a batched `TotalYearsAsync`.
>
> ⚠ **The narrowing is what makes it cheap, and it is also what keeps the single call no dearer than it
> was**: the cursus is read only for the students this level is actually the last year of, and the
> waivers only for those who then turn out to owe something. A batch where nobody is in his final year —
> the ordinary case — is **two queries whatever its size**.
>
> **Run against the live base the same day** (`SMOKE-TEST.md` §24), and it ended with the base exactly
> as it started: 0 inscriptions en 2026-2027, 0 dérogations. 60 of the 686 6ᵉ année Médecine of
> 2025-2026 still owe a stage; one call refused all 60 and wrote nothing, a mixed call refused one and
> created the other, and the refused student went into the **6ᵉ** année without objection. The
> narrowing shows in the wall clock: **722 ms** into the final year, **56 ms** into the year below,
> where neither the cursus nor the waivers are read. The manual path from the dossier refuses with the
> same sentence.
>
> ⚠ **`Contains` is right here and wrong in `ForPromotionAsync`**, and the distinction is now written on
> both: a caller-supplied list is bounded by what somebody selected; a promotion is 8 077 rows nobody
> enumerated. Reach for the predicate whenever the set is *described* rather than *listed*.
>
> Two things the rewrite had to preserve, and both are now tests rather than only comments:
> `TryGetValue` over a `Dictionary<Guid, int>` (its default is 0, which makes every year somebody's
> last), and the registration's text outranking the student's stamp.
>
> **▶ SESSION 26 — the year is a thing you can set, correct and remove.**
>
> Suite **989 green** (965 + 24: 14 handler tests, 10 endpoint tests). Both new guards proven to bite —
> disabling them fails exactly four tests, one per guard per layer.
>
> **The finding.** `AcademicYear` had a create and a list, and nothing else: designating « l'année en
> cours » was only possible as a *side effect* of creating another year, and there was no way at all to
> remove one entered by mistake. Two things the schema says that nobody had read:
>
> - **`AcademicGroups.AcademicYearId` is `CASCADE`** while five other foreign keys are `RESTRICT`. So an
>   ungated delete is destructive in two different ways and neither announces itself — a raw 500 on the
>   restricting ones, and the year's rosters silently gone on the cascading one.
> - **Two years sharing a day was never prevented**, and `ServiceOccupancyCalculator` bounds a year by
>   its *dates* rather than by `AcademicYearId` — deliberately, so a slot stamped with the wrong year
>   still surfaces. That choice is only safe while the two cannot disagree; overlapping years count
>   every slot in the overlap twice against a service's load. The base satisfies the rule; nothing was
>   enforcing it.
>
> **What was built.** `AcademicYears/Manage/` — set current, update, delete, plus
> `AcademicYearCalendarGuard` shared with create. `AcademicYear` became an `Entity` with `init`
> accessors over backing fields, so an object initialiser still builds one but only
> `MakeCurrent`/`Relinquish`/`Rename`/`Reschedule` can change one afterwards.
>
> ⚠ **Two bugs found in existing code by building it:**
> - `CreateAcademicYearCommandHandler` demoted the sitting year with `ExecuteUpdateAsync`, which the
>   **in-memory provider does not support** — so the one part of that handler that can leave the base
>   with no current year at all could never be reached by a test. Both handlers now demote through the
>   aggregate, in their own `SaveChanges` before the promotion (the unique filtered index is checked at
>   the end of each statement, so ordering is not EF's to choose).
> - `LegacyImportPlanner` flipped `IsCurrent` on the last year directly. The compiler caught it the
>   moment the property stopped having a setter — which is precisely the write the change exists for.
>
> **▶ SESSION 25 — a service holds who is standing in it, so the balance is per column.**
>
> Suite **965 green** (926 + 39: 7 column-balance tests, 7 rotation-config tests, 11 carried over from
> the guard's fallout, and 14 endpoint tests for `groups/assign-student` and
> `registrations/{id}/outcome` — the two routes that had handler coverage and no pipeline coverage). Frontend `tsc --noEmit` and `npm run lint` clean in the changed files.
>
> **The finding, and the user found it in the printed document.** `RotationArranger` built the
> capacity-weighted service queue over the cohorts of the whole *call* and indexed each cell by its
> global position. The crossover leaves one partition free per column — every other cell is refused
> because the group is already placed elsewhere — and partitions are contiguous in the ordering while
> each service owns a contiguous run of the queue. So the free partition fell entirely inside one
> service's run. Measured on **5MED Psychiatrie 2025-2026**: all nine columns in a single service,
> 69-85 students against a capacity of 20, two of five services unused all year. Reproduced 9/9 in a
> replay of `queue[(ci + phase·⌊n/T⌋) mod n]`. **Nothing reported it** — 60 cells written, no failure,
> and the conflicts it counted are the ones the crossover is made of.
>
> **What was built.**
> - The queue is built **per column**, over the cohorts that column actually writes, and the leftover
>   tie-break rotates by the column's phase. With 148 services on one imported capacity a stable
>   tie-break gave the same leading services the remainder in every column of the year.
> - The step is at least 1 — `⌊m/cycleLength⌋` is 0 whenever a column set is smaller than the cycle,
>   which froze a `PerPeriod` run into one service.
> - `StageWouldFillEveryColumn` refuses the unscoped arrange on a stage nothing has crossed into,
>   narrowed twice: only when the call names neither partition nor window, and only when another stage
>   of the promotion declares the same windows.
> - `GET levels/{id}/rotation-cycle` reads the block back into its form, from the axis on disk.
>   `RotationPeriodsSource` says whether kₛ came from the apply, from the cells, or from nowhere.
>
> **Verified live**: the user re-ran Psychiatrie between turns — five services in every période,
> 12/12/13/11/12 over the year, exactly what the fix projects.
>
> **The repair is done.** Measured against the live base 2026-08-24: the catastrophic form existed
> only in Psychiatrie. What remained was the frozen tie-break, repaired the same day —
> Urologie 18·15·9·9·9 → 13·12·12·12·11, Ophtalmologie 24·18·18 → 21·20·19, ORL 33·27 → 30·30, and
> Neurologie from 7 of 8 services to 8 of 8. Cell totals unchanged and 0 rosters double-booked, so
> only the service assignment moved.
>
> **▶ SESSION 24 — the CNPN moved off the student and onto the registration.**
>
> Suite **926 green** (897 + 29: 18 effectivity handler tests, 5 aggregate tests, 6 endpoint tests).
> Frontend `tsc --noEmit` clean; `npm run lint` clean in all changed files.
>
> **The finding.** `Student.CnpnVersionId` is one stamp for a whole cursus, so a level's requirement
> set was always resolved from where the student stands *today*. That makes one real case
> unrepresentable: **a 4ᵉ année student still owing two stages from his 3ᵉ année must be judged under
> the 3ᵉ année he actually sat**, and reshaping that level for the promotions behind him silently
> changed his debt. A second gap: the faculty's actual cut is « la 3ᵉ année de 2026-2027 et en
> dessous » — a statement about (level, year) that deliberately catches the repeater and spares the
> student one year ahead of him — and a one-shot `CnpnTargeting` run leaked on repeaters and returners
> because somebody had to remember to re-run it each September.
>
> **What was built.**
> - `Registration.CnpnVersionId` + `CnpnSource` — the governing text for one (student, level, year),
>   resolved once at creation and frozen. Migration `RegistrationCnpnAndLevelEffectivity` backfills the
>   six imported years from the student's stamp, marked `Backfilled` (not `StudentStamp`: nobody was
>   asked at the time). Nullable, and staying that way — ~2,200 students carry no stamp.
> - `CnpnLevelEffectivity` + `Cnpn/Effectivity/` — author, list, delete, preview, apply. « et en
>   dessous » is one row per level, never a stored comparison.
> - `RegistrationCnpnStamper` — one implementation of the resolution, used by `CreateRegistration`,
>   `CreateManyRegistrations`, `ApplyReinscription` and the late-apply path alike.
> - Consumers moved to `r.CnpnVersionId ?? r.Student.CnpnVersionId`: `CohortProvisioner`,
>   `AutoArrangeGroupsCommandHandler`, `DeliberationPlanner`, `RecordRegistrationOutcomeCommand`,
>   `GetStudentRegistrationsQuery`.
> - Frontend: `CnpnEffectivityPanel` on the CNPN page (above targeting — it is the mechanism that
>   should normally be used), and a per-year CNPN badge on the admin student dossier.
>
> **The migration is applied and the backfill is verified.** Measured 2026-08-18 on the live base:
> **43 605 registrations stamped, all `Backfilled`, 0 divergence from the student stamps, 0 nulls.** The
> backfill changed no behaviour — it froze what the application was already computing.
>
> **Smoke test executed** (`SMOKE-TEST.md` §20, steps a–d), and the base was **restored**: 0 effectivity
> rules, student totals unchanged at 6 460 / 1 980 / 1 745. The catch-up preview was run against real
> volume and reconciles exactly — **936 concernées / 936 à re-rattacher / 936 étudiants / 0 année
> close**, matching the 936 rows of 3ᵉ année Médecine 2025-2026 — and provably wrote nothing.
> §20e (actually applying it) was **not** executed: it re-stamps 936 registrations and moves 936
> confirmed student stamps, which is the faculty's call.
>
> ✅ **The defect found by that pass is fixed, and it was not in the CNPN feature.**
> `src/app/loadingMiddleware.ts` dispatched *before* `next(action)` — a re-entrant dispatch that
> notified every subscriber while the action in flight was still unreduced, so components cached a
> `pending` snapshot of a query the store already held as `fulfilled`. It self-corrected wherever a
> later dispatch followed, so only the **last query to settle on a page** stayed stale — which on the
> CNPN page was the effectivity table. Forwarding first fixes it; verified live. ⚠ **App-wide and
> long-standing: other screens may have been showing stale data the same way.**
>
> **Not done, deliberately** (the session was scoped to the CNPN alone): the year-segregation audit,
> academic-year update/delete, and the Inscriptions screen (`AdminLayout.tsx:83`, still `soon: true`).
> `ReinscriptionReport` also gained no CNPN counters — the rollover stamps correctly but does not say
> how many students a rule moved. The rule's own preview covers the same ground before the fact.
>
> ---

> **▶ SESSION 24 (cont.) — the last year now refuses to begin on an unvalidated stage.**
>
> Suite **937 green** (926 + 11). `OutstandingStageFinder` reads what a student owes across his whole
> cursus — the déliberation's existing check only ever saw the deliberated year, so a 6ᵉ année owing a
> 4ᵉ année stage was invisible. `FinalYearGuard` turns that into a refusal on **all three** paths that
> create a registration (réinscription, single, bulk), and `FinalYearEntryWaiver` is the audited
> exception: keyed (student, year), reason required, **snapshot of what was owed** at grant time,
> refused when nothing is owed, irrevocable once used. `ReinscriptionReport` counts blocked *and*
> waived.
>
> ⚠ **A bug the tests caught, worth remembering:** `GetValueOrDefault` on a `Dictionary<Guid, int>` of
> final years returns **0**, not null — so every student with no CNPN on record read as "his text runs
> 0 years", making every year his last. The guard fired hardest on precisely the case it was written to
> stand aside for. `TryGetValue` now, plus a `> 0` check.
>
> Migration `FinalYearEntryWaiver` is a **new table only** — no data change, nothing to back up beyond
> routine.
>
> ---

> **⚠ THE LIVE BASE WAS CHANGED THIS SESSION** (backup: `%TEMP%\pgsh-20260818-203100.dump`, verified
> restorable, 19 MB / 34 tables — also at `/tmp/` inside the `postgres-0fae29d8` container).
>
> Three CNPN effectivity rules were authored for 1650.25 (1ère année from 2024-2025, 2ème from
> 2025-2026, 3ème from 2026-2027) and the first two applied. **21 registrations moved onto the
> six-year text** and now read `CnpnSource = 'Effectivity'`; the 1 981 already-correct rows were left
> untouched. Student stamps: 2174.18 6 460 → 6 440, 1650.25 1 980 → 2 001. The 1ère and 2ème années
> of 2025-2026 are now wholly on 1650.25. Full detail and the SQL in `SMOKE-TEST.md` §20g.
>
> The 3ème année rule fires at the next réinscription: repeaters re-entering the 3rd year get the
> six-year text, students passing into the 4th keep the seven-year one.
>
> ⚠ **One of the 21 was a data fix**: a Médecine registration stamped with the Pharmacie text. **56
> more like it remain** and need a decision — item 7 above.
>
> ---

> **State of the base after session 23:** unchanged except that **2026-2027 now exists** (not marked
> current). 0 verdicts recorded, 0 registrations in 2026-2027 — the one-student rollover test was
> reverted. `SMOKE-TEST.md` step **19** says exactly what was executed and what could not be.
>
> ⚠ **Everything from sessions 18-23 is still uncommitted.** `git status` is the inventory.
>
> ---

> **▶ SESSION 23 — the year could not be closed from the app at all, and the canvas was the wrong shape.**
>
> Suite **897 green** (859 + 38, incl. 8 endpoint tests). Frontend builds, `tsc --noEmit` clean.
>
> **0 · The finding that framed the session.** 14.3a and 14.3b were built, tested and documented — and
> **had no UI whatsoever**. No page, no API slice, nothing: grepping the frontend for *deliberation* or
> *réinscription* returns comments. Every route was reachable only with a bearer token. So "nobody in
> the running app can close a year" was the actual state, and it is why re-shaping the canvas cost
> nothing: the redesign landed *before* the screen was written.
>
> **1 · The canvas is a list of exceptions now, and it covers the year.** The user's framing was right
> and the reason it is safe is worth writing down: a student holds **one registration per academic
> year** (unique index), so matching a CNE across every level of one year is exactly as unambiguous as
> within one promotion — it is matching across *years* that breaks, and that is still impossible.
> `DeliberationScope(LevelId?, AcademicYearId?, DefaultUnlistedToAdmis)`; routes moved from
> `levels/{id}/deliberation*` to `deliberation*`.
>
> Four rules the default lives or dies by, all covered by tests that were verified to bite:
> - **"Is this his last year?" is asked per student, from `CnpnVersion.TotalYears`** — never per level.
>   From 2026-2027 one 6ᵉ année Médecine holds both texts, so the level cannot answer it. Below every
>   text's final year the answer is the same whichever text applies, so an unstamped student needs no
>   stamp to be promoted safely.
>   ⚠ **What happens at or above that year changed the same day — see 5 below.** It shipped as
>   « default to Diplômé, and block on an unstamped student »; it is now « promote, never graduate,
>   and count them ». If you read only one thing here, read that.
> - **The default never overwrites a verdict already recorded**, not even an inferred one. Otherwise
>   re-uploading last week's file after twelve hand corrections silently flips all twelve back. It is
>   also what makes the import re-runnable, the way the réinscription is.
> - **`ConfirmedDefaultCount`, not a checkbox.** The risk is the student nobody named, and a
>   registration created *between the preview and the apply* adds one. The number the operator saw is
>   echoed back and refused on mismatch.
> - **`Level.IsPromotion`** — « Retrait » has no year to clear, and a year-wide default would have
>   promoted the withdrawn.
>
> **2 · Réinscription is year-wide too**, `levelId` optional. Rows are ordered **attention-first**
> under a cap: a bounded report whose cap can hide the one row somebody must act on is worse than an
> unbounded one.
>
> **3 · Two flexibility paths the exceptions file makes mandatory rather than nice.**
> - `Registrations/Outcome/` — record or **reopen** one student's verdict. Re-uploading a promotion's
>   file must never be the way to fix one row. ⚠ Found on the way: **`UpdateRegistrationCommand` wrote
>   `Status` directly**, leaving `OutcomeSource` null, so the edit form read « Admis » while the
>   réinscription reported « aucune décision enregistrée » and refused to carry the student over.
>   Neither screen was wrong about what it read. Routed through `RecordYearOutcome` / `ReopenYear`.
>   Reopening reports `LaterRegistrationExists` and deletes nothing — that row can carry a group,
>   cohorts and published périodes.
> - `AcademicGroups/Join/` — **joining a roster is not transferring between two.** Every step of
>   `TransferStudentCommand` filters on assignments a newcomer does not have, so it put him on the
>   roster with no cohorte and no période: a student the planning had never heard of, in a group that
>   looked correct. `LateArrivalScheduler` materialises **only windows that have not closed** — he owes
>   the October stage and it shows unserved, but nothing claims he stood in a service on days he was
>   not enrolled (`StagesAlreadyOver`). Opposite choice from `MaterializeAtTargetAsync`, and rightly:
>   a transferred student really did serve those periods, with another group.
>
> **4 · Frontend.** `YearClosurePage` (Académique → « Clôture & réinscription ») drives both acts on one
> screen, with the per-promotion breakdown the confirmation is actually read from. `AdminStudentDetailPage`
> gained the verdict select, « Rouvrir l'année » and « Affecter à un groupe ». ⚠ **`RegistrationStatus`
> was still the pre-14.3a five-value union** — `Graduated` and `Excluded` missing — and both status maps
> were exhaustive `Record<>`s, so widening the type is what found them; a graduate would have rendered
> blank. `StudentRegistrationResponse` gained `outcomeSource`, `outcomeRecordedOn`, `academicGroupId`
> and `academicGroupLabel`: the dossier could not otherwise tell a pronounced verdict from a typed one.
>
> **Mutation-checked**, each breaking exactly the intended tests: the already-decided skip, the
> confirmation count, and the closed-window rule.
>
> **5 · Executed against the real base the same day** (smoke steps 19a/19b, read-only paths). Every
> figure reconciles against SQL: 8,077 registrations − 3 named − 1 already-decided = **8,073 admis par
> défaut, 2,016 diplômés**, and the row that proves the per-student CNPN rule is *Sixième Année
> Médecine: 686 admis / 2 diplômés*. The refusal path was exercised too (unknown CNE + « Peut-être »):
> two diagnosed rows, apply disabled, nothing written.
>
> ⚠ **And it found the thing no test could.** The default is right for years 1–6 and **wrong for a
> final year**: 855 of the 1 657 in 7ᵉ année Médecine have been in the 7ᵉ année before (132 of them
> four times), and 74 of 356 in 6ᵉ Pharmacie. The 7ᵉ is the thesis year — students sit there until
> they defend, and PGSH has no record of a defence — so « silence = diplômé » would graduate **at
> least ~930 students who are simply still enrolled**. An exceptions file only works where the
> exception is rare; in a final year it is reversed, and the list the faculty actually holds is the
> *defence roll*. **Nothing was applied**, and the user chose the fix the same day — **the default
> promotes and never graduates** (PHASES **14.3e**). Anyone who may be in his last year is counted
> (`FinalYearUndecided`) and left untouched.
>
> ⚠ **The change removed two concepts instead of adding one.** `DefaultIssues` existed only to block
> the file when an unstamped student sat on a possibly-final year; nobody in a possible final year is
> decided for any more, so that student needs no special case and the import lost a blocking condition
> it never needed. `DefaultedGraduations` went with it — the default writes exactly one outcome now.
> *When a rule stops guessing, the machinery built to police the guess should disappear too; if it does
> not, the rule is still guessing somewhere.*
>
> **Re-verified live after the change:** 6 057 admis par défaut / 2 016 en dernière année, and
> *Sixième Année Médecine* splits **686 / 2** — the two students on the six-year text, which is the
> per-student rule visible on real data.
>
> ⚠ **Also measured: the preview took >30 s year-wide.** `FinalYearByStudentAsync` and
> `StudentsWithUnvalidatedStagesAsync` shipped 8,077 ids back down in `Contains(…)`; both now filter by
> the same (year, level) predicate that selected the registrations — 45 ms as a join, and it cannot
> drift from the scope. Fixed and confirmed live: the same preview now returns in seconds.
>
> ### Still open after this session
>
> 1. **The year-wide apply is still not executed**, deliberately: it writes 6 057 verdicts and there is
>    no undo but a restore. Everything up to the confirmation is verified. **2026-2027 was created**
>    during the run (not marked current) and the rollover verified on one student, then reverted — the
>    base is back to 0 verdicts and 0 registrations in 2026-2027.
> 1b. **19e (joining a roster) is not executable on this base.** Every current-year registration has a
>    group, and the only group-less one reachable is what the rollover creates in 2026-2027 — a year
>    with no groups, cohorts or published grid. Its interesting half needs a published schedule, and
>    the base holds **zero** grid-linked périodes anywhere. Covered by unit tests only.
> 1c. ⚠ **The final-year rule was changed mid-session (14.3e) and its live numbers re-verified**:
>    6 057 admis / 2 016 en dernière année, with 6ᵉ Médecine splitting 686 / 2. But the *canvas* copy
>    and the reference tab have only been read, not used by anyone to actually name a graduate — the
>    "upload the defence roll" flow has never been walked.
> 2. ~~No endpoint coverage for the new routes.~~ **Added**: `DeliberationEndpointTests`, 8 tests.
>    The one that matters is `Silence_is_a_verdict_only_when_the_flag_is_actually_sent` — the same
>    upload twice, differing only by `defaultUnlistedToAdmis`, with the unnamed student telling them
>    apart. A handler test builds the scope directly and can never catch that flag failing to bind, and
>    the failure is silent both ways. Verified to bite by hardcoding the flag to `false`: exactly the
>    three tests that depend on it fail. Also covers the multipart path (a non-workbook is a 400, not a
>    500), the preview writing nothing, and 401 vs 403.
> 2b. **The group-join and single-outcome routes still have no endpoint test.**
> 3. ~~The exceptions canvas has never met 5,000 rows.~~ **It has**: the year-wide canvas over 8,077
>    registrations is a 321 KB workbook and downloads without a perceptible wait. Three tabs as
>    designed (`Déliberation` / `Étudiants (référence)` / `Mode d'emploi`).
> 4. Testcontainers, still. Items 2–3 of session 22 unchanged.
> 5. ⚠ Pre-existing lint error in `AdminStudentDetailPage.tsx` (`setState` in an effect, the edit-student
>    modal) — untouched, but it now sits at a different line number.
>
> ---

> **▶ SESSION 22 — a guard nobody can reach, and a guard everybody switches off.**
>
> Two defects of the same family: a rule that is *written* correctly and *enforced* never. Suite
> **859 green** (852 + 5 + 2). Sessions 18-21 are now **committed and pushed** on
> `cnpn-versioning-and-year-scoping` — four commits by session, plus these.
>
> **1 · `Levels.NotAPromotion` could not be checked, so it wasn't — twice.** The smoke step said
> "force the act anyway". But once « Retrait » stopped being offered in the pickers — the session-21
> fix working — the refusal became unreachable through the app, and firing it headlessly needs a
> bearer token. **A guard that can only be checked by hand, and can only be reached by defeating
> another guard, will not be checked.** The answer was not a better way to get a token.
>
> New **`PGSH.Tests/Integration/`**: `ApiFactory` hosts the real `Program.cs` in-process
> (`WebApplicationFactory`), so a test reaches a route the way a browser does — routing, the
> required-ness of a query parameter, model binding, authentication, `SyncUserMiddleware`, the
> exception handler, the `Result.Failure` → problem-details mapping. Five tests on
> `POST groups/assign-partitions`:
> the marker refused **400 / `Levels.NotAPromotion`**; **and nothing written while refusing** — the
> point of the whole file, since a guard ordered *after* the write returns the same `Result.Failure`
> and passes the handler test; a real promotion still cut (without that control, a route that 400s on
> everything satisfies both refusal tests); `levelId` omitted refused rather than applied year-wide;
> and an anonymous caller **401** — the authorization layer had never been asserted anywhere.
>
> ⚠ Verified the tests *bite* by breaking the guard and confirming they fail. That run also exposed
> the store leaking between tests — rows one test wrote took three unrelated ones down, hiding which
> assertion broke — hence `ResetAsync` per test. The same mutation now fails exactly two.
>
> ⚠ **Two constraints.** The store is still InMemory: this closes the *pipeline* blind spot, not the
> *store* one — **Testcontainers is still not built**, so a green suite is not proof a query runs on
> PostgreSQL. And `PGSH.Tests` now references `PGSH.API`, so **`dotnet test` fails with MSB3021 while
> the Aspire stack is running**; build elsewhere with `-p:BaseOutputPath=<tmp>/` and do *not* also set
> `BaseIntermediateOutputPath` (MSB4006, circular dependency).
>
> **2 · `AllowOverCapacity` was switching off a rule that is not negotiable.** One checkbox governed
> both "this service is over its number" and "this service does not take 1ère année". The base is
> structurally over-subscribed — **233 of 353 planned cells are over capacity (66%)** — so the
> override is ticked as a matter of routine, and the hard rule was switched off every time it was
> reached. *A rule enforced only when nobody needs the override is not enforced.*
> `EnsureCapacityAsync` → **`EnsureIntakeAsync`**, called unconditionally: admissibility is checked
> whatever the caller asks for, occupancy only when the override is off. The refusal now **says it
> cannot be forced**, and the checkbox — relabelled « dépassement d'**effectif** » — no longer claims
> a power it lacks; its description literally promised to force a service that refuses the promotion.
> The occupancy lookup is built only when a number will actually be read, so the common publish does
> no more work than when the flag skipped everything.
> Confirmed by mutation: putting the override back in front of the admissibility check fails the new
> test and **only** that test.
>
> ### Still open after this session
>
> 1. **Testcontainers.** `Integration/` proves the pipeline; nothing proves the SQL. FK constraints,
>    unique indexes, `OnDelete` and query translatability are still invisible to every test.
> 2. **Only `assign-partitions` has endpoint coverage.** The harness is the expensive part and it is
>    built; extend it where a guard is unreachable by hand or ordering matters, not everywhere.
> 3. Items 2 and 3 of session 21 below (the unreproduced error-boundary trip; the gap-fill card still
>    never exercised) are unchanged.
>
> ---

> **▶ SESSION 21 — the partition count was being read off a subset, in three different places.**
>
> ⚠ Numbered 21 because [`SMOKE-TEST.md`](SMOKE-TEST.md) already attributes steps **12j**, **12k/12l/15**
> and **16** to sessions 18, 19 and 20 — the roster/promotion split, the service occupancy view and
> `Stage.RotationMode`, all sitting uncommitted in the working tree. **Those three sessions have no
> entry here**; read their smoke steps and CLAUDE.md for what they do.
>
> Session 17 left five open findings. Three were the same defect wearing different clothes: **a count
> taken from a subset and used as if it described the whole.** Suite **852 green** (841 + 11).
> **Verified live against the real base** (admin session, 2026-08-16, migration applied + API restarted)
> — smoke step **17**, executed.
>
> **1 · Med6's lopsided partitions (session 17, finding 2) — cause found, and it was not
> `AutoArrangeGroups`.** That handler never writes `RotationGroup` at all; the suspicion recorded last
> session was wrong. The write is `RotationArranger`'s gap-fill, and it was passing
> `PartitionAllocator` **the cohorts of the stage being arranged** rather than the promotion.
> `BuildLabels` takes "the existing partition count" from the labels it is shown, and a stage routinely
> reaches only part of its promotion — `CohortProvisioner` skips what a text does not require, and
> cohorts are provisioned stage by stage. So a promotion cut into ten, seen through a stage whose
> cohorts happened to carry only A and B, *is* a promotion cut into two: every unlabelled roster went
> into those two, permanently. **A = 42, B = 42, C–J = 2 each** is exactly 80 rosters filled 40/40 on
> top of a clean A–J × 2. The balance was wrong for the same reason — "fill the smallest partition"
> measured over a subset is not the promotion's smallest.
> The mirror case is worse because it is silent: a stage whose own cohorts carry no label made
> `alreadyCut` false, so a legitimate partition target was refused as *not partitioned* and the rosters
> stayed unlabelled — invisible to every partition filter downstream.
> New `PromotionPartitioning` reads the cut from (année, niveau). ⚠ **An arrange still labels only the
> rosters it is actually placing**: the count and the balance come from the whole promotion, the write
> does not, because partitioning a roster this arrange never touches is `AssignRotationGroupsCommand`'s
> act, with its own guards and its own audit entry.
>
> **2 · `levelId` was optional on the cut and the clear, and year-wide it reached everything.**
> `AssignRotationGroupsCommand` / `ClearRotationGroupsCommand` filtered on the level *only if one was
> given*. Without it: every promotion of the year cut in one act — each with its own partition count,
> folded by `BuildLabels` into a single one for all of them — and « Non réparti » along with them, the
> one roster that belongs to no promotion and whose labelling moves 4,725 students as a single body.
> CLAUDE.md already claimed « AssignRotationGroups can no longer reach it »; it could. Both commands now
> take `int LevelId`, so the compiler refuses the year-wide call and `?levelId=` is required on the two
> endpoints. Every existing caller already sent it.
>
> **3 · The Plan macro tab counted partitions off a 200-row page (session 17, finding 4).**
> New `GET groups/partitioning` (`GetPromotionPartitioningQuery`) returns each partition's membership
> and the unlabelled rosters, counted over the whole promotion — two integers per roster, no student
> payload. The page now reads `partitions`, `groupCountByPartition`, the unlabelled count and its
> numbers from it; the bespoke "first 12… +N" truncation is gone, replaced by `GroupNumberRanges`, so
> the numbers print the way the répartition prints a cell.
>
> **4 · « L'année en cours » is now a singleton the database enforces** — `IX_AcademicYear_IsCurrent`,
> unique with filter `"IsCurrent"` (migration `PartitionScopeAndIndexGaps`). `AcademicYearResolver`
> takes the *first* row flagged current, so two flagged at once means two screens disagreeing about
> which promotion they show with nothing to say so. The migration demotes any extras first (highest
> `Id` wins, which is what `CreateAcademicYear` would have left) — it should touch zero rows.
>
> **5 · Two Phase-13 items were never real.** `Registration.LevelId` and `Registration.AcademicGroupId`
> are not missing indexes: EF Core creates one per FK by convention, and both are in the snapshot and
> in the database. Scaffolding the "fix" produced a `RenameIndex` and nothing else. Recorded in
> PHASES.md rather than silently dropped. The 2026-08-06 audit list above it is also **all closed** —
> checked in code, not assumed.
>
> **6 · « Retrait » is a status wearing a level's clothes, and it was offered as a promotion.**
> Raised as "damaged Access data" — it is not. `CODE_N = 'MED00'` marked a *withdrawal* instead of a
> year, and `LevelMapper` kept it as a `Level` (year 0) on purpose so the registrations and the stages
> already served that year survived. The data is coherent: 12 registrations, **all** `Withdrawn`, 8
> carrying real périodes, parcours reading 1ère → 2ème → 3ème → Retrait, and two students who came back
> afterwards. It is also unrepairable — MED00 *replaced* the real year in the source.
> What it cost: the marker appeared in every promotion picker, and one of its rosters carried partition
> **E** (an artefact of `SplitAcademicGroupsPerLevel` copying the folded roster's label onto each
> shard). `CnpnTargetPlanner` had already been forced to special-case year 0 by hand — the third such
> exception is now a domain rule instead: **`Level.IsPromotion`**, with `Levels.NotAPromotion` refused
> by *assign* and *auto-arrange*. ⚠ The **clear is deliberately not refused** — it is how a label
> already on a marker comes off, and it is what removed the live one. Reads split on intent:
> `GetLevelsQuery.PromotionsOnly` is **off** by default (the dossier and the parcours must still name a
> withdrawn registration's level) and the planning pickers pass it via `getPromotionLevels`.
>
> **Contract changes to know about:** `POST groups/assign-partitions` and `DELETE groups/partitions`
> now **require** `levelId`; new `GET groups/partitioning?levelId=&academicYearId=`;
> `GET levels` accepts `promotionsOnly`.
> **A migration is pending** (`20260816200851_PartitionScopeAndIndexGaps`) and **the API must be
> restarted** before any of this shows.
>
> Recipe: [`SMOKE-TEST.md`](SMOKE-TEST.md) step **17**.
>
> ### Still open after this session
>
> 1. ✅ **The lopsided promotions were repaired** (2026-08-16, with the user's go-ahead): 4Med and
>    5Pharma cleared and re-cut into 9 *Alterné* → **8 rosters per partition** on both. Neither had a
>    planned cell or a published period, so nothing depended on the old labels, and nothing else moved
>    — 13,604 cohortes, 860 cellules, 98,555 affectations, 105,626 périodes, identical before and after.
>    Every other promotion was checked and is even (3Med 40/40, 5Med 7×6+6×3, Med6 10×10).
> 2. **The unexplained error-boundary trip** on the first Aïd date-move save (session 17, finding 1) —
>    still not reproduced, still nothing to act on until it recurs.
> 3. **The gap-fill card still cannot be shown** with no unlabelled roster anywhere. Clearing Med6 for
>    the repair above is the natural moment to exercise it.
>
> ---

> **▶ SESSION 17 — the three open findings of session 16, closed.**
>
> All four were "the system knows, and does not say". Suite **783 green** (773 + 10).
> **Verified live against the real base** (admin session, 2026-08-13, after an API restart) — smoke
> steps **12g**, **12h**, **12i**. Two gaps are named in those step headers and in "Open findings".
>
> **1 · The répartition told you nothing had been planned when the periods existed.**
> `PeriodAxis` was fed the *cells*, so a level whose axis had just been applied — 60 slots, nothing
> arranged — produced zero columns and printed « Aucune période n'est planifiée ». Indistinguishable
> from an apply that had failed, which is what it looked like.
> Now the axis is built from `declaredSlots ∪ cells`: a period authored but not yet arranged keeps its
> column and shows its hatched holes. `RepartitionSummary.DeclaredSlotCount` separates the two empty
> states, which call for **opposite acts** — lay an axis, or arrange into the one that exists.
> ⚠ Knock-on, deliberate: a *partially* arranged level now prints the unarranged periods too, so
> `EmptyCells` (and its orange alert) rises. Those cells were always holes; the table was hiding them.
> The cells' own windows are unioned in rather than assumed to be a subset — a cell reaches the level
> through its *cohort*, so a slot belonging to some other stage would otherwise drop out of the table.
>
> **2 · Gap-fill had no button.** `AssignUnlabelled` has always existed and has always been the safe
> act — it touches only `null` labels, so it cannot move a group an existing plan placed. But the UI
> showed the assign form *only while no label existed*, so level 6's 20 unlabelled groups were
> repairable only through « Redécouper » (a full re-cut) sitting beside « Supprimer les partitions ».
> That adjacency is what cleared level 3 last session. Now: a teal « Compléter les groupes sans
> partition » card, naming the group numbers, above and apart from the two acts that move groups —
> and the delete is behind a `ConfirmModal` naming the level and what survives. **Backend unchanged.**
>
> **3 · Editing a holiday reported nothing.** Delete reported `SlotsSpanning`; the edit — the
> *September estimate corrected the day the décret lands* — said nothing. It now reports the same
> number over the **union of the span it left and the span it arrived at**: the first was laid around
> a holiday that is no longer there, the second has just gained one it never counted. Counted once
> where they overlap (the usual one-day correction) and **before** the write. `DatesMoved` gates it —
> ticking « Date confirmée » on a span already right moves no day count, and reporting slots there
> would teach the user to dismiss the one report that matters.
> `UpdateHolidayCommand` is now `ICommand<UpdateHolidayResult>` and the endpoint returns `200`, not `204`.
>
> **4 · The répartition's colour key said "partition" and drew "stage".** Reported from the running
> base: 3Med with 2 partitions printed Chirurgie orange and Médecine green. `RotationGroup` sat on
> `RepartitionRow`, meaning « the partition its first period belongs to » — which is not a fact about
> the row: over the year the row visits **every** partition, because that is what the crossover is.
> With two partitions every Médecine row opens on A and every Chirurgie row on B, so a per-row band
> is *arithmetically identical* to colouring by stage — plausible, self-consistent and wrong.
> `RotationGroup` now lives on `RepartitionCell` (the handler already computed it per cell and threw
> it away), the tint is on the `<td>`, and the legend finally names what the colours are. A cell whose
> cohorts disagree stays untinted, with a « Partitions mêlées » key rather than an invented colour.
> The old test asserted `Rows.Select(r => r.RotationGroup) == "A","A","B","B"` — the bug, encoded.
> Its replacement uses a fixture that actually mirrors, which is what the old one never did.
> ⚠ While in there: the zebra striping is a `background-image` and so was erasing the "no service"
> hatching on even rows. Harmless when unplanned cells were rare — but change 1 makes whole hatched
> columns normal, so the hatch now out-specifies it.
>
> **Contract changes to know about:** `RepartitionSummary` gained `DeclaredSlotCount` (6th positional
> member); `RepartitionRow.RotationGroup` **moved to** `RepartitionCell.RotationGroup`;
> `PUT calendar/holidays/{id}` returns a body. **The API must be restarted** before any of this shows.
>
> Recipes: [`SMOKE-TEST.md`](SMOKE-TEST.md) steps **12g** and **12h**.
>
> ### What the live run proved, in numbers
>
> - **3Med banding**: 104 / 104 cells carry a band class, **0** rows carry the old one. Every Chirurgie
>   row `AABB`, every Médecine row `BBAA` — two distinct sequences across 26 rows. A = `rgb(253,238,228)`,
>   B = `rgb(230,240,226)`, identity columns white, and `chirP1 === medP3 && chirP3 === medP1`.
> - **Med6**: 60 créneaux / 10 colonnes, 0 rows → the new second message. **Med1**: 0 / 0 → the first.
> - **Aïd al-Fitr** moved onto a weekend and back: 247 → 248 → 247 jours ouvrables, row cost 1 → 0 → 1.
>   Calendar restored byte-identical (14 entrées, 20/03→21/03, provisoire).
>
> ### Open findings from this session (ranked)
>
> 1. ⚠ **One unexplained error-boundary trip, not reproduced.** The first Aïd date-move save failed and
>    did not persist; three identical saves afterwards all succeeded, console clean, no data harmed.
>    Cause not established. If it recurs, read the console *before* reloading.
> 2. ⚠ **Med6's partitions are lopsided: A = 42, B = 42, C–J = 2 each.** Session 16 recorded a clean
>    re-cut to A–J × 10. `ReassignAll` fills the smallest partition each time and cannot produce this,
>    so ~80 groups were labelled *afterwards* by a path that used its own partition count (2) instead of
>    the level's existing ten — `AutoArrangeGroups` is the obvious suspect. Harmless today because Med6
>    has no arranged cells, and it would make the crossover nonsense the moment it does. **Not
>    investigated.**
> 3. **The gap-fill card could not be shown**: no level currently has an unlabelled group. Only its
>    *absence* is verified (Med6, Med5). The `AssignUnlabelled` path itself is unchanged backend code.
> 4. ⚠ **The Plan macro tab reads groups at `pageSize: 200`** (`getAcademicGroupOptions`), and a
>    promotion adds ~100 groups a year. The new « N groupes sans partition » count inherits that cap,
>    as do `partitions` and `groupCountByPartition` — pre-existing, but the count is now shown
>    prominently, so past 200 groups on one level it would read low. Fixing it means giving the tab a
>    real paged/aggregate source, not raising the number.
> 5. The frontend repo carried sessions 14–16 **uncommitted** (`HolidaysPage`, `RepartitionPage`,
>    `repartition/`, `ServiceFormModal` … untracked since `5e47caa`). Committed together with this
>    session's work, because the two are interleaved in the same files and cannot be separated.
>
> ---

> **▶ SESSION 16 — jours ouvrables, un découpage qu'on peut défaire, et la colonne en semaines.**
>
> Three asks, all built, all verified live against the real base (admin session, 2026-08-13).
> Commits `8a95fba` · `ca4a41b` · `24ea48e`. Suite **773 green**.
>
> **1 · Un-partitioning** — `ClearRotationGroupsCommand`, `DELETE groups/partitions`. Needed because
> `BuildLabels` lets the *existing* partition count win over the requested one, so a promotion cut into
> two stays two-way for every later assign whatever is asked for. Refused while any cell is published.
> **It destroys nothing** — nothing points at a label, so no row goes and no FK breaks; only the planned
> cells stop describing a partition (`PlannedCellsAffected`, an arrange is owed).
> ⚠ **Proven the hard way**: I cleared level 3 by accident mid-session (stray click, audit caught it as
> `PARTITIONS_CLEARED {"levelId":3}`). All 320 cells, 1872 assignments and 9283 periods survived, and the
> original A/B was reconstructible *from the cells* — restored and verified identical. The guarantee is
> real, but see "Open findings" for the UI gap that made the mis-click possible.
>
> **2 · Column stated in months, weeks (1–4…) or jours ouvrables** — `GenerateAxisWindowsQuery`,
> `GET stages/axis-windows`. **Moved server-side**: it was `setUTCMonth` in the page, right for calendar
> months and wrong the moment a duration means worked days, since no browser has the holiday table.
> Months/weeks stay calendar-exact so a monthly axis still lands on the 1st; `WorkingDays` is the only
> unit under which two columns hold the same amount of stage. Measured live on one span: `mois` swings
> **18–22** worked days (warning fires), `jours ouvrables ×20` gives **exactly 20 every column**.
>
> **3 · The calendar** — `Domain/Calendar/`: `Holiday` (dated span, National | Religious | Academic,
> `IsConfirmed`) + pure `WorkingDayCalendar`; page *Formation → Jours fériés*.
> ⚠ **Half of it cannot be computed.** The ten fixed Gregorian days are generated (Nouvel An Amazigh only
> from 2024, when the décret took effect). Aïd al-Fitr, Aïd al-Adha, 1ᵉʳ Moharram, Mawlid are lunar,
> fixed by decree — entered or absent, and absence is *reported*. Workflow: enter the estimate in
> September **unconfirmed** (a Hijri date drifts ~11 days earlier each year), tick « Date confirmée » when
> the decree lands. Under `mois`/`semaines` the dates never move when you correct it; under
> `jours ouvrables` the end date is derived, so the column and everything after it shift. **Months for
> stable dates, worked days for stable amounts of stage.**
> ⚠ `MissingReligious` asks the **whole Gregorian year**, never the queried span (`ca4a41b`) — a lunar
> date lands anywhere, so an autumn axis was reporting every spring holiday missing.
>
> **⚠ I had `Stage.DurationInDays` backwards, corrected in `24ea48e`.** It is **already in worked days**
> for 25 of 27 stages (14×7, 22×7, 30×2, 42×3, 44×6, 66×2). Only two rows hold 30. So *author axes in
> jours ouvrables*: Med6 at 22 j.o./column meets every stated duration **exactly** (CHIRURGIE k=2 → 44)
> while its calendar span swings 60–67 days. Nothing is converted — which of `Stage` and `CurriculumStage`
> is authoritative is still PHASES 15.1.
>
> **Verified live, end to end.** Med6 six stages `k=[2,2,2,2,1,1]` → `T=10`; refused at 2 partitions
> naming the multiple, re-cut to A–J × 10, applied 60 slots. Every invariant holds (each partition visits
> all six stages covering columns 1–10; each stage holds exactly `Lₛ` partitions in all ten columns), and
> the windows are live on Affectations as `P1 01/09→30/09 … P3 31/10→03/12 … P10 17/06→16/07` — P3 is 34
> days because it swallowed two fériés, P10 starts the 17th because Moharram is the 16th.
>
> **Stopping a stage harms nothing** (the explicit question). Paused 80 cohorts / 1872 assignments: the
> database is **byte-identical** before and after — capacities 2960/148, quotas 0, allowed services 28,
> curricula 9 / 27 stages, CNPN 4, registrations 8076 Active. `StagePauseRunner` scopes to `Ongoing`; all
> of these are `Completed`, so it is a correct no-op. On Med6 the buttons are *disabled* with the reason
> inline (« aucune rotation dans la période choisie »).
>
> Recipes: [`SMOKE-TEST.md`](SMOKE-TEST.md) steps **12e** (jours fériés + the working-day unit) and
> **12f** (taking back a partitioning) — both now executed, not just written.
>
> ### Open findings from this session (not fixed, ranked)
>
> 1. ⚠ **No gap-fill in the UI, and *Supprimer les partitions* sits next to *Redécouper*.** Level 6 had
>    20 groups with no label and no button fills only those — `AssignUnlabelled` is unreachable once any
>    label exists (pre-existing, not a regression). Combined with a destructive button one click away,
>    that is what let me wipe level 3. Suggest: a « Compléter les groupes sans partition » action, and put
>    the delete behind a confirm naming the level.
> 2. **Répartition annuelle says « Aucune période n'est planifiée » when slots exist but nothing is
>    arranged.** After applying an axis that reads like the apply failed. Distinguish "no slots" from
>    "slots, no cells".
> 3. **Editing a holiday reports nothing.** Delete reports `SlotsSpanning`; the *edit* path — which is
>    exactly the "confirm Aïd the day before" moment — says nothing about which windows were laid over the
>    old date. Same report belongs on update.
>
> ---

> **▶ SESSION 15 — stages of unequal length now rotate on one axis, and the dates are entered once.**
>
> The rotation cycle took a single `periodsPerStage` for a whole block. Fine for the new 3rd year (two
> semesters × three stages × one period), useless for the 6th, where four stages take two periods and two
> take one. Generalised, not worked around.
>
> **The identity that drives everything.** A partition needs `T = Σkₛ` columns to visit every stage; if
> `Lₛ` partitions sit in stage *s* at once then `Lₛ·T = P·kₛ`, so **`Lₛ = P·kₛ/T`** and `P` must be a
> multiple of `T / gcd(kₛ)`. Refusals name that multiple.
>
> Run the real 6th year through it — `k = [2,2,2,2,1,1]` → `T = 10`, `P = 10`, `L = [2,2,2,2,1,1]`. **That
> is the ten monthly columns of `Med6.png`.** The reference document is the formula.
>
> ⚠ **A period is one *service*, not one stage — I modelled it backwards first.** "Chirurgie has 2
> periods" means two *different services*. My first attempt gave a 2-period stage one two-column slot,
> which parks a group in one service for two months. The ported tests failed immediately and were right.
> Correct model: every stage carries a slot **per axis column**, and a partition takes a **run of `kₛ`
> consecutive** ones — which also removed a `kₛ | T` condition I had briefly imposed.
>
> ⚠ **The closed form is gone.** `(lane + t) mod S` is a cyclic Latin square that only exists for equal
> durations. `RotationTiling` solves an exact cover, backtracking across **partitions and columns
> together** — filling each partition greedily would report "impossible" whenever an early one took a
> column a later one needed, a wrong answer rather than a slow one.
>
> ⚠ **Some mixes are genuinely impossible.** Stages of 2 and 1 give `T = 3`; a two-column run must cover
> column 2 wherever it starts, so one stage is always full and the other always empty. No `P` fixes it.
> The search is exhaustive, so `NoFeasibleArrangement` is a **proof**. Keep that property if it is ever
> optimised.
>
> **Dates entered once** — the other half of the ask, and it falls out: supply the axis at its finest
> granularity (10 monthly windows) and every stage's slots are cut from that one list, so a 2-period and a
> 1-period stage on the same block cannot drift. `PeriodAxis` already handled multi-column stages on the
> read side, so the répartition needed no change.
>
> **Decision taken on the partition count** (you left it to me): the command **takes** `P` from the
> promotion's real partitioning and validates it, rather than deriving one. Deriving would silently re-cut
> partitions to suit a single block, fighting the `Reassign` guard, and a level's partitioning is shared
> across its blocks. The refusal names the multiples that work, so it is as helpful as deriving.
>
> Request shape changed: `stages: [{ stageId, periods }]` replaces `stageIds` + `periodsPerStage`.
> Recipe: [`SMOKE-TEST.md`](SMOKE-TEST.md) step **12c**. Suite: **721 green**.
>
> ---
>
> **▶ SESSION 14 — the crossover is generated now (« mirror effect », generalised).**
>
> Asked for as: configure one stage, get the opposite for the other. Built as the general rotation,
> because two stages is the easy case and the new CNPN's 3rd year is six (three per semester).
>
> `Stages/RotationCycle/` produces the `PartitionStagePlan[]` matrix `GenerateMacroPlanCommand`
> **already consumes** — so it generates what was previously ticked by hand and changes nothing
> downstream. `POST levels/{id}/rotation-cycle[/preview]`; the apply writes the slots and *returns* the
> matrix, which you then post to `stages/macro-plan`.
>
> ⚠ **One correction to the mental model this was specified with.** A block of S stages at k periods
> each occupies **`S × k` columns, not `partitions × k`.** The timeline has to fit one partition
> visiting every stage; partitions subdivide *who is where*, they do not lengthen it. The two coincide
> only when S = P — which is why the 2-stage/2-partition example hid it. Three stages at k=1 with six
> partitions is **3** columns, two partitions per stage per turn. The planner refuses a wrong window
> count and states the arithmetic.
>
> The rule: partition *p* takes lane `p mod S`, and in turn *t* sits in stage `(lane + t) mod S`. S = 2
> is exactly the requested mirror. `RotationCyclePlanner` is **pure** — no DB, no clock — so the
> crossover is tested by properties over many (S, k, P): every partition visits every stage exactly
> once, none is ever in two at a time, every stage is occupied in every column.
>
> Uneven splits are **reported, not refused**: P not a multiple of S still gives everyone every stage,
> the turns just carry unequal effectifs; fewer partitions than stages leaves a stage empty for a turn.
> Only the faculty knows if that was intended.
>
> The axis is **replaced wholesale** (half-old/half-new columns are the misalignment this removes),
> scoped to the stages named — so semester 1 and semester 2 are two independent blocks — and **refused
> outright while any cell is published**.
>
> **▶ AND THE GAP IT WORKS AROUND — read this one.** Nothing in the schema says Médecine P1 and
> Chirurgie P1 are the same window: `StageSlot` is keyed (stage, year, period number), the axis is
> *derived* from dates, and neither guard notices a drift (`SlotOverlapGuard` is per-stage by design;
> `GroupScheduleConflictGuard` only fires on a real double-booking, which a crossover never is).
>
> ⚠ **The small drift is the dangerous one.** Chir P1 two days longer than Med P1 means Chirurgie's
> window *contains* Médecine's, so `PeriodAxis` drops it as a composite and the error vanishes without
> trace. `PeriodAxisDiagnostics` now reports it on `LevelRepartitionResponse.AxisDisagreements`. It is
> **never an error** — Med6 legitimately runs Chirurgie's P1 over two months and ANES REA's over one,
> and code cannot tell that from a typo. Recipes: SMOKE-TEST **12c** and **12d**. The structural fix (a
> declared axis entity) is logged against 15.1; `RotationCycle` avoids the class in practice.
>
> Suite: **714 green** (+41).
>
> ---
>
> **▶ ALSO SESSION 14 — the partition stripe, explained and now optional.**
>
> You spotted that a cell of the annual planning holds groups `1, 3, 5` rather than `47-50`, and
> diagnosed it correctly: the macro split alternates. Confirmed in the code — `PartitionAllocator` fills
> the *smallest* partition walking groups in number order, so with two partitions it alternates on every
> group. **The stripe was never designed; balance was the only property sought and the stripe falls out
> of it.** The step is the partition count, not always 2, and `RotationArranger` defaults that count to
> `services.Count` when nobody passes one.
>
> The cell inherits it because the service is picked by the cohort's **index within the partition**
> (`serviceQueue[(ci + offset) % n]`), and the queue repeats each service in a capacity-sized run — so
> consecutive indices share a service, and consecutive indices of a striped partition are non-consecutive
> group numbers. `GroupNumberRanges` is right to refuse to merge across the hole.
>
> **`PartitionStrategy` now offers both**, `Interleaved` staying the default so nothing existing moves:
>
> | | 8 groups, 2 partitions | printed cell |
> |---|---|---|
> | `Interleaved` (default) | A = 1,3,5,7 | `1, 3, 5, 7…` |
> | `Contiguous` | A = 1-4 | `1-40` — matches `Med3.png` |
>
> `POST groups/assign-partitions` takes `strategy` and `reassign`, and **returns each partition's
> membership through `GroupNumberRanges.Format`** — so the two are comparable at a glance without
> arranging anything. Recipe: [`SMOKE-TEST.md`](SMOKE-TEST.md) step **12b**.
>
> ⚠ **Two things before adopting `Contiguous` as the default.**
> 1. **Re-cutting is destructive to a plan.** A gap-fill never reshuffles (by design); `reassign: true`
>    does, and it is **refused outright while any cell is published** — students were sent there. Planned
>    cells are counted (`plannedCellsAffected`) so you know an arrange is owed.
> 2. **Group numbers run contiguously per CNPN text** (`AutoArrangeGroupsCommandHandler` numbers per
>    bucket), so from 2026-2027 a contiguous partition can land entirely inside one text where an
>    interleaved one mixes both. `CohortProvisioner` skips unrequired stages, so that changes which rows
>    exist in each partition's half of the matrix. **Check on real data first** — this is the one thing I
>    could not settle from the code.
>
> Also fixed while here: the interleaved tie-break was `counts.MinBy(...)` over a `Dictionary`, i.e. it
> relied on dictionary enumeration order. Stable in practice, guaranteed nowhere; now an explicit
> `(count, labelIndex)` ordering.
>
> Suite: **673 green** (+13).
>
> ---
>
> **▶ RESUME HERE (2026-08-09, session 14). A year is now closed by declaration, because PGSH cannot
> compute the verdict and never could.**
>
> **⚠ TWO THINGS BEFORE YOU TEST.**
> 1. **Migration `20260809151109_RegistrationYearOutcome` is generated but not applied** — restart
>    `PGSH.AppHost` and `MigrationService` applies it. (Starting the stack *before* it existed is what
>    threw `PendingModelChangesWarning` at `Worker.cs:95` — that was the missing migration, nothing else.)
>    Two nullable columns on `Registrations` (`OutcomeSource`, `OutcomeRecordedOn`) plus
>    `IX_Registration_Year_Level`. **No data migration**: every existing row keeps
>    `OutcomeSource = null`, which is exactly "nobody has pronounced yet".
>    - ⚠ It also **drops `IX_Registrations_AcademicYearId`**, and that is correct: the new composite
>      index leads with `AcademicYearId`, so Postgres serves the FK lookups from its prefix and EF's
>      convention skips the redundant one. Not a regression — one less index to maintain.
> 2. **There is no frontend for this yet.** The three déliberation routes and two réinscription routes
>    work end to end and are covered, but no screen calls them — see *Still to build* below.
>
> **The framing, which is yours and is the right one.** PGSH has no exams, no TP, no notes de module
> and no jury. So it cannot know who cleared a year, and the fix is not a cleverer inference — it is to
> **accept the verdict as input**, in the shape the évaluation import already proved works: a canvas per
> promotion, filled from the PV de déliberation, previewed, then applied.
>
> **This supersedes the Phase 14.3 inference for every year going forward**, and makes the inference
> *safe* for the six imported years: `Registration.OutcomeSource` is `Declared` or `Inferred`, and
> `RecordYearOutcome` refuses to let a guess overwrite a fact. That was the objection that had 14.3
> blocked, and it is now answered. Renumbered: **14.3a** (déliberation, done), **14.3b** (réinscription,
> done), **14.3c** (the inference, still needs your rulings on three cases in NOTES.md).
>
> **Two acts, months apart, and they must stay apart** — deliberation in July, re-registration in
> September, because not every *admis* comes back. Fusing them would invent registrations for students
> who abandoned.
>
> | | route | policy on error |
> |---|---|---|
> | Déliberation | `GET\|POST levels/{id}/deliberation[/template\|/preview]` | **all-or-nothing** |
> | Réinscription | `GET levels/{id}/reinscription/preview`, `POST levels/{id}/reinscription` | **skip, idempotent** |
>
> ⚠ **The two policies differ on purpose, do not "make them consistent".** The uploaded canvas is not
> stored, so a half-closed promotion could never be reconstructed — hence all-or-nothing. A rollover can
> simply be re-run once the odd verdicts are corrected, so refusing 690 legitimate rows over three
> anomalies buys nothing.
>
> **`RegistrationStatus` gained `Graduated` and `Excluded`.** Both distinctions earn their keep in
> exactly one consumer, the réinscription: *Admis* → niveau + 1, *Redoublant* → même niveau,
> *Diplômé / Exclu / Abandon* → nothing. Collapsing either pair breaks it.
>
> **Three judgement calls worth knowing before you change them:**
> - **An *Admis* whose stages are not all validated is flagged, never blocked.** The jury rules on the
>   whole year; PGSH sees only stages — and with **0 authored `StageSlots`** an unmarked stage is the
>   *normal* state, so enforcing this would refuse every import.
> - **`Diplômé` off the final year is refused where the CNPN is known, and stands aside where it is
>   not.** ~2,200 stamps are inferred and 19 students carry none.
> - **New registrations are `Active` with no group.** Nothing in the app filters planning by
>   `Registration.Status` (checked), so `Pending` would be planned identically while claiming not to be
>   enrolled. The empty group is what puts them in the "Non réparti" bucket auto-arrange reads next.
>
> **Also this session:** `GET /students/{id}/history` had **no read scoping** — only
> `RequireAuthorization()`, so any logged-in user could read any student's transfers, délocalisations and
> failures. Now behind `EnsureCanReadStudentDossierAsync`, same as the parcours. That was the one real
> gap inside Phase 14.2.
>
> **Still to build on this:** the frontend for both acts (upload + preview table + apply, mirroring the
> évaluation import modal), and 14.2's admin student file, which is otherwise untouched.
>
> Suite: **660 green** (+29). Test recipes: [`SMOKE-TEST.md`](SMOKE-TEST.md) steps **13** and **14**.
>
> ---
>
> **⚠ RESTART THE API BEFORE TESTING.** The targeting endpoints
> (`POST cnpn-versions/{id}/target[/preview]`) are new since the running process started, and
> `PGSH.API`'s DLLs cannot be rebuilt while it holds them — the *Simuler* button 404s until you
> restart. Code compiles clean (0 CS errors; the 20 MSB errors are the file lock).
>
> **▶ THE TEXTS THEMSELVES (session 13).** A CNPN is **three layers**, and only two had a screen:
> **(1) the text** — code, intitulé, `TotalYears` (6 vs 7 — *what kind of degree it defines*),
> publication, and the intake year from which new registrations attach automatically;
> **(2) what it demands** — the per-level stage requirements; **(3) whom it binds** — the targeting.
> Layer 1 existed only as migration-inserted rows, so adding an arrêté, renaming `PHARM-LEGACY` or
> fixing a wrong `TotalYears` all meant SQL. Now `Cnpn/Manage/` + a **Textes CNPN** card at the top of
> the CNPN page, with a **completeness bar per text** (`4 / 6 niveaux`) so half-finished is visible,
> and **« X reprend Y »** — clone every level of one text from another in one action, then edit only
> the years the arrêté changes. Levels already saved by hand are never overwritten; levels beyond the
> target's span are counted, not silently dropped.
>
> ⚠ **Two texts of one programme may not claim the same intake year.** Version selection resolves
> "the latest intake at or before entry"; a tie has no defensible winner. Guarded on create *and*
> edit.
>
> ⚠ **Delete is gated on students, not on curricula** — the two foreign keys pointing at a text
> behave differently: `Users` is `NO ACTION` (raw FK violation → 500) and `Curriculums` is `CASCADE`
> (silent destruction of authored requirement sets). So deletion refuses while anyone carries the
> stamp, inferred included, and otherwise reports how many requirement sets went with it. Allowing
> the cascade is safe *because* of the gate: a text nobody follows has nobody who could owe anything.
> It is for the mistyped row — a superseded arrêté stays, because its students stay. The UI disables
> the control rather than letting it fail, and warns when the text governs an intake.
>
> ⚠ **The Stages page is neither year- nor CNPN-scoped**, and it never was — it is the timeless
> catalogue. Switching the navbar year there changes nothing, and the Durée/Coefficient it shows come
> from `Stage`, **not** from any CNPN. Those columns duplicate `CurriculumStage`'s and agree today only
> because the history reconstruction seeded one from the other; the first text that reweights a stage
> makes them disagree. To read what a text requires, use the CNPN page. Logged against Phase 15.1
> alongside `Stage.LevelId`, since it is the same root cause.
>
> **▶ TARGETING (session 13).** Who a CNPN binds is now **authored**, not inferred: a rule
> (`programme + année ≤ N`) is previewed, reviewed, then frozen. `Cnpn/Targeting/`, and see
> CLAUDE.md → "The CNPN is a cohort's text" for the four rules that make it safe — no stored rule,
> selector *plus* standing rule, bulk never moves a confirmed stamp, and disagreements between the
> rule and the arrêté are reported rather than resolved.
>
> Same session, from a browser stress test of the CNPN page: **"Texte comparé" defaulted to arrêté
> 2175.22** (my sort fix pushed non-governing texts last, and `to` takes the last element — and `to`
> is the picker the editor *writes to*); **two red `Erreur 404` toasts** on every load (the error
> middleware had no 404 branch — a 404 from a query is now silent, from a mutation still toasts); the
> page **opened on "Retrait"** (year 0); and the pickers **allowed a Pharmacie level with a Médecine
> arrêté**, which only failed at save time. All four fixed.
>
> **▶ TEST FIRST: [`SMOKE-TEST.md`](SMOKE-TEST.md)** — nine checks over sessions 11 and 12, with the
> expected numbers from your own data, and a rollback procedure at the bottom. Both migrations are
> already applied to the dev database.
>
> **▶ RESUME HERE (2026-08-08, session 12). The CNPN is versioned: Médecine goes 7 years → 6, and
> the two texts run side by side for years.**
>
> **The text.** `cnpn/CNPN Diplôme de Docteur en Médecine.pdf` — arrêté **1650.25** du 26 juin 2025,
> BO 7422 du 17 juillet 2025. (Not password-protected; `pdfinfo` says `Encrypted: no`. Its Arabic has
> no Unicode CMap, so text extraction yields nothing — rasterise with `pdftoppm -png -r 150` and read
> the pages as images.)
>
> **⚠ The decree's transition rule is not the one described in conversation.** Art. 2: effective from
> **2024-2025** (retroactive — it has already governed two intakes), and *"students registered before
> academic year 2024-2025 remain subject to arrêté 2174.18 **in its form prior to the amendment by
> arrêté 2175.22**"*. So the criterion is **date of first registration**, not current level, and there
> are **three** texts, not two. The two criteria agree for students who never repeated and part company
> for the 2,635 who have: **21 students** (1 at level 1, 20 at level 2) are new-CNPN by the level rule
> and old-CNPN by the decree. The decree wins.
>
> **`Curriculum` is re-keyed `(CnpnVersionId, LevelId)`** — see CLAUDE.md → "The CNPN is a cohort's
> text". `CnpnAssignment` (`Application/Stages/Cnpn/`) resolves the text from the earliest recorded
> registration; `Student.CnpnVersionId` is sticky and only `AssignCnpnVersion` writes it.
>
> **Migration `20260808135315_CnpnVersioning`, applied.** Verified on a clone of live data first. It
> creates the four texts, attributes each recorded curriculum to the text governing the intake that
> reached its level, **unions** the years that collapse onto one version (the reconstruction
> under-reports, so union recovers more of the text), and stamps every student. Result on live data:
>
> | Text | Years | Curricula | Students |
> |---|---|---|---|
> | 2174.18 (Médecine, superseded) | 7 | 6 | 6,460 |
> | 1650.25 (Médecine, in force from 2024-25) | 6 | **0** | 1,980 |
> | 2175.22 (citation only, governs nobody) | 7 | 0 | 0 |
> | PHARM-LEGACY (placeholder) | 6 | 3 | 1,745 |
>
> 1,980 = the 2,001 students at levels 1–2 minus the 21 the decree keeps on the old text. Curricula
> collapsed 51 → 9. 19 students stayed unstamped: they have no registration at all (import artifacts).
>
> **⚠ Two things scolarité must do next.**
> 1. **1650.25 has zero recorded requirements.** Nothing historical maps to it — its stage lists have
>    to be entered from the PDF. Until then, six-year students have no CNPN content.
> 2. **`PHARM-LEGACY` is a placeholder** I invented so Pharmacie's 13 existing curricula had somewhere
>    to go. Replace its code, label and reference with the real Pharmacie text.
>
> **⚠ Known gap, deferred by agreement:** the new CNPN is **12 semesters** with typed placements
> (S1–4 immersion/nursing, S5–8 part-time clinical, S9–12 full-time + family medicine) and credits
> (10/20/30 per semester). PGSH has year-levels and a free coefficient, so 1650.25's requirements can
> only be recorded approximately. That is the next piece of modelling work.
>
> **~2,200 assignments are flagged `CnpnAssignmentIsInferred`** — students whose entry was never
> imported, deduced from their current level. The one assumption in the whole backfill is that the
> 1,013 at level 2 did not repeat an unrecorded first year.
>
> **Planning under two texts (same session).** Answering "how do we configure stages for 2nd-years on
> the new CNPN vs 3rd-years on the old one":
> - **2025-2026 needs nothing.** Levels 2 and 3 are different levels and already carry different
>   stages via `Stage.LevelId`; the new CNPN gives years 1–2 no clinical placements anyway. The
>   divergence is invisible this year.
> - **2026-2027 is when it bites**, at level 3: ~920 students arriving on the six-year text plus the
>   repeaters on the seven-year one, in one level.
> - **Done:** groups are now homogeneous by CNPN (`AutoArrangeGroupsCommandHandler` splits by
>   (year, level, version); unstamped students get their own "CNPN à confirmer" bucket), and
>   `CohortProvisioner` refuses a cohort for a stage the group's text does not require — standing
>   aside where no set is recorded, and reporting refusals as `NotRequiredByCnpn` (surfaced in the
>   macro-plan result and as an orange "n hors CNPN" badge on the Groupes page).
> - **Also found:** planning previously consulted `Curriculum` **nowhere at all** — the macro plan was
>   whatever an admin ticked in the matrix, with nothing checking the stage was required.
> - **Also missing:** "médecine de famille" (طب الأسرة, semesters 11–12) does not exist as a `Stage`.
>   The new CNPN requires it; someone must create it.
>
> Suite: **505 green**.
>
> ---
>
> **Session 11. The academic year is a hard boundary, not a filter
> callers may forget.**
>
> **The report.** The groups page felt heavy; the évaluation-import canvas for a 6ème année stage
> came back with ~3,500 rows; and it was unclear whether changing the navbar year revalidated
> anything. All three turned out to be the same defect seen from three angles.
>
> **The defect.** A stage keeps a **cohort per (group, year)**, and groups are per year — so anything
> reached by `stageId` alone spans every promotion that ever took the stage. Measured on the live
> data: `CHIRURGIE` (6ème année) has **3,553** assignments across **6** academic years, of which
> **688** belong to the current one. That is the canvas number exactly. The same unscoped shape was
> in `EvaluationImportPlanner`, `RotationArranger`, `SchedulePublisher`, `StagePeriodRunner`,
> `StagePauseRunner` and `StudentAffectationService` — i.e. *publishing, arranging, starting,
> closing, pausing and resuming* a stage all reached into past promotions whenever they were scoped
> by partition label rather than by explicit cohort ids.
>
> **`AcademicYearResolver`** (`Application/AcademicYears/`) is the new single answer: an omitted year
> resolves to the year flagged current and **never** to "all years". Widening on absence is the bug;
> a read that genuinely spans years must say so by other means. Every handler above now takes the
> year and every command carries `int? AcademicYearId`.
>
> **`StageSlot` is year-stamped** (migration `20260808114953_StageSlotAcademicYear`, applied). It
> held `StageId + PeriodNumber + dates` with no year, so P1 authored for 2025-2026 would have
> surfaced — with 2025 dates — under every other year. Unique index is now
> `(StageId, AcademicYearId, PeriodNumber)`, FK to `AcademicYears` is `Restrict`, and
> `SlotOverlapGuard`'s level-wide rule became level-**and-year**-wide: two promotions never share a
> student, so identical calendar windows in different years are not a clash. The table was empty
> (see periods, below) so nothing needed backfilling, but the migration backfills defensively.
>
> **Students are now filtered by year, not just formatted by it.** `GetStudentsQueryHandler` applied
> `AcademicYearId` only to the projected level/group/status columns, so "students of 2024-2025"
> listed the entire imported history with every column past the name blank — and the dashboard's
> "étudiants inscrits" counted everyone ever enrolled.
>
> **⚠ There are no periods in the app, and that is data the legacy system never had.** Verified:
> `StageSlots` **0** rows, `CohortSlotAssignments` **0**, `ServicePeriods` **105,626**. The Access
> base had no planning grid — only, per student per stage, a date range and a mark. See
> `LegacyImportPlanner.cs:20` and its `CohortSlotAssignmentId = null`. So the importer produced real
> rotations but zero P1/P2 windows, and everything that reads periods (the grid, the Affectations
> period chips, the `SinglePeriod` import scope) is legitimately empty. **The grid has to be
> authored going forward** — that is the next piece of work, and it is now year-scoped so each
> promotion gets its own windows.
>
> **Frontend.** Groups list pages for real (25/page + `totalCount`, debounced label search) instead
> of asking for one 200-row page; `StageTimelinePage`'s second year picker is gone (the navbar drives
> it); `StageDetailPage`'s cohort-creation modal opens on the navbar year and follows it; the import
> modal, the schedule grid, and every start/close/pause/resume/publish/auto-arrange call now send
> `academicYearId`, which is also what makes RTK Query refetch them when the navbar year changes.
>
> **Tests.** `PGSH.Tests/Application/YearScopingTests.cs` — 16 cases on the boundary itself (canvas
> scoping + fallback + empty-year refusal, import can't reach another year's student, grid slots and
> cohorts, same period number once per year, cross-year windows don't overlap, same-year ones still
> do, lifecycle and affectation confined to one year, student population). Suite: **487 green**.
>
> **Two design questions raised and closed — do not re-litigate without new numbers.**
> - *Denormalise `AcademicYearId` onto `Cohort`* to shorten the two-hop filter? **No.** Measured on
>   CHIRURGIE (563 cohorts, 6 years): the join is **49 of 910 shared buffers, ~5%**; the remaining 861
>   are the nested loop into `InternshipAssignments`, untouched by it. The join was never the cost —
>   the missing predicate was. Drift is *not* the reason to refuse (a composite FK to
>   `AcademicGroup(Id, AcademicYearId)` would make the copy non-driftable); it simply buys ~5%.
> - *A global EF query filter on the year?* **No.** ~101 handlers touch year-constituted tables and
>   ~15 are deliberately cross-year — parcours, level dossier, curriculum comparison, revalidation's
>   cross-level retake. Those are the load-bearing reads, and `IgnoreQueryFilters()` is all-or-nothing.
>
> **Vocabulary, now written on the entities.** Three things are called "groupe":
> `AcademicGroup` (the roster, per year+level) → `Cohort` (that roster doing one stage) →
> `CohortSlotAssignment` (that cohort in one period, in one service). A group is therefore **not "in a
> service"** — it is in a *sequence* of them, one per period, two levels out. XML docs on all three
> cross-reference each other.
>
> **Pre-flight guard fixed (was a tracked offender, now acute).** With zero `StageSlots` anywhere,
> "Répartition automatique" was clickable and guaranteed to fail on *every* stage in the app. It is
> now disabled with the specific reason — no periods / no allowed services / no cohorts in selection —
> mirroring the three guards `RotationArranger` returns. The grid's both-empty state also said
> "Aucune cohorte…" unconditionally (its second arm was unreachable); it now names both and points at
> the year-scoped nature of créneaux.
>
> ---
>
> **Session 10. Student portal now shows the whole parcours, not just
> the current registration.**
>
> **The defect.** The portal read its stages from `GET /internship-assignments?registrationId=…`
> using `student.currentRegistration`. A registration is *one academic year*, so a 6th-year student
> saw only the months since September, and every previous year's stages and marks disappeared the
> day a new registration was created. The dashboard then labelled a card "Stages planifiés" but gave
> it `assignments.length` — the total — so a stage that had been served, closed and marked still
> counted as planned. `Status` (workflow) and `Result` (academic outcome) were being conflated.
>
> **New read model.** `GET /students/{id}/parcours` → `GetStudentParcoursQuery`
> (`Application/Students/GetParcours/`). Every registration the student holds, most recent first,
> each with its stage attempts. Scoped by `ExecutionAuthorizer.EnsureCanReadStudentDossierAsync` —
> administration or the student themselves, same as the level dossier; a chef cannot read it.
> Deliberately unpaginated: bounded by years enrolled × stages of a level, it cannot grow with the
> faculty. A stage carries **its own level**, not the registration's, so a cross-level retake reads
> correctly, and attempts are numbered by academic year (`attemptNumber`).
>
> **`ParcoursTotals` — five disjoint buckets**, the fix for the conflation above:
> `planned` / `ongoing` / `awaitingVerdict` (rotations over, marks not all in) / `validated` /
> `failed`. The verdict outranks the workflow status. Mirrored on the client by `stageStateOf` in
> `features/student/utils/stageState.tsx` — **change one, change both.**
>
> **Frontend.** Year rail on Mes Stages (switch academic year; the catalogue of not-yet-assigned
> stages is fetched only for the year in progress), a "Relevé de stages" tab on Historique next to
> the event timeline, dashboard stats rebuilt off the totals, and the stage detail page resolves its
> attempt through the parcours with an `?attempt=<assignmentId>` tab per sitting — a past year's
> stage no longer reads "pas encore affecté". `getMyAssignments` is gone from the student slice.
>
> **UI follow-up (same session).** The selected year moved from component state into the URL
> (`/student/stages?year=<registrationId>`) so the dashboard can link to one; tapping a year on the
> dashboard now opens *that* year instead of dropping the student back on the current one. The
> progression card lists **every** registration (was `slice(0, 4)`) inside an autosized scroll area.
> Mobile: the stage detail page's right panel used `gridColumn: 'span 2'` inside a `base: 1`
> `SimpleGrid`, which forced horizontal overflow on a phone — it is a `Grid` now, with `order` so the
> affectation comes first on small screens; the filter pills and attempt tabs scroll on one line
> instead of wrapping into a block; the objectives table and rotation dates no longer overflow.
> `ParcoursTotalsBar` gained a headline (`n sur m validés` + %), a track behind the bar, and legend
> chips that wrap.
>
> **Verified in Chrome at ~494 px CSS width** against the running stack (student Jawad, 5
> registrations): dashboard reads 9 validés / 16 affectés, 0 en cours, 7 en attente, 0 à venir — the
> old build would have said "16 planifiés". All 5 years listed; tapping 2024-2025 opened
> `/student/stages?year=…` on that year. `documentElement.scrollWidth === clientWidth` on dashboard,
> stages and relevé: **no horizontal overflow anywhere**, no console errors.
>
> Two things that only showed up on the device and are now fixed: the `RegistrationBadge` on
> `CurrentStageCard`'s blue gradient was invisible (`variant="light"` paints a pale tint of its own
> colour) — it takes an `onDark` prop now; and the relevé's four-column table pushed the **Note**
> column off a phone screen and truncated the state badge to "EN ATTE…" — under `sm` it renders
> stacked rows instead (`hiddenFrom`/`visibleFrom` pair in `ParcoursRecord`).
>
> **Past years badged "EN COURS" — explained, deferred** (user, 2026-08-08). Not a UI bug: PGSH is
> **not linked to the pedagogical side of the faculty**, so nothing ever moves a `Registration` off
> `Active` and every past year still reads "En cours". The badge is faithful. The agreed inference —
> *a registration is failed when a later one exists at the same level (1,2,3,**3**,4 → the first 3
> failed); otherwise validated; the latest is in progress* — plus the four cases that still need a
> ruling, are written up in [NOTES.md → "`Registration.Status` is unmanaged"](NOTES.md) and queued as
> **Phase 14.3**. Do not patch this in the UI.
>
> **▶ NEXT UP — [Phase 14 in PHASES.md](PHASES.md)**, specified with the user 2026-08-08:
> **14.1** printable relevé de stages by **year** and by **whole cursus** (today only the per-stage
> fiche exists) + **14.1b** the signed-delivery flow; **14.2** the admin student file gaining all
> stages + all events (the `parcours` endpoint already serves the first and is already authorized for
> admins; ⚠ `GetStudentHistoryQuery` has no read scoping yet); **14.3** the registration-status
> inference above; **14.4** the *répartition annuelle des stages* planning matrix.
>
> **Settled this session, so nobody re-opens them:**
> * **Output = client-side print** (`@media print` on a React view). No server-side PDF library.
> * **NO AVERAGE above the stage.** The only mean is *within* a stage (mean of its periods —
>   `StageScoring`, already built). No year average, no cursus average, no coefficient roll-up;
>   `Stage.Coefficient` must not be used to invent one. Closes the question open since session 10.
> * **Signature is a separate microservice**, reached through the demandes queue:
>   `student requests → demandes queue → scolarité generates → signed → returned as the demande's
>   response`. ⚠ **Phase 5 (demandes) is a prerequisite**; until then 14.1 is a print view only.
> * **Template**: one exists but is "quite messed up" — the user supplies **logos + header/footer
>   texts**, the layout is ours. `example_stage_assignement/` shows the house style.
>
> **14.4 is the one genuinely new artefact** — the *Répartition annuelle des stages*, the table the
> faculty actually publishes (see `example_stage_assignement/Med3.png`, `Med6.png`). Rows are
> `Stage | Service (Chef)`, columns are the level's periods with their date ranges, cells are
> **collapsed group-number ranges** (`47-50` = groups 47,48,49,50), banded by rotation partition.
> Planning only — no marks, no execution state. **No new modelling is needed**: every cell is an
> existing `CohortSlotAssignment`; what is missing is the orientation. `GetStageScheduleQuery` is
> cohorts × slots for *one* stage; 14.4 is (stage, service) × periods for a *whole level*. It also
> ships as a **public read-only view** because students are emailed only their group number today —
> and it is explicitly **transitional**, to be switched off once everyone uses the portal. One thing
> to check in the data first: whether all stages of a level share a single period axis (`Med3` says
> yes; `Med6`'s repeated column pairs may mean two-month slots drawn over a one-month axis).
>
> ⚠ **Open question for the user:** there is still **no cross-stage average** anywhere — no
> "moyenne générale" on the dashboard or the relevé. `StageScoring` settles the per-stage note
> (mean of period marks) but nothing in the domain says how stages roll up into a year mark, and
> `Stage.Coefficient` exists without a rule that uses it. Left out rather than invented.
>
> Verified: 471/471 tests green (15 new in `StudentParcoursTests`); both new EF queries confirmed to
> translate to Postgres SQL via a throwaway `ToQueryString()` probe (the in-memory suite cannot see
> translation defects); `npm run build` + `tsc --noEmit` clean.

> **(2026-08-07, session 9). The three agreed tasks are DONE and the import is now
> VERIFIED against the running stack.**
>
> 1. ✅ **Import smoke-tested end-to-end** (see "Evaluation import" below for what was proven).
> 2. ✅ **Anonymous access closed** — see "Authorization lockdown" below. It was far worse than the 7
>    schedule routes: `GET /api/students` was serving **5701 students' name, email, CNE, Apogée and
>    CIN with no token at all**.
> 3. ✅ **Every confirmed defect from the 2026-08-06 audit is now closed** — see "Audit defects" below.
>    Decisions taken (user delegated them): délocalisation = **Scolarité/SuperUser only**; record,
>    fiche and evaluation reads = **owner, or a chef/staff of a service the student rotates through,
>    or the administration**; `CompletePeriod` **gains** the `IsStarted` guard for symmetry with
>    `PausePeriod`.
> 4. ✅ **Role model confirmed and the frontend brought in line** (user, 2026-08-07):
>    **administration** = `Scolarite` + `SuperUser` (more can be added); **employees** = `Professor`,
>    `Secretaire`, …; then **students**. A secrétaire is an *employee*, not administration — the
>    backend already had this right (`Roles.Administrative`), but `routes/index.tsx` let her into the
>    whole admin portal. She now reaches only **Présences** there (the one thing the API scopes to
>    her, via chef-*or*-staff), every other admin route sits behind an administrative-only inner
>    guard, the sidebar is built from the caller's role, and the employee zone accepts `Secretaire`.
>    ⚠ Keep that guard and `Roles.Administrative` in step — they are two halves of one rule.
>
> **✅ DONE this session (2026-08-07, session 9).**
> * **Committed the 78-file backlog** (`efcc581`) — the whole 07/08 work stream, incl. `PGSH.Tests`,
>   was uncommitted. The frontend repo had no checkpoint since June either (`62c0930`).
> * **Audit defect #2 fixed** (`9a8d896`): editing an evaluation with objectives threw
>   `DbUpdateConcurrencyException`. New `InternshipAssignment.AmendEvaluation` routes the amend through
>   the aggregate, so it also fixes #6 (a mark change left **no** audit trail) via a new
>   `EvaluationAmendedDomainEvent`, and #7 (`EvaluationSubmittedDomainEvent` published the `TotalScore`
>   that `Normalize()` had just nulled — now `StageScoring.PeriodMark`, field renamed `Mark`). #8 fixed
>   too: new `EvaluationObjectiveResolver` checks objective ids against the period's own stage.
>   ⚠ The regression test was **verified to fail against the old behaviour** before being kept.
> * **Admin one-by-one evaluation** (`6f219ea` + FE `62c0930`): the chef's modal is now shared
>   (`features/evaluations/`), reached from `StudentRecordModal` with an Évaluer/Modifier button per
>   rotation. Backend change was one route: `employees/me/service-periods/{id}/objectives` →
>   `service-periods/{id}/objectives` (the query never was chef-only). Also killed the long-standing
>   `set-state-in-effect` lint error by remounting the form by key instead of mirroring server data.
> * **Fiche gate** (`40fb21d`): now needs `Result == Validé` **and** `Status == Validated`. Three
>   existing tests had never ratified, so they only drove the lifecycle to `Evaluated`; rewritten.
> * **Excel import** (`efb9582` + FE `d1dcb90`) — see the section below, now built.
>
> ---
>
> **▶ Superseded plan (kept for context) — admin evaluation entry + Excel import.** Agreed order:
> 1. **Admin one-by-one evaluation UI** (small, FE-only). The backend ALREADY allows it —
>    `ExecutionAuthorizer` bypasses chef-scoping for `Roles.Administrative`, and
>    `CreateServiceEvaluationCommandHandler` / `UpdateServiceEvaluationCommandHandler` honour it (covered by
>    `EvaluationHandlerTests.An_administrative_user_may_evaluate_any_service`). The gap is purely frontend:
>    `features/admin/api/adminApi.ts` exposes **no** evaluation endpoints. Reuse the chef's `EvaluationModal`
>    from `features/employee/pages/EmployeeServicesPage.tsx`.
> 2. **Excel/CSV bulk evaluation import** — agreed design (see "Evaluation import" section below): explicit
>    mode (whole-stage vs per-period), mandatory **preview/dry-run** before apply, all-or-nothing transaction,
>    every row routed through `assignment.SubmitEvaluation` (no bulk shortcut writes), `IEvaluationSheetParser`
>    port in Application + ClosedXML adapter in Infrastructure, downloadable pre-filled template.
> 3. **Fiche gate** — `GetFicheDeValidationQueryHandler` gates only on `Result == Validé`, so a student can
>    print the fiche BEFORE the administration ratifies. Suggest also requiring `Status == Validated`. One line,
>    needs your yes.
> 4. Still open from the audit: **délocalisation authorization hole** and the **IDOR on record/fiche** (below),
>    plus the **pause-scope** decision (level-wide vs per-cohort — my recommendation is level-wide).
>
> **▶ NEW (2026-08-07) — "Valider" re-defined as RATIFICATION, not academic override.** ⚠ Semantics change.
> Admin *Valider* (Suivi des affectations) means **officialise the professor's evaluation whatever it says** —
> it does NOT mean "the student passed". `InternshipAssignment.Validate()` previously set
> `Result = StageAssignmentResult.Validé` **unconditionally**, so ratifying a chef's 6/20 flipped the student to
> *passed* while `FinalScore` still read 6. `Reject()` was the mirror image. Both now move **`Status` only**:
> * **`Status`** = workflow — who has signed off (`… → Evaluated → Validated/Rejected`);
> * **`Result`** = academic outcome, written **solely** by `RecomputeFinalScore` from the marks.
> Bulk *Valider* was already correct in one respect: `ValidateCohortAssignmentsCommandHandler` only touches
> assignments at `Status == Evaluated`, so it can never validate a student the chef has not evaluated — but the
> skip is **silent** (20 students / 3 evaluated → toast says "3 validés", nothing says the other 17 are
> ineligible). Two existing tests had encoded the OLD meaning and were rewritten, not patched. No migration.
>
> **▶ NEW (2026-08-07) — student portal 403 on Stages fixed.** Opening a stage rendered `AttendanceSummary`,
> which called `GET /service-periods/{id}/attendance`; that handler used `EnsureCanRecordAttendanceAsync` — the
> **write** scope (admin, or chef/staff of the service) — so a student got `AttendanceNotAllowed` (403).
> `errorMiddleware.ts` toasts every 403 while the other queries succeed → "error, but the page works fine".
> New `ExecutionAuthorizer.EnsureCanReadAttendanceAsync` = everyone who may record it **plus the student who
> owns the period**. Write scope untouched (a student still cannot record presence, nor read a classmate's).
> An unknown period still returns **NotFound**, not Forbidden, so the widened check can't leak row existence.
>
> **▶ NEW (2026-08-06) — chef worklist year-scoping REMOVED (two live incidents).** The chef saw his services
> but **no groups**, twice. Root cause both times: the worklist was implicitly scoped to the current academic
> year — first by `Registration.AcademicYearId`, then (my first fix) by the year's calendar span. Both couple
> live work to bookkeeping that drifts out of step with the dates rotations actually run on; when it drifts the
> chef silently sees **nothing**. Incident 2 data: year flagged current = 2024-2025, rotations ran Jun–Sep 2026
> → all 263 started periods filtered out. **Year scoping is now opt-in**: no `AcademicYearId` ⇒ no year filter
> at all (the chef UI has no year selector). An unknown year id leaves the list unscoped rather than empty.
> `ChefWorklistYearScopeTests` was rewritten (the old 4 asserted the implicit default). **Lesson: never gate a
> worklist on a bookkeeping record that can disagree with the data.**
>
> **▶ NEW (2026-08-06) — stage-period OVERLAP validation (new business rule).** Creating a period never checked
> dates (only duplicate `PeriodNumber`), and updating one checked **nothing**. New `SlotOverlapGuard`
> (`Application/Stages/Slots/`) enforces: **no two periods of the same academic LEVEL may run at the same time**
> — inside one stage or across two. A level's students follow every one of its stages, so an overlap would put a
> group in two services on the same day. Different levels may share dates. Windows are **inclusive of both ends**
> → a period ending 31/03 and the next starting 31/03 now COLLIDE (next must start 01/04). New error
> `StageErrors.SlotOverlap`. Wired into both create + update; DI in `Application/DependencyInjection.cs`.
>
> **▶ NEW (2026-08-06) — Postgres now PERSISTS + search fixes.** `PGSH.AppHost/Program.cs`: Postgres had no
> volume (Keycloak did — that's why only its data survived). Added `.WithDataVolume("pgsh-postgres-data")` +
> `ContainerLifetime.Persistent`. Seeder verified idempotent (every block guarded by `AnyAsync`). ⚠ A named
> volume starts EMPTY — a pre-switch backup is at `pgsh-backup-20260806.dump` (repo root, `*.dump` gitignored);
> restore with `pg_restore -U postgres -d TodoDatabase --clean --if-exists`. **Seeder academic years were
> hardcoded** (fixed 2022-2025, 2024-2025 flagged current) while the rest of the seed lays rotations out around
> *today* → the "current year" was two years stale. Now derived from the current date (year runs 1 Sept → 31 Aug).
> **Search:** `GroupsPage.tsx` fetched matches then silently kept `items[0]` — "Alaoui" with two Alaouis showed
> an arbitrary student's group, and the query destructured only `data` (no `isFetching`) so nothing rendered
> while loading. Now lists every match as pickable chips + spinner + skeletons. Backend: `Appogee` was matched
> **case-sensitively** while every other field was lowered (`"ap2200a"` never found `AP2200A`); fixed, and
> `.Trim()` added to all 7 search handlers. Other pages already had debounce + `isFetching` + `setPage(1)`.
>
> **▶ NEW (2026-08-06) — test suite 36 → 260.** `PGSH.Tests` now covers the assignment state machine, all three
> evaluation modes + chef scoping, chef worklist scoping, pause/resume, student record + fiche, délocalisation
> (domain + handler), cohort transfers, bulk cohort ops, schedule publishing incl. cross-stage capacity, the
> mid-stage reroute, slot overlap, student search, attendance read/write scope, and 22 command-validator tests.
> Shared seeding lives in `PGSH.Tests/TestHarness.cs`. ⚠ **Still 100% unit-level** — `UseInMemoryDatabase`
> ignores FK constraints, unique indexes, `OnDelete` and SQL translatability, so constraint/translation defects
> are invisible. **Agreed next step: Testcontainers-over-real-Postgres integration tests + `WebApplicationFactory`
> functional tests** (the only way to cover the authorization gaps below). Neither is built yet.
>
> **▶ NEW (2026-08-06) — code audit: CONFIRMED defects, none fixed yet.** Reproduced with runnable probes:
> 1. 🔴 **`stages/delocalize` has NO authorization at any layer** — endpoint is bare `.RequireAuthorization()`
>    and `DelocalizeStudentCommandHandler` has no `ExecutionAuthorizer` check. A **student** can POST their own
>    `registrationId` with `outcome: Validated`, which runs `Delocalize()` → `SubmitEvaluation()` → `Result =
>    Validé` → the fiche gate then passes. Self-service stage validation. Needs your policy call (Scolarité +
>    SuperUser only?).
> 2. 🔴 **Editing an evaluation with objectives throws `DbUpdateConcurrencyException`.**
>    `UpdateServiceEvaluationCommandHandler.cs:63` pre-sets `Id = Guid.NewGuid()` on `ObjectiveScore` children
>    of an **already-tracked** evaluation → EF classifies them `Modified`, not `Added` (proven via
>    `ChangeTracker.DetectChanges`) → `UPDATE … WHERE Id = <new guid>` → 0 rows. Only fires when
>    `ObjectiveScores` is non-empty, so "Valider le stage" mode works and the other two break. Fix: drop the
>    pre-set `Id`, mutate the collection in place. Same gotcha the domain warns about at
>    `InternshipAssignment.cs:246` and `:277`.
> 3. 🔴 **`MidStageTransferRescheduler.RerouteAsync` NREs on a slot-less period.** The "missing slots" guard
>    (`:51-62`) admits `CohortSlotAssignmentId == null`, then null-propagation drops it from `missing`, so the
>    guard passes and `:66` dereferences `CohortSlotAssignment!`. Any ad-hoc period hits this — including the
>    one `Delocalize()` creates. Confirmed `NullReferenceException`.
> 4. 🟠 **`RerouteAsync:79` start-date is not clamped** — `date < target.EndDate ? date : target.StartDate`
>    produced a period starting 2025-12-20 for a slot opening 2026-01-01. Should be
>    `date > target.StartDate ? date : target.StartDate`. Also `:72` / `MaterializeAtTargetAsync:168` set
>    `EndDate` with no floor at `StartDate` → a backdated transfer yields `EndDate < StartDate`.
> 5. 🟠 **IDOR on `GET internship-assignments/{id}/record` and `/fiche`** — neither handler checks ownership or
>    role, and `GET internship-assignments` is unscoped, so a student can enumerate ids and read every
>    classmate's marks, comments and attendance.
> 6. 🟡 `UpdateServiceEvaluationCommandHandler` bypasses the aggregate (mutates fields inline, calls
>    `RecalculateFinalScore`) → **no domain event**, so a mark change leaves no audit trail.
> 7. 🟡 `EvaluationSubmittedDomainEvent` publishes `evaluation.TotalScore`, which `Normalize()` has just set to
>    `null` for both validate-only modes. Should publish `StageScoring.PeriodMark(evaluation)`. Latent (no
>    subscriber yet).
> 8. 🟡 Objective ids are never validated against the period's stage — `GetValueOrDefault(...)!` in both
>    evaluation handlers; a foreign id is silently weighted 1, a nonexistent one dies on the FK as a 500.
> 9. 🟡 `ResumePeriod` (`InternshipAssignment.cs:137`) shifts every later period with no filter on
>    `IsComplete` / `IsInterrupted` → resuming back-dates closed rotations and terminal history rows.
> 10. 🟡 **`CompletePeriod` has no `IsStarted` guard** (unlike `PausePeriod`), so a rotation that never ran can
>     be closed and then evaluated. Deliberately left **uncovered by tests** — a test either way would cement
>     an asymmetry that has not been ruled on. Your call.
>
> **▶ NEW (2026-07-02b) — Stage validation roll-up (all-periods-must-pass) + student notes record + fiche de validation.**
> Validation rule CHANGED (`InternshipAssignment.RecomputeFinalScore` + new `Domain/Stages/StageScoring.cs`):
> a stage is `Validé` only when EVERY (non-interrupted) period is individually validated — one failed period
> => `NonValidé`; the final note is the **mean of the periods' marks** (validate-only period = 10/0); the
> verdict is withheld (`NonÉvalué`) until all periods are evaluated. Replaces the old "mean ≥ 10" rule.
> `StageScoring.PeriodMark/IsPeriodValidated` is the shared source of truth (domain + read handlers).
> New reads: **`GET internship-assignments/{id}/record`** (`GetStudentStageRecordQuery`) = full per-period
> detail (mark, verdict, full evaluation, attendance counts) for the click-through detail view;
> **`GET internship-assignments/{id}/fiche`** (`GetFicheDeValidationQuery`) = print-ready fiche payload,
> gated on `Result == Validé` (`StageErrors.FicheNotAvailable`), objective table with marks (validate-only
> => 10), empty header/footer left for the FE attestation template. Notes list (`GET internship-assignments`)
> gained `partitionLabels` + `periodNumber` + `search` (name/appogée/CNE) filters and an `AllPeriodsEvaluated`
> flag on the summary. No migration. Tests green (19 total): `StageValidationScoringTests`,
> `StageScoringTests`, `FicheDeValidationHandlerTests` (+ the mid-stage set below).
>
> **▶ NEW (2026-07-02) — Mid-stage transfer now auto-hands-off + xUnit test project added.**
> Bug fixed: a Temporary transfer done while the stage is *en cours* with the "Transfert en cours de stage"
> toggle OFF left the in-flight `ServicePeriod` pinned to the ORIGIN service, so the NEW chef got
> `NotServiceChef` and couldn't evaluate after clôture. `MidStageTransferRescheduler.MaterializeAtTargetAsync`
> now (a) cuts the origin in-flight period to `IsInterrupted` and (b) lands the student on the target group's
> running period as started — **only when the target group is already running that period** (else keeps the
> origin period, no void). `CompletePeriod`/`PausePeriod` reject interrupted periods
> (`StageErrors.PeriodInterrupted`) so a bulk close can't revive them; the old chef's worklist now shows
> interrupted periods as grayed "parti vers…" rows (`ServicePeriodResponse.IsInterrupted` added). **No new
> migration** (`IsInterrupted` column already exists). New **`PGSH.Tests`** xUnit project (7 tests green):
> `MidStageTransferReschedulerTests`, `InterruptedPeriodTests`, `InternshipAssignmentLifecycleTests`. Caveat:
> students transferred BEFORE this fix are not auto-repaired — re-do the transfer.
>
> **▶ RESUME HERE (next session).** Délocalisation (#3) is **DONE (2026-06-25, session 7)** — see
> "Délocalisation" below. ⚠ **One migration pending DB apply: `20260625194358_Delocalization`** (the running
> API holds the old schema → the délocalisation endpoint 500s until applied; restart the Aspire stack so
> MigrationService runs it, or `dotnet ef database update`). Next, in order:
> 1. **Apply the migration + verify délocalisation end-to-end** (recipe in that section).
> 2. **Revalidation (#4)** — fail → revalidate in same service unless a transfer/déloc demande; may span
>    years. **Open: group-all-revalidations vs ad-hoc per-student.** Full 4-type model in agent memory
>    `project_student_mobility`.
> 3. Optional refinement: scope the chef gray/green markers to the **current stage's chef** + show **net
>    group headcounts** (deferred from the transfer build — markers still use the existing group-model logic).
> 4. Remaining Suivi item: small-service planning skip (#3, needs your decision — see that section below).
> 5. **Follow-up (admin eval UI):** there is still NO standalone admin screen to enter/edit a ServiceEvaluation;
>    délocalisation captures the validate-only verdict + fiche inline at recording time. If a délocalisation's
>    verdict must be entered LATER (the two-phase "student returns" flow), build an admin period-eval entry.
>
> **✅ MIGRATIONS APPLIED + VERIFIED (2026-06-25, session 6).** All five migrations — `EvaluationModes`,
> `ServicePeriodIsStarted`, `TransferType_OnCohortMembership`, `ServicePeriodIsInterrupted`,
> `ServicePeriodPause` — are now in `TodoDatabase` (confirmed against the running Aspire Postgres). Schema
> checked: `ServicePeriods.{IsStarted,IsInterrupted,IsPaused}`, `CohortMembership.{TransferType,OriginalCohortId}`,
> the `PeriodPause` table, and `ServiceEvaluation.{Mode,Outcome,TotalScore}` all present. The running API
> already serves the session-5 routes (`schedule/pause` + `/resume`).
> Build backend via `PGSH.Infrastructure.csproj` (API DLLs lock while the app runs); add ef migrations with
> `--startup-project PGSH.Infrastructure` (design-time factory) since the running API blocks the API build.
> _Updated 2026-06-25._

## ▶ CNPN editing UI ✅ DONE (2026-08-08, session 10)

The CNPN (per-`(level, year)` requirement set) had a full write API — `PUT levels/{id}/curriculum/{yearId}`
(`SaveCurriculumCommand`) and `POST …/copy` (`CopyCurriculumCommand`), both Scolarité-only, 10 tests —
but `CurriculumPage` was **read-only**, so recording a text meant calling the API by hand. Closed.

- `adminApi.ts` gained `saveCurriculum` (PUT) + `copyCurriculum` (POST); both invalidate the
  `curriculum-{level}-{year}` tag **and** `CURRICULUM_DIFF`, since every comparison now reads differently.
- New `features/admin/components/CurriculumEditor.tsx`. The **compared** year ("ce qui s'applique
  aujourd'hui") is the one edited. No text for it ⇒ *Cloner depuis* another year, or *Saisir le texte*;
  a text ⇒ *Modifier le texte* → stage picker limited to the level's stages (coefficient/duration
  pre-filled from the stage catalogue), per-row inputs, remove.
- **Dropped stages are shown before the save lands**, with the rule spelled out: removal releases only
  new students, so anyone who failed the stage still owes it. That is the whole reason this screen exists.
- Pre-flight guards mirror `SaveCurriculumCommandValidator` — Enregistrer is disabled with a tooltip
  reason when nothing changed, a coefficient is < 1, a duration is < 1, or the reference exceeds 200 chars.
  Cloning is offered only for a year with no text, which is the only case the server accepts.
- ⚠ **`react-hooks/set-state-in-effect` is an error here.** The form is remounted by key on
  (level, year, curriculum id) instead of mirroring server data into state, and the page's two
  default-selection effects became derived values. Lint + `npm run build` clean; backend suite 456 green.

**Test recipe:** admin (Scolarité) → Académique → **CNPN (programme)** → pick a level and set *Texte
comparé* to a year with no CNPN ⇒ orange "Aucun CNPN enregistré" + *Cloner depuis* → pick the previous
year → **Cloner** ⇒ the programme appears. **Modifier le texte** → drop a stage ⇒ a red banner names it
and says the student repasses it anyway → change a coefficient → **Enregistrer** ⇒ the comparison table
re-reads *Retiré* / *Recoté* for the same pair of years without a page reload.

## ▶ Audit defects ✅ ALL CLOSED (2026-08-07, session 9)

The ten confirmed defects from the 2026-08-06 audit are done. #2, #6, #7 and #8 were closed earlier
this session with the evaluation-amend work; the rest here.

| # | What it was | Now |
|---|---|---|
| 🔴 1 | `stages/delocalize` had no role check — a **student could self-validate a stage** by posting their own registrationId with `outcome: Validated` | Scolarité/SuperUser only (`EnsureIsAdministrative`) |
| 🔴 3 | `RerouteAsync` **threw** on a slot-less rotation: the missing-slots guard admitted it (a null cell has no period number, so it fell out of the list being collected) and the loop then dereferenced it | ad-hoc rotations refused with `CannotRerouteAdHocPeriod`; NRE reproduced by test before fixing |
| 🟠 4 | remaining-window dates unclamped — a transfer before the slot opened started the rotation early; a backdated one left `EndDate < StartDate` | `RemainingWindowStart` + `CutShortAt`, shared by the reroute and the plain materialisation |
| 🔴 5 | **IDOR** on `/record` and `/fiche` — ids are guessable, neither handler checked anything | `EnsureCanReadAssignmentAsync`: owner, chef/staff of a service the student rotates through, or admin |
| 🟡 9 | `ResumePeriod` shifted *every* later rotation, back-dating closed and interrupted ones | only rotations still ahead move |
| 🟡 10 | `CompletePeriod` had no `IsStarted` guard, so a rotation nobody ran could be closed then graded | symmetric with `PausePeriod`; the bulk runner skips failures rather than aborting, so a mixed selection closes what it can |
| — | `GET service-periods/{id}/evaluation` had **no authorization at all** (found while probing the stack) | same read scope as the period's attendance |

The read/write rules all live in `ExecutionAuthorizer` beside the existing ones, so there is still one
place that knows who may act on what. The attendance read scope was rewritten in terms of the shared
period-read rule rather than duplicated.

## ▶ Authorization lockdown ✅ DONE (2026-08-07, session 9)

Probing the running API turned up `GET /api/students` answering **unauthenticated** with 5701
students' name, email, CNE, Apogée and CIN. Not an isolated miss: **44 of the 93 endpoint files never
called `RequireAuthorization` at all** — including `Delete` for hospitals, levels, services, students
and registrations, and the 13 stage-schedule routes flagged in earlier sessions.

All 44 files now carry `.RequireAuthorization()` (**56 endpoints**), the baseline the other 49 files
already used. Verified after restart — every one of these now returns **401**:
`GET students · stages · hospitals · levels · employees · internship-assignments · service-periods`,
`POST stages/{id}/schedule/pause`, `DELETE levels/{id}`, `DELETE hospitals/{id}`.

Nothing depended on anonymous access: the only unauthenticated screens are the landing and about
pages, and neither calls the API.

⚠ **This closes the anonymous door only.** Per-role scoping is still the Phase 8 `PermissionProvider`
stub, so *any* authenticated user — including a student — can still reach these routes. The
délocalisation self-validation hole (RESUME #3) is exactly that shape and is **not** fixed by this.

## ▶ Evaluation import (Excel) ✅ BUILT + VERIFIED (2026-08-07, session 9)

Everything in the agreed design below is implemented, with two deliberate narrowings — both flagged
rather than silently dropped:
- **.xlsx only, no CSV.** ClosedXML does not read CSV, and a hand-rolled one would mangle the quoted
  commas that the *Remarque* column is full of. Say the word and it's a small adapter branch.
- **`ValidateObjectives` is refused** (`StageErrors.ImportModeNotSupported`): a sheet carries one
  verdict per student, and per-objective marks do not fit on a line.
- **The scope AND the mark type are both chosen at upload.** The agreed sheet has a `Résultat` *and* a
  `Note` column, which would mean inferring the mode from whichever is filled — and "never inferred"
  was the non-negotiable. So the mode is explicit and the *other* column being filled is reported as
  `MissingValue` on that row.

**Layout as built:**
```
Application/Stages/Evaluations/Import/
  EvaluationImportContracts.cs     // scope, row, report, template, IEvaluationSheetParser port
  EvaluationImportPlanner.cs       // THE engine — preview and apply both run this and nothing else
  PreviewEvaluationImportQuery.cs  // dry run (+ handler)
  ImportEvaluationsCommand.cs      // apply (+ validator + handler)
  GetEvaluationImportTemplateQuery.cs
Infrastructure/Evaluations/ClosedXmlEvaluationSheetParser.cs   // ClosedXML 0.105.1, MIT
API/Endpoints/ServiceEvaluations/ImportEvaluations.cs          // the only layer that sees IFormFile
Frontend: features/evaluations/{types/import.types.ts, components/EvaluationImportModal.tsx}
          reached from AssignmentsPage → "Importer les notes"
```
Routes: `GET|POST stages/{stageId}/evaluations/import/template|preview` and
`POST stages/{stageId}/evaluations/import`, all taking `?scope=&mode=&periodNumber=`.

**Why the planner is one class:** preview and apply calling *the same* code is what makes the dry run
trustworthy. It therefore loads **tracked** entities in both cases; the preview simply never saves.

**✅ VERIFIED end-to-end against the running stack (2026-08-07), admin session, stage 2
"Stage d'anatomie clinique" (601 students, 302 assignments fully clôturées):**
- **Template** — `notes-stage-2.xlsx`, 601 students pre-filled with CNE/Apogée + indicative
  name/group, plus a *Mode d'emploi* sheet that reflects the chosen scope and mark type.
- **Multipart binding works.** `IFormFile` + `[AsParameters] ImportOptions` + `.DisableAntiforgery()`
  bind correctly, including the enums and the nullable `periodNumber`, from a real browser upload.
  This was the one thing unit tests could not reach.
- **Preview caught all four planted faults** on one file — blank note (`MissingValue`), 25/20
  (`InvalidValue`), unknown CNE (`UnknownStudent`), student listed twice (`DuplicateStudent`) —
  Appliquer stayed disabled and `ServiceEvaluation` count was **unchanged (40)** afterwards.
- **Apply** — 5 rows × 2 rotations = 10 evaluations written (40 → 50); marks exact, `FinalScore` =
  mean, `Result` derived (8 ⇒ NonValidé), `Status` → `Evaluated`. The roll-up ran through the
  aggregate, as designed.
- **Overwrite** — re-import with new marks left the count at **50** (amended, not duplicated);
  14 → 17 and 8 → 13, with `Result` correctly flipping NonValidé → Validé.
- **Per-period scope** — same file at *Une période / P1* reported **5** rotations instead of 10 and
  touched only P1: Anaïs ended `14.00,17.00`. Innocent ended mean 10.50 but **NonValidé**, which is
  the all-periods-must-pass rule showing through the import.
- **Pre-flight guard** — switching to *Une période* with no period chosen marks the Select *Requis*,
  disables both the template and upload buttons, and clears the stale report.

**Test recipe (to re-run later):**
1. Start the stack (`dotnet run --project PGSH.AppHost`), log in as Scolarité.
2. Suivi → Affectations → pick a stage whose rotations are **clôturées** (the import refuses open ones).
3. **Importer les notes** → portée *Tout le stage*, type *Note (0–20)* → **Télécharger le modèle**.
   ⇒ an .xlsx with CNE/Apogée pre-filled per student, plus a "Mode d'emploi" sheet.
4. Fill a few *Note* cells, leave one blank, mistype one CNE, put `25` in another → upload.
   ⇒ the preview must show `Valeur manquante`, `Étudiant inconnu`, `Valeur invalide`, and **Appliquer
   stays disabled**. Confirm **nothing** was written (the students' notes are unchanged).
5. Fix the file, re-upload ⇒ all rows green, Appliquer enabled → apply.
   ⇒ toast with the rotation count; open a student's **Dossier de stage**: the note is there, the stage
   note recomputed, status *Évaluée*.
6. Re-upload the same file with a different note ⇒ rows read **Remplace**, apply, note updated.
7. Repeat with portée *Une période* + a period chip, and with type *Validé / Non validé*.

## ▶ Evaluation import (Excel/CSV) — original agreed design (2026-08-07)

Bulk entry of evaluations from a sheet keyed on **CNE / Apogée**, carrying `valid | unvalid | note`, either
**per period** or **for the whole stage** (applied across all its periods).

**Non-negotiables (agreed):**
- **Mandatory preview / dry-run.** Parse → validate every row → show a report → user confirms → apply.
  Grades are the highest-consequence data in the system; an import that silently applies 200 rows where 8 were
  mis-keyed is worse than no import. Matches the pre-flight-guard rule already in force.
- **All-or-nothing** per import, one transaction. Partial grade imports are unreconcilable.
- **Every row goes through `assignment.SubmitEvaluation`** — the same aggregate path as a chef's single
  evaluation, so scoring, roll-up and domain events stay identical. No bulk shortcut writes.
- **Mode is chosen explicitly at upload**, never inferred from which columns are present (inference is where
  import tools become unpredictable).

**Sheet shape** — `CNE | Apogée | Période | Résultat | Note | Remarque`. `Période` blank/absent in
whole-stage mode. `Résultat` ∈ {Validé, Non validé} **or** `Note` ∈ 0–20, matching the three existing
`EvaluationMode`s.

**Per-row preview outcomes:** unknown student · student not in this stage · period not yet closed ·
already evaluated (⇒ *will overwrite*, not an error — amending is a requirement) · assignment already ratified
(⇒ refused, consistent with `StageErrors.EvaluationReadOnly`).

**Layout (clean-architecture split — Application never learns what .xlsx is):**
```
Application/Stages/Evaluations/Import/
  ImportEvaluationsCommand.cs      // stageId, mode, rows — ALREADY parsed
  PreviewEvaluationImportQuery.cs
  EvaluationImportRow.cs
  EvaluationImportReport.cs
  IEvaluationSheetParser.cs        // port
Infrastructure/Evaluations/ClosedXmlEvaluationSheetParser.cs   // adapter (ClosedXML, MIT)
API/Endpoints/Evaluations/ImportEvaluations.cs                 // IFormFile boundary
```
Plus a **downloadable template** generated from the stage's real periods, pre-filled with its students'
CNE/Apogée, so nobody hand-builds columns or mistypes an identifier.

## ▶ Verification + stale-status refetch fix ✅ DONE (2026-06-25, session 6)

- **Verified the transfer + pause stack against the live DB** (stack was already running): 5 migrations applied,
  schema columns/tables present, session-5 API routes live. Transferred-student data is correct — the one
  Temporary loan's 2 periods are both `IsStarted=true, IsPaused=false` (no corruption).
- **Bug fixed — student/chef saw stale period status ("Planifié" after Reprendre).** Root cause was NOT the
  domain (`ResumePeriod` correctly keeps `IsStarted=true`, only flips `IsPaused→false`) but **cross-API-slice
  cache staleness**: `pauseStagePeriods`/`resumeStagePeriods` live in the **admin** RTK Query slice and
  invalidate `Assignment/LIST`, but the student detail reads `getAssignmentById` (**student** slice, tag
  `Registration/assignment-{id}`) and the chef reads `getServicePeriodsByService` (**employee** slice) — a
  mutation in one slice can't invalidate a query in another. Fix: `refetchOnMountOrArgChange: true` on the
  student stage-detail (`getAssignmentById`), student stage-list (`getMyAssignments`), and chef worklist
  (`getServicePeriodsByService`) call sites, so revisiting a page always shows live status. FE build + lint
  clean (only the pre-existing `EmployeeServicesPage.tsx:109` eval-effect error remains). _Frontend repo commit._
- **⚠ Backlog (security): schedule endpoints are unauthenticated.** `stages/{id}/schedule/{auto-arrange,
  publish,start,complete,pause,resume}` have **no `.RequireAuthorization()`** — an unauthenticated POST mutates
  data (a probe paused 604 periods). Pre-existing project-wide posture (auth/CORS lockdown = Phase 12), but a
  mutating endpoint with no guard is worth fixing ahead of the rest. `GET /service-periods` 401s (it has a
  guard); the schedule group never did.

## ▶ Délocalisation (mobility #3) ✅ DONE (2026-06-25, session 7)

Student does the **whole stage outside the faculty** (hometown / abroad); the app has no control over the
rotation. Recorded as a single **ad-hoc, pre-completed** `ServicePeriod` at an external service; the
paper-validation verdict + fiche reference are captured in the same admin action (délocalisation is logged
after the student returns). Build: Application → 0 errors; API reached copy-step with 0 CS errors (DLL lock
only = API running); migration scaffolded; frontend `npm run build` + eslint clean on changed files.

- **Domain**: `ServicePeriod.{IsDelocalized (bool), Delocalization?}`; new child entity `Delocalization
  {Reason, DemandeId?}` (1:1, mirrors `PeriodPause`). `ServiceEvaluation.FicheReference (string?)` — paper-proof
  ref, URL/text for now (real upload = Phase 5 with Demande). `HistoryType.Delocalization`. New
  `StudentDelocalizedDomainEvent`. `InternshipAssignment.Delocalize(stageId, serviceId, start, end, reason,
  demandeId?)`: refuses if any period is started/complete/interrupted (`StageErrors.StageAlreadyUnderway`),
  drops planned periods, adds ONE ad-hoc period `IsStarted=IsComplete=IsDelocalized=true`, Status→Completed,
  raises the event. **Tracked-child key gotcha respected** — period/Delocalization/eval Ids never pre-set.
- **Application**: `DelocalizeStudentCommand` (+validator, IAuditableCommand `STUDENT_DELOCALIZED`). Handler
  finds the student's `(group, stage)` cohort (errors: `NoGroupForDelocalization`, `CohortMissingForStage`,
  `Services.NotFound`), reuses an existing not-yet-started assignment or creates one, calls `Delocalize`, and
  — when `Outcome` is supplied — records a **validate-only** `ServiceEvaluation` (`Mode=ValidatePeriod` +
  `FicheReference`) via `SubmitEvaluation` (→ Status Evaluated, score auto-maps 10/0). `StudentDelocalizedEventHandler`
  writes `History.Delocalization {stage, service, reason}`. `FicheReference` also threaded through the generic
  Create/Update eval commands + `ServiceEvaluationResponse` + GetByPeriod projection; `ServicePeriodSummary`
  (admin GetById) now carries `IsDelocalized`/`DelocalizationReason`.
- **Infra**: `DelocalizationConfiguration` (unique FK on ServicePeriodId, cascade) + `FicheReference` maxlen
  1000. Migration `20260625194358_Delocalization` (IsDelocalized bool default false, FicheReference nullable,
  Delocalization table). **⚠ pending DB apply.**
- **API**: `POST stages/delocalize` (binds command directly, `.RequireAuthorization()`). Eval Update Request
  gained `FicheReference`.
- **Frontend**: `GroupDetailPage` roster gets a teal plane action → `DelocalizeModal` (stage Select = Planned
  assignments; searchable/debounced external-service Select via `getServices`; date-range; required motif;
  optional verdict SegmentedControl `Validation plus tard / Validé / Non validé` → required fiche-reference
  TextInput when a verdict is chosen, pre-flight guarded). `delocalizeStudent` mutation + `DelocalizeStudentRequest`
  type. Admin still has no standalone period-eval screen (see RESUME #5).
- **Test recipe (after migration applied)**: ensure the stage has cohorts for the student's group (run
  affectation/macro-plan first), and that the external hospital/service exists in *Infrastructures* (add it if
  not). Group detail → student row → ✈ **Délocaliser** → pick the Planned stage + external service + date range +
  motif → optionally **Validé** + fiche ref → Enregistrer. Verify: the stage's assignment shows **Terminé/Évalué**
  with the validate-only result; the student **History** shows a *Délocalisation* row (stage · service · motif);
  the in-faculty chef worklist never shows the student (external service has no chef).

## ▶ Transfer bug-fixes + Stage Pause/Resume ✅ DONE (2026-06-25, session 5)

Batch from user testing the transfer model. Build: backend `PGSH.Infrastructure` + `PGSH.API`
(temp-output to dodge the running-API DLL lock) 0 errors; frontend `npm run build` + eslint clean on
changed files (the one pre-existing `EmployeeServicesPage.tsx:109` eval-effect lint error is untouched).

1. **History UX (direction + motif).** A temporary loan was only a `CohortTransfer`, which
   `GetStudentHistoryQueryHandler` filters out → the outgoing loan + motif were invisible and only the
   *return* row showed (reading loaned→home, hence "reversed"). Fix: `StudentCohortTransferredDomainEvent`
   now carries `TransferType`; for a **Temporary** move `StudentCohortTransferredEventHandler` writes a
   **visible** `GroupTransfer` history with GROUP labels + `stage` + `reason` + `temporary:true`
   (definitive still writes the hidden `CohortTransfer`). `HistoryPage.tsx` got a transfer-aware renderer:
   `Groupe X → Groupe Y` arrow (correct order) + "Motif :" + stage chip; `temporary`→"Prêt temporaire",
   `temporaryReturn`→"Retour de prêt"; French key labels replace the raw capitalized dump.
2. **"Loaned out" badge.** `GetGroupByIdQuery` roster rows now carry `LoanedToGroup`/`LoanedStage`
   (active Temporary `CohortMembership`) + an `IncomingLoans` list. `GroupDetailPage` shows a grape
   "Prêt → Groupe X · stage" badge on loaned-out students and an "N prêt(s) entrant(s)" table.
3. **Incoming loan now evaluable.** New `MidStageTransferRescheduler.MaterializeAtTargetAsync`: on a
   normal (non-reschedule) transfer it rehomes the future, not-started rotation onto the **target
   cohort's slot cells** → real, actionable `ServicePeriod`s (joins the target group's progress via
   `IsStarted`), so the synth green row is suppressed and the new chef can clôturer/évaluer. No-op when
   the target group has no schedule for the stage (informational row stays as fallback). Wired in
   `TransferStudentCommandHandler` (the assignment query now always `.Include(ServicePeriods…StageSlot)`).
4. **Stage Pause/Resume (new).** `ServicePeriod.IsPaused` + child `PeriodPause{StartDate,ResumeDate?,Kind,Reason}`.
   `InternshipAssignment.PausePeriod`/`ResumePeriod` — resume closes the open pause, adds the lost days to
   that period's end and **cascade-shifts every later period** of the assignment. `CompletePeriod` now
   refuses a paused period. New `StagePauseRunner` (mirrors `StagePeriodRunner`) + `PauseStagePeriodsCommand`/
   `ResumeStagePeriodsCommand` (scope: cohorts/partition/periods), endpoints `POST stages/{id}/schedule/pause`
   + `/resume`. Suivi count bucket `InternshipStatus.Paused` (display-only, never persisted on assignments).
   Migration `ServicePeriodPause`. **FE:** Suivi bar Pause (modal: kind Examens/Vacances/Autre + reason) /
   Reprendre buttons (pre-flight guarded by `selectionHasTargetPeriod`); chef worklist "En pause" badge;
   admin/chef period responses carry `IsPaused`/`PauseReason`; calendar (`StageTimelinePage` +
   `GetYearTimelineQuery`) draws hatched orange pause bands per partition and extends the partition bar to
   the shifted `ServicePeriod` end (dates shift on ServicePeriod, NOT StageSlot — slots are shared across
   partitions). **Deferred:** capacity re-check on resume (shifted windows can overflow a service — currently
   no guard; matches the forced-override stance, flag if a warning is wanted).

## ▶ Forced mid-stage transfer (hand-off) ✅ DONE (2026-06-25)

Exceptional escape hatch (e.g. a student must leave her group mid-rotation). **Opt-in** so the normal
transfer is untouched. Decisions locked with user: old in-progress period **closed early + kept as history,
NOT re-evaluated** by the old chef; new chef evaluates the remainder **as a full stage**; new window =
**remaining time only** (transfer date → slot end) + future periods in full.

- **Domain**: new `ServicePeriod.IsInterrupted` — terminal history (kept w/ attendance, excluded from chef
  worklist, suivi counts, score, and the "all periods done" lifecycle checks: `CompletePeriod`/`SubmitEvaluation`
  now treat `IsComplete || IsInterrupted` / `Evaluation != null || IsInterrupted` as done).
- **`MidStageTransferRescheduler`** (`Stages/Planning/`, DI-registered): runs only on an assignment with a
  started, not-complete period. Completed periods untouched; the in-progress one → `IsInterrupted` + EndDate
  clamped to the transfer date, and a NEW started period is created against the **target cohort's slot cell**
  for the remaining window; future periods → old removed, re-created full (inactive). Fails with
  `StageErrors.TargetScheduleMissingPeriods` if the target group has no slot for a moved period.
- **Command**: `TransferStudentCommand.Reschedule` (default false). Handler loads ServicePeriods +
  `CohortSlotAssignment.StageSlot` only when rescheduling, calls the rescheduler after `TransferToCohort`.
- **Worklist/counts**: `GetMyServicePeriodsQueryHandler` filters `!IsInterrupted` (actionable rows) and the
  incoming-synth existence check now matches on `CohortSlotAssignmentId == sa.Id` (so a re-materialised period
  dated from the transfer day suppresses the synthesized green row → the new chef sees a **real, actionable**
  row instead). `GetAssignmentStatusSummaryQueryHandler` excludes interrupted periods.
- **EF + migration**: `ServicePeriodIsInterrupted` (bool default false). **Pending DB apply** (see ⚠ — now
  FOUR migrations pending).
- **Frontend**: `TransferModal` got a "Transfert en cours de stage" `Switch` (default off) wiring
  `reschedule`. Build: backend 0 errors; FE `npm run build` + eslint clean.
- **Capacity** on the target service is **not** hard-blocked for this forced path (admin override by design).
- **Follow-up (small):** surface `IsInterrupted` as an "interrompu (transfert)" badge in the student/admin
  period detail views (logic already excludes it everywhere that matters; this is cosmetic).
- **Test recipe**: start a stage (admin Démarrer P1) so the student has a started period → Transférer →
  Définitif + target group + reason + **toggle "Transfert en cours de stage"** → the old chef's in-progress
  row disappears (kept as interrupted history), the **target group's service chef** now sees the student as a
  real actionable rotation (remaining dates) and can clôturer/évaluer normally; suivi count no longer shows a
  stuck "en cours".

## ▶ Transfer types: Temporary vs Definitive ✅ DONE (2026-06-25)

**Open question RESOLVED — transfer unit = GROUP (not service), for both types.** Full 4-type mobility model
captured in agent memory `project_student_mobility`. This session implemented types 1+2 (transfer); déloc (3)
and revalidation (4) are still queued.

- **Domain**: new `enum TransferType {Temporary,Definitive}` (`Domain/Registrations/TransferType.cs`).
  `CohortMembership` gained `TransferType` (text via `HasConversion`, **default `Definitive`** so the initial
  membership + legacy rows never look like a loan) + nullable `OriginalCohortId` (where a temporary loan
  returns). `InternshipAssignment.TransferToCohort(...)` takes a `TransferType` and records type +
  `OriginalCohortId` (= previous cohort, only for Temporary). New `CompletePeriod` path: when the stage
  finishes (all periods complete → `Completed`), `EndTemporaryTransferIfAny` closes the active temporary
  membership (sets `EndDate`) and raises `TemporaryTransferEndedDomainEvent` — remaining stages were never
  moved so nothing else is undone.
- **Application**: `TransferStudentCommand` gained `Type` (default **Temporary**) + nullable `StageId`.
  Handler branches: **Definitive** = existing behaviour (registration group changes + ALL active assignments
  cascade, same-group guard applies); **Temporary** = registration group **untouched**, only the named
  stage's active assignment(s) move (validator requires `StageId` when Temporary; returns
  `Transfers.NoActiveAssignment` 409 if none). New `TemporaryTransferEndedEventHandler` writes a
  `History.GroupTransfer` row (`temporaryReturn:true`) for the return. Added `StageName` to
  `InternshipAssignmentSummaryResponse` (used by the transfer modal's stage picker).
- **Auto-revert correctness**: the revert reads `MembershipHistory`, so **all three** CompletePeriod callers
  now `.Include(a => a.MembershipHistory)` (`StagePeriodRunner`, `CompletePeriodsCommandHandler`,
  `CompleteServicePeriodCommandHandler`) — otherwise it silently no-ops.
- **EF + migration**: `20260625091416_TransferType_OnCohortMembership` (TransferType text **defaultValue
  "Definitive"** hand-set; `OriginalCohortId` int null). **Pending DB apply** (see ⚠ above).
- **Frontend**: `GroupDetailPage` `TransferModal` got a `SegmentedControl` Temporaire (1 stage) / Définitif
  (année) + a stage `Select` (this student's Planned/Ongoing assignments via
  `useGetInternshipAssignmentsQuery`, `skipToken` when no student) shown only for Temporary, with helper
  text. `TransferStudentRequest` gained `type`+`stageId`; `transferStudent` now also invalidates
  `Assignment/LIST`. Build: backend `PGSH.Infrastructure` 0 errors; frontend `npm run build` + eslint clean
  on changed files.
- **Test recipe (after stack restart)**: pick a student doing a stage → Transférer → **Temporaire**, choose
  that stage + a target group + reason → confirm the chosen stage's assignment now sits in the target group's
  cohort while OTHER stages stay put and `Registration.AcademicGroupId` is unchanged; close that stage's
  periods (Suivi → Clôturer) → a `GroupTransfer` "retour" history row appears + the temporary membership has
  an `EndDate`. Repeat with **Définitif** → registration group changes + all active assignments cascade.

## ▶ Current work stream: Student Mobility (design LOCKED)

Full design in agent memory `project_student_mobility.md`. Build order **1 → 2 → 3 → 4**.
Recently shipped before this (verified): chef-worklist granularity + truncation fix, planning-grid
click-to-edit perf, optimistic allowed-services, central `pageSize` clamp (≤200), `GET /service-periods`
admin-only. Stress-test recipes live in `NOTES.md` → "Regression / Stress Checks".

**#1 — Normal transfer: traceability + chef gray/green visibility ✅ DONE (2026-06-05).**
- **Reason now required**: `TransferStudentCommand.Reason` is non-nullable + validator `NotEmpty`.
- **Same-group guard**: transferring to the student's current group now returns `Error.Conflict`
  ("AcademicGroups.SameGroup" → 409). Loop also skips any assignment whose target cohort == current cohort.
- **`DbUpdateConcurrencyException` on transfer — FIXED.** Diagnostic confirmed `CohortMembership[Modified]`:
  the *new* membership in `InternshipAssignment.TransferToCohort` was created with a pre-set
  `Id = Guid.NewGuid()` and added to an already-tracked assignment. Because `CohortMembership.Id` is a
  store-generated key (`ValueGeneratedOnAdd` by convention), a non-sentinel key on an entity reached via a
  tracked parent makes EF classify it **Modified** (UPDATE a non-existent row → 0 rows) instead of **Added**.
  Fix: don't set `Id` in `TransferToCohort` — let EF generate it (→ Added → INSERT). The affectation /
  schedule paths were unaffected because they `DbSet.Add(rootAssignment)`, cascading children as Added
  regardless of key. (Diagnostic try/catch was removed after the fix.)
- **Domain-event publishing under a pooled DbContext — FIXED (systemic).** After the save succeeded, MediatR
  threw `Cannot resolve INotificationHandler<…> from root provider because it requires scoped
  IApplicationDbContext`. Cause: Aspire `AddNpgsqlDbContext` **pools** `ApplicationDbContext`, so pooled
  instances are built from the **root** provider → the injected `IPublisher` was the root mediator →
  notification handlers (which need the scoped `IApplicationDbContext`) resolved from root and failed. This
  broke **every** domain event with a scoped-dependency handler (validation, rejection, status change,
  group/cohort transfer), not just transfer. Fix: `ApplicationDbContext` now injects the pool-safe singleton
  `IServiceScopeFactory` and `PublishDomainEventsAsync` opens a fresh `CreateAsyncScope()` to resolve a
  scoped `IPublisher` per save. Handlers run in their own scope/context (correct — events fire post-commit).
- **Worklist gray/green overlay** in `GetMyServicePeriodsQueryHandler` (`Application/Employees/MyServices/`),
  surfaced via `ServicePeriodResponse.Transfer` (`TransferMarker{Direction,GroupLabel,ServiceName,Reason,Date}`):
  - **Outgoing** (gray, strike-through, "→ Groupe m · Service Y") = a real published period whose
    `CohortSlotAssignment.CohortId != InternshipAssignment.CurrentCohortId` (student left after publish).
    Destination group/service from the current cohort's slot for the same window; reason/date from the
    active `CohortMembership`.
  - **Incoming** (green, "← Groupe n · Service X") = **synthesized** rows (no real `ServicePeriod`, since
    transfer does NOT re-publish): current cohort's slot lands in a chef service, assignment's active
    membership has a `TransferReason`, and no matching period exists. Origin group/service from the most
    recent closed membership.
  - Transfer rows are **non-actionable** and excluded from the active/à-évaluer/évalué counts + status
    filter (shown only under "Tous"); separate "entrant/sortant" badges at service + window + group level.
- **History DTO field fix**: `StudentHistoryResponse.EventType` → `HistoryType` (serializes as `historyType`)
  to match the frontend contract — the mismatch crashed the student dashboard/history once real history rows
  existed (they were empty before because domain events couldn't publish — see pooled-context fix above).
  Frontend `historyConfig` now exposes `getHistoryConfig()` with a fallback so an unknown type never crashes.
- **Traceability readback** works: `HistoryPage` renders `Metadata` (from/to/reason) generically
  for `GroupTransfer`/`CohortTransfer`. (Polish opportunity: friendlier metadata key labels — `fromGroup`
  shows as "Fromgroup".)
- **Note / known limitation**: transfer never moves or re-publishes `ServicePeriod`s, so the new chef sees
  the incoming student only as an informational green row — they cannot complete/evaluate until the target
  cohort's schedule is (re)published. If actionable hand-off is wanted later, that's a separate change.
- Build: `PGSH.Infrastructure` → 0 errors; frontend `npm run build` passes; no new lint errors (the one
  EmployeeServicesPage lint error at the eval-modal effect is pre-existing). API DLL-copy lock = API running.

**#2 — Evaluations, 3 modes (all evals): ✅ DONE (2026-06-05).** numeric score · validate-whole-period
(no score) · validate-each-objective (pass/fail).
- **Domain**: new `EvaluationMode {Numeric,ValidatePeriod,ValidateObjectives}` + `EvaluationOutcome
  {Validated,NotValidated}` (`PGSH.Domain/Stages/EvaluationMode.cs`). `ServiceEvaluation` gained `Mode`,
  nullable `TotalScore`, `Outcome`, and a `Normalize()` that clears fields not used by the mode and
  **derives** the period `Outcome` for ValidateObjectives (validated iff all *mandatory* objectives pass,
  or all objectives when none mandatory). `ObjectiveScore.Score` is now `int?`; added `Outcome`.
  `InternshipAssignment.SubmitEvaluation` calls `Normalize()`; `RecomputeFinalScore` only counts
  **Numeric** evaluations (validate-only periods leave `FinalScore` null). `EvaluationSubmittedDomainEvent.TotalScore` → `decimal?`.
- **Application**: Create/Update commands + validators carry `Mode`/`Outcome` and per-objective `Score?`/`Outcome?`;
  validators are mode-conditional (`When(...)`). `ServiceEvaluationResponse`/`ObjectiveScoreResponse` +
  GetByPeriod projection expose mode/outcome. Update handler calls `Normalize()` before `RecalculateFinalScore()`.
- **API**: `service-evaluations/{id}` PUT `Request` carries `Mode`/`Outcome`.
- **EF + migration**: `Mode`/`Outcome` stored as `text` via `HasConversion<string>()`; `TotalScore`/`Score`
  made nullable. Migration `20260605205344_EvaluationModes` (Mode `defaultValue:"Numeric"` so legacy rows
  stay valid). **Not yet applied to the DB** — MigrationService applies on next Aspire start, or run
  `dotnet ef database update`.
- **Frontend**: chef `EvaluationModal` (`EmployeeServicesPage`) has a mode `SegmentedControl` (ValidateObjectives
  disabled when the stage has no objectives) + per-objective/period Validé·Non validé toggles; live derived
  result preview. Student `EvaluationDetail` (`StageDetailsPage`) shows a Validé/Non validé badge in place of
  the note when the eval isn't numeric, per-objective too. Types updated in both `employee.types.ts` +
  `student.types.ts`.
- Build: `PGSH.Infrastructure` → 0 errors; frontend `npm run build` passes; lint clean except the one
  pre-existing seeding-effect error in `EmployeeServicesPage`. (API DLLs locked = API running.)

**#2 refinements (2026-06-06):**
- **Chef "Terminer" removed.** Closing a `ServicePeriod` is an **administrative** act (done when the
  scheduled window is due), not the chef's. The chef view (`EmployeeServicesPage`) now shows only
  **Évaluer** (once closed) / **Modifier**; active rows show a dimmed "En attente de clôture" hint, no
  button. Removed the dead `completeServicePeriod` mutation from `employeeApi.ts` (admin keeps its own in
  `adminApi.ts`). This also resolved a chef-side 404 (`/service-periods/[object Object]/complete`) — the
  path is simply gone now.
- **Score auto-calc (10/0 mapping).** `InternshipAssignment.RecomputeFinalScore` now produces a number for
  EVERY mode: numeric objectives use their mark; a validate-only objective/period maps to **10 (validated)
  / 0 (not)**; weighted-average → `FinalScore`. `Result` auto-derives from a **≥10 threshold**
  (`Validé`/`NonValidé`), and the existing admin `Validate()`/`Reject()` still override it terminally
  (evals lock after, so recompute can't clobber). `FinalScore`/`Result` already surface in
  `InternshipAssignmentResponses`. No schema change. (Replaces last turn's "validate-only ⇒ null score".)

**Transfer UI bug fixes (2026-06-06, shipped):** in `GroupDetailPage.tsx` `TransferModal`, a single value
was used as BOTH the academic-year id (groups query) AND the current-group id (exclude filter) — so the
target dropdown showed the student's own group and dropped any group whose id == the year id. Now takes
`academicYearId` + `currentGroupId` separately. Also the reason field was labelled "optionnel" but the
backend requires it (`NotEmpty`) → now `required` + submit disabled until filled.

**Suivi follow-ups (2026-06-06):**
- ✅ **No-op guard DONE.** `AssignmentsPage` disables Démarrer/Clôturer (+ "aucune rotation dans la période
  choisie" hint) when none of the checked cohorts has a rotation in the selected period(s)
  (`selectionHasTargetPeriod`, from the cohort→periods grid map).
- ✅ **Stale "en cours" count (#2) DONE (2026-06-08).** `GetAssignmentStatusSummaryQueryHandler` now counts
  **per ServicePeriod (rotation)** instead of grouping by ASSIGNMENT `Status`. Each in-scope period is bucketed
  into the existing `InternshipStatus` labels: complete+eval → `Evaluated`, complete+no-eval → `Completed`
  (à-évaluer), `IsStarted` → `Ongoing`, else `Planned`; terminal assignment verdicts (`Validated`/`Rejected`)
  override the period state so admin actions still show. Query gained optional `PeriodNumbers` (scoped via
  `ServicePeriod.CohortSlotAssignment.StageSlot.PeriodNumber`); endpoint accepts `periodNumbers`; frontend
  `AssignmentsPage` passes `periodArg` to `useGetAssignmentStatusSummaryQuery` so the card tracks the selected
  period chips. This also fixes the **Clôturer** issue (closing ONE period now shows complete/à-évaluer per
  period, not stuck "en cours" — the card no longer reads the cumulative assignment `Status`). NOTE: the card
  now reflects only published periods; period-less (unpublished) assignments no longer appear in the counts.
  Build: `PGSH.Infrastructure` 0 errors; API only DLL-copy-locked (running); frontend `npm run build` + lint clean.
- ✅ **Bulk start/close perf + idempotency (#4) DONE (2026-06-08).** Took option (a). New stage-level
  `StartStagePeriodsCommand` / `CompleteStagePeriodsCommand` (`Stages/Cohorts/Bulk/StageLifecycleCommands.cs`)
  act on the WHOLE selection in ONE round-trip via shared DI service `StagePeriodRunner`
  (`Stages/Planning/`, mirrors `SchedulePublisher.PublishStageAsync`): load every in-scope assignment +
  periods once, mutate, single `SaveChanges`. Scoping precedence: `CohortIds` (the Suivi UI's arbitrary
  selection) → `PartitionLabels` (whole rotation, macro parity) → all stage cohorts; `PeriodNumbers` narrows
  the window. Idempotency: re-running on an already-started/closed selection loads once and finds nothing
  pending (`!IsStarted` / `!IsComplete` filter) → near no-op, one query + zero writes, not N. Endpoints
  `POST stages/{stageId}/schedule/start` + `/complete` (`StageLifecycleRequest{CohortIds,PartitionLabels,
  PeriodNumbers}`, returns `{started}`/`{completed}`). Frontend `AssignmentsPage` Démarrer/Clôturer now fire
  ONE `useStartStagePeriodsMutation`/`useCompleteStagePeriodsMutation` with `{stageId, cohortIds: selectedIds,
  periodNumbers: periodArg}` instead of the per-cohort `runForSelected` loop (loop kept only for Valider).
  Build: `PGSH.Infrastructure` 0 errors; API DLL-copy-locked only (running); frontend `npm run build` + lint clean.
- ✅ **Chef sees the STAGE per service (#5) DONE (2026-06-09).** `ServicePeriodResponse` gained `StageName` +
  `LevelLabel` (nullable). Projected from `…Cohort.Stage.Name` / `.Stage.Level.Label` in BOTH
  `GetMyServicePeriodsQueryHandler` (published rows from `InternshipAssignment.Cohort.Stage`, incoming-transfer
  rows from the chef-service slot's `sa.Cohort.Stage`) and the admin `GetServicePeriodsQueryHandler`. Frontend
  (`EmployeeServicesPage`, `employee.types.ts`): stage shown in three places — a navy stethoscope line under the
  service name on the card header (distinct stages joined with ` · `, since a service can host several stages
  across windows), a stage badge + level on each **window** header row, and a stage badge in the evaluation
  modal subheader. Build: `PGSH.Infrastructure` 0 errors; frontend `npm run build` passes; lint clean on the
  changed files (the one line-108 seeding-effect lint error in `EmployeeServicesPage` is pre-existing/untouched).
  NOTE: API has no new endpoint — restart the stack so the running API picks up the new Application DLL/fields.
- ⏳ **Planning skips a small allowed service (#3).** By design: `RotationArranger.BuildServiceQueue` weights a
  service `floor(capacity / avgCohortSize)`; a service smaller than one (atomic) cohort gets weight 0 and is
  EXCLUDED (a whole ~group would overflow it) — so e.g. Cardiologie@Harrouchi is dropped if its capacity <
  group size, even when others saturate. Options: raise that service's capacity ≥ group size; OR partial-group
  placement (Phase 7.5 #4, big); OR soften the weight rule. Needs user decision.

**Queued (agreed, not built):**
- ✅ **Temporary vs Definitive transfer DONE 2026-06-25** — see "Transfer types" section at the top.
  Auto-revert at stage end implemented; transfer unit resolved to GROUP.
- **Gray/green marker refinement (deferred from the transfer build):** scope the chef incoming/outgoing
  markers to the **current stage's chef** + show NET group headcounts (old −1, new +1). Markers still use the
  existing group-model logic in `GetMyServicePeriodsQueryHandler`, so a temporary loan's green row shows for
  the target group's service chef (expected under the group model).
- **Delocalization (#3 in the mobility model):** out-of-faculty stage; no eval control; student returns with
  a paper validation entered by admin/scolarité via the #2 validate-only eval + fiche attachment.
- **Revalidation (#4):** fail → revalidate in the **same service** failed, unless a transfer/déloc demande is
  attached (then another group's service). May span academic years. **Open design choice:** keep all
  revalidations of one stage together in a group vs ad-hoc per-student `ServicePeriod`
  (`CohortSlotAssignmentId == null`). See `project_student_mobility` + `domain_revalidation` in agent memory.
- Dedicated admin "edit final score/result" override endpoint (beyond Validate/Reject) — small follow-up.

**Optimization sweep (cross-cutting, agreed 2026-06-25 — ongoing).** Two standing quality rules now
documented in `PGSH.Frontend/CLAUDE.md` → "Performance & Pre-flight Validation"; apply them whenever touching
a page, and burn down the known offenders below:
- **Pre-flight guards** — disable any action the server is guaranteed to reject / that is a no-op, with an
  inline reason, instead of firing the request and showing an error toast. ✅ `ScheduleGridModal`
  "Répartition automatique" **DONE (2026-08-08)** — disabled with the specific reason (no periods / no
  allowed services / no cohorts in selection), mirroring `RotationArranger`'s three guards. Still open:
  audit every *other* page's primary mutation for the same.
- **Debounce search inputs** — every server-querying free-text field must use `useDebouncedValue` (300–350ms)
  + `skip` until ≥2 chars (pattern in `EmployeesPage`/`GroupsPage`/`InfrastructurePage`). Reported laggy:
  the **academic-group student search**. Sweep all search/filter inputs for missing debounce; memoize
  client-side filters of already-loaded lists.

**Admin "suivi" period-scoped bulk start/close ✅ DONE (2026-06-06).** Root issue found: the old
`CompletePeriodsCommand` closed EVERY incomplete period of a cohort — including FUTURE ones. Now
`StartCohortAssignmentsCommand`/`CompletePeriodsCommand` take an optional `IReadOnlyList<int>? PeriodNumbers`
(scoped via `ServicePeriod.CohortSlotAssignment.StageSlot.PeriodNumber`); null/empty = all periods (old
behaviour). Endpoints `cohorts/{id}/start-assignments` + `/complete-periods` accept optional body
`{ periodNumbers }` (`PeriodScopeOptions`). Frontend `AssignmentsPage` ("Suivi → Affectations"): a
**period chip row** (P1 · dd/MM→dd/MM …, from `getStageSchedule`) in the bulk bar scopes Démarrer/Clôturer
to the chosen periods; none selected = all. Cohort selection (all / by partition A/B) already existed.
adminApi `startCohortAssignments`/`completeCohortPeriods` now take `{ cohortId, periodNumbers? }`
(also updated the call in `StageDetailPage`).
**Confirmed model (user 2026-06-06):** year (global navbar context) → stage → its cohorts. **Macro** = select a
whole partition (sidebar "Par rotation" → "Sél." on A/B/…) and act on all its periods. **Micro** = same
partition selection + pick period chip(s) to scope the action to one period window. **Cross-stage selection
is intentionally NOT supported** — stages are handled individually. (Per-period START is still
whole-assignment in the domain — the window only filters which cohorts start; Clôturer is truly per-period.)
**Period chips refinement (2026-06-06):** the period bar is now its own always-visible toolbar (not buried in
the bulk bar), **defaults to ALL periods selected** (empty `selectedPeriods` ⇒ `effectivePeriods` = all), and
**filters the cohort list**: deselecting a period hides cohorts that don't run in any still-selected period
(cohort→periods read from the `getStageSchedule` grid `cells`). This matches macro planning where a partition
only occupies a window of periods (A=P1–2, B=P3–4). All periods selected ⇒ no cohort filter.

**Per-period START — `ServicePeriod.IsStarted` ✅ DONE (2026-06-06).** Root cause of "the chef sees ALL periods
en cours, even future ones": *started* lived on the assignment (`Status=Ongoing`), not the period — so the chef
worklist showed every published period once the assignment was Ongoing. Fix: new `ServicePeriod.IsStarted` flag.
`InternshipAssignment.StartPeriod(periodId)` activates one period (Planned→Ongoing on first); `Start()`
(single-row admin action) now activates ALL periods. `StartCohortAssignmentsCommandHandler` rewritten to start
PERIODS scoped by `PeriodNumbers` (unscoped = all). `GetMyServicePeriodsQueryHandler.LoadPublishedPeriodsAsync`
now filters `p.IsStarted` — future un-started periods stay hidden from the chef. Migration
`20260606124812_ServicePeriodIsStarted` (default false + backfill `IsStarted=true WHERE IsComplete=true` so
completed/evaluated history stays visible). No frontend change needed: the period chips already send scoped
`periodNumbers` to Démarrer, and the chef worklist filters server-side. So: admin starts P1 for partition A →
only those periods go active → only the P1-service chefs see those students; future periods appear to nobody
until started. NOTE: two migrations now pending DB apply — `EvaluationModes` + `ServicePeriodIsStarted`.

**#3 — Revalidation:** new `InternshipAssignment` for the failed stage on the **current** registration,
placed as an **ad-hoc `ServicePeriod`** (`CohortSlotAssignmentId == null`) into an allowed service chosen
by scolarité — not into a year-1 cohort. Write `History.Revalidation`. Graduation gate: stage satisfied
if ≥1 assignment `Result = Validé` in any year.

**#4 — Delocalization:** new out-of-region movement type; reuse `Hospital`/`Service` catalog (add the
external one if missing); on return record the outcome via the #2 validate-only eval + attach the
**fiche de validation** file. Rare cases.

**Demande = Phase 5 (later):** transfers/delocalizations carry `Reason` + nullable `DemandeId` now;
wire to the real demande in Phase 5.

> Reminder: Clean Arch + CQRS/Result; routes under `/api`, no leading slash; enums as real types;
> build backend via `PGSH.Infrastructure.csproj` (API DLL locked while running); commit only when asked.

---

# Previous work stream — Partition Macro Planning (historical)

Living handoff for the rotation-planning work. See `PHASES.md`, `NOTES.md`, `SCHEMA.md`.

_Last updated: 2026-06-03 (published-lock + student-status fixes done; cross-stage capacity done — Phase 7.5 #1)._

---

## What this work stream is

Make hospital-rotation **planning and affectation flexible** — operable for *all groups
or a single partition / period window* — and add a one-click **macro orchestrator**, to
match the faculty rotation sheets in `example_stage_assignement/` (Med3, Med6).

**Domain model (two levels):**
- **Macro** — `AcademicGroup.RotationGroup` (A, B, C…) splits a level's groups into
  partitions. Each partition runs a stage in a window of periods. Med3: Partition A does
  Médecine in periods 1–2 then Chirurgie 3–4; Partition B mirrored.
- **Micro** — within a partition's window, groups are distributed across services by a
  capacity-proportional cyclic rotation. A cohort = one group = atomic (~15–20 students).

---

## Done (Phase 7.1 — Complete)

All in `PGSH.Application/Stages/Planning/` (DI-registered shared services; handlers and the
orchestrator call them — no nested MediatR):

- `PartitionAllocator` (labelling), `RotationArranger` (scoped cyclic rotation),
  `StudentAffectationService`, `SchedulePublisher`, `CohortProvisioner`.
- **Scoping**: `AutoArrangeStageScheduleCommand` + `AssignAllStudentsByStageCommand` take
  optional `PartitionLabels` + `PeriodNumbers`; new `PublishStageScheduleCommand`. Auto-arrange
  removal scoped to `targetCohorts ∩ targetSlots` (arranging one window never wipes another).
- **`GenerateMacroPlanCommand`** (`POST /stages/macro-plan`): per `(RotationGroup, StageId,
  PeriodNumbers)` → ensure cohorts → affect → arrange → optionally publish. Lenient on missing slots.
- **Partitions are per (year, level)**: `AssignRotationGroupsCommand.LevelId`; `CohortProvisioner`
  matches groups to each stage's level (a label reused across levels never crosses).
- **Capacity-aware allocation**: weight = `floor(capacity / avgStudents)`; services smaller than
  one group are excluded, not force-overflowed. No artificial saturation when per-period capacity ≥ demand.
- **Frontend** (`PGSH.Frontend/src/features/admin/`): `ScheduleGridModal` per-partition/window
  auto-arrange + scoped publish, stacking-guard alert, saturation banner + full-report Drawer.
  `GroupsPage` Macro Plan tab (per-level partition setup, partition×stage matrix with per-cell
  period windows, "Générer le plan" with step toggles). Per-row "Vider le groupe" + "Vider toutes"
  (`EmptyAllYearGroupsCommand`, `DELETE /groups/all/students`). Debounce on the last two searches.

---

## Done since Phase 7.1 (published-lock + student-status fixes, 2026-06-03)

Three correctness/UX gaps in the planning grid + student view — "published is locked" was
enforced inconsistently, and unplanned stages were mislabelled:

- **`DeleteStageSlot` now blocks when a slot has published cells** (`StageErrors.SlotPublished`).
  Previously it cascaded-deleted cells and `SetNull`-orphaned published `ServicePeriod`s → silent drift.
- **Bulk `ClearSlotAssignments` now returns `{ cleared, skipped }`** instead of silently skipping
  published cells under a 200 OK. UI shows "X vidés, Y ignorés (publiés)".
- **Student `StageListPage`** distinguishes **"À venir / Non planifié"** (no `InternshipAssignment`)
  from **"Planifié"** (assignment exists, status `Planned`).

## Next (Phase 7.5)

Priority order:

1. ~~**Global cross-stage capacity (CORRECTNESS).**~~ **DONE 2026-06-03.** New
   `ServiceOccupancyCalculator` (`Application/Stages/Planning/`) measures a service's load as the
   students on it over any **overlapping** slot window, across all stages. Wired into grid display
   (`GetStageScheduleQueryHandler`), auto-arrange saturation (`RotationArranger`), and a new
   **pre-publish guard** (`SchedulePublisher.EnsureCapacityAsync` → `StageErrors.CapacityExceeded`).
   The publish guard was previously **absent** (the error was defined but unused). Per-cohort publish
   fails strictly; stage publish still skips already-published/unconfigured cohorts but now **hard-fails
   on a capacity overflow** rather than silently over-booking.
2. **Long-running ops robustness.** Mutations (démarrer, publish, macro plan) run to completion
   in the background; not aborted by navigation or other requests; no client timeout. Move heavy
   ops to a background job / chunking + progress; ensure handlers are idempotent.
3. **UX**: per-stage capacity-fit gauge; one-click "ajuster les capacités" from the saturation
   drawer; window-picker chips (replace free-text "1,2") + auto free-window suggestion; validate
   referenced periods exist per stage in the macro tab; save macro plan as a reusable template.
4. **Partial-group placement (model change)**: allow splitting a group across services (as the
   faculty sheet does, 9–11 + 12) to remove wasted seats. Largest effort.

## Phase 7.6: Stage Timeline / Calendar — Phase A DONE (2026-06-04)

Gantt/Teams-style **read-only** calendar, hierarchy **Année → Niveaux → Stages → Partitions**.
Key constraint honoured: `Stage` has **no dates** — every bar is derived from `StageSlot.StartDate/EndDate`
(stage span = min/max over its slots; partition span = min/max over the slots its cohorts occupy).
- Backend: `GetYearTimelineQuery` (`Application/Stages/Timeline/`) + `GET /academic-years/{id}/timeline?levelId=`
  (`Endpoints/Stages/YearTimeline.cs`). No schema change; reuses `ServiceOccupancyCalculator` for saturation.
- Frontend: `StageTimelinePage` at `/admin/timeline` (nav "Calendrier"), custom CSS Gantt (date→% via
  dayjs, no Gantt dep), `useGetYearTimelineQuery` (tag `Stage/TIMELINE`, `refetchOnMountOrArgChange` +
  manual refresh — fine-grained invalidation not wired across the ~15 plan mutations yet).
- Range picker shipped too: `@mantine/dates` + `dayjs`, CSS in `main.tsx`, `DatesProvider locale=fr`;
  slot start/end in `ScheduleGridModal` is now `DatePickerInput type="range"` (string "YYYY-MM-DD" values).

**Phase B (planned, not started):** drag/resize bars → write `StageSlot` dates, re-run capacity check,
confirm + undo. Full breakdown in root `PHASES.md` §7.6 and `PGSH.Frontend/PHASES.md` §9.

---

## Two clarifications resolved this session

- **Does a new request abort a long one?** No. RTK Query mutations and queries have independent
  abort controllers; SPA navigation doesn't cancel in-flight mutations. They run concurrently
  (separate scoped `DbContext`; Postgres MVCC reads don't block on writes).
- **Cross-stage service capacity?** Not counted together — see Phase 7.5 item #1.

---

## Build / verification state

- `dotnet build PGSH.Application/...` → **0 errors**. `PGSH.API` → **0 CS errors**.
- **Full `dotnet build PGSH.sln` fails only on DLL-copy locks** because the API (PID seen: 16892)
  and Visual Studio hold the assemblies. Stop them for a clean solution build.
- Frontend `npm run build` (tsc + Vite) passes; `npm run lint` clean in all changed files
  (the ~17 pre-existing lint errors are in untouched files: EmployeeServicesPage, StudentLayout,
  main.tsx, routes/index.tsx).
- **Live end-to-end test still pending** an API restart. Test recipe: a year with 1Med + 2Med,
  assign 2 partitions to one level and 4 to the other (independent); define slots per stage; run
  the macro plan with A→[1,2]/B→[3,4]; verify `GET /stages/{id}/schedule` cells, partition
  isolation, and that a sufficiently-sized stage reports 0 saturées.

---

## Key files

- Backend planning: `PGSH.Application/Stages/Planning/*`, `Stages/MacroPlan/*`,
  `Stages/Schedule/AutoArrange/*`, `Stages/Cohorts/{Assign,AssignByStage,PublishSchedule,BulkCreate}/*`,
  `AcademicGroups/{AssignRotationGroups,Empty}/*`.
- Endpoints: `PGSH.API/Endpoints/Stages/{Schedule,AssignAllStudents,GenerateMacroPlan}.cs`,
  `PGSH.API/Endpoints/AcademicGroups/{AssignRotationGroups,Empty,EmptyAll,DeleteAll}.cs`.
- Frontend: `features/admin/components/ScheduleGridModal.tsx`, `features/admin/pages/GroupsPage.tsx`,
  `features/admin/api/adminApi.ts`, `features/admin/types/admin.types.ts`.
- No schema/migration changes were made in this work stream.
