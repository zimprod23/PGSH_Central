using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicYears.Create;

internal sealed class CreateAcademicYearCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<CreateAcademicYearCommand, int>
{
    public async Task<Result<int>> Handle(CreateAcademicYearCommand request, CancellationToken ct)
    {
        bool labelExists = await dbContext.AcademicYears.AnyAsync(y => y.Label == request.Label, ct);
        if (labelExists)
            return Result.Failure<int>(Error.Conflict("AcademicYear.Duplicate", $"An academic year with label '{request.Label}' already exists."));

        if (request.IsCurrent)
            await dbContext.AcademicYears
                .Where(y => y.IsCurrent)
                .ExecuteUpdateAsync(s => s.SetProperty(y => y.IsCurrent, false), ct);

        var year = new AcademicYear
        {
            Label = request.Label,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            IsCurrent = request.IsCurrent,
        };

        dbContext.AcademicYears.Add(year);
        await dbContext.SaveChangesAsync(ct);
        return year.Id;
    }
}
