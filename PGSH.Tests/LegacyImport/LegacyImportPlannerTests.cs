using FluentAssertions;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.LegacyImport.Legacy;
using PGSH.LegacyImport.Mapping;
using Xunit;

namespace PGSH.Tests.LegacyImport;

// The structural heart of the migration. Legacy `AffectStage` is at rotation grain with no row above
// it — the internship is implicit in (NUMINS, CODEST) — while PGSH has an assignment owning periods.
// Getting that fold wrong either splits one stage into several or merges several into one.
public class LegacyImportPlannerTests
{
    private const int NumIns = 250001;
    private const int Cardiologie = 7;      // a MED04 stage
    private const int Pediatrie = 3;        // another MED04 stage
    private const int ServiceA = 124;
    private const int ServiceB = 126;

    private static LegacyDatabase Source(
        IEnumerable<LegacyStageAssignment> rows,
        string levelCode = "MED04",
        int? groupNumber = 5) =>
        new(
            AcademicYears: [new LegacyAcademicYear("2024/2025", true)],
            Niveaux: [new LegacyNiveau("MED04", "Quatrième Année Médecine", "Médecine", 4)],
            Stages:
            [
                new LegacyStage(Cardiologie, "MED04", "Cardiologie", 1, 22),
                new LegacyStage(Pediatrie, "MED04", "Pédiatrie", 2, 44),
                new LegacyStage(64, "MED06", "MEDECINE", 2, 44),
            ],
            Services:
            [
                new LegacyService(ServiceA, "Hôp.IbnSina: Cardiologie A - Pr.R.Fellat"),
                new LegacyService(ServiceB, "HMIMV: Cardiologie - Pr.A.Benyass"),
            ],
            Students: [new LegacyStudent(6024248, "TAZI OMAR", "1100000099", null, "M", null, null, null, null, null, null)],
            Registrations: [new LegacyRegistration(NumIns, 6024248, "2024/2025", levelCode, groupNumber, "N", false)],
            StageAssignments: [.. rows]);

    private static LegacyStageAssignment Row(
        int codeSt, int codeS, string per1, string per2, decimal? note) =>
        new(NumIns, codeSt, codeS, per1, per2, note, "N");

    private static LegacyImportPlan Plan(LegacyDatabase source) => new LegacyImportPlanner().Plan(source);

    [Fact]
    public void Several_rotations_of_one_stage_fold_into_a_single_assignment()
    {
        // The same (NUMINS, CODEST) served across two services is ONE stage, not two.
        var plan = Plan(Source(
        [
            Row(Cardiologie, ServiceA, "13/01/2025", "12/02/2025", 10),
            Row(Cardiologie, ServiceB, "23/05/2025", "17/07/2025", 12),
        ]));

        plan.Assignments.Should().ContainSingle();
        plan.Assignments[0].ServicePeriods.Should().HaveCount(2);
    }

    [Fact]
    public void Different_stages_stay_separate_assignments()
    {
        var plan = Plan(Source(
        [
            Row(Cardiologie, ServiceA, "13/01/2025", "12/02/2025", 12),
            Row(Pediatrie, ServiceA, "24/03/2025", "22/04/2025", 14),
        ]));

        plan.Assignments.Should().HaveCount(2);
        plan.Assignments.Should().OnlyContain(a => a.ServicePeriods.Count == 1);
    }

    [Fact]
    public void A_mark_of_ten_or_more_validates_the_stage()
    {
        var plan = Plan(Source([Row(Cardiologie, ServiceA, "13/01/2025", "12/02/2025", 14)]));

        var assignment = plan.Assignments.Single();
        assignment.FinalScore.Should().Be(14m);
        assignment.Result.Should().Be(StageAssignmentResult.Validé);
        // Historical stages are ratified so they do not sit in the administration's worklist forever.
        assignment.Status.Should().Be(InternshipStatus.Validated);
    }

    [Fact]
    public void A_mark_below_ten_is_a_real_failure_not_a_missing_value()
    {
        // Settled with the user: Note < 10 is "not validated", so 0 is a genuine failing mark.
        var plan = Plan(Source([Row(Cardiologie, ServiceA, "13/01/2025", "12/02/2025", 0)]));

        var assignment = plan.Assignments.Single();
        assignment.FinalScore.Should().Be(0m);
        assignment.Result.Should().Be(StageAssignmentResult.NonValidé);
    }

    [Fact]
    public void The_minus_one_sentinel_means_never_graded()
    {
        var plan = Plan(Source([Row(Cardiologie, ServiceA, "13/01/2025", "12/02/2025", -1)]));

        var assignment = plan.Assignments.Single();
        assignment.ServicePeriods.Single().Evaluation.Should().BeNull();
        assignment.Result.Should().Be(StageAssignmentResult.NonÉvalué);
        assignment.Status.Should().Be(InternshipStatus.Completed);
        assignment.FinalScore.Should().BeNull();
    }

    [Fact]
    public void A_null_mark_is_treated_the_same_as_the_sentinel()
    {
        var plan = Plan(Source([Row(Cardiologie, ServiceA, "13/01/2025", "12/02/2025", null)]));

        plan.Assignments.Single().Result.Should().Be(StageAssignmentResult.NonÉvalué);
    }

    [Fact]
    public void One_failing_rotation_fails_the_whole_stage()
    {
        var plan = Plan(Source(
        [
            Row(Cardiologie, ServiceA, "13/01/2025", "12/02/2025", 16),
            Row(Cardiologie, ServiceB, "23/05/2025", "17/07/2025", 4),
        ]));

        var assignment = plan.Assignments.Single();
        assignment.FinalScore.Should().Be(10m);                       // mean of 16 and 4
        assignment.Result.Should().Be(StageAssignmentResult.NonValidé);  // but one period failed
    }

    [Fact]
    public void An_interrupted_rotation_becomes_two_periods_carrying_the_same_mark()
    {
        // The legacy row holds one note for the whole interrupted stage; leaving the second half
        // ungraded would stop the assignment ever reaching a verdict.
        var plan = Plan(Source(
            [Row(Cardiologie, ServiceA, "22/04/2019", "31/05/2019 & de: 25/06/2019 à:12/07/2019", 13)]));

        var assignment = plan.Assignments.Single();
        assignment.ServicePeriods.Should().HaveCount(2);
        assignment.ServicePeriods.Should().OnlyContain(p => p.Evaluation!.TotalScore == 13m);
        assignment.Result.Should().Be(StageAssignmentResult.Validé);

        plan.Report.Problems.Should().Contain(p => p.Kind == LegacyImportProblemKind.SplitPeriod);
    }

    [Fact]
    public void History_carries_no_planning_grid_so_periods_are_recorded_ad_hoc()
    {
        var plan = Plan(Source([Row(Cardiologie, ServiceA, "13/01/2025", "12/02/2025", 12)]));

        // Null is the schema's own meaning for "outside the published schedule" — which is what a
        // rotation served years before PGSH existed is.
        plan.Assignments.Single().ServicePeriods.Single().CohortSlotAssignmentId.Should().BeNull();
        plan.Stages.Should().NotBeEmpty();
    }

    [Fact]
    public void A_stage_from_another_level_is_still_imported()
    {
        // 275 rows are INM interns serving MED06 stages. A stage is not a criterion for failing the
        // year, so carrying one across levels is normal and must not be dropped.
        var plan = Plan(Source([Row(64, ServiceA, "13/01/2025", "12/02/2025", 12)]));

        plan.Assignments.Should().ContainSingle();
        plan.Report.Problems.Should().NotContain(p => p.Kind == LegacyImportProblemKind.UnknownLevelCode);
    }

    [Fact]
    public void A_cohort_is_created_per_stage_and_group()
    {
        var plan = Plan(Source(
        [
            Row(Cardiologie, ServiceA, "13/01/2025", "12/02/2025", 12),
            Row(Pediatrie, ServiceA, "24/03/2025", "22/04/2025", 12),
        ]));

        // Legacy has no cohort concept at all; it is derived from (stage, the registration's group).
        plan.Cohorts.Should().HaveCount(2);
        plan.Cohorts.Should().OnlyContain(c => c.AcademicGroup.GroupNumber == 5);
    }

    [Fact]
    public void A_registration_with_no_group_still_gets_one()
    {
        // 17,529 registrations carry no GROUPE_STG. AcademicGroupId is what the cohort hangs off, so
        // they land in a clearly-named bucket rather than being dropped.
        var plan = Plan(Source(
            [Row(Cardiologie, ServiceA, "13/01/2025", "12/02/2025", 12)], groupNumber: null));

        plan.AcademicGroups.Should().ContainSingle();
        plan.AcademicGroups[0].GroupNumber.Should().Be(0);
        plan.AcademicGroups[0].Label.Should().Be("Non réparti");
    }

    [Fact]
    public void One_group_number_in_two_promotions_is_two_rosters()
    {
        // ⚠ GROUPE_STG restarts at 1 for each promotion, so « Groupe 5 » of the 4th year and
        // « Groupe 5 » of the 6th year are different sets of students who merely share a number.
        // Folding them into one row is not a labelling slip: a roster is the unit
        // GroupScheduleConflictGuard forbids from being in two services at once, so the 4th year's
        // dates then refuse the 6th year's, and the second promotion planned comes out nearly empty.
        var source = new LegacyDatabase(
            AcademicYears: [new LegacyAcademicYear("2024/2025", true)],
            Niveaux:
            [
                new LegacyNiveau("MED04", "Quatrième Année Médecine", "Médecine", 4),
                new LegacyNiveau("MED06", "Sixième Année Médecine", "Médecine", 6),
            ],
            Stages: [new LegacyStage(Cardiologie, "MED04", "Cardiologie", 1, 22)],
            Services: [new LegacyService(ServiceA, "Hôp.IbnSina: Cardiologie A - Pr.R.Fellat")],
            Students:
            [
                new LegacyStudent(6024248, "TAZI OMAR", "1100000099", null, "M", null, null, null, null, null, null),
                new LegacyStudent(6024249, "ALAMI SARA", "1100000098", null, "F", null, null, null, null, null, null),
            ],
            Registrations:
            [
                new LegacyRegistration(NumIns,     6024248, "2024/2025", "MED04", 5, "N", false),
                new LegacyRegistration(NumIns + 1, 6024249, "2024/2025", "MED06", 5, "N", false),
            ],
            StageAssignments: []);

        var plan = new LegacyImportPlanner().Plan(source);

        plan.AcademicGroups.Should().HaveCount(2, "one number, two promotions");
        plan.AcademicGroups.Should().OnlyContain(g => g.GroupNumber == 5);
        plan.AcademicGroups.Select(g => g.Level!.Year).Should().BeEquivalentTo([4, 6]);

        // And each student is in their own promotion's roster, not in whichever was created first.
        plan.Registrations.Should().OnlyContain(r => r.AcademicGroup!.Level!.Year == r.Level.Year);
    }

    [Fact]
    public void The_no_group_bucket_stays_one_per_year_across_promotions()
    {
        // « Non réparti » belongs to no promotion by definition — splitting it per level would invent
        // rosters nobody is a member of.
        var source = new LegacyDatabase(
            AcademicYears: [new LegacyAcademicYear("2024/2025", true)],
            Niveaux:
            [
                new LegacyNiveau("MED04", "Quatrième Année Médecine", "Médecine", 4),
                new LegacyNiveau("MED06", "Sixième Année Médecine", "Médecine", 6),
            ],
            Stages: [new LegacyStage(Cardiologie, "MED04", "Cardiologie", 1, 22)],
            Services: [new LegacyService(ServiceA, "Hôp.IbnSina: Cardiologie A - Pr.R.Fellat")],
            Students:
            [
                new LegacyStudent(6024248, "TAZI OMAR", "1100000099", null, "M", null, null, null, null, null, null),
                new LegacyStudent(6024249, "ALAMI SARA", "1100000098", null, "F", null, null, null, null, null, null),
            ],
            Registrations:
            [
                new LegacyRegistration(NumIns,     6024248, "2024/2025", "MED04", null, "N", false),
                new LegacyRegistration(NumIns + 1, 6024249, "2024/2025", "MED06", null, "N", false),
            ],
            StageAssignments: []);

        var plan = new LegacyImportPlanner().Plan(source);

        plan.AcademicGroups.Should().ContainSingle();
        plan.AcademicGroups[0].Label.Should().Be("Non réparti");
        plan.AcademicGroups[0].Level.Should().BeNull();
    }

    [Fact]
    public void A_withdrawal_registration_is_imported_as_withdrawn()
    {
        var plan = Plan(Source(
            [Row(Cardiologie, ServiceA, "13/01/2025", "12/02/2025", 12)], levelCode: "MED00"));

        plan.Registrations.Single().Status.Should().Be(RegistrationStatus.Withdrawn);
    }

    [Fact]
    public void A_live_registration_is_active_because_the_year_outcome_is_unknowable_here()
    {
        // Whether the student passed the YEAR depends on subject marks, which live in the half of the
        // legacy database that is out of scope. Active is the honest import, not a guessed verdict.
        var plan = Plan(Source([Row(Cardiologie, ServiceA, "13/01/2025", "12/02/2025", 12)]));

        plan.Registrations.Single().Status.Should().Be(RegistrationStatus.Active);
    }

    [Fact]
    public void An_unreadable_date_pair_drops_only_that_rotation_and_says_so()
    {
        var plan = Plan(Source([Row(Cardiologie, ServiceA, "13/01/2025", null, 12)]));

        plan.Assignments.Should().BeEmpty();
        plan.Report.Problems.Should().Contain(p => p.Kind == LegacyImportProblemKind.UnreadablePeriod);
    }

    // PGSH allows one registration per student per year (IX_Registration_Student_Year is unique);
    // Access enforced nothing, and holds 3 pairs that break it. The in-memory suite cannot see unique
    // indexes at all, so this only surfaced against real PostgreSQL — hence pinning it here.
    [Fact]
    public void Two_registrations_in_one_year_keep_the_one_that_actually_has_rotations()
    {
        var source = new LegacyDatabase(
            AcademicYears: [new LegacyAcademicYear("2024/2025", true)],
            Niveaux: [new LegacyNiveau("MED04", "Quatrième Année Médecine", "Médecine", 4)],
            Stages: [new LegacyStage(Cardiologie, "MED04", "Cardiologie", 1, 22)],
            Services: [new LegacyService(ServiceA, "Hôp.IbnSina: Cardiologie A - Pr.R.Fellat")],
            Students: [new LegacyStudent(21025416, "TAZI OMAR", "219", null, "M", null, null, null, null, null, null)],
            Registrations:
            [
                // The stale "retrait" row, entered first and never cleaned up …
                new LegacyRegistration(240002107, 21025416, "2024/2025", "MED00", 1, "N", false),
                // … alongside the real one the student actually served.
                new LegacyRegistration(240003592, 21025416, "2024/2025", "MED04", 49, "N", false),
            ],
            StageAssignments: [new LegacyStageAssignment(240003592, Cardiologie, ServiceA, "13/01/2025", "12/02/2025", 12, "N")]);

        var plan = new LegacyImportPlanner().Plan(source);

        plan.Registrations.Should().ContainSingle();
        plan.Registrations[0].Status.Should().Be(RegistrationStatus.Active);   // MED04, not the retrait
        plan.Assignments.Should().ContainSingle();                             // the rotation survives
        plan.Report.Problems.Should().Contain(p => p.Kind == LegacyImportProblemKind.DuplicateRegistration);
    }

    [Fact]
    public void When_neither_duplicate_has_rotations_the_later_entry_wins()
    {
        // The later Numins is the correction — on the real data that also happens to be the row whose
        // level follows the student's actual progression.
        var source = new LegacyDatabase(
            AcademicYears: [new LegacyAcademicYear("2017/2018", true)],
            Niveaux: [new LegacyNiveau("MDPH03", "Troisième Année Pharmacie", "Pharmacie", 3)],
            Stages: [],
            Services: [],
            Students: [new LegacyStudent(15002550, "ALAMI SARA", "220", null, "F", null, null, null, null, null, null)],
            Registrations:
            [
                new LegacyRegistration(170000800, 15002550, "2017/2018", "MDPH02", null, "N", false),
                new LegacyRegistration(170000881, 15002550, "2017/2018", "MDPH03", null, "N", false),
            ],
            StageAssignments: []);

        var plan = new LegacyImportPlanner().Plan(source);

        plan.Registrations.Should().ContainSingle();
        plan.Registrations[0].Level.Year.Should().Be(3);
    }

    [Fact]
    public void The_hospital_tree_is_rebuilt_from_the_service_strings()
    {
        var plan = Plan(Source([Row(Cardiologie, ServiceA, "13/01/2025", "12/02/2025", 12)]));

        plan.Hospitals.Select(h => h.Name).Should()
            .BeEquivalentTo(["Hôpital Ibn Sina", "Hôpital Militaire Mohammed V"]);
        plan.Hospitals.Should().OnlyContain(h => h.Center == plan.Center);
        plan.Services.Should().HaveCount(2);
    }

    [Fact]
    public void Renamed_level_codes_do_not_produce_duplicate_levels()
    {
        var plan = Plan(Source([Row(Cardiologie, ServiceA, "13/01/2025", "12/02/2025", 12)]));

        plan.Levels.Select(l => (l.Year, l.AcademicProgram)).Should().OnlyHaveUniqueItems();
    }
}
