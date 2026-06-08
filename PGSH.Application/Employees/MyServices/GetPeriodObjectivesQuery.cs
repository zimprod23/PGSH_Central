using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Employees.MyServices;

/// <summary>
/// The evaluation criteria (stage objectives) for a service period, so the chef can
/// score each one. Chef-scoped: only the chef of the period's service (or an admin)
/// may read them.
/// </summary>
public sealed record GetPeriodObjectivesQuery(Guid PeriodId)
    : IQuery<IReadOnlyList<PeriodObjectiveResponse>>;

public sealed record PeriodObjectiveResponse(
    int     Id,
    string  Label,
    string? Description,
    int     Weight,
    bool    IsMandatory);
