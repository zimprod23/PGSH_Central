using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.InternshipAssignments.Reject;

public sealed record RejectAssignmentCommand(Guid AssignmentId) : ICommand;
