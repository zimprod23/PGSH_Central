using FluentAssertions;
using FluentValidation.TestHelper;
using PGSH.Application.AcademicGroups.Transfer;
using PGSH.Application.Stages.Attendance.Record;
using PGSH.Application.Stages.Delocalization;
using PGSH.Application.Stages.Evaluations.Create;
using PGSH.Application.Stages.Evaluations.Update;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using Xunit;

namespace PGSH.Tests.Application;

// The input contract enforced before a command ever reaches its handler. These rules are what stop a
// malformed request from reaching the domain, so each mode's required fields are pinned down here
// rather than left to the handler to discover.
public class CommandValidatorTests
{
    private static readonly Guid PeriodId = Guid.NewGuid();
    private static readonly Guid RegistrationId = Guid.NewGuid();

    // ─── Create evaluation ────────────────────────────────────────────────────

    private static readonly CreateServiceEvaluationCommandValidator CreateEval = new();

    private static CreateServiceEvaluationCommand NewEvaluation(
        EvaluationMode mode, decimal? total = null, EvaluationOutcome? outcome = null,
        List<ObjectiveScoreRequest>? scores = null) =>
        new(PeriodId, mode, total, outcome, null, scores ?? []);

    [Fact]
    public void A_numeric_evaluation_requires_a_note()
    {
        CreateEval.TestValidate(NewEvaluation(EvaluationMode.Numeric))
            .ShouldHaveValidationErrorFor(x => x.TotalScore);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(21)]
    public void A_numeric_note_outside_zero_to_twenty_is_refused(decimal note)
    {
        CreateEval.TestValidate(NewEvaluation(EvaluationMode.Numeric, total: note))
            .ShouldHaveValidationErrorFor(x => x.TotalScore);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(20)]
    public void A_numeric_note_inside_the_scale_is_accepted(decimal note)
    {
        CreateEval.TestValidate(NewEvaluation(EvaluationMode.Numeric, total: note))
            .ShouldNotHaveValidationErrorFor(x => x.TotalScore);
    }

    [Fact]
    public void A_per_objective_note_outside_the_scale_is_refused()
    {
        var command = NewEvaluation(EvaluationMode.Numeric, total: 12m,
            scores: [new ObjectiveScoreRequest(1, 25, null, null)]);

        CreateEval.TestValidate(command).ShouldHaveAnyValidationError();
    }

    [Fact]
    public void An_objective_score_must_name_a_real_objective()
    {
        var command = NewEvaluation(EvaluationMode.Numeric, total: 12m,
            scores: [new ObjectiveScoreRequest(0, 15, null, null)]);

        CreateEval.TestValidate(command).ShouldHaveAnyValidationError();
    }

    [Fact]
    public void Validating_the_whole_period_requires_a_verdict()
    {
        CreateEval.TestValidate(NewEvaluation(EvaluationMode.ValidatePeriod))
            .ShouldHaveValidationErrorFor(x => x.Outcome);
    }

    [Fact]
    public void Validating_the_whole_period_needs_no_numeric_note()
    {
        CreateEval.TestValidate(NewEvaluation(EvaluationMode.ValidatePeriod, outcome: EvaluationOutcome.Validated))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validating_by_objective_requires_at_least_one_objective()
    {
        CreateEval.TestValidate(NewEvaluation(EvaluationMode.ValidateObjectives))
            .ShouldHaveValidationErrorFor(x => x.ObjectiveScores);
    }

    [Fact]
    public void Validating_by_objective_requires_a_verdict_on_each_one()
    {
        var command = NewEvaluation(EvaluationMode.ValidateObjectives,
            scores: [new ObjectiveScoreRequest(1, null, null, null)]);

        CreateEval.TestValidate(command).ShouldHaveAnyValidationError();
    }

    [Fact]
    public void Validating_by_objective_with_a_verdict_on_each_one_is_accepted()
    {
        var command = NewEvaluation(EvaluationMode.ValidateObjectives,
            scores: [new ObjectiveScoreRequest(1, null, EvaluationOutcome.Validated, null)]);

        CreateEval.TestValidate(command).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void An_evaluation_must_name_a_period()
    {
        var command = new CreateServiceEvaluationCommand(
            Guid.Empty, EvaluationMode.ValidatePeriod, null, EvaluationOutcome.Validated, null, []);

        CreateEval.TestValidate(command).ShouldHaveValidationErrorFor(x => x.ServicePeriodId);
    }

    // ─── Update evaluation ────────────────────────────────────────────────────

    private static readonly UpdateServiceEvaluationCommandValidator UpdateEval = new();

    [Fact]
    public void An_amendment_must_name_the_evaluation_it_changes()
    {
        var command = new UpdateServiceEvaluationCommand(
            Guid.Empty, EvaluationMode.Numeric, 12m, null, null, []);

        UpdateEval.TestValidate(command).ShouldHaveValidationErrorFor(x => x.EvaluationId);
    }

    [Fact]
    public void An_amendment_obeys_the_same_scale_as_the_original()
    {
        var command = new UpdateServiceEvaluationCommand(
            Guid.NewGuid(), EvaluationMode.Numeric, 30m, null, null, []);

        UpdateEval.TestValidate(command).ShouldHaveValidationErrorFor(x => x.TotalScore);
    }

    // ─── Transfer ─────────────────────────────────────────────────────────────

    private static readonly TransferStudentCommandValidator Transfer = new();

    [Fact]
    public void A_transfer_always_requires_a_reason_for_traceability()
    {
        var command = new TransferStudentCommand(RegistrationId, 5, "", TransferType.Definitive);

        Transfer.TestValidate(command).ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Fact]
    public void A_temporary_transfer_must_name_the_stage_it_applies_to()
    {
        var command = new TransferStudentCommand(RegistrationId, 5, "Prêt", TransferType.Temporary);

        Transfer.TestValidate(command).ShouldHaveValidationErrorFor(x => x.StageId);
    }

    [Fact]
    public void A_definitive_transfer_needs_no_stage()
    {
        var command = new TransferStudentCommand(RegistrationId, 5, "Motif", TransferType.Definitive);

        Transfer.TestValidate(command).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void A_transfer_must_name_a_real_target_group()
    {
        var command = new TransferStudentCommand(RegistrationId, 0, "Motif", TransferType.Definitive);

        Transfer.TestValidate(command).ShouldHaveValidationErrorFor(x => x.TargetGroupId);
    }

    // ─── Délocalisation ───────────────────────────────────────────────────────

    private static readonly DelocalizeStudentCommandValidator Delocalize = new();

    private static DelocalizeStudentCommand NewDelocalization(
        string reason = "Stage à Casablanca", DateOnly? start = null, DateOnly? end = null) =>
        new(RegistrationId, 1, 1, start ?? new DateOnly(2026, 3, 1), end ?? new DateOnly(2026, 3, 31), reason);

    [Fact]
    public void A_delocalization_always_requires_a_motif()
    {
        Delocalize.TestValidate(NewDelocalization(reason: ""))
            .ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Fact]
    public void A_delocalization_cannot_end_before_it_starts()
    {
        var command = NewDelocalization(
            start: new DateOnly(2026, 3, 31), end: new DateOnly(2026, 3, 1));

        Delocalize.TestValidate(command).ShouldHaveValidationErrorFor(x => x.EndDate);
    }

    [Fact]
    public void A_single_day_delocalization_is_accepted()
    {
        var day = new DateOnly(2026, 3, 1);

        Delocalize.TestValidate(NewDelocalization(start: day, end: day))
            .ShouldNotHaveValidationErrorFor(x => x.EndDate);
    }

    [Fact]
    public void A_fiche_reference_longer_than_the_column_is_refused()
    {
        var command = NewDelocalization() with { FicheReference = new string('x', 1001) };

        Delocalize.TestValidate(command).ShouldHaveValidationErrorFor(x => x.FicheReference);
    }

    [Fact]
    public void A_well_formed_delocalization_passes()
    {
        Delocalize.TestValidate(NewDelocalization()).ShouldNotHaveAnyValidationErrors();
    }

    // ─── Attendance ───────────────────────────────────────────────────────────

    private static readonly RecordAttendanceCommandValidator Attendance = new();

    [Fact]
    public void Presence_must_be_recorded_against_a_period()
    {
        var command = new RecordAttendanceCommand(Guid.Empty, new DateOnly(2026, 3, 1), AttendanceStatus.Present);

        Attendance.TestValidate(command).ShouldHaveValidationErrorFor(x => x.ServicePeriodId);
    }

    [Fact]
    public void An_out_of_range_presence_status_is_refused()
    {
        var command = new RecordAttendanceCommand(PeriodId, new DateOnly(2026, 3, 1), (AttendanceStatus)42);

        Attendance.TestValidate(command).ShouldHaveValidationErrorFor(x => x.Status);
    }

    [Theory]
    [InlineData(AttendanceStatus.Present)]
    [InlineData(AttendanceStatus.Absent)]
    [InlineData(AttendanceStatus.JustifiedAbsent)]
    [InlineData(AttendanceStatus.Late)]
    public void Every_defined_presence_status_is_accepted(AttendanceStatus status)
    {
        var command = new RecordAttendanceCommand(PeriodId, new DateOnly(2026, 3, 1), status);

        Attendance.TestValidate(command).ShouldNotHaveAnyValidationErrors();
    }
}
