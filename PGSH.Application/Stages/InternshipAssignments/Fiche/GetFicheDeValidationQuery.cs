using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.InternshipAssignments.Fiche;

public sealed record GetFicheDeValidationQuery(Guid AssignmentId)
    : IQuery<FicheDeValidationResponse>;
