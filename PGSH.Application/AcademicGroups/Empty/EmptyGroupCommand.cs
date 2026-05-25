using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.AcademicGroups.Empty;

public sealed record EmptyGroupCommand(int GroupId) : ICommand<int>;
