using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Students.Registrations.Delete;

public sealed record DeleteRegistrationCommand(Guid RegistrationId, Guid StudentId) : ICommand;