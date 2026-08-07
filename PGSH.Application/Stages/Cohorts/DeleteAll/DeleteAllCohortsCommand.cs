using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Cohorts.DeleteAll;

public sealed record DeleteAllCohortsCommand(int StageId, int? AcademicYearId = null) : ICommand<int>;
