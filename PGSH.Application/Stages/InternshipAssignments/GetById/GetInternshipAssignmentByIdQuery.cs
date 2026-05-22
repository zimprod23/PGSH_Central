using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.InternshipAssignments.GetById;

public sealed record GetInternshipAssignmentByIdQuery(Guid Id) : IQuery<InternshipAssignmentResponse>;
