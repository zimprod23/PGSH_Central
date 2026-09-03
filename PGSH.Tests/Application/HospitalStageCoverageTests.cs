using FluentAssertions;
using PGSH.Application.Hospitals.Coverage;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// « Cet hôpital peut-il accueillir toute la rotation de cette promotion ? » — asked before the
/// placement is promised.
///
/// <para>The case it was written for, measured on the live catalogue 2026-09-03: the Hôpital
/// Militaire Mohammed V carries a service for all six 6ᵉ année stages, and for six of the seven 5ᵉ
/// année ones — <b>Santé Publique authorises a single service and it is elsewhere</b>. Without this
/// read that one row is discovered at the sixth cell, after somebody has already said yes.</para>
/// </summary>
public class HospitalStageCoverageTests
{
    private const int Militaire = 2;
    private const int ChirurgieId = 2;
    private const int ImmersionId = 3;
    private const int OtherPromotionStageId = 4;

    private const int CivilService = 1;
    private const int MilitaryService = 2;
    private const int SantePubliqueService = 3;

    private static GetHospitalStageCoverageQueryHandler Handler(ApplicationDbContext db) => new(db);

    /// <summary>
    /// Three stages covering the three verdicts, plus a fourth on another promotion that must not
    /// appear at all.
    /// </summary>
    private static async Task SeedAsync(ApplicationDbContext db)
    {
        var cardio = db.SeedCatalog();
        var chirurgie = db.SeedStage(ChirurgieId, "Chirurgie");
        db.SeedStage(ImmersionId, "Stage d'immersion");

        db.SeedLevel(99, "Sixième année", 6);
        var otherPromotion = db.SeedStage(OtherPromotionStageId, "Anesthésie", levelId: 99);

        db.SeedHospital(Militaire, "Hôpital Militaire Mohammed V");

        var civil = db.SeedService(CivilService, "Cardiologie");
        var military = db.SeedService(MilitaryService, "Cardiologie (militaire)", hospitalId: Militaire);
        var santePublique = db.SeedService(SantePubliqueService, "Santé Publique");

        db.Allow(cardio, civil, military);

        // The Santé Publique shape: authorised, exactly one service, and not at this hospital.
        db.Allow(chirurgie, santePublique);

        // The immersion stage authorises nothing — which is openness, not exclusion.
        db.Allow(otherPromotion, military);

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task It_names_the_services_the_hospital_offers_for_each_stage()
    {
        await using var db = TestHarness.NewContext(nameof(
            It_names_the_services_the_hospital_offers_for_each_stage));
        await SeedAsync(db);

        var result = await Handler(db).Handle(
            new GetHospitalStageCoverageQuery(Militaire, TestHarness.LevelId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.HospitalName.Should().Be("Hôpital Militaire Mohammed V");
        result.Value.StageCount.Should().Be(3);

        var cardio = result.Value.Stages.Single(s => s.StageId == TestHarness.StageId);
        cardio.Coverage.Should().Be(StageHospitalCoverage.Covered);
        cardio.AllowedServiceCount.Should().Be(2);
        cardio.ServicesAtHospitalCount.Should().Be(1);
        cardio.ServicesAtHospital.Select(s => s.ServiceId).Should().Equal(MilitaryService);
    }

    /// <summary>
    /// ⚠ <b>The distinction the whole read turns on.</b> One stage authorises services and none is
    /// here — go elsewhere, or authorise one. The other authorises none at all — and an unenforced
    /// whitelist means every service is allowed, so that is a list nobody has saisi, not a refusal.
    /// Reported as one number they would send the user to solve the wrong problem.
    /// </summary>
    [Fact]
    public async Task An_unauthored_list_is_counted_apart_from_a_hospital_that_is_excluded()
    {
        await using var db = TestHarness.NewContext(nameof(
            An_unauthored_list_is_counted_apart_from_a_hospital_that_is_excluded));
        await SeedAsync(db);

        var result = await Handler(db).Handle(
            new GetHospitalStageCoverageQuery(Militaire, TestHarness.LevelId), default);

        result.Value.CoveredStageCount.Should().Be(1);
        result.Value.UnauthoredStageCount.Should().Be(1);

        result.Value.Stages.Single(s => s.StageId == ChirurgieId)
            .Coverage.Should().Be(StageHospitalCoverage.NotAtThisHospital);

        var immersion = result.Value.Stages.Single(s => s.StageId == ImmersionId);
        immersion.Coverage.Should().Be(StageHospitalCoverage.NoServicesAuthored);
        immersion.AllowedServiceCount.Should().Be(0);
        immersion.ServicesAtHospital.Should().BeEmpty();
    }

    /// <summary>
    /// Coverage is a fact about a hospital <i>and</i> a promotion. Another promotion's stage is
    /// covered by this hospital and is still none of this answer's business.
    /// </summary>
    [Fact]
    public async Task Another_promotions_stage_is_not_in_the_answer()
    {
        await using var db = TestHarness.NewContext(nameof(Another_promotions_stage_is_not_in_the_answer));
        await SeedAsync(db);

        var result = await Handler(db).Handle(
            new GetHospitalStageCoverageQuery(Militaire, TestHarness.LevelId), default);

        result.Value.Stages.Should().NotContain(s => s.StageId == OtherPromotionStageId);

        var sixth = await Handler(db).Handle(new GetHospitalStageCoverageQuery(Militaire, 99), default);
        sixth.Value.StageCount.Should().Be(1);
        sixth.Value.CoveredStageCount.Should().Be(1);
    }

    [Fact]
    public async Task An_unknown_hospital_is_refused()
    {
        await using var db = TestHarness.NewContext(nameof(An_unknown_hospital_is_refused));
        await SeedAsync(db);

        var result = await Handler(db).Handle(
            new GetHospitalStageCoverageQuery(4242, TestHarness.LevelId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Hospitals.NotFound");
    }

    [Fact]
    public async Task An_unknown_promotion_is_refused()
    {
        await using var db = TestHarness.NewContext(nameof(An_unknown_promotion_is_refused));
        await SeedAsync(db);

        var result = await Handler(db).Handle(
            new GetHospitalStageCoverageQuery(Militaire, 4242), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Levels.NotFound");
    }
}
