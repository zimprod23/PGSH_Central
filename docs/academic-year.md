# Managing the academic year itself

> Read before creating, correcting, deleting, or moving the current-year flag on an `AcademicYear`.
>
> Split out of `CLAUDE.md` on 2026-09-06 — the text is unchanged. See [`CLAUDE.md`](../CLAUDE.md) for the always-on rules and the map of these documents.

## Managing the year itself — set it, correct it, remove it
`AcademicYears/Manage/`. Three acts a year needed and did not have, and each is guarded by something
the year cannot see about itself.

- **`SetCurrentAcademicYearCommand`** (`POST academic-years/{id}/current`) is deliberately its own
  route, not a field on the update. A year is normally created months before it becomes current, so
  folding the two together left « créer une année » as the only way to move the flag — the flag moved
  as a side effect of something else.
- ⚠ **Demotion is saved as its own statement, before the promotion.** `IX_AcademicYear_IsCurrent` is
  unique and filtered and Postgres checks it at the end of each statement, so two flagged rows is a
  constraint violation, not a state EF can order its way out of. One `SaveChanges` emitting both
  updates leaves that order to EF, which has no reason to pick the safe one.
- ⚠ **…and it goes through the aggregate, not `ExecuteUpdateAsync`.** That helper is *unsupported by
  the in-memory provider*, so the demote `CreateAcademicYearCommandHandler` performed could never be
  reached by a test — the one part of the handler that can leave the base with no current year at all.
  The exposure is identical either way; only the coverage differs.
- **Deleting is refused while anything year-constituted exists, and the refusal names every count** —
  registrations, périodes, cohortes, dérogations, règles d'effectivité, CNPN whose intake year it is.
  Counted together rather than short-circuited: a user who clears the registrations only to be told
  about the périodes has been sent round the loop twice.
  - ⚠ **The schema makes an ungated delete destructive in two different ways, neither of which
    announces itself.** Measured 2026-08-24: five FKs are `RESTRICT` (a raw violation, i.e. a 500 with
    nothing actionable in it) while **`AcademicGroups.AcademicYearId` is `CASCADE`** — the rosters go
    silently. The chain stops there only because `Cohorts` and `Registrations` restrict on the roster.
    Same bargain as `DeleteCnpnVersionCommand`: what may cascade is exactly what has no meaning once
    the thing that constituted it is gone, and an empty roster of a year nobody registered in records
    nothing. `RostersRemoved` is reported so the confirmation can name it.
  - ⚠ **The current year is never deleted.** Every unscoped handler resolves through it, so removing it
    leaves no answer to « quelle année ? ». Designating another one first is the reversible act.
- ⚠ **Two years may not share a day** — `AcademicYearCalendarGuard`, shared by create and update
  because a rule enforced on one path only is a row that can be created and then never saved.
  `ServiceOccupancyCalculator` bounds a year by its **dates** rather than by `AcademicYearId`
  (deliberately — a slot stamped with the wrong year but dated inside this one should surface), and
  that is only safe while the two cannot disagree. Overlapping years count every slot in the overlap
  twice against a service's load, which is the number the publish guard refuses on. The rule was never
  enforced; the base happens to satisfy it.
- **Moving a year's span does not move the périodes laid on it**, so narrowing one can leave its own
  slots outside it. Reported (`SlotsOutsideSpan`), not refused — a year is routinely corrected while
  its axis is a draft — and counted **before** the write, or the slots that fell out become
  indistinguishable from slots that were always elsewhere. Same shape as `UpdateHolidayCommand`.
- **The entity carries `init` accessors over explicit backing fields**, not plain setters: an object
  initialiser — how the seeder, the importer and the tests build a year — still works, while nothing
  can change a year *afterwards* except `MakeCurrent` / `Relinquish` / `Rename` / `Reschedule`. The
  compiler found the one place that was doing it the other way (`LegacyImportPlanner` flipping
  `IsCurrent` on the last year), which is exactly the write the guard exists for.
