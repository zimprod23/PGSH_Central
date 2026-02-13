using FluentValidation;

namespace PGSH.Application.Stages.Delete
{
    public class DeleteStageCommandValidator: AbstractValidator<DeleteStageCommand>
    {
        public DeleteStageCommandValidator() 
        { 
            RuleFor(c => c.StageId).NotEmpty();
        }
    }
}
