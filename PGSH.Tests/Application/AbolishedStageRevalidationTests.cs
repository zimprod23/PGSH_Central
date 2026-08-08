using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Stages.Curricula.Compare;
using PGSH.Application.Stages.InternshipAssignments.GetDossier;
using PGSH.Application.Stages.Revalidation;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// The whole abolished-stage scenario, end to end, on the shape it really has in the data:
/// <c>Pharmacie Clinique 3</c> ran 2019-20 → 2022-23 and was then dropped from the CNPN while
/// Clinique 1 and 2 carried on.
///
/// <para>
/// Policy settled with the user: <b>the student still serves the abolished stage.</b> Removing it from
/// a later CNPN releases <em>new</em> students from it; it does not erase an obligation already
/// incurred. There is no waiver and no substitution — which is exactly why <see cref="Stage"/> is a
/// timeless catalogue entry rather than something with an expiry date: the stage record survives its
/// removal from the curriculum, so it can still be served.
/// </para>
/// </summary>
public class AbolishedStageRevalidationTests
{
    private const int PharmacieY5 = TestHarness.LevelId;
    private const int Clinique1 = TestHarness.StageId;   // 1 — still current
    private const int Clinique3 = 83;                    // abolished after 2022-2023
    private const int OfficinePharmacy = 705;

    private sealed record Scenario(
        Registration Failed,
        Registration Current,
        Cohort RetakeCohort,
        Service OriginalService);

    private static Scenario SeedSara(ApplicationDbContext db)
    {
        var clinique1 = db.SeedCatalog();
        clinique1.Name = "Pharmacie Clinique 1";
        var clinique3 = db.SeedStage(Clinique3, "Pharmacie Clinique 3", coefficient: 2);

        db.SeedAcademicYear(TestHarness.PreviousYearId, "2022-2023",
            new DateOnly(2022, 9, 1), new DateOnly(2023, 8, 31));

        var pharmacy = db.SeedService(OfficinePharmacy, "Hôp.Spécialités: Pharmacie");

        // 2022-2023 — the CNPN still required Clinique 3, and Sara failed it there.
        var oldCohort = db.SeedCohort(clinique3, 20, "Groupe 20", TestHarness.PreviousYearId);
        var failed = db.SeedRegistration("Sara", "Alami", oldCohort.AcademicGroup,
            academicYearId: TestHarness.PreviousYearId);
        db.SeedGradedAssignment(failed, oldCohort, pharmacy, mark: 7m, from: new DateOnly(2022, 10, 1));

        RecordCurriculum(db, 1, TestHarness.OldCnpnId, Clinique1, Clinique3);
        RecordCurriculum(db, 2, TestHarness.NewCnpnId, Clinique1);   // Clinique 3 abolished

        // 2025-2026 — Sara is registered again; her group has no Clinique 3 cohort, because no group
        // runs a stage the CNPN no longer contains. The retake gets its own.
        var currentGroup = new AcademicGroup
        {
            Id = 60, Label = "Groupe 60", GroupNumber = 60, AcademicYearId = TestHarness.CurrentYearId,
        };
        db.AcademicGroups.Add(currentGroup);
        var current = db.SeedRegistration("Sara", "Alami", currentGroup);
        current.StudentId = failed.StudentId;
        current.Student   = failed.Student;

        var retakeCohort = new Cohort
        {
            Id = 61, Label = "Pharmacie Clinique 3 — rattrapage",
            StageId = Clinique3, Stage = clinique3,
            AcademicGroupId = currentGroup.Id, AcademicGroup = currentGroup,
        };
        db.Cohorts.Add(retakeCohort);

        return new Scenario(failed, current, retakeCohort, pharmacy);
    }

    private static void RecordCurriculum(ApplicationDbContext db, int id, int versionId, params int[] stageIds)
    {
        var curriculum = new Curriculum { Id = id, LevelId = PharmacieY5, CnpnVersionId = versionId };
        foreach (int stageId in stageIds) curriculum.AddStage(stageId, 2, 42);
        db.Curriculums.Add(curriculum);
    }

    [Fact]
    public async Task The_comparison_shows_the_administration_the_stage_was_abolished()
    {
        await using var db = TestHarness.NewContext("abolished-compare");
        SeedSara(db);
        await db.SaveChangesAsync();

        var result = await new CompareCurriculaQueryHandler(db).Handle(
            new CompareCurriculaQuery(PharmacieY5, TestHarness.OldCnpnId, TestHarness.NewCnpnId),
            default);

        result.Value.Entries.Should().ContainSingle(e => e.Change == CurriculumChange.Removed)
            .Which.StageId.Should().Be(Clinique3);
    }

    [Fact]
    public async Task Sara_still_serves_the_abolished_stage_in_the_service_where_she_failed_it()
    {
        await using var db = TestHarness.NewContext("abolished-serve");
        var s = SeedSara(db);
        await db.SaveChangesAsync();

        var result = await new RevalidateStageCommandHandler(db, db.AdminAuthorizer()).Handle(
            new RevalidateStageCommand(
                s.Current.Id, Clinique3,
                CohortId:  s.RetakeCohort.Id,
                StartDate: new DateOnly(2026, 2, 1),
                EndDate:   new DateOnly(2026, 3, 15),
                Reason:    "Rattrapage — stage supprimé du CNPN depuis 2023-2024",
                DemandeId: Guid.NewGuid()),
            default);

        result.IsSuccess.Should().BeTrue("removal from a later CNPN does not erase an obligation already incurred");

        var retake = await db.InternshipAssignments
            .Include(a => a.ServicePeriods)
            .FirstAsync(a => a.Id == result.Value);

        retake.RegistrationId.Should().Be(s.Current.Id);
        retake.Status.Should().Be(InternshipStatus.Planned);

        // Back to the service she failed it in, and outside the published schedule.
        var period = retake.ServicePeriods.Should().ContainSingle().Subject;
        period.ServiceId.Should().Be(s.OriginalService.Id);
        period.CohortSlotAssignmentId.Should().BeNull();
    }

    [Fact]
    public async Task The_original_failure_is_left_untouched_as_history()
    {
        await using var db = TestHarness.NewContext("abolished-history");
        var s = SeedSara(db);
        await db.SaveChangesAsync();

        await new RevalidateStageCommandHandler(db, db.AdminAuthorizer()).Handle(
            new RevalidateStageCommand(s.Current.Id, Clinique3, CohortId: s.RetakeCohort.Id), default);

        var original = await db.InternshipAssignments
            .FirstAsync(a => a.RegistrationId == s.Failed.Id);

        original.FinalScore.Should().Be(7m);
        original.Result.Should().Be(StageAssignmentResult.NonValidé);
    }

    [Fact]
    public async Task Once_the_retake_is_passed_the_stage_counts_as_acquired()
    {
        await using var db = TestHarness.NewContext("abolished-passed");
        var s = SeedSara(db);
        await db.SaveChangesAsync();

        var opened = await new RevalidateStageCommandHandler(db, db.AdminAuthorizer()).Handle(
            new RevalidateStageCommand(
                s.Current.Id, Clinique3,
                CohortId:  s.RetakeCohort.Id,
                StartDate: new DateOnly(2026, 2, 1),
                EndDate:   new DateOnly(2026, 3, 15)),
            default);

        // She serves it and passes.
        var retake = await db.InternshipAssignments
            .Include(a => a.ServicePeriods)
            .FirstAsync(a => a.Id == opened.Value);

        var period = retake.ServicePeriods.Single();
        retake.Start();
        retake.CompletePeriod(period.Id);
        retake.SubmitEvaluation(period.Id, new ServiceEvaluation
        {
            ServicePeriodId = period.Id, Mode = EvaluationMode.Numeric, TotalScore = 13m,
        });
        retake.Validate();
        await db.SaveChangesAsync();

        var dossier = await new GetStudentLevelDossierQueryHandler(db, db.AdminAuthorizer()).Handle(
            new GetStudentLevelDossierQuery(s.Current.StudentId, PharmacieY5), default);

        var clinique3 = dossier.Value.Stages.Single(x => x.StageId == Clinique3);
        clinique3.State.Should().Be(DossierStageState.Validated);
        clinique3.AttemptCount.Should().Be(2, "the failure and the retake are both on record");
        clinique3.BestScore.Should().Be(13m);
    }
}
