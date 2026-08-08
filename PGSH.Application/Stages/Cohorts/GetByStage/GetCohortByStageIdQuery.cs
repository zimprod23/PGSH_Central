using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.Cohorts.GetById;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cohorts.GetByStage;

/// <summary>
/// The cohorts of one stage, scoped to an academic year and paged.
///
/// A cohort exists per (stage, group), and groups are per year — so a stage accumulates cohorts for
/// every year it has ever run. On the imported data "Chirurgie" has 681 of them, of which only 80
/// belong to the current year. Without <paramref name="AcademicYearId"/> this returned all 681, each
/// carrying three correlated counts, which is what made the cohort screen unusable.
/// </summary>
public sealed record GetCohortsByStageQuery(
    int     StageId,
    int?    AcademicYearId = null,
    int     PageNumber     = 1,
    int     PageSize       = 50,
    string? SearchTerm     = null) : IQuery<PaginatedResponse<CohortResponse>>;
