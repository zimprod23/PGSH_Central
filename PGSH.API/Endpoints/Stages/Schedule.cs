using MediatR;
using Microsoft.AspNetCore.Mvc;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
using PGSH.Application.Stages.Schedule;
using PGSH.Application.Stages.Schedule.AutoArrange;
using PGSH.Application.Stages.Slots;

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

        app.MapPost("stages/{stageId:int}/schedule/auto-arrange",
            async (int stageId, int? partitionCount, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new AutoArrangeStageScheduleCommand(stageId, partitionCount), ct);
                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithTags("Stages");
    }
}

internal sealed record SlotRequest(int PeriodNumber, string? Label, DateOnly StartDate, DateOnly EndDate);
internal sealed record SetAssignmentRequest(int ServiceId);
