using MediatR;
using Microsoft.AspNetCore.Mvc;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Cohorts.Bulk;
using PGSH.Application.Stages.Cohorts.PublishSchedule;
using PGSH.Application.Stages.Schedule;
using PGSH.Application.Stages.Schedule.AutoArrange;
using PGSH.Application.Stages.Slots;
using PGSH.Domain.Stages;

namespace PGSH.API.Endpoints.Stages;

internal sealed class StageScheduleEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("stages/{stageId:int}/schedule",
            async (int stageId, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new GetStageScheduleQuery(stageId), ct);
                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithTags("Stages");

        app.MapPost("stages/{stageId:int}/slots",
            async (int stageId, SlotRequest request, ISender sender, CancellationToken ct) =>
            {
                var command = new CreateStageSlotCommand(stageId, request.PeriodNumber, request.Label, request.StartDate, request.EndDate);
                var result = await sender.Send(command, ct);
                return result.Match(id => Results.Created($"stages/{stageId}/slots/{id}", id), CustomResults.Problem);
            })
            .WithTags("Stages");

        app.MapPut("stages/{stageId:int}/slots/{slotId:int}",
            async (int stageId, int slotId, SlotRequest request, ISender sender, CancellationToken ct) =>
            {
                var command = new UpdateStageSlotCommand(slotId, stageId, request.Label, request.StartDate, request.EndDate);
                var result = await sender.Send(command, ct);
                return result.Match(Results.NoContent, CustomResults.Problem);
            })
            .WithTags("Stages");

        app.MapDelete("stages/{stageId:int}/slots/{slotId:int}",
            async (int slotId, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new DeleteStageSlotCommand(slotId), ct);
                return result.Match(Results.NoContent, CustomResults.Problem);
            })
            .WithTags("Stages");

        app.MapPut("stages/{stageId:int}/slots/{slotId:int}/cohorts/{cohortId:int}",
            async (int slotId, int cohortId, [FromBody] SetAssignmentRequest request, ISender sender, CancellationToken ct) =>
            {
                var command = new SetCohortSlotAssignmentCommand(cohortId, slotId, request.ServiceId);
                var result = await sender.Send(command, ct);
                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithTags("Stages");

        app.MapDelete("stages/{stageId:int}/slots/{slotId:int}/cohorts/{cohortId:int}",
            async (int slotId, int cohortId, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new ClearCohortSlotAssignmentCommand(cohortId, slotId), ct);
                return result.Match(Results.NoContent, CustomResults.Problem);
            })
            .WithTags("Stages");

        app.MapDelete("stages/{stageId:int}/slots/{slotId:int}/cohorts",
            async (int slotId, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new ClearSlotAssignmentsCommand(slotId), ct);
                return result.Match(r => Results.Ok(new { cleared = r.Cleared, skipped = r.Skipped }), CustomResults.Problem);
            })
            .WithTags("Stages");

        app.MapPost("stages/{stageId:int}/schedule/auto-arrange",
            async (int stageId, [FromBody] AutoArrangeRequest? request, ISender sender, CancellationToken ct) =>
            {
                var command = new AutoArrangeStageScheduleCommand(
                    stageId,
                    request?.PartitionCount,
                    request?.PartitionLabels,
                    request?.PeriodNumbers);
                var result = await sender.Send(command, ct);
                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithTags("Stages");

        app.MapPost("stages/{stageId:int}/schedule/publish",
            async (int stageId, [FromBody] PublishStageRequest? request, ISender sender, CancellationToken ct) =>
            {
                var command = new PublishStageScheduleCommand(
                    stageId,
                    request?.PartitionLabels,
                    request?.PeriodNumbers,
                    request?.AllowOverCapacity ?? false);
                var result = await sender.Send(command, ct);
                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithTags("Stages");

        app.MapPost("stages/{stageId:int}/schedule/start",
            async (int stageId, [FromBody] StageLifecycleRequest? request, ISender sender, CancellationToken ct) =>
            {
                var command = new StartStagePeriodsCommand(
                    stageId, request?.CohortIds, request?.PartitionLabels, request?.PeriodNumbers);
                var result = await sender.Send(command, ct);
                return result.Match(count => Results.Ok(new { started = count }), CustomResults.Problem);
            })
            .WithTags("Stages");

        app.MapPost("stages/{stageId:int}/schedule/complete",
            async (int stageId, [FromBody] StageLifecycleRequest? request, ISender sender, CancellationToken ct) =>
            {
                var command = new CompleteStagePeriodsCommand(
                    stageId, request?.CohortIds, request?.PartitionLabels, request?.PeriodNumbers);
                var result = await sender.Send(command, ct);
                return result.Match(count => Results.Ok(new { completed = count }), CustomResults.Problem);
            })
            .WithTags("Stages");

        app.MapPost("stages/{stageId:int}/schedule/pause",
            async (int stageId, [FromBody] StagePauseRequest? request, ISender sender, CancellationToken ct) =>
            {
                var command = new PauseStagePeriodsCommand(
                    stageId, request?.Kind ?? PauseKind.Exam, request?.Reason,
                    request?.CohortIds, request?.PartitionLabels, request?.PeriodNumbers);
                var result = await sender.Send(command, ct);
                return result.Match(count => Results.Ok(new { paused = count }), CustomResults.Problem);
            })
            .WithTags("Stages");

        app.MapPost("stages/{stageId:int}/schedule/resume",
            async (int stageId, [FromBody] StageLifecycleRequest? request, ISender sender, CancellationToken ct) =>
            {
                var command = new ResumeStagePeriodsCommand(
                    stageId, request?.CohortIds, request?.PartitionLabels, request?.PeriodNumbers);
                var result = await sender.Send(command, ct);
                return result.Match(count => Results.Ok(new { resumed = count }), CustomResults.Problem);
            })
            .WithTags("Stages");
    }
}

internal sealed record SlotRequest(int PeriodNumber, string? Label, DateOnly StartDate, DateOnly EndDate);
internal sealed record SetAssignmentRequest(int ServiceId);
internal sealed record AutoArrangeRequest(int? PartitionCount, IReadOnlyList<string>? PartitionLabels, IReadOnlyList<int>? PeriodNumbers);
internal sealed record PublishStageRequest(IReadOnlyList<string>? PartitionLabels, IReadOnlyList<int>? PeriodNumbers, bool AllowOverCapacity = false);
internal sealed record StageLifecycleRequest(IReadOnlyList<int>? CohortIds, IReadOnlyList<string>? PartitionLabels, IReadOnlyList<int>? PeriodNumbers);
internal sealed record StagePauseRequest(PauseKind? Kind, string? Reason, IReadOnlyList<int>? CohortIds, IReadOnlyList<string>? PartitionLabels, IReadOnlyList<int>? PeriodNumbers);
