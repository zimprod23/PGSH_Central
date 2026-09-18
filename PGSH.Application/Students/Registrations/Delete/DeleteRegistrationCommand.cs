using FluentValidation;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Extensions;

namespace PGSH.Application.Students.Registrations.Delete;

/// <param name="StudentId">
/// Whose registration this is, restated by the caller so the delete can check it owns what it is
/// about to remove.
/// </param>
public sealed record DeleteRegistrationCommand(Guid RegistrationId, Guid? StudentId) : ICommand;

/// <summary>
/// ⚠ <b>This one is not a screen selector, and it is still fixed the same way.</b> A caller omitting
/// the owner is a broken client rather than a user with an empty field — but a bare 400 from the model
/// binder names nothing, pauses the process under a debugger, and is a poor answer even to a broken
/// client. The parameter stays required; what changes is that the refusal can be read.
/// </summary>
internal sealed class DeleteRegistrationCommandValidator : AbstractValidator<DeleteRegistrationCommand>
{
    public DeleteRegistrationCommandValidator() =>
        RuleFor(x => x.StudentId).IsARequiredReference(
            "L'identifiant de l'étudiant est obligatoire : une inscription ne se supprime qu'en "
            + "confirmant à qui elle appartient.");
}