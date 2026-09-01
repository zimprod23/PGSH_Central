using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Calendar;
using PGSH.Domain.Calendar;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Revalidation;

/// <param name="From">
/// The earliest day the retake could begin; today when omitted. The window is laid forward from the
/// first <em>worked</em> day at or after it.
/// </param>
public sealed record GetRevalidationContextQuery(Guid RegistrationId, int StageId, DateOnly? From)
    : IQuery<RevalidationContextResponse>;

internal sealed class GetRevalidationContextQueryHandler(
    IApplicationDbContext dbContext,
    WorkingDayProvider workingDays,
    ExecutionAuthorizer authorizer)
    : IQueryHandler<GetRevalidationContextQuery, RevalidationContextResponse>
{
    public async Task<Result<RevalidationContextResponse>> Handle(
        GetRevalidationContextQuery request, CancellationToken cancellationToken)
    {
        // Same audience as the act itself: this read says what a student still owes and where he
        // would be sent, which is scolarite's business and not a student's.
        var access = authorizer.EnsureIsAdministrative(StageErrors.RevalidationNotAllowed);
        if (access.IsFailure)
            return Result.Failure<RevalidationContextResponse>(access.Error);

        var registration = await RegistrationQuery(dbContext, request.RegistrationId)
            .FirstOrDefaultAsync(cancellationToken);

        if (registration is null)
            return Result.Failure<RevalidationContextResponse>(Error.NotFound(
                "Registrations.NotFound", $"Registration '{request.RegistrationId}' not found."));

        var stage = await StageQuery(dbContext, request.StageId).FirstOrDefaultAsync(cancellationToken);
        if (stage is null)
            return Result.Failure<RevalidationContextResponse>(StageErrors.NotFound(request.StageId));

        bool alreadyOnThisRegistration = await RevalidationPlanner
            .ExistingAssignmentQuery(dbContext, request.RegistrationId, request.StageId)
            .AnyAsync(cancellationToken);

        var priorAttempts = await RevalidationPlanner
            .PriorAttemptsQuery(dbContext, registration.StudentId, request.StageId, request.RegistrationId)
            .ToListAsync(cancellationToken);

        var eligibility = RevalidationPlanner.CheckEligibility(
            priorAttempts, alreadyOnThisRegistration, request.StageId);

        // The governing text is read in the project's one order: the registration's own stamp first,
        // the student's current one only as a fallback. Null is "never resolved", not "owes nothing".
        int? textId = registration.CnpnVersionId ?? registration.StudentCnpnVersionId;
        var text = textId is { } id
            ? await GoverningTextQuery(dbContext, id, stage.LevelId, request.StageId)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var governing = text is null ? null : new RevalidationText(
            text.CnpnVersionId, text.Code, text.Label,
            registration.CnpnVersionId is not null ? registration.CnpnSource : null,
            FromRegistration: registration.CnpnVersionId is not null,
            StatesThisStage: text.DurationInDays is not null,
            text.DurationInDays,
            text.Coefficient);

        var calendar = await workingDays.BuildAsync(cancellationToken);

        var failure = RevalidationPlanner.LastFailure(priorAttempts);
        var lastFailure = failure is null
            ? null
            : await BuildLastFailureAsync(failure, request.StageId, calendar, cancellationToken);

        // The proposal is laid from the TEXT's duration, never the catalogue's. Every student who
        // reaches this screen is on an older text by construction, so the catalogue is wrong for
        // precisely this population. No duration recorded means no proposal, never an invented one.
        var proposal = governing?.DurationInDays is { } duration
            ? calendar.Lay(request.From ?? DateOnly.FromDateTime(DateTime.UtcNow), duration)
            : null;

        var cohorts = await CohortOptionsQuery(dbContext, request.StageId, registration.AcademicYearId)
            .ToListAsync(cancellationToken);

        return new RevalidationContextResponse(
            request.RegistrationId,
            request.StageId,
            stage.Name,
            stage.LevelId,
            stage.LevelLabel,
            CanOpen: eligibility.IsSuccess,
            RefusalCode: eligibility.IsFailure ? eligibility.Error.Code : null,
            RefusalMessage: eligibility.IsFailure ? eligibility.Error.Description : null,
            governing,
            stage.DurationInDays,
            stage.Coefficient,
            lastFailure,
            proposal is null ? null : new RevalidationWindow(
                proposal.Start, proposal.End, proposal.WorkingDays, proposal.CalendarDays,
                proposal.HasProvisionalDates,
                [.. proposal.HolidaysHit.Select(h => h.Name)]),
            cohorts);
    }

    private async Task<RevalidationPriorAttempt> BuildLastFailureAsync(
        RevalidationPlanner.PriorAttempt failure, int stageId,
        WorkingDayCalendar calendar, CancellationToken cancellationToken)
    {
        var detail = await FailureDetailQuery(dbContext, failure.RegistrationId, stageId)
            .FirstOrDefaultAsync(cancellationToken);

        return new RevalidationPriorAttempt(
            failure.RegistrationId,
            detail?.AcademicYearId ?? 0,
            detail?.AcademicYearLabel ?? string.Empty,
            failure.OriginalServiceId,
            detail?.ServiceName,
            detail?.StartDate,
            detail?.EndDate,
            detail is { StartDate: { } from, EndDate: { } to } ? calendar.Count(from, to) : null);
    }

    internal sealed record RegistrationRow(
        Guid StudentId, int AcademicYearId, int? CnpnVersionId, RegistrationCnpnSource? CnpnSource,
        int? StudentCnpnVersionId);

    internal static IQueryable<RegistrationRow> RegistrationQuery(
        IApplicationDbContext dbContext, Guid registrationId) =>
        dbContext.Registrations
            .AsNoTracking()
            .Where(r => r.Id == registrationId)
            .Select(r => new RegistrationRow(
                r.StudentId, r.AcademicYearId, r.CnpnVersionId, r.CnpnSource,
                r.Student.CnpnVersionId));

    internal sealed record StageRow(
        string Name, int LevelId, string? LevelLabel, int Coefficient, int DurationInDays);

    internal static IQueryable<StageRow> StageQuery(IApplicationDbContext dbContext, int stageId) =>
        dbContext.Stages
            .AsNoTracking()
            .Where(s => s.Id == stageId)
            .Select(s => new StageRow(
                s.Name, s.LevelId, s.Level.Label, s.Coefficient, s.DurationInDays));

    internal sealed record TextRow(
        int CnpnVersionId, string Code, string Label, int? DurationInDays, int? Coefficient);

    /// <summary>
    /// The governing text, plus what it states of this stage at this level. The requirement is read
    /// as two scalar sub-selects rather than by joining the collection, so a text with nothing
    /// recorded comes back with nulls instead of falling out of the result entirely: "the text says
    /// nothing" and "there is no text" are different answers and the screen tells them apart.
    /// </summary>
    internal static IQueryable<TextRow> GoverningTextQuery(
        IApplicationDbContext dbContext, int cnpnVersionId, int levelId, int stageId) =>
        dbContext.CnpnVersions
            .AsNoTracking()
            .Where(v => v.Id == cnpnVersionId)
            .Select(v => new TextRow(
                v.Id, v.Code, v.Label,
                dbContext.CurriculumStages
                    .Where(cs => cs.Curriculum.CnpnVersionId == v.Id
                              && cs.Curriculum.LevelId == levelId
                              && cs.StageId == stageId)
                    .Select(cs => (int?)cs.DurationInDays)
                    .FirstOrDefault(),
                dbContext.CurriculumStages
                    .Where(cs => cs.Curriculum.CnpnVersionId == v.Id
                              && cs.Curriculum.LevelId == levelId
                              && cs.StageId == stageId)
                    .Select(cs => (int?)cs.Coefficient)
                    .FirstOrDefault()));

    internal sealed record FailureDetailRow(
        int AcademicYearId, string AcademicYearLabel, string? ServiceName,
        DateOnly? StartDate, DateOnly? EndDate);

    internal static IQueryable<FailureDetailRow> FailureDetailQuery(
        IApplicationDbContext dbContext, Guid registrationId, int stageId) =>
        dbContext.InternshipAssignments
            .AsNoTracking()
            .Where(a => a.RegistrationId == registrationId && a.Cohort.StageId == stageId)
            .Select(a => new FailureDetailRow(
                a.Registration.AcademicYearId,
                a.Registration.AcademicYear.Label,
                a.ServicePeriods.OrderByDescending(p => p.StartDate)
                    .Select(p => p.Service.Name).FirstOrDefault(),
                a.ServicePeriods.Min(p => (DateOnly?)p.StartDate),
                a.ServicePeriods.Max(p => (DateOnly?)p.EndDate)));

    /// <summary>
    /// Where the retake could be slotted: cohortes running this stage in the year the student is
    /// registered in. Scoped by that year deliberately — an unscoped read returns every year the
    /// stage ever ran (681 rows for MED3 Chirurgie) and would offer a 2019 cohorte as a destination.
    /// </summary>
    internal static IQueryable<RevalidationCohortOption> CohortOptionsQuery(
        IApplicationDbContext dbContext, int stageId, int academicYearId) =>
        dbContext.Cohorts
            .AsNoTracking()
            .Where(c => c.StageId == stageId && c.AcademicGroup.AcademicYearId == academicYearId)
            .OrderBy(c => c.AcademicGroup.GroupNumber)
            .Select(c => new RevalidationCohortOption(
                c.Id, c.AcademicGroupId, c.AcademicGroup.Label,
                c.AcademicGroup.GroupNumber, c.AcademicGroup.RotationGroup));
}
