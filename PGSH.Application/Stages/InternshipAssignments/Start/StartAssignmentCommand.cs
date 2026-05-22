using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.InternshipAssignments.Start;

public sealed record StartAssignmentCommand(Guid AssignmentId) : ICommand;
