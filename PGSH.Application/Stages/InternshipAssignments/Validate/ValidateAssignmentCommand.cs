using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.InternshipAssignments.Validate;

public sealed record ValidateAssignmentCommand(Guid AssignmentId) : ICommand;
