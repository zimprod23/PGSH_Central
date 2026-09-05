using MediatR;
using Microsoft.AspNetCore.Mvc;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Cohorts.Bulk;
using PGSH.Application.Stages.Cohorts.PublishSchedule;
using PGSH.Application.Stages.Cohorts.UnpublishSchedule;
using PGSH.Application.Stages.Schedule;
using PGSH.Application.Stages.Schedule.AutoArrange;
using PGSH.Application.Stages.Slots;
using PGSH.Domain.Stages;

namespace PGSH.API.Endpoints.Stages;

internal sealed class StageScheduleEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // The rows are paged and the partition is filtered server-side — a grid of a hundred cohortes
        // over ten columns is a thousand cells, and shipping them all is what made this screen slow
        // to open and slow to close. `pageNumber`/`pageSize` fall back to the query's own defaults so
        // an older client keeps working, bounded instead of unbounded.
        app.MapGet("stages/{stageId:int}/schedule",
            async (int stageId, int? academicYearId, string? rotationGroup, int? pageNumber, int? pageSize,
                   ISender sender, CancellationToken ct) =>
            {
                var query = new GetStageScheduleQuery(
                    stageId, academicYearId, rotationGroup,
                    pageNumber ?? 1, pageSize ?? GetStageScheduleQuery.DefaultPageSize);
                var result = await sender.Send(query, ct);
                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithTags("Stages")
            .RequireAuthorization();

        app.MapPost("stages/{stageId:int}/slots",
            async (int stageId, SlotRequest request, ISender sender, CancellationToken ct) =>
            {
                var command = new CreateStageSlotCommand(
                    stageId, request.AcademicYearId, request.PeriodNumber, request.Label,
                    request.StartDate, request.EndDate);
                var result = await sender.Send(command, ct);
                return result.Match(id => Results.Created($"stages/{stageId}/slots/{id}", id), CustomResults.Problem);
            })
            .WithTags("Stages")
            .RequireAuthorization();

        app.MapPut("stages/{stageId:int}/slots/{slotId:int}",
            async (int stageId, int slotId, SlotRequest request, ISender sender, CancellationToken ct) =>
            {
                var command = new UpdateStageSlotCommand(slotId, stageId, request.Label, request.StartDate, request.EndDate);
                var result = await sender.Send(command, ct);
                return result.Match(Results.NoContent, CustomResults.Problem);
            })
            .WithTags("Stages")
            .RequireAuthorization();

        app.MapDelete("stages/{stageId:int}/slots/{slotId:int}",
            async (int slotId, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new DeleteStageSlotCommand(slotId), ct);
                return result.Match(Results.NoContent, CustomResults.Problem);
            })
            .WithTags("Stages")
            .RequireAuthorization();

        app.MapPut("stages/{stageId:int}/slots/{slotId:int}/cohorts/{cohortId:int}",
            async (int slotId, int cohortId, [FromBody] SetAssignmentRequest request, ISender sender, CancellationToken ct) =>
            {
                var command = new SetCohortSlotAssignmentCommand(cohortId, slotId, request.ServiceId);
                var result = await sender.Send(command, ct);
                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithTags("Stages")
            .RequireAuthorization();

        app.MapDelete("stages/{stageId:int}/slots/{slotId:int}/cohorts/{cohortId:int}",
            async (int slotId, int cohortId, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new ClearCohortSlotAssignmentCommand(cohortId, slotId), ct);
                return result.Match(Results.NoContent, CustomResults.Problem);
            })
            .WithTags("Stages")
            .RequireAuthorization();

        app.MapDelete("stages/{stageId:int}/slots/{slotId:int}/cohorts",
            async (int slotId, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new ClearSlotAssignmentsCommand(slotId), ct);
                return result.Match(r => Results.Ok(new { cleared = r.Cleared, skipped = r.Skipped }), CustomResults.Problem);
            })
            .WithTags("Stages")
            .RequireAuthorization();

        app.MapPost("stages/{stageId:int}/schedule/auto-arrange",
            async (int stageId, [FromBody] AutoArrangeRequest? request, ISender sender, CancellationToken ct) =>
            {
                var command = new AutoArrangeStageScheduleCommand(
                    stageId,
                    request?.AcademicYearId,
                    request?.PartitionCount,
                    request?.PartitionLabels,
                    request?.PeriodNumbers);
                var result = await sender.Send(command, ct);
                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithTags("Stages")
            .RequireAuthorization();

        app.MapPost("stages/{stageId:int}/schedule/publish",
            async (int stageId, [FromBody] PublishStageRequest? request, ISender sender, CancellationToken ct) =>
            {
                var command = new PublishStageScheduleCommand(
                    stageId,
                    request?.AcademicYearId,
                    request?.PartitionLabels,
                    request?.PeriodNumbers,
                    request?.AllowOverCapacity ?? false);
                var result = await sender.Send(command, ct);
                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithTags("Stages")
            .RequireAuthorization();

        // ⚠ POST, not DELETE: it carries a body (year + partitions) and it answers with a report the
        // caller has to read — how many cohortes were left alone because their rotation has begun.
        // Mirrors schedule/publish, which is the act it undoes.
        app.MapPost("stages/{stageId:int}/schedule/unpublish",
            async (int stageId, [FromBody] UnpublishStageRequest? request, ISender sender, CancellationToken ct) =>
            {
                var command = new UnpublishStageScheduleCommand(
                    stageId,
                    request?.AcademicYearId,
                    request?.PartitionLabels);
                var result = await sender.Send(command, ct);
                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithTags("Stages")
            .RequireAuthorization();

        app.MapPost("stages/{stageId:int}/schedule/start",
            async (int stageId, [FromBody] StageLifecycleRequest? request, ISender sender, CancellationToken ct) =>
            {
                var command = new StartStagePeriodsCommand(
                    stageId, request?.AcademicYearId, request?.CohortIds, request?.PartitionLabels,
                    request?.PeriodNumbers);
                var result = await sender.Send(command, ct);
                return result.Match(count => Results.Ok(new { started = count }), CustomResults.Problem);
            })
            .WithTags("Stages")
            .RequireAuthorization();

        app.MapPost("stages/{stageId:int}/schedule/complete",
            async (int stageId, [FromBody] StageLifecycleRequest? request, ISender sender, CancellationToken ct) =>
            {
                var command = new CompleteStagePeriodsCommand(
                    stageId, request?.AcademicYearId, request?.CohortIds, request?.PartitionLabels,
                    request?.PeriodNumbers);
                var result = await sender.Send(command, ct);
                return result.Match(count => Results.Ok(new { completed = count }), CustomResults.Problem);
            })
            .WithTags("Stages")
            .RequireAuthorization();

        app.MapPost("stages/{stageId:int}/schedule/pause",
            async (int stageId, [FromBody] StagePauseRequest? request, ISender sender, CancellationToken ct) =>
            {
                var command = new PauseStagePeriodsCommand(
                    stageId, request?.Kind ?? PauseKind.Exam, request?.Reason, request?.AcademicYearId,
                    request?.CohortIds, request?.PartitionLabels, request?.PeriodNumbers);
                var result = await sender.Send(command, ct);
                return result.Match(count => Results.Ok(new { paused = count }), CustomResults.Problem);
            })
            .WithTags("Stages")
            .RequireAuthorization();

        app.MapPost("stages/{stageId:int}/schedule/resume",
            async (int stageId, [FromBody] StageLifecycleRequest? request, ISender sender, CancellationToken ct) =>
            {
                var command = new ResumeStagePeriodsCommand(
                    stageId, request?.AcademicYearId, request?.CohortIds, request?.PartitionLabels,
                    request?.PeriodNumbers);
                var result = await sender.Send(command, ct);
                return result.Match(count => Results.Ok(new { resumed = count }), CustomResults.Problem);
            })
            .WithTags("Stages")
            .RequireAuthorization();
    }
}

// AcademicYearId is nullable on the wire and resolved to the current year server-side, so an older
// client cannot silently widen a stage operation to every promotion that ever took the stage.
internal sealed record SlotRequest(int AcademicYearId, int PeriodNumber, string? Label, DateOnly StartDate, DateOnly EndDate);
internal sealed record SetAssignmentRequest(int ServiceId);
internal sealed record AutoArrangeRequest(int? AcademicYearId, int? PartitionCount, IReadOnlyList<string>? PartitionLabels, IReadOnlyList<int>? PeriodNumbers);
internal sealed record PublishStageRequest(int? AcademicYearId, IReadOnlyList<string>? PartitionLabels, IReadOnlyList<int>? PeriodNumbers, bool AllowOverCapacity = false);
/// <remarks>
/// ⚠ No <c>Force</c>, and the absence is the design: forcing destroys marks and attendance, and the
/// act allowed to do that is the per-cohorte « Dépublier », which names what that one cohorte costs
/// and asks a second time. A bulk sweep must never become the way round it.
/// </remarks>
internal sealed record UnpublishStageRequest(int? AcademicYearId, IReadOnlyList<string>? PartitionLabels);
internal sealed record StageLifecycleRequest(int? AcademicYearId, IReadOnlyList<int>? CohortIds, IReadOnlyList<string>? PartitionLabels, IReadOnlyList<int>? PeriodNumbers);
internal sealed record StagePauseRequest(int? AcademicYearId, PauseKind? Kind, string? Reason, IReadOnlyList<int>? CohortIds, IReadOnlyList<string>? PartitionLabels, IReadOnlyList<int>? PeriodNumbers);
