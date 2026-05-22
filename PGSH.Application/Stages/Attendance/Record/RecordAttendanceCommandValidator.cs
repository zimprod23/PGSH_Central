using FluentValidation;

namespace PGSH.Application.Stages.Attendance.Record;

public sealed class RecordAttendanceCommandValidator : AbstractValidator<RecordAttendanceCommand>
{
    public RecordAttendanceCommandValidator()
    {
        RuleFor(x => x.ServicePeriodId).NotEmpty();
        RuleFor(x => x.Date).NotEmpty();
        RuleFor(x => x.Status).IsInEnum();
    }
}
