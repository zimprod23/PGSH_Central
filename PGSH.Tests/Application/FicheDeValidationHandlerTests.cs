using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Stages.InternshipAssignments.Fiche;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

// The fiche de validation certifies a passed stage, so the handler must refuse to produce it until
// the assignment is validated (which only happens once every period is evaluated and all pass).
public class FicheDeValidationHandlerTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"fiche-{Guid.NewGuid()}")
            .Options);

    [Fact]
    public async Task Returns_not_found_for_an_unknown_assignment()
    {
        await using var db = NewContext();

        var result = await new GetFicheDeValidationQueryHandler(db)
            .Handle(new GetFicheDeValidationQuery(Guid.NewGuid()), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("InternshipAssignments.NotFound");
    }

    [Fact]
    public async Task Refuses_the_fiche_while_the_stage_is_not_validated()
    {
        await using var db = NewContext();
        db.InternshipAssignments.Add(new InternshipAssignment { Id = Guid.NewGuid() });  // Result defaults to NonÉvalué
        await db.SaveChangesAsync();
        var storedId = (await db.InternshipAssignments.AsNoTracking().FirstAsync()).Id;

        var result = await new GetFicheDeValidationQueryHandler(db)
            .Handle(new GetFicheDeValidationQuery(storedId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ServiceEvaluations.FicheNotAvailable");
    }
}
