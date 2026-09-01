using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Curricula.SeedFromHistory;

internal sealed class SeedCurriculaFromHistoryCommandHandler(
    CurriculumHistoryReconstructor reconstructor,
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
        // without an HTTP identity; only the authorisation belongs here. Injected rather than
        // new'd: the handler was reaching past its own collaborator to build one, which meant it
        // had to take that collaborator's dependencies as its own.
        return await reconstructor.ReconstructAsync(request.DryRun, cancellationToken);
    }
}
