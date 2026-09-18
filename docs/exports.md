# Exports — a document that leaves the system

> Read before adding a sheet, a column, or a second export.
>
> Split out of `CLAUDE.md` on 2026-09-06 — the text is unchanged. See [`CLAUDE.md`](../CLAUDE.md) for the always-on rules and the map of these documents.

## An export is a document, and it leaves the system — `Exports/`
Two .xlsx downloads: the roll (`students/export`) and the post-validation stage record
(`stages/assignments/export`). `PGSH.Application/Exports/` holds the format-agnostic workbook model
(`ExportWorkbook` / `ExportSheet` / `ExportCell`), the French labels and the errors;
`ClosedXmlExportWorkbookWriter` in Infrastructure is the **only** code that knows what .xlsx is —
the same split as the three `ClosedXml*SheetParser`s, in the other direction.

- **One writer, or three faculties.** Three handlers each styling their own workbook is three
  documents that agree on nothing: the header band, the frozen pane, the auto-filter, the date and
  number formats are decided once. Add a sheet, not a styling pass.
- ⚠ **Cells are typed, and that is not cosmetic.** A date written as text cannot be sorted and a mark
  written as text cannot be averaged — which is the first thing anybody does to a post-validation
  file, and it fails silently. `ExportCell.Day/Numeric/Count/Text/Paragraph` carry the value in its
  own type. Identifiers stay `Text` on purpose: a CNE that looks like a number must not lose its
  leading zeros, and « 3-4 » must not become a date.
- **Every sheet carries a caption naming its scope** — promotion, année, row count. A file whose only
  statement of scope was its name cannot be audited three months later, and every export here is
  scoped by a year the caller was allowed to omit. Same reasoning behind `ExportFileName`.
- ⚠ **An export is the one read deliberately exempt from pagination**, so it is the one read that can
  pull the base into memory. Both are capped (`ExportErrors.TooManyRows`) and the refusal names the
  count *and* the axis that narrows it — « trop de lignes » alone sends the user back to the same
  button. Year-scoped, neither cap can be reached by the real data; they bite only on a caller that
  has found a way to widen past a year.
- ⚠ **Flat queries only.** The périodes of an attempt and the objective scores of an évaluation are
  collections; folded into a projection they are exactly the shape that killed the macro plan. Three
  top-level reads keyed on the parent id, joined in memory — pinned by `SqlTranslationTests`. The
  scope is defined **once** (`StageAssignmentExportQueries.Scoped`) and the other two reach it through
  `IN (subquery)`: two copies of a year filter is how a périodes sheet ends up describing a different
  population from the stages sheet beside it.
- ⚠ **The search is the list's, not the export's own** — `StudentSearch.WhereStudentMatches`
  (12/09/2026). A file downloaded from a filtered screen has to hold what that screen showed, and two
  spellings of « what the term means » are two chances to disagree: before the shared helper the
  export matched the whole term against five columns while the list used six, so « Mohamed Alami »
  came back as an empty file *and* an empty list, and an Apogée narrowed them differently. One call,
  one rule. Same reasoning as « nothing is recomputed » below, applied to the *scope* rather than to
  the figures.
- **Nothing is recomputed.** `StageScoring` gives the mark and the verdict, `ServicePeriodLifecycle`
  the state, `WorkingDayCalendar` the durations, `ServiceChefDirectory` the chef. An export that averaged differently from the fiche de
  validation would be a document contradicting the system it came from.

### ⚠ A column blank on every row is indistinguishable from a column the export forgot
`ExportNotes`, printed under the caption of every sheet. **This was reported within minutes of the
first real download**, against a file that was correct in every cell: the roll of 2026-2027 came out
with `Groupe`, `N° groupe`, `Partition`, `Source de la décision` and `Convention` empty on all 5 932
lines, and read as broken. Measured the same day, every one of those blanks was the truth —
**0 inscriptions carry a roster pointer**, `OutcomeSource` is null across a year nobody has
deliberated, and `AgreementType` is `None` for **all 10 206 students in the base**.

- **The note is computed from the rows actually exported**, not from a list somebody maintains, so a
  column added later is covered without anyone remembering to.
- **It says « aucune valeur dans cet export », never « données manquantes ».** An empty `Convention`
  means nobody is under one; the note's job is to say the export looked and found nothing, not to
  accuse the base.
- ⚠ **The roster columns get a second, specific note, because their emptiness has two causes that
  call for opposite acts** — no roster exists (cut the promotion) versus rosters holding nobody
  (assign the students). Same shape as `RepartitionSummary.DeclaredSlotCount` separating « no
  periods » from « periods nobody is in », and as `OutsideYearCount` saying what a year filter
  removed. The count of rosters in scope is queried **only when the answer will be printed**.
- **A note that fires whatever the data says is noise, and noise is dismissed** — which puts the real
  one back out of sight. A partly-filled column is not an empty one, and an export with no rows has
  no empty columns to name.

### The roll is an export of *registrations*, and the promotion is a column
Nom, prénom, CNE and Apogée belong to a person and never move; niveau, groupe, partition and statut
are facts about the year — and 2 635 students in this base have sat in more than one. Cut from
`Students` the row would have to pick a registration and could not say which, so **the row is the
registration**.

- ⚠ **An omitted year is the current one, never all of them.** A file labelled « liste des étudiants »
  holding six promotions of history is the évaluation-import defect with a different button on it.
- **`Programme` and `Niveau` are always columns, and `levelId` still cuts the per-promotion file.**
  The columns cost nothing and make a row self-describing when two exports are merged or one is
  opened a year later; a file whose scope lived only in its name cannot do that. Asking for one *or*
  the other was a false choice — the filter gives the per-promotion document with the columns intact.
- **The CNPN column follows the read order** `r.CnpnVersionId ?? r.Student.CnpnVersionId`, and
  « Origine CNPN » says which of the two answered. Blank is « jamais résolu », not « rien dû ».

### Several périodes is not several stays — `StagePeriodFolder`
The question the stage export had to answer: 01/01→01/02 then 02/02→02/03 is *one* rotation written
twice when the service never changed and the windows meet, and *two* when they do not. **The merge is
decided by the service, never by the dates.**

A **stay** is a maximal run of périodes in the *same service* with *no worked day between them*. One
stay prints as one span; several print joined by « · », with the services joined by « → » in the same
order, so the two cells correspond position by position.

| case | Découpage | Service(s) | Période(s) |
|---|---|---|---|
| one période | `Période unique` | `Cardiologie` | `01/01/2025 – 01/02/2025` |
| two, one service, meeting | `Service unique — 2 périodes contiguës` | `Cardiologie` | `01/01/2025 – 02/03/2025` |
| two, one service, a hole | `Service unique — 2 périodes, 1 interruption(s)` | `Cardiologie` | `01/01/2025 – 01/02/2025 · 17/02/2025 – 02/03/2025` |
| two services | `Rotation — 2 services, 2 périodes` | `Cardiologie → Pneumologie` | `01/01/2025 – 01/02/2025 · 02/02/2025 – 02/03/2025` |

- ⚠ **The multi-période fact is never carried by the string alone.** `Nb périodes` and `Nb services`
  are numeric columns, so « montre-moi les stages faits en deux services » is a filter rather than a
  reading exercise. Merging two windows into one span must not erase that it was recorded in two.
- ⚠ **A gap is measured in *worked* days.** A calendar-day test calls every Friday → Monday hand-over
  an interruption — which is exactly how one column follows another, since `WorkingDayCalendar` never
  lets a window swallow its trailing weekend. A declared holiday between two windows is not a hole
  either.
- ⚠ **Both break conditions matter.** Breaking only on the service change (the shape
  `SchedulePublisher.BuildStays` uses — correctly, since it works from contiguous grid columns) would
  swallow a real interruption inside one printed span. Breaking only on the gap would merge S1 → S2
  into one line and lose the second service.
- ⚠ **Durations are summed over the périodes, never measured end to end.** An interrupted stage's span
  contains days nobody served, and `Fin − Début` is the number that makes a 22-jour stage read as 60.
- **Most rows arrive already collapsed**: `SchedulePublisher` folds a `SingleService` run into one
  `ServicePeriod`, and 5ᵉ/6ᵉ année are `SingleService` in 51 923 of 51 924 imported placements. The
  folding exists for 3ᵉ and 4ᵉ année, which genuinely rotate, and for the Access history.
- `StagePeriodFolder` is **pure** — no store, no clock — like `PeriodAxis`, `RotationTiling` and
  `OccupancyTimeline`, and for the same reason: the cases are exact rather than approximately seeded.

### A folded run is one période and several créneaux, and the file has to say both
`CoveredSlotFolder` + `StageAssignmentExportQueries.SlotCoverageQuery`. Under `SingleService`
`SchedulePublisher` collapses the `kₛ` cells of a run into **one** `ServicePeriod` spanning them —
correctly, since the student stands in one service and is marked once — so the Périodes sheet showed
one row where the grid authored three columns, and the axis those columns belong to was nowhere in
the document. Reported 2026-08-31: « on ne voit qu'une période alors qu'on en a trois ».

- **Both facts are on the row.** « Découpage » still reads « Période unique » and « Nb périodes » is
  still 1 — the fold was never the defect, the silence about what it folded was. Beside them,
  **`Nb créneaux`** (a number, so « publiés sur trois créneaux » is a filter), **`Créneaux`**
  (« P1-P3 ») and, on the Périodes sheet, **`Détail des créneaux`** — one line per column with *its
  own* window and worked-day count, which is exactly what the folded période's span cannot state.
- ⚠ **Still one row per période, never one per créneau.** A run is marked once; repeated across its
  three columns the note is counted three times by the first pivot anybody builds. The unit that
  carries a verdict stays the unit of the sheet.
- ⚠ **Read through `ServicePeriodSlotCoverage`, never `ServicePeriod.CohortSlotAssignmentId`.** That
  FK names only the **first** cell of a run, so the trailing columns — the entire subject here — have
  nothing pointing at them. Same trap as `RotationCycleContext`'s, and the fixture seeds coverage for
  every cell or the case proves nothing.
- **A période with no coverage leaves the cell empty, not `0`.** An ad-hoc période — imported
  history, a délocalisation, a revalidation — came from no grid, and « 0 » there reads as a count that
  failed. « Origine » already says « Hors grille » for exactly those rows.
- **Only consecutive numbers merge** (`P1, P3-P4`), as `GroupNumberRanges` does and for the same
  reason: « P1-P3 » is a claim that P2 is in the run, and a run that skipped it never held that
  column. The name is the créneau's authored label, falling back to `P{n}`.
- Measured on the live base 2026-08-31: **5 831 grid-linked périodes covering 7 497 cells**, the
  1 666-cell difference being folded runs. 5MED Gynécologie Obstétrique is 833 périodes each covering
  **3** créneaux (P4 08/12→07/01, P5 08/01→07/02, P6 08/02→07/03); the other six 5MED stages are
  `PerPeriod` and cover one apiece.

### Who leads a service is one rule, and both documents ask it — `ServiceChefDirectory`
`Application/Hospitals/Chefs/`. The resolution order was a private method inside
`GetLevelRepartitionQueryHandler` until the stage export needed the same answer; two documents of one
faculty disagreeing about who leads a service is the drift `StageScoring` and
`ServicePeriodLifecycle` exist to prevent. Pure directory + `ServiceChefProvider` that builds it —
the same split as `WorkingDayCalendar` / `WorkingDayProvider`.

- **Order is authority order**: the tenure open on the date → the sitting chef → the legacy note
  (`ServiceChefSourceNote`). Unchanged; only its address is.
- ⚠ **How much of that order is in force is `ServiceChefSourcePolicy`, and it is `SourceNoteOnly`
  today** (2026-09-03) — decided **once**, in `ServiceChefPolicy.InForce`, which both the
  export and the répartition pass. Not per document: the reason the order was extracted in the first
  place is that two pages of one faculty must not name different people for one service, and letting
  each narrow its own sources rebuilds exactly that drift. Restoring the dated record is one line.
  - **Why:** the base holds **2** chef affectations and both are test links. An affectation is the
    *better rule* and today the *wrong answer*; the note is the faculty's own last record for 140 of
    the 148 services.
  - ⚠ **What it costs is stated, not silent.** A service named only by an affectation now prints
    **no chef at all** — deliberate, since a blank says less wrongly than the wrong name — and
    `ExportNotes.ChefSourceNote` puts that sentence under the caption of the two sheets that print a
    chef. Silent under `Authority`: a note that fires whatever the policy says is noise.
  - **Every name printed is therefore `FromSourceNote`**, so « Origine du chef » reads « Note
    (import) » throughout and the répartition's tooltip fires on every row. Narrowing the sources is
    not a licence to stop saying the name is undated — the uniform column is the honest one.
  - **The authority order stays covered in `ServiceChefDirectoryTests`** (pure, policy-parameterised)
    while the two handler suites assert the narrowing. That division is what makes flipping the
    constant a one-line change rather than a rediscovery — checked by flipping it: 5 handler tests
    fail, 0 directory tests do.
  - ⚠ **The trail is loaded under *every* policy, including the one that will not print it.** The
    policy narrows what a document may **name**, never what the directory **knows**:
    `HasWithheldLinkedChef` has to be able to say « quelqu'un est rattaché, et ce n'est pas ce
    nom-là », and skipping the read answers `false` on exactly the two services that need `true` —
    an optimisation that erases its own subject. It was written that way for one commit.
- ⚠ **The as-of date is per question, not per build.** The répartition asks once, at the start of the
  axis. The export asks **per période**, because a file covering a year of rotations spans months and
  a chef who took over in January did not lead the students who stood there in October. That is why
  the whole tenure trail is loaded rather than filtered in SQL — a predicate cannot be pushed down
  for a date that is not known yet — and the trail is bounded by the services in scope.
- ⚠ **A délocalisation prints « hors faculté » in the chef column, not a blank.** The service is
  outside the faculty and nobody here supervised the rotation, so there is no name to resolve — and a
  blank reads as one the export *failed* to resolve, which sends somebody looking for it. « Origine du
  chef » is left empty on those rows for the same reason: there is no source to qualify. The
  « Délocalisé » column of the Périodes sheet says the rest. See
  [`delocalization.md`](delocalization.md).
- ⚠ **`Origine du chef` travels beside the name, always.** 140 of the 148 imported services name
  their professor only in a free-text note the Access base last recorded, and that note is
  **undated**: printed unqualified it claims this student served under that chef, which nothing in
  the base supports. « Affectation » / « Note (import) » / « Mixte » — same reasoning as
  `OutcomeSource` and `CnpnSource`.
- **Linking the professor in Personnel is what upgrades the rows**, and the export needs no change
  for it: a tenure or a sitting chef simply outranks the note. Measured 2026-08-31: 2 of 148 services
  now carry one, against 140 carrying only the note. ⚠ **Held back by the policy above until those
  two rows are real chefs rather than test links** — that is the one line to flip, and both documents
  and their notes follow it without further change.
- ⚠ **Two flat queries, not one projection carrying the tenures.** A tenure projects to a computed
  element with no key, and a collection of those inside a `Select` is the shape Npgsql refuses —
  the family that killed the macro plan. `SqlTranslationTests` pins both.
- ⚠ **The service page asks this directory too, and it did not — which cost a real « d'où sort ce
  nom ? » on 2026-09-03.** `ServiceDetailPage` ranked the same three sources itself: `serviceChef`
  (the **sitting FK, null on all 148 services**), then the note, with the open tenure filed under a
  divider labelled « Historique ». So Pédiatrie1 headlined « Pr.N.Elhafidi » and exported as
  « Youssef Alaoui » — the base's only two chef affectations, both open since 29/08/2026 and both
  test links — with nothing on either screen naming the other. **One rule, two sides of a network
  boundary**, exactly like `ServicePeriodResponse.State`.
  - `ServiceDetailResponse.ChefAttribution` (name + `FromSourceNote` + `LinkedChefWithheld`) is the
    resolved answer, **as of today** — this screen asks « qui dirige ce service ? », not « qui le
    dirigeait quand ce document a été publié ? », which is why the as-of date is a parameter.
  - `ChefFromSourceNote` **stays** beside it and is a different question: the raw note is what the
    *fiche says*, the attribution is who PGSH *names*. The page prints the second.
  - ⚠ **`LinkedChefWithheld` is the sentence the page owed and did not have.** Without it the
    narrowed policy just moves the confusion: a tenure marked « en cours » sits under a headline
    naming somebody else, plus « Désignez un chef de service » — advice already satisfied. It is
    **false when nobody is linked**, because « personne » and « quelqu'un que rien n'imprime » call
    for opposite acts. Same rule as `ExportNotes` and `OutsideYearCount`.
  - ⚠ **The `ServiceChefId` half must stay visible even when it is not the printed name.** It is the
    *rattachement* — configuration an admin edits — and `ChefHistory` lists **tenures only**, so
    « un chef est rattaché (voir ci-dessous) » pointed at a section that could not contain him. The
    page prints the link on its own line whatever the attribution says. Latent today (0 of 148
    services carry the FK) and the same class of « the page points at nothing » as the original.
  - ⚠ **An absent `ChefAttribution` means *unknown*, never « the note ».** Deriving it client-side
    from `ChefFromSourceNote` — for an API process predating the field — is a second resolution
    order on the client, i.e. the defect. The page says it is unresolved instead.
  - **The other two screens are closed the same way** (2026-09-05): the services **list** carries
    `ServiceSummaryResponse.ChefAttribution`, and the **student** portal's service page reads the
    attribution the `/services/{id}` response already carried — it was a client-side omission, no
    server change. Both used to print the sitting FK, i.e. « — » and « aucun chef de service
    désigné » on all 148 services, while the student's own répartition named one for 140 of them.
    - **The list resolves over the *page's* ids**, never over the filtered query: a directory built
      for every matching service grows with the catalogue for rows nobody is looking at. Same rule
      as the planning grid reading its published cells from the ids it just returned.
    - ⚠ **The row keeps `ServiceChefName` beside the attribution**, and the two are different
      questions: the FK is the *rattachement* — configuration, and what `?serviceChefId=` filters
      on — while the attribution is who PGSH **names**. Collapsing them is what the fix undoes.
    - ⚠ **`ServiceChefAttributionResponse` moved to `Application/Hospitals/Chefs/`**, out of the
      fiche's folder, with a `From(directory, serviceId, asOf)` factory. Three screens print it, and
      a response type owned by whichever query needed it first is how the second one ends up with a
      copy — the rule `UserResponse` and `LevelResponse` already state. The factory also keeps the
      *two* calls (`For` and `HasWithheldLinkedChef`) on one as-of date: split across callers, one
      of them eventually forgets the second, and a screen that cannot say « quelqu'un est rattaché,
      et ce n'est pas ce nom-là » is the screen that produced the original confusion.
    - **The student's card never dresses a note up as a personnel record.** A name from the import
      note is printed with « D'après la fiche du service » and no grade, no PPR, no invented « Dr. »
      — those exist only where an `Employee` is actually behind the name. And `linkedChefWithheld`
      with no name reads « Non communiqué », not « aucun chef désigné »: the second is false.

### Three sheets, because two questions are asked at once
« Stages » is one row per **attempt** — the unit that carries a note and a verdict, and therefore the
unit a PV is drawn from. « Périodes » is one row per **période** (with the créneaux it covers stated
on that row — see above). « Synthèse » counts the verdicts per
stage. Folding the detail into the first sheet would either lose it or make every row a paragraph;
leaving the first sheet out would hand a reader several lines per student and nowhere to read a
verdict. **`Réf. stage` is on both sheets and is the join** — a detail nobody can key back to the row
it belongs to is a detail nobody reads.

- ⚠ **Scoped by the *registration's* level, not the stage's.** The file is « la promotion et ce qu'elle
  a fait cette année », so a 6ᵉ année student revalidating a 3ᵉ année stage belongs on the 6ᵉ année's
  document — his own. Both levels are printed, which is what makes the row readable as a rattrapage
  rather than as one filed in the wrong place. (`GetStudentLevelDossierQuery` scopes the other way, on
  purpose: it answers « what does this student owe *at this level* ».)
- ⚠ **The year is read, never inferred from dates** — `a.Registration.AcademicYearId`. The two rules
  disagree on 7 030 of 105 626 périodes and the registration is right every time.
- **The unmarked rows are in the document by default.** A file whose whole purpose is « où en est la
  promotion » must show the holes, or a missing évaluation reads as a student nobody planned.
  `onlyEvaluated=true` is the caller saying the file is a PV rather than a state of play — and it is
  the switch a **pre-validation** export will reuse, not a second pipeline.
- **« Moyenne du stage » is legitimate** — the mean of the students' notes *within one stage* is a
  class average. It is the mean *across* stages that this project does not have and must not invent.
  « Taux de validation » is measured over the whole population, not over the evaluated part: a stage
  with one mark entered is not 100 % validated.

## Un export qui revient : le canevas des affectations (13/09/2026)

`GetAffectationSheetTemplateQuery` passe par le même `ExportWorkbook` / `IExportWorkbookWriter` que les
autres documents, et pour la raison habituelle : le fichier que quelqu'un télécharge pour **lire** et
celui qu'il téléverse pour **écrire** doivent être le même document, sinon le second est un formulaire
que personne n'a jamais vu.

⚠ **Il sort pré-rempli, jamais vide** — une ligne par (étudiant, stage du niveau), les périodes déjà
servies remplies. Un canevas vierge voudrait dire retaper les identifiants à la main, et un code mal
tapé est une ligne qui n'appartient à personne — ou pire, à quelqu'un d'autre. C'est aussi ce qui rend
l'aller-retour lisible : ce qui revient est un **écart** par rapport à ce qui est parti.

⚠ **Ses `Notes` portent une charge qu'aucune colonne ne peut porter** : ce qu'une ligne blanche veut
dire (« pas encore planifié », sautée) et ce qu'une ligne à moitié remplie veut dire (un refus). C'est
exactement l'usage pour lequel `ExportSheet.Notes` existe — une colonne vide sur toutes les lignes est
indiscernable d'une colonne que l'export a oubliée.

Voir [`affectation-sheet.md`](affectation-sheet.md).

---

## Une colonne remplie sur *une partie* des lignes (13/09/2026)

Signalé ainsi : « le numéro et le libellé du groupe n'apparaissent pas dans l'export des étudiants,
alors que dans l'export des affectations si — c'est donc à moitié cassé ».

**Les deux exports avaient raison, et ils ne lisent pas la même chose.**

| Export | Chemin vers le groupe | Peut-il être vide ? |
|---|---|---|
| Affectations | `InternshipAssignment.Cohort.AcademicGroup` | **Non** : une affectation n'existe que si l'étudiant a été placé dans une cohorte, et une cohorte appartient à un groupe |
| Rôle des étudiants | `Registration.AcademicGroupId` | **Oui** : c'est l'appartenance au groupe, et seules les promotions déjà découpées en portent une |

⚠ **C'est le même piège que `docs/planning-rosters.md` nomme** : « une affectation ne pend pas au
pointeur de groupe ». Deux notions de « dans quel groupe est cet étudiant », et elles ne se remplissent
pas au même moment.

**Le vrai défaut était la note, pas la colonne.** `ExportNotes.EmptyColumns` ne signalait une colonne
que si elle était vide sur **toutes** les lignes — la bonne question tant qu'aucune promotion n'était
découpée. Depuis que la 3ᵉ MED l'est, le rôle de 2026-2027 sort avec **933 lignes sur 6 839** portant un
groupe : la colonne n'est plus vide, plus aucune note ne se déclenche, et le lecteur reçoit une colonne
blanche à 86 % sans un mot. C'est exactement la lecture que la classe existe pour empêcher.

`ExportNotes.ColumnFill` répond maintenant sur « incomplète » et pas seulement « vide », et la note
sépare **quatre** états :

| État | Ce que le document dit |
|---|---|
| aucun groupe n'existe | « la promotion n'a pas été découpée » |
| des groupes, personne dedans | « le découpage est fait, la répartition non » |
| **certaines lignes seulement** | « 933 ligne(s) sur 6 839 portent un groupe… » |
| toutes les lignes | **rien** — une note qui se déclenche toujours est du bruit |

⚠ **Et délibérément pas de note générique « colonne partielle ».** La moitié des colonnes d'un rôle
sont légitimement partielles — un CIN, un CNE, une date de naissance — et une note sur chacune est du
bruit, qui est ignoré, ce qui remet la vraie hors de vue. La question n'est posée que pour la colonne
dont le blanc appelle **deux actes opposés**.

→ `ExportTests.A_column_filled_on_some_rows_says_how_many`
