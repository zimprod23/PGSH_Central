using PGSH.Domain.Common.Utils;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;

namespace PGSH.LegacyImport.Mapping;

/// <summary>
/// The complete graph the import would write, in the order it has to be written: reference data first
/// so its identity keys exist, then people, then the stages they served.
/// </summary>
public sealed record LegacyImportPlan(
    Center Center,
    IReadOnlyList<Hospital> Hospitals,
    IReadOnlyList<Service> Services,
    IReadOnlyList<Level> Levels,
    IReadOnlyList<Stage> Stages,
    IReadOnlyList<AcademicYear> AcademicYears,
    IReadOnlyList<AcademicGroup> AcademicGroups,
    IReadOnlyList<Student> Students,
    IReadOnlyList<Registration> Registrations,
    IReadOnlyList<Cohort> Cohorts,
    IReadOnlyList<InternshipAssignment> Assignments,
    LegacyImportReport Report,
    /// <summary>
    /// Hospitals whose city was attributed rather than read out of the legacy string. Neither city nor
    /// service type exists in the source, so these are the rows <c>--review</c> asks a human to confirm.
    /// </summary>
    IReadOnlySet<string> HospitalsWithAssumedCity);

public sealed record LegacyImportReport(
    int Centers,
    int Hospitals,
    int Services,
    int Levels,
    int Stages,
    int AcademicYears,
    int AcademicGroups,
    int Students,
    int Registrations,
    int Cohorts,
    int Assignments,
    int ServicePeriods,
    int Evaluations,
    int SyntheticCne,
    IReadOnlyList<LegacyImportProblem> Problems)
{
    public IEnumerable<(LegacyImportProblemKind Kind, int Count)> ProblemsByKind() =>
        Problems.GroupBy(p => p.Kind).Select(g => (g.Key, g.Count())).OrderByDescending(x => x.Item2);
}

public sealed record LegacyImportProblem(LegacyImportProblemKind Kind, string Message);

public enum LegacyImportProblemKind
{
    /// <summary>The service string named no hospital; it hangs off the catch-all establishment.</summary>
    ServiceWithoutHospital,

    /// <summary>A `CodeN` outside the known set — the row is skipped rather than misfiled.</summary>
    UnknownLevelCode,

    UnknownAcademicYear,
    UnknownStudent,

    /// <summary>No `Inscription` at all, so the programme could not be derived.</summary>
    StudentWithoutRegistration,

    /// <summary>Fewer than two dates in PER1/PER2 — no window can be built, so the rotation is dropped.</summary>
    UnreadablePeriod,

    /// <summary>An interrupted rotation, carried over as several periods.</summary>
    SplitPeriod,

    /// <summary>
    /// An odd number of dates in PER1/PER2: the trailing one describes a window with no end, so it is
    /// dropped. Reported rather than silently discarded — the parser promises never to guess an end.
    /// </summary>
    DanglingPeriodDate,

    /// <summary>
    /// One student holding two registrations in the same academic year — impossible in PGSH, where
    /// <c>IX_Registration_Student_Year</c> is unique. The richer row is kept, the other reported.
    /// </summary>
    DuplicateRegistration,
}
