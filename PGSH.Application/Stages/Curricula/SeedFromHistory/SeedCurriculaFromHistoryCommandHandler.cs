using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Employees.MyServices;
using PGSH.Application.Stages.Cnpn;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Curricula.SeedFromHistory;

internal sealed class SeedCurriculaFromHistoryCommandHandler(
    IApplicationDbContext dbContext,
    CnpnAssignment assignment,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<SeedCurriculaFromHistoryCommand, CurriculumSeedReport>
{
    public async Task<Result<CurriculumSeedReport>> Handle(
        SeedCurriculaFromHistoryCommand request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(StageErrors.AdministrativeOnly);
        if (access.IsFailure)
            return Result.Failure<CurriculumSeedReport>(access.Error);

        // Derivation lives in the reconstructor so the migration tooling can run the same rule
        // without an HTTP identity; only the authorisation belongs here.
        return await new CurriculumHistoryReconstructor(dbContext, assignment)
            .ReconstructAsync(request.DryRun, cancellationToken);
    }
}
