using FluentAssertions;
using PGSH.Application.Stages.InternshipAssignments.Fiche;
using PGSH.Application.Employees.MyServices;
using PGSH.Domain.Stages;
using Xunit;

namespace PGSH.Tests.Application;

// The fiche de validation certifies a passed stage, so the handler must refuse to produce it until
// the assignment is both validated by the marks and ratified by the administration. The full set of
// gate cases lives in StudentStageRecordTests; these two cover the bare refusals.
public class FicheDeValidationHandlerTests
{
    [Fact]
    public async Task Returns_not_found_for_an_unknown_assignment()
    {
        await using var db = TestHarness.NewContext("fiche-unknown");

        var result = await new GetFicheDeValidationQueryHandler(db, db.AdminAuthorizer())
            .Handle(new GetFicheDeValidationQuery(Guid.NewGuid()), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("InternshipAssignments.NotFound");
    }

    [Fact]
    public async Task Refuses_the_fiche_while_the_stage_is_not_validated()
    {
        await using var db = TestHarness.NewContext("fiche-unvalidated");
        // Seeded through the harness rather than as a bare row: an assignment always hangs off a
        // registration, and the read scoping resolves the owning student through it.
        var stage = db.SeedCatalog();
        var cohort = db.SeedCohort(stage, 10, "Groupe 10");
        var registration = db.SeedRegistration("Omar", "Tazi", cohort.AcademicGroup);
        var assignment = db.SeedAssignment(registration, cohort);
        await db.SaveChangesAsync();

        assignment.Result.Should().Be(StageAssignmentResult.NonÉvalué);

        var result = await new GetFicheDeValidationQueryHandler(db, db.AdminAuthorizer())
            .Handle(new GetFicheDeValidationQuery(assignment.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ServiceEvaluations.FicheNotAvailable");
    }
}
