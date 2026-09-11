# Planning — the rotation axis, the crossover, and the services

> Read before touching the rotation cycle, the macro plan, `RotationArranger`, `SchedulePublisher`, the planning grid, or a stage's allowed services.
>
> Split out of `CLAUDE.md` on 2026-09-06 — the text is unchanged. See [`CLAUDE.md`](../CLAUDE.md) for the always-on rules and the map of these documents.

## ⚠ L'axe vient APRÈS le découpage, pas avant

`PreviewRotationCycleQuery` lit les partitions sur `AcademicGroups.RotationGroup` de la promotion :
sans roster, `partitionLabels` est vide et `ApplyRotationCycleCommand` refuse par
`RotationCycleErrors.NoPartitions`. L'ordre réel d'une campagne est donc :

1. **découper la promotion en rosters** (`AutoArrangeGroupsCommand`),
2. **assigner les partitions** (`AssignRotationGroupsCommand`),
3. **poser l'axe** (`ApplyRotationCycleCommand`) — c'est ici que les dates entrent,
4. répartir en services, 5. publier.

⚠ **Conséquence pratique** : le découpage et les partitions ne dépendent d'aucune date, donc ni le
calendrier des fériés ni les semaines d'examens ne les bloquent. Ce qu'ils bloquent est l'**étape 3**.
Vérifié dans le code le 11/09/2026, contre une note de file qui annonçait l'ordre inverse.

## A block of stages runs on one axis, and the crossover is solved, not formula'd
`Stages/RotationCycle/` turns "these stages run concurrently, stage *s* for *kₛ* periods" into the matrix
`GenerateMacroPlanCommand` already consumes. It generates what used to be ticked by hand; nothing
downstream of the matrix changed.

**The arithmetic.** A partition needs `T = Σkₛ` columns to visit every stage. If `Lₛ` partitions sit in
stage *s* at once then, counting partition-columns two ways, `Lₛ·T = P·kₛ`, so:

```
T  = Σ kₛ            columns  (the shared axis, entered once)
Lₛ = P · kₛ / T      partitions concurrently in stage s — must be a whole number
```

- ⚠ **`T` is `Σkₛ`, never `partitions × k`.** Partitions do not lengthen the timeline, they subdivide who
  is where. Three stages at k=1 with six partitions is **3** columns with 2 partitions per stage — not 6.
- ⚠ **`Lₛ` integral pins `P` to a multiple of `T / gcd(kₛ)`.** Refusals name that multiple
  (`PartitionCountIncompatible`), because "wrong number" without "here is what works" is useless.
- ⚠ **A period is a *column of the axis*, and every stage carries a slot per column** — a partition takes
  a run of `kₛ` consecutive ones. Modelling a 2-period stage as one 2-column slot is still wrong, because
  the *other* stages need those columns for the crossover. Whether the group changes service between them
  is a separate question, answered by `Stage.RotationMode` — see below.
- ⚠ **Une durée n'entre dans l'axe que par *kₛ*, et personne ne le recalcule pour vous.** Les colonnes
  d'un axe ont toutes la même largeur — c'est ce qui rend le croisement possible — donc « ce stage dure
  deux fois plus longtemps » s'exprime en lui donnant `kₛ = 2`, jamais en élargissant sa colonne.
  - ✅ **…mais le `kₛ` qu'une durée implique se lit maintenant tout seul** — `PromotionAxis`
    (`Domain/Stages/`, 12/09/2026) : `kₛ = durée_s / pgcd(durées)` et `T = Σkₛ`, purement, sans
    magasin. C'est ce qui permet de répondre « cette promotion tient-elle ? » **avant** qu'un axe
    soit posé, puisque la tranche qui se tient dans un stage à un instant donné est `N·kₛ/T`. Il
    **ne pose aucun créneau** : l'axe reste autorisé à la main, et ceci n'en est que l'arithmétique.
    → [`docs/services.md`](services.md)
  Mesuré le 07/09/2026 : la faculté a mis MED3 Médecine et Chirurgie à **30 j.** et les quatre autres
  à **15 j.** dans le catalogue, alors que l'axe posé porte **6 colonnes de 30 j. ouvrables** et
  `kₛ = 1` partout. Honorer la nouvelle lecture, c'est *T* = 2+2+1+1+1+1 = **8** colonnes de 15 j. —
  autrement dit **reposer le bloc**, que `ApplyRotationCycleCommand` refuse sur une promotion publiée
  (804 cellules). ⚠ **Rien ne le signale**, et c'est correct : `PreviewRotationCycleQuery.Note`
  n'avertit que sur un *déficit* de jours ouvrables ; une colonne trop large est du mou, pas un manque.
  Changer une durée au catalogue ne touche donc **aucune** grille déjà posée.
- ⚠ **Some duration mixes are impossible, not unsupported.** Stages of 2 and 1 give `T = 3`, and a
  two-column run must cover column 2 wherever it starts — so every partition is there and the other stage
  stands empty. No `P` fixes it. `RotationTiling`'s search is exhaustive, so `NoFeasibleArrangement` is a
  proof, not a timeout.
- **The arrangement is an exact cover** (`RotationTiling`), backtracking across partitions *and* columns.
  With equal `kₛ` it reproduces the cyclic Latin square the closed form used to give; unequal `kₛ` break
  that form outright, because stage boundaries of different lengths no longer line up.
- **`RotationCyclePlanner` is pure** (no DB, no clock), which is what lets the invariants be tested
  exhaustively — every partition visits every stage once, for exactly `kₛ` periods, tiling the year, with
  every stage at exactly `Lₛ` in every column.
- ⚠ **`Lₛ > 1` means those partitions are arranged in *one* call, never one each.**
  `GenerateMacroPlanCommandHandler` groups the matrix into `ConcurrencyBlock`s — same stage, same
  window — and hands all their labels to `RotationArranger` together, because the service queue is
  balanced over the cohorts of a single call. One call each balances every partition against the full
  service list in ignorance of the others, and the leftovers *stack*: `BuildServiceQueue`'s stable
  ordering gives the remainder to the same leading services every time, and every partition of a
  column carries the same rotation offset. Measured on Med5 (Gynécologie `k=3`, `L=3`, five services,
  twenty groups): three calls of 7/7/6 gave **6/5/3/3/3**, one call of 20 gives **4/4/4/4/4** — which
  is what `MED05.png` prints.
- **Authoring the axis and running the plan are two acts.** The apply writes the `StageSlot`s and
  *returns* the matrix; the caller hands it to the macro plan. Same separation as déliberation /
  réinscription, and it keeps cohort provisioning, arranging and publishing on their existing path.
- **The axis is replaced wholesale, never merged** — half-old, half-new columns are the exact
  misalignment the feature removes — and is **refused outright while any cell is published**.
  - ⚠ **Cells cascade with the slots** (`CohortSlotAssignments → StageSlots` is `CASCADE`), so both the
    preview and the apply state the count (`PlannedCells` / `PlannedCellsRemoved`). Nothing is lost that
    an arrange cannot rebuild from the returned matrix, but a destructive act nobody is shown a number
    for is one nobody agreed to — the same rule as `RostersRemoved` and `PlannedCellsAffected`.
- **Removing a block is its own act** — `DeleteRotationCycleCommand`
  (`DELETE levels/{id}/rotation-cycle?stageIds=…`). Replacing an axis is not undoing one: a block
  entered by mistake could only be written over, never taken back, short of deleting each stage's slots
  by hand from its own grid. Same shape as `ClearRotationGroupsCommand` beside « Redécouper ».
  - ⚠ **Scoped to the stages named, never to the level.** One promotion legitimately holds several
    blocks — the new CNPN's 3ᵉ année is two semesters — so a removal keyed on the level would take the
    other semester with it.
  - Refused while anything on it is published (`CannotDeletePublished`; unpublish first, that path is
    guarded and says what it costs), and `NoBlockToDelete` rather than a cheerful success on a
    promotion that never had one.
- The new CNPN's 3rd year is **two blocks** (three stages per semester), not one block of six. The 6th
  year is **one** block of six with mixed durations: `k = [2,2,2,2,1,1]`, `T = 10`, `P = 10`,
  `L = [2,2,2,2,1,1]` — which is exactly the ten monthly columns of `Med6.png`. Blocks of one level
  coexist; replacement is scoped to the stages named in the command.
- **A column is stated in months, weeks or *jours ouvrables*** — `GenerateAxisWindowsQuery`, and it is
  a **server** call. Laying the axis in the browser with `setUTCMonth` was right for calendar months and
  silently wrong the moment a duration means worked days: no client has the holiday table. Months and
  weeks stay calendar-exact (a monthly axis must land on the 1st); only `WorkingDays` lays each column
  to a fixed count of worked days, and it is the **only** unit under which two columns are the same
  amount of stage — février and mars are not.

## ⚠ The axis is laid on the **promotion's** calendar, not the faculty's
`GenerateAxisWindowsQuery` takes `LevelId` (and `AcademicYearId`) and resolves its calendar through
`WorkingDayProvider.ForPromotionAsync` — the faculty's holidays **plus** the exam windows that
promotion declared (`PromotionPause`, `PHASES.md` §17). `PreviewRotationCycleQuery` does the same for
its `DurationChecks`, so what the screen says a stage gets is what the axis actually gives it.

- ⚠ **Send the level.** Omitted, the columns are laid on the faculty calendar alone, land squarely on
  a week the promotion is sitting exams, and nothing on either side says so. `RotationCyclePage`
  disables « Générer les fenêtres » until a promotion is chosen for exactly this reason — it is a
  precondition of the act, not a detail of the form.
- **This is where the compensation happens, and it is the whole of it.** A window declared in
  September makes the axis generated afterwards put its *kₛ* worked days in a column that ends later on
  the wall calendar; the grid is written from those windows and the périodes are published from the
  grid, so all three are laid against the same days. Nothing is pushed onto an assignment —
  `InternshipAssignment.ResumePeriod` *accumulates*, which is why it can be neither corrected nor
  revoked and this can.
- ⚠ **Declaring a window over a grid already laid moves nothing**, deliberately. The créneaux keep
  their dates and are now short by what the window takes; `PreviewPromotionPauseQuery` counts that per
  stage and per créneau, plus the rotations crossing it split by lifecycle state. Silently rewriting
  the dates of a published promotion is the one thing this must not do.
- ⚠ **…and whether that shortfall can be repaired at all is a fact about the promotion, not about the
  window — measured on the live base 2026-09-06.** Re-laying the axis is a real act *only while nothing
  has been published from it*: `ApplyRotationCycleCommand` refuses on `PublishedCells > 0` for the
  **whole block**, and the 3ᵉ MED holds **804**. So for a published promotion — precisely the one a
  late window hurts — there is **no remedy today**, and the days are lost until §17.1 exists. The
  preview therefore carries `PublishedCellsInGrid` and its warning branches on it: **a report that
  prescribes a refused button is worse than one that prescribes nothing.**
- **`GeneratedAxisColumn.Pauses` is reported apart from `Holidays`.** A holiday is everyone's; a pause
  is this promotion's, and the promotion rotating through the same service the same morning does not
  have it. Merging them would make a column's explanation false for the neighbour reading it.

## A service holds who is standing in it, so the balance is per **column**
`RotationArranger` builds the capacity-weighted service queue over the cohorts of **one period**, and
indexes it by their position *within that period*. Not over the cohorts of the call: the two coincide
only when the caller scoped to a `ConcurrencyBlock`, and « auto-répartir ce stage » — every partition,
every période, one call — is a real button that does not.

- ⚠ **The crossover leaves one partition per column, and the whole partition landed in one service.**
  Every other cell of the column is refused (the group is already placed in another stage over that
  window), so a column holds `n/P` cohorts while the queue was built for `n`. Partitions are
  contiguous in the ordering and each service owns a contiguous run of the queue, so the partition
  fell *inside* one service's run. Measured on 5MED Psychiatrie 2025-2026 (60 groups, 9 partitions,
  5 services, 2026-08-18): **all nine columns went to a single service — 69 to 85 students against a
  capacity of 20 — and two of the five services were never used all year.** Reproduced exactly, 9/9,
  from `queue[(ci + phase·⌊n/T⌋) mod n]`.
- ⚠ **Nothing reported it.** 60 cells written, no failure, and the `GroupConflicts` it counted are the
  ones the crossover is made of — indistinguishable from a correct plan. The printed répartition is
  the only place it shows, which is where the user found it.
- **A column's shape cannot be improved on; which services carry the remainder can.** Seven groups
  over five services is 2,2,1,1,1 whichever way they fall — the column indexes every queue position
  exactly once, so rotating the queue cannot change the multiset. Only the *leftover* tie-break can,
  and it was stable: with equal capacities (all 148 imported services carry the same default) the
  same two leading services carried the pair in every column of the year. `BuildServiceQueue` now
  breaks ties by the column's phase.
- **The step is at least 1.** `⌊m/cycleLength⌋` is 0 whenever a column set is smaller than the cycle,
  which froze a `PerPeriod` run into one service — `SingleService` by accident.
- **`SingleService` decides once for the run**, over everyone the run touches and from its first
  phase, so a group still stands in one service for the whole run and two partitions doing the stage
  in different windows still land differently.
- ⚠ **A published cell is excluded from its column's balance rather than counted against it.** The
  free cohorts spread over every service, including one an already-published cohort is sitting in.
  Harmless today (0 grid-linked periods in the whole base) and worth closing when publication is real.
- **A cohorte weighs its members minus its délocalisés** (`CohortsQuery`, 2026-09-06). A student
  serving the stage outside the faculty needs no place in the rotation, and weighting the queue by him
  spreads a cohorte over services to hold people who are not there. Same arithmetic as
  `ServiceOccupancyCalculator` — see [`delocalization.md`](delocalization.md).
- **A service `IsExternal` is dropped from the pool** before anything is weighted, even if an older row
  authorises it. It is a place the faculty does not run; the only way a student reaches it is a
  délocalisation.
- ⚠ **…and the arranger balances a column it did not size.** How many students a column *holds* is
  settled two acts earlier, by the cut and the partitioning — `RotationArranger` only decides which
  services carry them. Until 11/09/2026 those two acts composed badly: `RosterCut` emitted the larger
  rosters first and `PartitionAllocator.Contiguous` gives partition A the first block of roster
  numbers, so every oversized roster landed in the leading partitions. Measured on the live base: the
  3ᵉ MED came out **100, 100, 100, 93, 90 ×6** where the cut was meant to be even, which spent the
  entire margin of Santé Publique and Simulation Médicale (100 places, columns of 100) without any
  screen saying so. Fixed in `RosterCut` — the sizes and the count are unchanged, only the order they
  are dealt in. → [`planning-rosters.md`](planning-rosters.md) §②ter.

## Which stage a partition is in is authored; which service it lands in is computed
Two decisions, two owners, and confusing them is where this area goes wrong.

| decides | owner | evidence |
|---|---|---|
| which rosters travel together | `AssignRotationGroupsCommand` | 9 partitions of 6-7 rosters |
| which partition is in which stage over which columns — **the crossover** | rotation block (`RotationCycle`) / macro matrix | one stage itinerary per partition |
| which service each cohort gets inside one (stage, column) | `RotationArranger` | 7 distinct service paths inside one partition |

Measured on 5MED 2025-2026: every partition has **one** stage itinerary and **6-7** service paths.
A roster's year is its partition's; its services are not, and must not be — a partition sharing a
service is the whole-partition-in-one-service defect above.

- ⚠ **`RotationArranger` cannot invent the crossover.** It has no notion of `kₛ`, so an unscoped
  arrange writes a cell for every (cohort × column) it is not refused. On a stage nothing has crossed
  into yet that is the whole promotion in one stage all year, and every stage arranged afterwards
  gets nothing — refused now as `StageWouldFillEveryColumn`, the `PerPeriod` counterpart of
  `SingleServiceRunNotScoped`. Two conditions narrow it and both matter: it fires only on the call
  that names *neither* a partition *nor* a window (naming either is authored targeting — « A →
  Médecine P1-P2 » is the faculty's own layout), and only when another stage of the promotion
  declares the same windows (a stage that *is* the whole axis starves nobody).
- **The unscoped arrange is a fill, not a plan, and its correctness is borrowed.** Psychiatrie was
  arrangeable in one click only because the other six stages already held every group in 8 of its 9
  columns: 480 of the 540 candidate cells refused, 60 free, one per roster — no freedom over columns
  at all, only over services. Pressed first, the same button decides the year.
- ⚠ **The caller's stage order is the first partition's year.** `RotationTiling.Enumerate` walks the
  stages as given, so `schedules[0]` is that order laid end to end and partition A takes the
  lowest-index schedule that fits. Entering Gynéco(3), Neuro, ORL… puts A in Gynéco P1-3, Neuro P4,
  ORL P5 — confirmed against the applied 5MED block. Preferred, not guaranteed: where no complete
  arrangement contains it, a later schedule wins over failing.
- **A block is read back from the axis, not from the request** — `GetRotationCycleQuery`
  (`GET levels/{id}/rotation-cycle`). Stages whose slots carry the identical window list *are* a
  block, so a date corrected afterwards on one stage's own grid shows through instead of being
  papered over, and a stage that drifted correctly falls out of the block.
  - ⚠ **The axis cannot state `kₛ`** — every stage of a block carries a slot on *every* column, which
    is exactly what makes the crossover possible. Recovered in order: the apply's audit entry → the
    widest run a cohort actually holds → nothing. `RotationPeriodsSource` says which, for the same
    reason `OutcomeSource` and `CnpnSource` do: a duration deduced from an empty grid is not one
    somebody entered.
  - The audit metadata is **not** filtered by year: it carries the *request's* `AcademicYearId`,
    which is null whenever the caller left it to the resolver. The stage set is matched against the
    slots on disk instead — a stronger check, since an apply for another year cannot match a block
    that is not there.

## The order the services are walked in is authored — `StageAllowedService.Rank`
`Stages/AllowedServices/` (`PUT stages/{id}/allowed-services/order`) + `ServiceRotationOrder`
(pure, in `Domain/Stages/`). The join carries a 1-based `Rank`; `RotationArranger` orders on it and
falls back to `Id`.

- ⚠ **It was `OrderBy(Service.Id)` — catalogue creation order, i.e. the legacy import order — and it
  decides which contiguous run of group numbers lands in which service.** `BuildServiceQueue` emits
  each service's block **consecutively**, and the earliest column of the footprint takes phase 0, so
  `offset = 0` and the cohort at column position 0 takes `queue[0]`. First groups, first service,
  first période, deterministically. Nobody had chosen that order.
- ⚠ **…and the stage's fiche listed the services by hospital then name — a *fourth* order.** Neither
  the authored one nor the one the arranger walked, so nothing on screen said which service was
  first, in the one place where being first decides something. The read now returns them in rotation
  order with the position beside each.
- **Why it exists: it is the cheap answer to a nominative placement.** The alternative is editing a
  cell on the planning grid, and the printed répartition **shows** that edit — `GroupNumberRanges`
  refuses to merge across the hole it leaves, so « 21-27 » becomes « 21-23, 25-27 » beside a lone
  « 24 » in another row, on a page where every other cell is a clean range. Reordering produces the
  same placement in whole ranges. `PLANNING.md` §11 ③.
- **What the lever can and cannot do.** Per stage, so « S1 for stage A, S2 for stage B » is two
  independent orders. ⚠ It moves the **whole promotion**, not one group — usually the point, never a
  pin. ⚠ Granularity is the **block**: a service's block width comes from its own capacity
  (`(int)(capacity / avgStudents)`), so reordering permutes blocks as units — you choose which
  service covers a position, not which group number. ⚠ Two conflicting requests on one stage can be
  unsatisfiable. ⚠ A service under one average cohort has weight 0 and is out of the rotation
  entirely; ranking it first does not bring it back.
- ⚠ **A rank is a position, always contiguous from 1** — every operation returns the whole re-based
  sequence rather than patching one row. Holes would make the number shown beside a service disagree
  with the place it takes in the queue: one number meaning two things.
- ⚠ **A partial list is refused, never completed** (`ServiceOrderNotAPermutation`), and the refusal
  names *which* of missing / unknown / duplicated applies, because they call for different acts. A
  short list is far likelier to be a page opened before somebody else authorised a service than an
  intention to leave it last — and silently appending states an order nobody authored, in the one
  place whose whole purpose is that the order is authored.
- ⚠ **Written in two statements, never one** (`ServiceRankWriter`). `IX_StageAllowedServices_Stage_Rank`
  is unique and non-deferrable, so a 1↔2 swap in one `SaveChanges` leaves the order of the two
  `UPDATE`s to EF and one of them violates the constraint half-way through. The rows are parked on
  their **negative** ranks first. Same shape, same reason, as `SetCurrentAcademicYearCommandHandler`
  saving the demotion before the promotion.
- ⚠ **…and the pair is one `ExecuteAtomicallyAsync`, because the parked state is worse than either
  end.** A connection dropped between the two saves leaves *every* rank negative, which `SortKeyOf`
  reads as « nobody chose » — so the stage silently reverts to id order, on a list somebody had just
  ordered by hand. Insertion and removal are inside the same transaction: the row added is the row
  being ranked last, and the row removed is the reason the survivors move up.
- ⚠ **Every row the writer touches is loaded *inside* that transaction.** `ExecuteAtomicallyAsync`
  opens each attempt with `ChangeTracker.Clear()`, so an entity handed in from outside would be
  detached by the time it is mutated and `SaveChanges` would write nothing — `CnpnTargetPlanner`'s
  defect exactly. That is also why `AddAllowedServiceCommandHandler` no longer writes through
  `stage.AllowedServices`.
- ⚠ **The permutation is re-checked inside the write** (`ServiceOrderIsStale`). The caller validated
  against a read taken before the transaction, so a service authorised in between arrives as a row
  the ranking says nothing about — it would keep a positive rank, collide, and surface as a 500
  naming an index. Its own sentence, not `ServiceOrderNotAPermutation`: what the caller sent *was*
  well-formed when checked, and blaming the payload for a concurrent edit sends the user hunting for
  a mistake that is not in it.
- **`ServiceRankWriter.RanksQuery` is the one lookup**, read by both `RotationArranger` and the
  stage's fiche. On neither of them deliberately: the arranger is the planning engine and the fiche a
  read screen, so hanging it off one makes the other depend on it for a fact belonging to neither.
- ⚠ **Rank 0 sorts *last*, never first** (`ServiceRotationOrder.SortKeyOf`). The column defaults to
  0, so a join row written by a corrective script would otherwise sort ahead of every service
  somebody deliberately placed and take the first run of group numbers — the exact opposite of what
  an absent choice means.
- **The migration backfills from `ORDER BY "ServiceId"`**, i.e. exactly what the arranger already
  did, so applying it changes no plan. The rank starts life describing the old behaviour; only an
  explicit reorder moves it. Without the backfill the unique index fails outright — all 146 authored
  rows would carry the default 0, several per stage.
- **Adding appends, removing closes the hole.** A newly authorised service has no claim on a position
  somebody chose for the others. Neither act touches a written cell: the order is read by the **next**
  auto-arrange, which is guarded and audited on its own terms.
- ⚠ **It is a payload join behind the same skip navigation.** `Stage.AllowedServices` stays
  `ICollection<Service>` (`UsingEntity<StageAllowedService>`), so the ~10 existing reads —
  `.Count`, `.Any(…)` in a predicate, the `Include` in `GetHospitalStageCoverageQueryHandler` — are
  untouched, including the `SelectMany`-over-a-skip-navigation trap. In a fixture, `Allow` leaves the
  rank at 0 (« nobody chose ») and `AllowInOrder` writes the join rows; **never both**, or EF tracks
  two instances of one key.

## A nominative placement request is answered by a roster, never by a transfer or a pin
`AcademicGroups/Placements/` (`GET groups/placements`) and `Hospitals/Coverage/`
(`GET hospitals/{id}/stage-coverage`). « Cet étudiant fait tous ses stages à l'hôpital militaire »,
« ces deux étudiantes ensemble, stage A en S1 et stage B en S2 », « ces frères dans le même service ».

- **The faculty already works this way, and it is in the imported data.** Measured 2026-09-03:
  2024-2025 6ᵉ année Médecine held **five rosters entirely at the HMIMV** — groupes 102, 116, 130,
  144, 158, of **6-7 students each**, i.e. normal-sized against a promotion average of 5.8. The
  military hospital is the largest in the base (**35 services**) and lists one for **every** 6ᵉ année
  stage.
- ⚠ **A « transfert définitif » does not send a student to a service, it sends him to a roster.**
  `TransferStudentCommand` moves `Registration.AcademicGroupId`; the target roster still gets its
  services from cells. So « groupe *ou* transfert » is a false alternative — the transfer is *how*
  somebody joins a group. What is genuinely a choice is which roster.
- ⚠ **`DelocalizeStudentCommand` is the wrong tool and the dangerous one.** It means *served entirely
  outside the faculty*: it drops the in-faculty rotation, creates the période already
  `IsStarted && IsComplete`, and waits for a paper fiche. HMIMV is **in** the catalogue with real
  chefs, so délocaliser there puts students outside the chef worklist, outside occupancy and outside
  in-app evaluation — for a hospital that is right there.
- ⚠ **A roster of one or two students is not free, and nothing reports the cost.**
  `RotationArranger.BuildServiceQueue` weights each service by how many *whole average-sized* cohorts
  it can hold, and **a cohort is atomic** — so a two-student cohort takes a queue position sized for
  an average roster and spends a full cohort's worth of that service's intake on two people. Nothing
  refuses it; the promotion's balance is simply a little wrong. Hence the order below.

**The order, cheapest first:** ① transfer into a roster that already goes there — no new roster, no
pin, no dent in the balance · ② **one** shared roster per recurring constraint (all the military
students of a promotion in one roster of 7, which is what 2024-2025 did), never one roster per
request · ③ pin the cells of an existing roster, accepting that it moves everyone in it · ④ a
dedicated 1-2 student roster, last.

- **①'s reachability is the whole reason these reads exist.** Nothing could be asked « quel groupe est
  au HMIMV ? », so finding the right existing roster meant reading the planning grid of every stage by
  eye — and the practical route was therefore ④, the most expensive one.
- ⚠ **`PlacementMatch.Exclusively` is two conditions and the *positive* one is the guard.** « Aucune
  cellule ailleurs » is vacuously true of a roster with no cell, so « il en tient au moins une » has
  to be asserted separately. Left out, every unarranged roster is returned as an exact match — and the
  base holds **0 cells on every year**, so that is every roster in the faculty. Removing that half
  fails five tests, the endpoint one included.
- ⚠ **`RosterPlacementSummary.PlacedRosters` is what an empty answer means.** « Personne n'y va » and
  « rien n'est encore réparti » call for opposite acts and a bare zero is read as the first. Same rule
  as `RepartitionSummary.DeclaredSlotCount` and `ExportNotes`.
- ⚠ **`StageHospitalCoverage.NoServicesAuthored` is not a weaker « non couvert ».** An empty
  allowed-services list is **not enforced** (`SetCohortSlotAssignmentCommandHandler` checks it « when
  configured »), so such a stage is open to every service — the blank means nobody authored the list.
  **Seven** stages of the catalogue are in that state (re-measured 2026-09-04; it was three before
  the 1650.25 immersion stages were added), and ⚠ **that is not a gap to close on sight** — see the
  planning-order rule in [`planning-rosters.md`](planning-rosters.md)
  (« An unplanned promotion is a state »). The feasibility this read exists for: **5ᵉ année
  Santé Publique authorises exactly one service and it is not at the HMIMV**, so « tout au militaire »
  is impossible for that promotion — discovered today at the sixth cell, after the promise.
- **The match is stated twice** — a SQL `EXISTS`/`NOT EXISTS` pair choosing the rosters, and counts in
  memory filling `Matches` and `RosterHospitalPlacement` — because EF needs the comparison inline
  inside its nested `Any`. What holds them together is a test asserting the *equivalence*: inside the
  `Anywhere` result, the rosters the in-memory classifier calls `Entire` are exactly the ones SQL
  returns for `Exclusively`.

### ✅ A pinned cell, and a service held for named rosters (2026-09-07)

Until this shipped, `CohortSlotAssignment` carried `{CohortId, StageSlotId, ServiceId}` and **nothing
that says a human chose it**, while `RotationArranger` deleted every unpublished cell in its reach and
rewrote it (`staleIds`). A pinned placement was therefore destroyed by the next « auto-répartir ce
stage », with the arrange reporting `Assigned = N` and looking entirely normal.

- **`CohortSlotAssignment.Source` — `Arranged` / `Pinned`.** A pinned cell is folded into the same
  `lockedCells` set as a published one, because everything downstream asks the same question — « is
  this cell mine to place? » — and the answer is no in both cases. What differs is what is
  *reported*: `RotationArrangeResult.PinnedCellsKept`, carried through `MacroPlanResult`. ⚠ *An act
  that deliberately writes fewer cells than it was asked for must say how many, or a nominative
  placement surviving reads as an arrange that half failed.*
- ⚠ **`SetCohortSlotAssignment` pins on overwrite too, not only on create.** Overwriting the service
  the arranger chose *is* the human decision; left `Arranged`, the correction just made would be
  undone by the next arrange — the same defect reached from the other end.
- **`StageAllowedService.PlacementMode` — `Rotation` / `Reserved`.** Reserved leaves the pool
  entirely, so only a pin puts anybody there. ⚠ **Its capacity leaves `totalCapacity` with it**, which
  is why `ReservedServices` is reported beside it: « il manque N places » is measured against a
  smaller ceiling on purpose. Reserving *every* service refuses as `Schedule.AllServicesReserved`,
  never as `NoServicesAdmitLevel` — « aucun ne vous accueille » would send the operator to widen
  quotas that were never the obstacle.
- ⚠ **A service cannot be reserved by filling it first.** `saturatedServices` is computed **after**
  `SaveChangesAsync`, as a report; the tiling weights by `CapacityFor(levelId)` and never reads live
  occupancy. That is why the reservation is a declaration and not an arrangement of the data.
- **Composing the roster is `ApplyBulkRosterAssignmentCommand`** — see
  [`planning-rosters.md`](planning-rosters.md). It moves students and places nobody: which service a
  roster goes to stays the grid's answer, with its own guards and its own audit entry.

`PHASES.md` §19.2 carries the whole record.

### A partner hospital is this same case, and « réservé » is the half that cannot be said

Written 2026-09-07, when the faculty asked for the GST Kénitra services: a form circulated, a list of
volunteers came back, those services are held **for them**. It is the HMIMV case with an added
exclusivity claim, and the boundary is worth stating because three quarters of it already works.

- ⚠ **The services belong in the catalogue, not outside it.** A GST hospital has chefs and evaluates
  in the app, so `IsExternal` and `DelocalizeStudentCommand` are the wrong tools — see
  [`delocalization.md`](delocalization.md). What the volunteers need is a **roster**.
- **The arranger's removal is *scoped***, and that is the interim procedure:
  `targetCohortIds.Contains(a.CohortId) && slotIds.Contains(a.StageSlotId)`. Give the volunteer
  rosters their own partition label, arrange the other partitions only, and their pinned cells
  survive while everyone else is rebalanced. It holds until somebody runs « Générer le plan », whose
  matrix targets every partition.
- ⚠ **Exclusivity has no expression today.** A service must be in the stage's allowed list for
  `SetCohortSlotAssignment` to accept a pin — and being in that list is exactly what puts it in the
  arranger's pool. So the run that rebalances the others **will place them there too**, and it will
  not notice: `saturatedServices` is computed **after** `SaveChangesAsync`, as a report. The tiling
  weights by `Capacity`, never by live occupancy. Publication refuses afterwards, which is the wrong
  end of the process to find out.
- ⚠ **A level quota of 0 is not the workaround.** It does drop the service from the pool
  (`Where(s => s.Capacity > 0)`) while leaving the pin acceptable — and it writes « ce service
  n'admet aucun étudiant de ce niveau » into the base, which is false, and reads that way in the
  occupancy report, the charge report and the publish guard for every other promotion.

✅ **All of that shipped on 2026-09-07** — `PlacementMode` is the piece that closed the second half,
and the section above records what the arranger now does. The partition-label detour still works and
is no longer needed; it is kept in `PHASES.md` §19.2 because it explains what the base does until the
AppHost restarts, and because the half it could not give is exactly the half the mode adds.

### ⚠ The in-memory provider refuses what Npgsql accepts, too
`SelectMany` over a **skip navigation** — `Stages.SelectMany(s => s.AllowedServices.Where(…))` —
throws `NotImplementedException` from `InMemoryQueryExpression.AddJoin`, while Npgsql translates it
without complaint. The mirror image of the translation blind spot this suite was built around, and it
means a query can be *correct in production and untestable here*.

- `GetHospitalStageCoverageQueryHandler` therefore loads the list with `Include` and filters it in
  memory. ⚠ **The verdict still comes from `StagesQuery`'s SQL aggregates, never from the loaded
  collection** — an un-Included collection is indistinguishable from an empty one (`CnpnSpanFloor`'s
  lesson), so counting here would report every stage as `NoServicesAuthored` on PostgreSQL with the
  whole suite green. Split as it is, a forgotten `Include` degrades to « couvert, mais aucun service
  nommé » — wrong in a way somebody can see.
- `AllowedServices.Any(…)` in a **predicate** and `AllowedServices.Count` in a **projection** are both
  fine on both providers. It is only `SelectMany` over the navigation that is not.

## Several périodes is not several services — `Stage.RotationMode` says which
A stage occupying `kₛ` columns can spend them moving S1 → S2 → … with an evaluation each, or standing
in **one** service for the whole run with **one** evaluation. `StageRotationMode` (`PerPeriod` default
/ `SingleService`) is the switch, and `Stage.LevelId` means a stage belongs to one promotion, so
per-stage is already per-promotion.

- ⚠ **Neither mode is the normal one.** Measured on the imported Access history 2026-08-14: 5ᵉ année is
  `SingleService` in **30,614 of 30,614** stage placements and 6ᵉ in 21,309 of 21,310, while 3ᵉ genuinely
  rotates (5,385 placements over two services, 409 over three). 5MED Gynécologie is one period of ~70
  calendar days against a catalogue of 44 j.o. — three columns, one service. The per-période rotation was
  never the general case; it is 3ᵉ and 4ᵉ année.
- **The axis is untouched.** `T = Σkₛ` belongs to the block, the group really does occupy all `kₛ`
  columns, and the cells still exist one per column — `PeriodAxis`, `GroupScheduleConflictGuard` and the
  printed répartition are unaffected. Only two things move:
  - `RotationArranger` freezes the rotation offset across the call (`runOffset`) instead of advancing it
    per column, so every cell of the run takes the same service. The phase still comes from the run's
    *first* column, so two partitions doing the stage in different windows still land differently.
  - `SchedulePublisher` collapses the run into **one** `ServicePeriod` spanning it. `StageScoring` and
    `RecomputeFinalScore` need no special case: the mean of one mark is that mark, and "every period
    validated" is that one validated.
- **A run is derived from the cells, not from the caller's window** (`SchedulePublisher.BuildStays`):
  maximal consecutive period numbers *with the same service*, per cohort. That is what makes publishing
  one concurrency block and publishing the whole stage produce the same stays. Breaking on a service
  change matters too — a cell edited by hand to another service is two stays, not one period whose
  service is a lie for half its span.
- ⚠ **A single-service stage must be arranged run by run** (`SingleServiceRunNotScoped`). Unscoped,
  "auto-arrange this stage" makes every column one run and hands a cohort one service for the whole year
  — written silently, looking exactly like a correct plan. The macro plan always scopes (a
  `ConcurrencyBlock` *is* a run), so the guard only bites the bare auto-arrange path. Non-contiguous
  windows are refused too (`SingleServiceRunNotContiguous`): a single stay cannot have a hole.
- ⚠ **The mode is frozen once the stage is published** (`RotationModeLockedByPublication`) — the periods
  on disk were shaped by it.

### `ServicePeriodSlotCoverage` — because one period can cover several cells
`ServicePeriod.CohortSlotAssignmentId` names the **first** cell of a run. It still answers "did this come
from the grid?", which is all ~25 call sites ask. It cannot answer "is *this cell* published?" — under
`SingleService` the trailing cells of a published run would read as free, and the arranger would rewrite
them or `DeleteStageSlot` would drop a column out from under a running stage.

- One coverage row **per covered cell under both modes**, so the guards read one table, not two.
- The five callers go through `PublishedCells` (`PublishedAmongAsync`, `IsCellPublishedAsync`,
  `SlotHasPublishedCellAsync`) rather than reading the FK: `RotationArranger`, `DeleteStageSlot`,
  `ClearCohortSlotAssignment`, `ClearSlotAssignments`, `RotationCycleContext`.
  - ⚠ **`RotationCycleContext` was the one that drifted**, and it is the worst place for it: it is the
    guard the rotation-cycle *apply* and *delete* stand on, so a run published under `SingleService`
    would have had its trailing columns deleted out from under it while the lead cell alone read as
    locked. `GetRotationCycleQuery` had it right from the start — the read was correct and the write
    guard was not, which is the dangerous way round. Latent only because every 6ᵉ année stage is
    `PerPeriod` and the base holds 0 grid-linked periods.
- The migration back-fills one row per existing grid-linked period — correct because nothing can have
  been published in `SingleService` mode before the mode existed.

## The planning grid is a matrix, and a matrix is not exempt from pagination
`GetStageScheduleQuery` returns **a page of cohorte rows plus a `StageScheduleSummary`** — never every
row. Measured 2026-08-31 on the live base: the current year's biggest stage carries **105 cohortes over
ten columns**, i.e. a thousand cells in one object and a thousand cell components mounted at once.

- ⚠ **The half that proves where the cost was: closing was slow too.** Closing does no server work at
  all, so the seconds were the browser mounting and unmounting the grid, not the query — the SQL
  behind it measures ~40 ms. Paging the rows is therefore the fix on both ends; the Mantine `Modal`
  also drops its exit transition, which kept the whole tree alive while it played.
- **The partition filter moved to the server with the paging** (`RotationGroup`). Filtering the rows
  the client happens to hold answers « aucune cohorte » for anyone sitting on page 3, and nothing
  distinguishes that from a partition nobody has cut — the same reason the chef worklist's search had
  to move.
- ⚠ **Everything the screen *states* had to move with it.** A bounded list can only describe itself,
  and the numbers beside it drive acts on the whole selection: « Publier tout (N) » fires one
  stage-wide call, so an N counted from 25 visible rows promises 25 and publishes 90. `Summary`
  carries `TotalCohorts`, `PublishedCohorts`, `ConfiguredUnpublishedCohorts`, the saturation report
  and the two derived facts below — all measured over the selection, in the store.
  - **`Partitions` is deliberately *not* narrowed by the filter.** They are the chips the user filters
    *with*; narrowed by the active filter there is no way back to the others.
  - **Each row carries `DelocalizedCount` beside `StudentCount`**, and the cells are measured on the
    difference. A roster délocalisé en masse otherwise shows a full membership beside cells loading
    nothing, which reads exactly like a bug — and the next person to open the grid re-arranges them
    back into the CHU. Zero is the ordinary case.
  - **`Saturations` is deduplicated per (créneau, service)**, which is what makes it bounded by
    columns × services rather than by cohortes — a dozen cohortes in one over-filled service is one
    problem, not twelve. `SaturatedCellCount` stays exact when the list is capped, and the UI says
    which of the two it is printing.
  - **`OccupiedSlotIds`** (the selection) is what « nouveaux créneaux uniquement » reads: off the page
    it would call a column empty because *this page's* cohortes are not in it, and then rewrite a
    rotation already arranged.
  - **`PartitionUsage`** (the **whole** stage, unfiltered) is what warns that partition B already
    holds the columns A is about to be arranged into. That question is about the rows the filter has
    just removed, so it can only be answered server-side.
- **A cohorte carries the columns it stands in** — `CohortResponse.PeriodNumbers`. « Démarrer /
  clôturer sur P4-P6 » used to fold that out of the grid's cells, which worked only while the grid
  shipped everything; past page 1 every cohorte would have read as running in no period at all and
  been dropped from the list silently. Read by a **second flat query** keyed on the page's ids, not by
  a collection subquery in the row projection — the element would be a computed `int` with no key,
  which is the shape Npgsql refuses.

## A bulk act is one command, or it is a storm of refusals
« Publier tout » on the stage page looped: one `PublishCohortSchedule` per cohorte, sequentially, each
rebuilding the service occupancy from scratch — and since `errorMiddleware` toasts every rejected
mutation, an over-capacity plan produced **one red toast per cohorte**, arriving one at a time as the
loop ground on. It now sends `PublishStageSchedule` once, which the grid modal already did.

- ⚠ **…and one call had to stop refusing on the first cell.** `SchedulePublisher.EnsureIntakeAsync`
  now collects **every** breach and returns one refusal (`Schedule.PublishRefusedByIntake`) naming the
  count, how many of them are the unforceable admissibility half, and the heaviest three. Refusing on
  the first meant fixing a stage-wide plan one service at a time, with a full re-publish between each.
- **A single breach keeps its own sentence** (`Schedule.CapacityExceeded` /
  `LevelCapacityExceeded` / `LevelNotAdmitted`). The aggregate exists for a promotion; wrapping one
  cell in « 1 affectation dépasse… » says strictly less than the message it replaced.
- The guard still runs **before** the write, and the tests assert the store is untouched after a
  refusal — a handler test alone cannot tell a pre-check from a post-check.
- **« Dépublier toutes » is one command too, since 2026-09-04** — `UnpublishStageScheduleCommand`.
  ⚠ **The lag was never the deletion.** The loop awaited one request per cohorte (134 on the 3ᵉ MED)
  and *each one invalidated the stage's cache tag*, so the page refetched a 134-row list after every
  request — a refetch storm on top of N round trips, plus one red toast per refusal.
  - ⚠ **There is deliberately no `Force`.** A cohorte whose rotation has begun is **skipped and
    counted**, never swept: forcing destroys marks and attendance, and the act allowed to do that is
    the per-cohorte « Dépublier », which names what *that* cohorte costs and asks twice. Same rule as
    `AllowOverCapacity` no longer waiving admissibility, and as `EmptyAllYearGroupsCommand` carrying
    no `DropAffectations`.
  - **Skip rather than refuse the batch**, which is the design decision worth keeping: refusing
    everything because one rotation started makes the button useless from the moment it is most
    needed — mid-year, when the point is to undo the hundred that have *not* begun. The report then
    carries what was left alone (`CohortsSkippedUnderway`, `PeriodsUnderway`, `EvaluationsAtRisk`,
    `AttendanceDaysAtRisk`, plus the heaviest few by name), which is the aggregate that had to be
    designed before the per-cohorte sentences could be replaced.
  - ⚠ **`CohortsUnpublished == 0` has two causes** — nothing was published, or everything has begun —
    so `NothingWasPublished` keeps them apart. A bare zero reads as a button that did nothing.
  - ⚠ **« Underway » is read exactly as the per-cohorte command reads it** (a started période, a mark,
    a day of attendance). A stricter bulk test would skip cohortes the single button undoes without
    complaint, and two acts destroying the same rows must not disagree about which rows they are.
  - **The toll is one grouped query over the *périodes*, keyed by cohorte** — grouping over the
    cohortes and folding `Attendance.Count` inside a second aggregate is the shape Npgsql refuses.
    Pinned by `SqlTranslationTests`.

## Read every cohorte's candidates once, not once per cohorte
`StudentAffectationService.AssignAsync` issued **one `Registrations` query per cohorte**. The macro
plan walks that loop once per concurrency block, so a single « Générer le plan » on a promotion of 105
rosters over seven stages was ~700 round trips. It is now one read for the call, keyed on
**(roster, niveau) together**.

- ⚠ **The pair is the guard.** Keyed on either half alone, a student registered in this roster at
  another level is affected to a stage he does not owe — the same trap as filtering by level and year
  with two independent `Any`s. Measured server-side on the live base: 92 ms of loop against 4 ms
  batched for one stage, before EF and the network are counted.
- **A long write still needs to say it is long.** With the round trips gone the run is seconds rather
  than tens of them, and the rotation-cycle page states what is being written and that interrupting
  costs the run but damages nothing — which is only truthful because of
  `ExecuteAtomicallyAsync` — see [`operations.md`](operations.md).

## Publishing never lands on top of a stage already served
⚠ **An assignment that already holds any `ServicePeriod` is skipped**, and the count is reported
(`SkippedAlreadyServed`). Measured 2026-08-14: every one of the 706 5MED assignments of 2025-2026 carries
an imported period per stage, while `IsPublishedAsync` only counts *grid-linked* ones — so publishing the
new répartition would have given each student a second set for the same stage, averaged into the note and
waited on by the lifecycle. Publication materialises a plan; it never re-materialises a past.

Filtered per **assignment**, not per cohort: a cohort routinely mixes students with the stage behind them
(repeaters, délocalisés) and students without, and the latter still need their schedule.

### ⚠ …et la publication est auditée depuis le 11/09/2026, sa dépublication l'étant depuis la phase 20
Le registre tenait le *défaire* sans le *faire*, sur l'acte qui crée les `ServicePeriod` — tout ce que
les chefs notent et tout ce que les présences visent. `COHORT_SCHEDULE_PUBLISHED` et
`STAGE_SCHEDULE_PUBLISHED`, avec la portée visée et **`allowOverCapacity`** : passer outre un service
qui a déclaré refuser d'être dépassé est un geste posé sciemment contre ce refus, et une publication
forcée était jusqu'ici indiscernable d'une publication ordinaire. ⚠ Le `SaveChanges` du handler est
**inconditionnel** là où le publisher n'écrit que s'il a des périodes à poser : « Publier » rejoué sur
un stage déjà publié — l'acte le plus banal d'une campagne — n'aurait sinon rien laissé.
[`docs/audit-calendar.md`](audit-calendar.md) ; `PublishScheduleAuditTests`.

## ⚠ Nothing declares that two stages share a period — the axis is derived
`StageSlot` is keyed `(StageId, AcademicYearId, PeriodNumber)`, so Médecine P1 and Chirurgie P1 are
independent rows with independent dates. No constraint ties them, and neither guard notices a drift:
`SlotOverlapGuard` is per-stage (which is what makes the crossover authorable), and
`GroupScheduleConflictGuard` only fires on a group actually double-booked, which a crossover never is.

- ⚠ **A small drift is more dangerous than a large one.** Where one window strictly contains another,
  `PeriodAxis` treats the outer as a composite and drops it — absorbing the mistake without trace. A
  partial overlap at least shows up as an extra column with hatched holes.
- `PeriodAxisDiagnostics` reports period numbers whose stages disagree, surfaced on the répartition
  response. It **cannot** be an error: Med6 legitimately has Chirurgie's P1 at two months and ANES REA's
  at one. Telling those apart is the human's job; showing them is ours.
- Using `RotationCycle` avoids the class entirely — the block's stages get one set of windows written
  once, so they cannot drift.
- **The axis is built from the windows the level *declares*, not from the ones something sits in** —
  `declaredSlots ∪ cells`, so a period authored but not yet arranged still gets its column and its
  hatched holes. Built from the cells alone it vanished, and an empty table was the only thing an
  admin saw after applying a rotation cycle: indistinguishable from an apply that failed.
  - ⚠ **An empty répartition has two causes and they call for opposite acts** — no periods (author an
    axis) or periods nobody is in (arrange). `RepartitionSummary.DeclaredSlotCount` is what separates
    them; `RowCount` alone collapses them. Same shape of mistake as widening on an omitted year: one
    state standing in for two.
  - The cells' own windows are unioned in rather than assumed to be a subset — a cell is tied to the
    level through its *cohort*, so a slot reached via another stage would otherwise take its column,
    and its cells, out of the table entirely.

## 📋 Un modèle de planification par niveau (demandé le 10/09/2026, basse priorité)

« Les groupes 1-2 passent en P1 au service S1, en P2 à S2… » : le circuit d'une promotion nommé une
fois, puis appliqué ; et la réciproque, **enregistrer comme modèle** une répartition qu'on vient de
générer. ⚠ **Rien n'est implémenté** : `HANDOFF.md` **0bb**, `PHASES.md` §27.4.

**La forme, et c'est la seule chose vraiment décidée ici : un modèle est la grille *sans ses dates et
sans ses rosters*** — (partition, `PeriodNumber`, `ServiceId`) par stage. Ce qui reste fixe d'une année
sur l'autre est le **nombre de colonnes** (P1, P2… — *T* = Σ*k*ₛ) ; les dates et les étudiants changent
chaque année. L'appliquer, c'est écrire des `CohortSlotAssignment` sur les cohortes de l'année contre
ses `StageSlot` ; l'enregistrer, c'est les relire.

- ⚠ **C'est l'axe qui rend deux années comparables**, et rien d'autre. Un modèle ne va qu'à une
  promotion de même *T* **et** de même jeu de stages — or le CNPN peut avoir bougé entre-temps, et un
  stage exigé sans créneau est dû et jamais servi. Donc un contrôle de compatibilité qui **nomme
  l'écart**, jamais une écriture partielle.
- ⚠ **Une cellule venue d'un modèle est une décision humaine → `CellSource.Pinned`.** Écrite
  `Arranged`, elle est détruite par la première « auto-répartir ce stage » sous un `Assigned = N`
  parfaitement normal — c'est exactement le défaut que la section précédente a coûté.
- ⚠ **La `PartitionStrategy` voyage avec le modèle.** Le modèle dit « partition A » ; l'année nomme
  les siennes en `Interleaved` ou `Contiguous`, et les groupes qu'un même nom désigne ne sont alors pas
  les mêmes. Sans la stratégie, le même modèle se lit comme un autre plan.
- ⚠ **Appliquer est un aperçu et des refus, comme l'arrangeur** : un service du modèle peut avoir
  disparu, être tenu hors rotation (`StageAllowedService.PlacementMode = Reserved`, dont la capacité
  part avec lui) ou dépasser le quota de la promotion.
- **Pourquoi basse priorité, et l'utilisateur l'a dit** : c'est un confort par-dessus un chemin qui
  marche déjà — poser l'axe, répartir, épingler — pas un manque.
