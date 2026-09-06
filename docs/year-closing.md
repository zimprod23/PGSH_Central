# Closing and opening a year — déliberation, réinscription, inscription, holds

> Read before touching `Registration.Status`, any bulk canvas or roll, or `RegistrationHold`.
>
> Split out of `CLAUDE.md` on 2026-09-06 — the text is unchanged. See [`CLAUDE.md`](../CLAUDE.md) for the always-on rules and the map of these documents.

## PGSH cannot know who passed the year — the faculty declares it
There is no exam, no TP, no note de module and no jury in this system; it covers stages. So
`Registration.Status` is not computed, it is **stated**: a canvas per promotion (`(year, level)`),
filled from the PV de déliberation and uploaded — `Application/Students/Registrations/Deliberation/`.
`RecordYearOutcome` is the only writer, and it records **how** the verdict was learned.

| Décision | `RegistrationStatus` | next year |
|---|---|---|
| Admis | `Validated` | niveau + 1 |
| Redoublant | `Failed` | même niveau |
| Exclu | `Excluded` | — |
| Diplômé | `Graduated` | — |
| Abandon | `Withdrawn` | — |

- ⚠ **`OutcomeSource` (`Declared` | `Inferred`) is load-bearing, not bookkeeping.** An inferred verdict
  may never overwrite a declared one — a guess that can silently replace a fact makes the whole column
  unreadable. It is also what lets Phase 14.3c back-fill the six imported years safely.
  `null` means nobody has pronounced yet, which is every legacy year.
- ⚠ **`Excluded` is not `Failed` and `Graduated` is not `Validated`.** One ends the cursus, the other
  repeats or advances. Collapsing either pair breaks the réinscription, which is the only consumer that
  has to tell them apart.
- **A contradiction against our own stage record is reported, never enforced.** An *Admis* with an
  unvalidated stage is flagged and the import proceeds: the jury rules on the whole year, we see only
  stages, and with 0 authored periods an unmarked stage is the norm. Same choice as `EntryPredatesText`.
- **Déliberation is all-or-nothing; réinscription is idempotent.** Not an inconsistency — the
  deliberation file is *not stored*, so a half-closed promotion cannot be reconstructed, while a
  rollover can simply be re-run once the odd verdicts are fixed. Keep both properties.
- **They are two acts, months apart** (July / September), and not every admis comes back. Never fuse
  them.

### The canvas is a list of exceptions, and silence is the verdict
A PV names the students the year went badly for; it does not restate 641 admissions. So
`DeliberationScope.DefaultUnlistedToAdmis` inverts what the file is — everyone not named is **Admis** —
and the scope is the **academic year**, with `LevelId` narrowing it to one promotion rather than
defining it.

- ⚠ **Year-wide is safe here, and it is not the widening-on-absence defect.** The *year* is still
  resolved to exactly one; a student holds at most one registration per year (unique index), so a CNE
  is as unambiguous across every level of one year as within one promotion. Matching across **years**
  is what makes a row ambiguous, and that remains impossible.
- ⚠ **The default promotes; it never graduates** — and "is this his last year?" is asked per
  *student*, from his own `CnpnVersion.TotalYears`, never per level. From 2026-2027 one 6ᵉ année
  Médecine holds both students whose text ends there (1650.25) and students who go on to a 7ᵉ
  (2174.18), so the level alone cannot answer it. Anyone who **may** be in his last year is counted
  (`FinalYearUndecided`) and left untouched.
  - **Why, measured 2026-08-18 on the real base:** *855 of the 1 657* students in 7ᵉ année Médecine had
    been in the 7ᵉ année before — 132 of them four times — and 74 of 356 in 6ᵉ Pharmacie. The final
    year is the **thesis year**: students sit in it until they defend, PGSH holds no record of a
    defence, and so "still there" and "finished" are *both* ordinary. Reading silence as diplômé
    graduated **~930 people who were simply still enrolled**. An exceptions file only works where the
    exception is rare; in a final year that is reversed, so the rule inverts there.
  - The faculty names its graduates instead — the **defence roll** is the document it actually holds,
    and a row reading `Diplômé` still records one.
  - ⚠ **Naming a « Diplômé » is stricter than being left undecided, and the two tests differ on
    purpose.** A row is refused (`NotAFinalYear`) unless `level.Year == TotalYears`, while
    `MayBeAFinalYear` stands aside on `>=`. So a registration sitting *above* its text's span can
    neither be promoted by silence nor graduated by name — measured 2026-08-29, the base holds **6**
    of them (5 in 7ᵉ année Médecine stamped `PHARM-LEGACY`, 1 in « Interne CHU »). Any generated PV
    must emit « Diplômé » on `==` only: one such row refuses the whole file.
  - ⚠ This is also why an absent CNPN stamp needs no special case any more. It used to *block* the
    file; now nobody in a possible final year is decided for either way, so the unstamped student
    falls into the same bucket and `DefaultIssues` is gone entirely.
- ⚠ **The default never overwrites a verdict already recorded** — not even an inferred one. Otherwise
  re-uploading last week's exceptions file, after twelve verdicts were corrected by hand, silently
  flips all twelve back to admis. It is also what makes the import safely re-runnable, the way the
  réinscription is. Changing a recorded verdict is explicit: name the student, or use the single-row
  path below.
- ⚠ **`Level.IsPromotion` is checked here too.** « Retrait » has no year to clear, and a year-wide
  default would otherwise promote the withdrawn.
- ⚠ **A boolean confirmation would not do.** The whole risk is the student nobody named, and a
  registration created *between the preview and the apply* adds one. `ApplyDeliberationCommand.
  ConfirmedDefaultCount` carries the number the operator was shown and refuses on a mismatch
  (`DefaultsNotConfirmed`). All-or-nothing still holds: a mistyped CNE means the student it was meant
  for is admitted by silence, so one bad row refuses the file.
- **Reports are bounded.** A year-wide file is whatever was uploaded and the reply is a single object —
  the exact shape that hides an unbounded collection. `Rows` is capped (`MaxReportedRows`) with
  `RowsTruncated`; the counts stay exact, and `ByLevel` is one entry per level. The réinscription orders
  its rows **attention-first** for the same reason: the cap must never hide a row somebody has to act on.

### One student at a time — because a promotion's file cannot be the only way in
`Registrations/Outcome/` — `RecordRegistrationOutcomeCommand`, `ReopenRegistrationYearCommand`
(`POST registrations/{id}/outcome[/reopen]`). A late jury, a PV corrected for one name, an abandon
notified in November. Under an exceptions file this path is *required*: re-uploading the promotion's
file is precisely what must not be needed to fix one row.

- ⚠ **`UpdateRegistrationCommand` used to write `Status` directly**, leaving `OutcomeSource` null — so
  the edit form showed « Admis » while the réinscription reported « aucune décision enregistrée » and
  refused to carry the student over. Neither was wrong about what it read. `Student.UpdateRegistration`
  now routes a year-outcome status through `RecordYearOutcome` and a return to `Active`/`Pending`
  through `ReopenYear`.
- ⚠ **Reopening does not undo what the verdict caused.** The réinscription may already have created
  next year's registration, and that row can carry a group, cohorts and published périodes. It is
  **reported** (`LaterRegistrationExists`), never deleted — deleting it would take a student's
  rotations with it.
- The single-row path stands aside on an absent CNPN stamp exactly as the canvas does: one student at a
  time must not be stricter than five hundred at once.

### Joining a roster is not transferring between two
`AcademicGroups/Join/` — `AssignStudentToGroupCommand` (`POST groups/assign-student`). The ordinary
September case: the déliberation is applied, the groups are cut, the schedule is published, and then
somebody registers.

- ⚠ **The transfer path silently did nothing for him.** Every step of `TransferStudentCommand` filters
  on assignments the newcomer does not have, so he landed on the roster with no cohorte and no période
   — a student the planning had never heard of, in a group that looked correct. Refused now
  (`AlreadyInAGroup` guards the mirror case), because the two acts differ in what they must carry.
- **Only windows that have not closed are materialised** (`LateArrivalScheduler`). A stage the roster
  finished in October gives him an `InternshipAssignment` — he owes it, and it shows unserved on his
  dossier — but no `ServicePeriod` claims he stood in a service on days he was not enrolled, and the
  count is reported (`StagesAlreadyOver`) so somebody decides between a délocalisation, a revalidation
  and next year. This is the opposite choice from `MaterializeAtTargetAsync`, which *does* materialise
  closed cells — rightly, since a transferred student really did serve them, with another group.
- A period is never back-dated before the day he joined, and a cell the roster has already **started**
  (read from its périodes, not from the calendar) gives him a started one, so he appears on the chef's
  screen the same day.

### Il y a trois actes sur un roster, et le troisième est une *correction*
`AcademicGroups/GroupChange/` — `ChangeStudentGroupCommand` (`POST groups/change-student-group`) et
`SwapStudentGroupsCommand` (`POST groups/swap-students`). Ils partagent `StudentGroupRelocator`,
parce qu'un échange **est** deux changements.

| acte | ce qu'il affirme |
|---|---|
| `AssignStudentToGroupCommand` | il n'est dans **aucun** roster et en rejoint un |
| `TransferStudentCommand` | il se déplace, et le déplacement est un fait : la rotation en cours est coupée, la suivante réhébergée, et le dossier dit quand et pourquoi |
| `ChangeStudentGroupCommand` | il a été **enregistré dans le mauvais roster** — il a toujours été dans celui-ci |

- ⚠ **« Sans historique » nomme le dossier, jamais le registre.** Le *dossier* est le récit de
  l'étudiant et ne doit rien montrer ; le *journal des actions* est la trace des actes
  d'administration et doit tout montrer. L'acte ne peut pas être défait — le groupe d'origine n'est
  écrit nulle part après coup — donc la commande est `IAuditableCommand` et son entrée est **le seul
  endroit où ce groupe survit**. Un registre qu'un acte destructeur peut contourner n'est pas un
  registre. Épinglé par `The_dossier_keeps_nothing_and_the_register_keeps_everything`, qui assied les
  deux moitiés ensemble : « aucune trace » serait sinon indiscernable de « acte non enregistré ».
- **Le silence est une propriété du domaine, pas du handler.** `Registration.ReassignToGroup` ne lève
  **aucun** événement, là où `TransferToGroup` lève `StudentGroupTransferredDomainEvent` dont le
  handler écrit la ligne `HistoryType.GroupTransfer`. ⚠ Le test de silence a besoin de son contrôle :
  « aucun événement » ne vaut rien si la fixture ne peut pas en produire un.
- **`InternshipAssignment.ReassignToCohort` réécrit la membership ouverte sur place** — pas de date
  de fin, pas de seconde ligne. ⚠ **Mais les lignes déjà closes restent** : elles enregistrent un
  transfert qui a réellement eu lieu et cet acte n'a pas à l'effacer. `OriginalCohortId` est remis à
  null, sans quoi l'auto-retour d'un prêt renverrait l'étudiant vers un groupe dont le dossier ne dit
  plus qu'il en vient.
- ⚠ **La garde de l'agrégat est `Status`, et rien d'autre — délibérément.** Une correction n'est
  vraie que tant que l'affectation est encore une prévision. Les faits plus profonds (période
  démarrée, note, présence) pendent de collections qu'un chargement sans `Include` rapporte
  **vides** : un agrégat qui répondrait « rien enregistré » à un appelant distrait laisserait passer
  exactement le cas qu'il existe pour arrêter. Le store est interrogé par le handler
  (`AffectationTollReader.ForRegistrationInRosterAsync`) et l'agrégat décide — la division que
  `CnpnSpanFloor` fait déjà.
- ⚠ **Aucun `Force`, pour la raison qui rend `RosterAffectationsUnderway` non forçable** : l'acte qui
  détruit notes et présences est « Dépublier », qui annonce son coût et demande deux fois. Le refus
  nomme les quatre chiffres — lus par le **même** `AffectationToll` que la dépublication, pour que
  deux actes ne décrivent pas les mêmes lignes différemment — et désigne le transfert.
- ⚠ **Aucun `Reason` non plus.** Tout autre acte sur un roster en prend un parce qu'il est écrit et
  relu ; ici il n'aurait nulle part où aller — l'acte *est* l'absence de trace sur la fiche.
- **Les périodes sont reconstruites depuis les cellules *publiées* de la cohorte d'arrivée**
  (`CohortMemberScheduler`), jamais depuis ses cellules tout court. Une cellule est un plan ; une
  période est l'étudiant qui s'y tient. Matérialiser toutes les cellules lui donnerait une rotation
  que ses camarades n'ont pas, sur une cohorte que personne n'a publiée — visible du chef de service,
  comptée dans l'effectif, et impossible à distinguer d'une vraie publication.
- ⚠ **Les périodes hors grille voyagent intactes** : une délocalisation, une revalidation, un
  historique importé ne viennent d'aucune répartition et aucune ne peut les reproduire.
  `AdHocPeriodsKept` les compte, pour la raison que `UnpublishCohortScheduleCommand` les compte.
- ⚠ **Les affectations manquantes sont créées ici et non par
  `StudentAffectationService.AssignRegistrationAsync`.** Cette méthode demande au *store* dans
  quelles cohortes l'étudiant est déjà ; les affectations qui viennent d'être repointées **ne sont
  pas sauvegardées**, donc le store les montre encore dans le roster de départ et elle en créerait
  une seconde par stage — la duplication qu'un re-découpage produisait.
- **L'échange lit les deux destinations avant de bouger qui que ce soit.** Prise au moment où on en a
  besoin, la seconde enverrait B dans le groupe où A vient d'arriver : les deux dans un roster et
  l'autre vide. Un seul `SaveChanges` pour les deux moitiés, donc un refus sur la seconde laisse la
  première exactement où elle était.

#### `CohortStayFolder` — plier les cellules d'une cohorte en séjours
`Domain/Stages/`, pur, comme `StageScoring` et `ServicePeriodLifecycle`. La règle était **privée dans
`SchedulePublisher`** jusqu'à ce qu'un second acte ait à produire les périodes que tient un membre
d'une cohorte.

- ⚠ **Écrite deux fois, elle aurait divergé sur `SingleService`** : l'étudiant déplacé aurait tenu
  *kₛ* périodes là où ses camarades en tiennent **une**, on lui aurait demandé *kₛ* notes, et sa
  moyenne se serait calculée autrement que celle de toute sa promotion.
- Un run rompt sur un **trou dans les numéros de colonne** *et* sur un **changement de service** : une
  cellule modifiée à la main vers un autre service fait deux séjours, pas une période dont le service
  est faux sur la moitié de sa durée.

⚠ **Un défaut latent corrigé avec la phase : `MidStageTransferRescheduler` n'écrivait aucune ligne de
couverture** sur ses trois créations de période. La FK répond « ça vient de la grille ? » ; seule
`ServicePeriodSlotCoverage` répond « *cette cellule* est-elle publiée ? », et c'est elle que lit
`PublishedCells`. Sans elle la cellule d'un étudiant transféré se lit **libre** : le prochain
auto-arrangement la réécrit sur un autre service pendant que sa période nomme toujours l'ancien.

### The fourth shape — the faculty's own roll, which is acts 1 and 2 at once
`Students/Registrations/ReinscriptionSheet/` — `POST reinscription/sheet[/preview]`. One spreadsheet,
one line per student: `Code · NOM · PRENOM · Etape 25-26 · Etape 2026/2027`. Those two étapes carry
the verdict with them, so a single upload records the decision on the year closing **and** creates the
registration for the year opening.

**It exists because it is what actually arrives.** `Reinscription/` *derives* next year from verdicts
already recorded (admis → niveau + 1); this one is handed the answer. Deriving a second time would
disagree with the file on 804 of its 6 862 lines.

- ⚠ **`Code` is the numéro Apogée**, not the CNE — the file has no CNE column at all, which is one
  more reason `Student.CNE` may be absent. Measured against the live base 2026-09-01: **6 813 of the
  6 862 codes match a student exactly, none is duplicated, and all 6 810 rows whose student holds a
  2025-2026 registration agree with it about the level.** The strictness below costs nothing.
- ⚠ **Silence is not a verdict here, and that inverts the déliberation.** That canvas is a list of
  *exceptions*, so a student it does not name is admis. This is the roll of who **is** coming back, so
  a student it does not name is not — a graduate, an exclusion, an abandon — and PGSH cannot tell
  those apart. Nothing is written for them; `NotCovered` reports the number (1 216 for 2026-2027, of
  which 999 are 7ᵉ année Médecine and 211 are 6ᵉ année Pharmacie, i.e. the thesis years).
- ⚠ **A level that has not moved is not always a redoublement.** In a final year it is the thesis
  still being written, which is as ordinary as finishing. Recording `Failed` there would be wrong
  twice: it is not a failure, and `RegistrationStatus.AnnulsItsStages` would **wipe the year's stage
  record** for 804 students. `DeriveOutcome` writes no verdict on a final-year repeat, on a
  réorientation (comparing a 3ᵉ année Médecine with a 1ʳᵉ année Pharmacie compares nothing), or where
  the closing year holds no registration. `WillRecordOutcome` is therefore deliberately smaller than
  `WillRegister`, and the UI says why — an operator expecting one verdict per registration would read
  the gap as rows silently dropped.
- **`OutcomeSource` is `Declared`.** This is the faculty's own document stating where each student is
  registered next year, not PGSH reading an enrolment sequence. Getting it backwards makes the whole
  column unreadable — `Inferred` may never overwrite `Declared`.
- **All-or-nothing on the errors, idempotent on everything else.** The line is whether a row is
  *wrong* or merely *not actionable*: a duplicated code or a level contradicting the registration on
  record refuses the whole file, because the write it produces is a verdict on somebody's year and
  nothing puts that back. An unknown student, a master's programme, a student already rolled over are
  skipped and counted, so the file can be re-sent once the missing students are inscribed.
- ⚠ **No `ConfirmedRowCount`, and the absence is the point.** The déliberation confirms a number
  because it writes a verdict onto students *nobody named*; this file names every student it touches,
  so a registration created between preview and apply is simply not in it. A number here would be
  ceremony, not a guard.
- **The final-year gate is the same one** — `FinalYearGuard.EnsureMayEnterManyAsync`, called once per
  destination level rather than once per row (four round-trips each would be ~27 000 for this file).
  A blocked row is a skip, named with what he owes.
- **There is no template route**, deliberately: the other three canvases are documents PGSH hands out
  and gets back, and generating a rival version of the faculty's own only invites the two to drift.
  The parser accommodates it instead — headers matched without accents or casing, the two « Etape »
  columns found by prefix and taken **in sheet order** because their year suffix changes every
  September.
- ⚠ **`Code` arrives as an Excel *number*.** Read through `GetString()` it can come back as
  `2.4008386E7`; every row would then match no `Appogee`, read as an unknown student — which is a
  *skip* — so the file would apply cleanly and do nothing.

#### …and an absence decides exactly one thing
A registration of the closing year the file does not mention belongs to somebody who is not coming
back. **In the last year of his own text that is a defence**, so the year is recorded « Diplômé ».
Anywhere else it is not decidable — abandon, exclusion, or a réinscription that has not arrived — and
nothing is written. Measured on the 2026-2027 roll: **1 006 absent in 7ᵉ année Médecine and 212 in 6ᵉ
année Pharmacie**, against **47** absent below a final year.

- **`Inferred`, never `Declared`.** Nobody named these students on a document; PGSH read an absence.
  That also makes the correction free: a real defence roll is `Declared`, and `Declared` overwrites
  `Inferred` while the reverse is refused.
- ⚠ **`FinalYearTest.IsExactlyFinal` is stricter than the déliberation's own « Diplômé » check, in
  two ways.** It compares with `==` rather than `>=`, which keeps out the 6 registrations sitting
  *above* their text's span; and it refuses to answer without a text, where the déliberation stands
  aside and lets « Diplômé » through. The difference is **who spoke**: the faculty naming a student
  may override PGSH's ignorance, an absence may not.
- ⚠ **This is what brought `ConfirmedGraduationCount` back.** The act needed no confirmation number
  while every write landed on a student the file names; a graduation lands on one it does **not**, so
  a registration created between the preview and the apply would have its cursus ended by a
  confirmation nobody gave for it — exactly `ApplyDeliberationCommand.ConfirmedDefaultCount`'s case.

### The faculty's level codes are a table, not a column — `FacultyLevelCodes`
`Application/Stages/Levels/`. `MED04`, `MDME3`, `MDPH06` → the `Level` they name, resolved to
`(Year, AcademicProgram)` because `IX_Level_Year_Program` is unique and a label is not.

- **The mapping is many-to-one and has been since 2025-2026.** The faculty is renaming its codes one
  promotion at a time as each cohort moves up, so `MED01` and `MDME1` are the *same* first year under
  two names. In 2026-2027 the third year is `MED03` for the students repeating it and `MDME3` for the
  ones arriving. A code column on `Level` could not hold both, and the rename is vocabulary, not
  structure. **`MDME3` and `MPHAR3` are new in the 2026-2027 roll and appear in no legacy row.**
- ⚠ **Codes PGSH knowingly does not manage are listed too** (`OutsideScope` — the `MMBTM` masters).
  That is the whole reason it is a table: an importer must tell « a programme we do not cover » from
  « a code nobody has told us about ». The first is skipped and counted, the second refuses the file,
  because a mistyped code silently dropped is a student who quietly does not get re-registered.
- `LegacyImport.LevelMapper` reads this rather than carrying its own copy. Two tables for one
  vocabulary is how a promotion gets imported at one level and re-registered at another.

### « Peut-être sa dernière année ? » is one rule — `FinalYearTest`
`Application/Stages/Progression/`. Pure, and shared by the déliberation's default and the
réinscription roll's verdict derivation. **It is a question, not a guard**: `FinalYearGuard` decides
whether somebody may *enter* a final year, this only says whether a year *might be* one — and every
caller responds to a yes by writing **nothing**. Two copies would disagree about 804 students in the
2026-2027 roll alone.

### The third act — inscription, for the people neither of the other two can see
`Students/Registrations/Inscription/` — `GET inscription/template`, `POST inscription/preview`,
`POST inscription`. The déliberation writes verdicts onto the closing year's registrations; the
réinscription reads those verdicts and creates the next year's. **Both begin from a registration the
student already holds**, which is precisely why neither can reach the September intake, a transfer
arriving from another faculty, a student coming back after an absence, or a réorientation. They hold
no registration to be read, and before this there was no bulk path that created a `Student` at all
(`Students/CreateMany/` was an empty directory; `CreateManyRegistrationsCommand` takes ids).

- **Four writing actions, and they partition on two questions** — does PGSH already hold this person,
  and is he entering the programme he was already in: `NewEntrant` (unknown, level 1) · `TransferIn`
  (unknown, above level 1) · `Returning` (known, no registration this year, same programme) ·
  `ProgrammeChange` (known, the level belongs to another programme).
  - ⚠ **« Sous convention » is not a fifth.** `Student.AgreementType` says how a place is funded, and
    an étudiant sous convention is any of the four — a first-year under an agreement is a
    `NewEntrant`, one arriving in 3ᵉ année a `TransferIn`. Made a kind it would overlap the others and
    the counts would stop adding up. It is a column any row may carry. Nor is « redoublant » one: he
    is carried by the réinscription from his own verdict.
- ⚠ **`LevelId` is required, and it is the guard.** The déliberation may omit it because the year
  makes an identifier unambiguous on its own; here nobody on the sheet holds a registration the level
  could be read from, so it has to be stated. Refused outright on a non-promotion
  (`NotAPromotion`) — « Retrait » has no stages and nobody to rotate.
- ⚠ **`AlreadyRegistered` is a skip, not an error** — the opposite of the déliberation, and for a
  stated reason: **this act creates identities**, so the file has to survive being re-sent with the
  late arrivals appended. The déliberation's file is not stored and cannot be re-sent; a rollover, and
  this, can. Everything else refuses the whole file.
- ⚠ **`ConfirmedStudentCount` is a number, never a checkbox**, and the stake is higher than the
  déliberation's `ConfirmedDefaultCount`. A student row is an *identity* — a CNE, a numéro Apogée, an
  address `SyncUserMiddleware` matches a Keycloak login against — and nothing puts a wrongly-created
  promotion back.

#### What a transfer owes is a fact nobody can reconstruct later — `PriorEnrolment`
One row per entry registration: institution, country, **last level year completed there**, the
équivalence reference, the date. Same shape as `FinalYearEntryWaiver` — a required reference and a
snapshot — because a decision that cannot say what it recognised is not a record.

- ⚠ **Required above the first year for a student PGSH has never seen** (`OriginRequired`), and it is
  not bookkeeping. Today « owed » is *every attempt came back NonValidé*, so a transfer with no
  attempt owes nothing and `FinalYearGuard` stands aside. That reading holds only while the definition
  is negative: **the day « owed » widens to the CNPN's requirement set** — the stated plan once
  1650.25's sets are entered — **a student transferred into 5ᵉ année owes every stage of the four
  years he did elsewhere.** `LastLevelYearCompleted` is the boundary that widening must not look
  below, and it cannot be reconstructed from anything else PGSH holds. It has to exist first.
- **No stages are invented.** Materialising validated `InternshipAssignment`s for the years done
  elsewhere would make the dossier look complete at the price of rows nobody served — which every
  count, mean, chef worklist and occupancy figure would then have to learn to exclude.
- All three of establishment, last year and reference are needed **together**; two of the three is
  refused rather than silently dropped.

#### Which identifiers name a student, and which only corroborate
`CNE` and `Appogee` **identify**; `CIN` and `Email` corroborate. All four are unique on `Students`,
but only the first two are what a row is understood to name.

- ⚠ **A row whose CNE is unknown while its e-mail belongs to somebody is a mistyped cell, not that
  person registering.** Matched on the e-mail it silently gives an existing student a registration
  under a newcomer's name; treated as a newcomer it violates the unique index at `SaveChanges` with
  nothing actionable in the message. It is neither — it is `IdentifierConflict`.
- ⚠ **In-file duplication is checked on *every* identifier the row carries, plus the student it
  resolved to** — not on the first one present. `IX_Registration_Student_Year` is unique, so one
  person on two lines is a raw constraint violation, i.e. a 500; and two lines for one new person,
  one written with his CNE and one with his Apogée, pass any check keyed on a single column.

#### Manufactured identifiers are never silent, and must survive the edit form
- ⚠ **`CNE` *and* `Appogee` are both NOT NULL UNIQUE.** `IX_Student_Appogee` carries a
  « WHERE Appogee IS NOT NULL » filter that reads as though absence were allowed — the column is
  required, so the filter can never be false, `""` is a *value*, and the second student without an
  Apogée collides with the first. A row must carry one of the two; whichever is missing is built from
  the other (`SANS-CNE-…` / `SANS-APOGEE-…`, prefixed so it is readable as provisional) and the row
  says so.
- ⚠ **A manufactured CNE is checked against `StudentIdentifierRules.IsValidCne` before it is
  written.** A validator describes what a *save* must satisfy, so a code the pattern rejects makes the
  student read-only the day somebody opens his file — the refusal naming a field nobody was editing.
  The prefix costs 9 of the 20 characters allowed, so a long Apogée really does overflow it. Refusing
  at creation is the cheap end of the same failure that made 5 646 students unsaveable once already.
- ⚠ **An e-mail is a login.** `Users.Email` is NOT NULL UNIQUE and an intake list routinely has no
  address column, so one is generated `prenom_nom@um5.ac.ma`. `SyncUserMiddleware` falls back to
  matching a Keycloak `sub` on e-mail, so a manufactured address that somebody already holds hands a
  student **another person's account**: the taken set is read from the store, not merely from the
  batch, and every generated address is reported per row and counted (`GeneratedEmails`). The lookup
  is built only when a row will read it.
- ⚠ **The generation rule lives in `StudentIdentifierRules`, once**, because there are two
  generators: `LegacyIdentityMapper` manufactured all 10 204 imported addresses and
  `InscriptionPlanner` manufactures every new one. They had already drifted — one kept letters only,
  the other letters *and digits*, so « Mohamed2 Alaoui » became `mohamed_alaoui` in the importer and
  `mohamed2_alaoui` here, for the same person. Two namespaces for one faculty, and the re-import
  Phase 16 plans would have renumbered people who already log in. **Letters only is what is on disk**,
  so it is what the rule states.

#### One student at a time — `InscribeStudentCommand`
`POST inscription/student`, a JSON body, no file. The transfer notified in November, the returner who
turns up in week three, the réorientation settled after the intake file was sent.

- **Every bulk import owes a single-row way in**, and it matters more here than for the déliberation:
  an inscription file names people who do not exist yet, so re-sending it to add one late arrival
  means re-stating a whole promotion to say one thing.
- **Every value arrives as text, exactly as a sheet cell would**, and is parsed by the same code — so
  the form and the file cannot disagree about what « 03/09/2006 » or « SM A » means, and a refusal
  reads identically on both. Typed fields here and strings there is two grammars for one column.
- **No preview and no confirmation.** `ConfirmedStudentCount` exists because a file has rows nobody
  read and can be edited between the simulation and the apply; here the request *is* the row.
- ⚠ **The refusal carries the row's own sentence** (`InscriptionErrors.RowRefused`, code
  `Inscription.<Action>`). « 1 ligne en erreur » is what a file needs and names nothing a form user
  can act on.
- **Both paths share `InscriptionPlanner` *and* `InscriptionApplier`.** Sharing only the planner
  would leave two copies of the writes, and it is the writes that create identities — same reason
  `FinalYearGuard.EnsureMayEnterManyAsync` is the implementation and the single-student call
  delegates to it.

#### A réorientation is the second path allowed to move a confirmed student stamp
A `CnpnVersion` belongs to exactly one `AcademicProgram`, so carrying a stamp across Médecine →
Pharmacie leaves `Student.CnpnVersionId` naming a text that governs a cursus the student has left —
and everything reading `TotalYears` from it (the final-year gate, the déliberation's « est-ce sa
dernière année ? ») then answers from the wrong arrêté.

- ⚠ **`RegistrationCnpnStamper.Fallback` was programme-blind**, so this was already wrong on any
  réorientation done through the ordinary registration form. It now refuses a carried stamp whose
  programme does not match the registration's level and falls through to `ResolveFromEntryAsync`,
  which resolves from the level's own programme.
- ⚠ **Unresolved is not « leave it as it was ».** Where PGSH holds no text of the new programme
  applying at or before his entry, `Student.ClearCnpnVersion()` removes the stamp. Null means « never
  resolved » — the same thing it means on the ~2 200 students nobody has stamped — and every reader
  falls back on it gracefully. Keeping the old one asserts something false.
- This does not contradict `CnpnTargeting`'s rule. What must never be re-evaluated is an *existing*
  stamp against a population re-selected each September; this fires once, on one named student, at the
  moment the faculty moves him between programmes.

## A guard that refuses loses the faculty's statement — `RegistrationHold`
`Registrations/Holds/` + `Domain/Registrations/RegistrationHold`. **The faculty's réinscription roll
is applied even where PGSH's own record says the student is not ready.** The registration is created
and *held*: it takes part in no roster cut, gets no cohort affectation and is published no période,
until somebody clears it by hand from « Signalements ».

- ⚠ **The case that forces it.** 182 of the 651 7ᵉ année Médecine the 2026-2027 roll re-registers
  read as owing an earlier stage (that measurement predates the « entrer » correction — see the
  count note below) — and in most of them the stage was served and only the évaluation
  is not keyed in. That is a fact about our data entry, not about the student. Refusing the row lost
  the faculty's statement; applying it silently lost ours. The hold keeps both, and turns a diff
  between a spreadsheet and a database into a worklist.
- ⚠ **Whether a signalement freezes is a property of the *reason*, not of the flag** —
  `RegistrationHoldReasonExtensions.Blocking`. A signalement means « quelqu'un doit regarder ceci »;
  blocking is a second, separate question.

  | reason | freezes | why |
  |---|---|---|
  | `OutstandingPriorStages` | **yes** | he may not start his final year's stages before clearing the earlier ones |
  | `AbsentFromReinscriptionRoll` | **yes** | nobody has explained the absence |
  | `IncompleteStudentFile` | **no** | his dossier is *thin*, not *wrong* |

  The first two say « nobody has established that this student may go on ». The third says « we are
  missing his paperwork », and nothing about a missing date de naissance says he may not rotate
  through a service. Collapsing them would either freeze people over a birth date or let an
  unexplained absence plan itself.
  - ⚠ **A list, not a method, because the policy has to translate it.** EF cannot call
    `BlocksPlanning()` inside a predicate; it translates `Contains` over a static array into an `IN`.
    So the array is the single statement and the method reads it.
  - ⚠ **`Plannable` is « no *blocking* hold », `Flagged` is « any hold ».** The worklist counts the
    second and planning obeys the first; a screen that conflated them would report 1 353 blocked
    students where only 1 327 are. `RegistrationHoldResponse.BlocksPlanning` is **sent**, never
    re-derived on the client — same split as `ServicePeriodResponse.State`.
  - ⚠ **`ReleaseHoldReport` carries `StillBlocked` beside `StillHeld`.** A student left carrying only
    « dossier à compléter » is on the worklist *and is planned*; telling the operator he is still
    frozen would be false.
- ⚠ **Every absentee is held, the 1 217 inferred graduations included.** The graduation is *our
  inference*, read off a blank cell, never the faculty's statement: a partial roll would end the
  cursus of people still enrolled with nothing on the row saying a human had looked. It costs a
  genuine graduate nothing — his year is closed, there is nothing to plan — and it catches what an
  absence most often really is, a réinscription that has not arrived, because the flag is still
  standing on the day somebody registers him by hand. The `Diplômé` verdict is still recorded
  (`Inferred`, self-correcting); the hold sits on top of it.
- **Holds need no confirmed count, unlike `WillGraduate`.** A hold is released in one click and the
  row keeps its history; a graduation ends a cursus and nothing puts that back. **Confirm what cannot
  be undone.**
- ⚠ **One predicate, `RegistrationHoldPolicy`, or five screens disagree about who is frozen.** Same
  rule as `ServicePeriodLifecycle` and `StageScoring`, and the expressions are the authority with the
  delegates compiled from them. In a **predicate** the collection aggregate is an `EXISTS` and
  translates; the same collection in a **projection** is the shape Npgsql refuses. Pinned by
  `SqlTranslationTests` — the two hottest planning reads compose it, so a translation failure would
  500 the first real « Générer le plan » with the whole suite green.
- **Excluded from**: `AutoArrangeGroupsCommandHandler` (the roster cut),
  `StudentAffectationService.EligibleRegistrationsQuery` (cohort affectation),
  `CohortProvisioner.GroupTextsQuery` (a held registration does not decide its roster's texts).
  ⚠ **The roster cut names them rather than dropping them** — a cut silently one student short looks
  exactly like a promotion that size, which is the failure the flag exists to remove.
- ⚠ **Released by hand, never by the condition lapsing**, and the note is required. A registration
  that quietly re-entered the répartition the day an évaluation was keyed in is precisely the silent
  behaviour being removed. The row survives its release so the file can say who cleared him and on
  what — the same snapshot bargain as `FinalYearEntryWaiver`. `StillHeld` is returned because two
  reasons can stand at once and « c'est réglé » is a different fact from « il en reste un ».
- ⚠ **No bulk release, deliberately.** It would undo in one click the only thing that made a 1 267-row
  inference safe to record.
- **Idempotent per reason**, because the roll is re-runnable: a second upload neither stacks flags nor
  rewrites evidence somebody is acting on.
- ⚠ **Two numbers, two code versions — do not conflate them.** **182** is what the gate refused *before* session 37 corrected « entrer » to mean « commencer » rather than « être inscrit en » ; it is the motivation, not the current count. With that fix the gate reaches only genuine entrants to a final year — the MED06 → MED07 population — and **measured live on 2026-09-02 the roll holds 60**. Both are real; 60 is what the preview prints today.
- ⚠ **The division with `FinalYearEntryWaiver` is principled, not accidental.** The *roll* holds; the
  *manual* paths (`CreateRegistrationCommand`, `CreateManyRegistrationsCommand`,
  `InscriptionPlanner`) still refuse, with the waiver as the deliberate override. The roll is the
  faculty's own document and outranks a hand-typed form, and ceremony per student is exactly what
  does not scale to 182 at once.

### ⚠ « Couvert par le fichier » means *named*, not *written*
`Skip()` dropped the source registration id, so a row skipped as **« déjà inscrit »** stopped counting
as mentioned — and `ReadAbsence`, which reads « not mentioned » as « ne revient pas », then inferred a
soutenance from the silence of a student the file names on his own line.

- **Latent until the roll was run twice, and the second run is now the normal path** (it is how the
  newcomers get created). Measured on the live base 2026-09-02: the re-upload offered **8 077 gels and
  791 « Diplômé » déduits** where the first pass had found 1 267 and 1 217 — i.e. it would have ended
  the cursus of 791 students it had itself re-registered minutes earlier.
- **The rule:** any row that resolves to a closing-year registration marks it covered, whatever the
  row then does. A skip is still a mention.
- The apply is *designed* to be re-runnable, which is what makes this class of defect dangerous rather
  than merely wrong: the second run is expected, encouraged, and was destructive.

### ⚠ …and idempotency that reads a navigation needs the `Include` *and* the index
`PlaceOnHold` is idempotent per reason by reading `Registration.Holds`. The roll's closing-year query
did not `Include` it, so the second upload raised a **second** absentee flag on all 1 267 of them —
2 534 rows where 1 267 were meant. **An un-Included collection is indistinguishable from an empty
one**, and this suite cannot see the mistake: the in-memory provider fixes navigations up from the
change tracker, so the idempotency test passed throughout.

- Fixed on both sides, deliberately: the `Include`, **and**
  `IX_RegistrationHold_Registration_Reason_Active` — unique on (registration, reason) among the
  unreleased rows. Same bargain as `IX_CnpnLevelEffectivity_Version_Level`: the next missed `Include`
  degrades to a constraint violation instead of silent duplication.
- `SchemaInvariantTests` asserts the index is *declared* on the Npgsql model. That is the half
  checkable without a database; whether PostgreSQL enforces it still needs Testcontainers.

### The roll creates the students it names and PGSH has never seen
26 of the 6 862 lines of the 2026-2027 file. They used to be skipped, on the rule that **creating an
identity is the inscription's act, not the rollover's** — which is sound, and the skip was still
wrong in practice: the only trace was a downloaded spreadsheet, so nobody acted on them.

- **Created from what the file actually carries** — the Apogée and the name — and flagged
  `IncompleteStudentFile`, which is advisory, so they partition and plan with everyone else while
  somebody finishes the dossier.
- ⚠ **No CNE is manufactured.** The row carries an Apogée and `Student.CNE` is optional since the
  `LEGACY-` placeholders were cleared, so a `SANS-CNE-…` would read in every list exactly like a code
  somebody holds. `BacYear` is required by the schema and absent from the roll, so it is left **empty**
  rather than invented — that emptiness is precisely what the flag names.
- ⚠ **The e-mail is the one invented value, because `Users.Email` is NOT NULL UNIQUE** — and it is a
  login: `SyncUserMiddleware` falls back to matching a Keycloak `sub` on it, so an address colliding
  with a real one hands a student another person's account. Allocated **in the planner** against the
  addresses in the *store* (not merely the batch), so the dry run shows the exact address that will be
  written, and printed on the row's own message rather than only counted — « N adresses générées »
  says nothing about *which* address a given student was handed.
- ⚠ **`dbContext.Registrations.Add(registration)`, not `Students.Add(student)`.** `Add` marks the
  reachable graph, and the graph is only whole from the registration: it references the student and
  owns the hold, while `Student.Registrations` was never populated. Adding the student alone left the
  registration untracked and **nothing was written** — caught by a test, not by the compiler.
- ⚠ **An unsaved hold cannot be released by id.** The key is store-generated, so every hold added in
  one unit of work carries `Guid.Empty`, and `FirstOrDefault(h => h.Id == holdId)` would lift whichever
  sits first. `ReleaseHold` refuses an empty id outright.

### The report is a screen and a document, and only one of them may be capped
`GetReinscriptionSheetExportQuery` — `POST reinscription/sheet/export`, three sheets
(Synthèse · Lignes · Absents).

- ⚠ **Written from `ReinscriptionSheetPlan.AllRows`/`AllAbsentees`, never `Report.Rows`.** The report
  is capped at `MaxReportedRows = 1000` and ordered attention-first so a browser survives it; the
  roll produces ~1 450 rows somebody must walk one at a time. Reading the capped list would stop at
  1 000 lines while looking exactly like a complete file.
- **It re-runs the planner rather than reading a stored report** — nothing is stored — which is the
  property, not a workaround: document and screen come from one plan, so a file printed for the
  archive cannot describe a different population from the one that was applied.
- ⚠ **It writes nothing, so it is offered before the confirmation and on a roll the apply would
  refuse.** « Donne-moi la liste des erreurs » is the request, and a refusal naming only the first
  offending line cannot answer it.
- **No `TooManyRows` cap.** The other two exports are scoped by a year the caller may omit; this one
  is bounded by the uploaded file plus one year's registrations. There is no axis to narrow, so a
  limit could only refuse a document the user has no other way to obtain.
