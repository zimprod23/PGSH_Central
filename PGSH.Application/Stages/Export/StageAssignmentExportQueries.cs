using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Stages;

namespace PGSH.Application.Stages.Export;

/// <summary>
/// The four reads behind the stage export, each <b>named, <c>internal static</c> and taking the
/// context</b> so <c>SqlTranslationTests</c> can hand it to <c>ToQueryString()</c>. A query buried in
/// a private async method cannot be compiled without running it, and the in-memory provider
/// translates nothing.
///
/// <para>⚠ <b>Four flat queries, never one nested read.</b> The périodes of an attempt, the
/// objective scores of an évaluation and the créneaux a période covers are all collections; folded
/// inside a projection they give exactly the shape that took down the macro plan — « Unable to
/// translate a collection subquery in a projection » — so each is fetched top-level, keyed on its
/// parent id, and joined in memory.</para>
///
/// <para>The scope is defined once, in <see cref="Scoped"/>, and the other three queries reach it
/// through an <c>IN (subquery)</c> rather than restating the predicate. Two copies of a year filter
/// is how a périodes sheet ends up describing a different population from the stages sheet it sits
/// beside.</para>
/// </summary>
internal static class StageAssignmentExportQueries
{
    /// <summary>
    /// Every attempt the file covers. ⚠ <c>a.Registration.AcademicYearId</c> — the year is read from
    /// the schema, which states it totally, and never approximated from the périodes' dates.
    /// </summary>
    internal static IQueryable<InternshipAssignment> Scoped(
        IApplicationDbContext dbContext,
        int yearId,
        int? levelId,
        int? stageId,
        int? academicGroupId,
        bool onlyEvaluated)
    {
        var query = dbContext.InternshipAssignments
            .AsNoTracking()
            .Where(a => a.Registration.AcademicYearId == yearId);

        // The promotion the student is registered in — not the level the stage belongs to. A
        // revalidation of an earlier year's stage is part of this promotion's record.
        if (levelId is { } level)
            query = query.Where(a => a.Registration.LevelId == level);

        if (stageId is { } stage)
            query = query.Where(a => a.Cohort.StageId == stage);

        if (academicGroupId is { } groupId)
            query = query.Where(a => a.Cohort.AcademicGroupId == groupId);

        // « Non évalué » is not a verdict, it is the absence of one — so filtering it out is the
        // caller saying the file is a PV rather than a state of play.
        if (onlyEvaluated)
            query = query.Where(a => a.Result != null && a.Result != StageAssignmentResult.NonÉvalué);

        return query;
    }

    internal static IQueryable<StageAssignmentExportRow> AssignmentsQuery(
        IApplicationDbContext dbContext,
        int yearId,
        int? levelId,
        int? stageId,
        int? academicGroupId,
        bool onlyEvaluated) =>
        Scoped(dbContext, yearId, levelId, stageId, academicGroupId, onlyEvaluated)
            .OrderBy(a => a.Registration.Level.AcademicProgram)
            .ThenBy(a => a.Registration.Level.Year)
            .ThenBy(a => a.Cohort.AcademicGroup.GroupNumber)
            .ThenBy(a => a.Registration.Student.LastName)
            .ThenBy(a => a.Registration.Student.FirstName)
            .ThenBy(a => a.Cohort.Stage.Name)
            .Select(a => new StageAssignmentExportRow(
                a.Id,
                a.Registration.Student.LastName,
                a.Registration.Student.FirstName,
                a.Registration.Student.CNE,
                a.Registration.Student.Appogee,
                a.Registration.AcademicYear.Label,
                a.Registration.Level.AcademicProgram,
                a.Registration.Level.Year,
                a.Registration.Level.Label,
                a.Cohort.Stage.Level.AcademicProgram,
                a.Cohort.Stage.Level.Year,
                a.Cohort.Stage.Level.Label,
                a.Cohort.AcademicGroup.Label,
                a.Cohort.AcademicGroup.GroupNumber,
                a.Cohort.AcademicGroup.RotationGroup,
                a.Cohort.StageId,
                a.Cohort.Stage.Name,
                a.Cohort.Stage.Coefficient,
                a.Cohort.Stage.RotationMode,
                a.Status,
                a.FinalScore,
                a.Result));

    /// <summary>
    /// Every période of those attempts, with its évaluation's <em>raw</em> parts. The mark is not
    /// computed here: <see cref="StageScoring"/> weights the objective scores, and reaching them from
    /// this projection would put a collection back inside it.
    /// </summary>
    internal static IQueryable<StagePeriodExportRow> PeriodsQuery(
        IApplicationDbContext dbContext,
        int yearId,
        int? levelId,
        int? stageId,
        int? academicGroupId,
        bool onlyEvaluated)
    {
        var scoped = Scoped(dbContext, yearId, levelId, stageId, academicGroupId, onlyEvaluated)
            .Select(a => a.Id);

        return dbContext.ServicePeriods
            .AsNoTracking()
            .Where(p => scoped.Contains(p.InternshipAssignmentId))
            .OrderBy(p => p.StartDate)
            .ThenBy(p => p.EndDate)
            .Select(p => new StagePeriodExportRow(
                p.Id,
                p.InternshipAssignmentId,
                p.ServiceId,
                p.Service.Name,
                p.Service.Hospital.Name,
                p.StartDate,
                p.EndDate,
                p.IsStarted,
                p.IsComplete,
                p.IsInterrupted,
                p.IsPaused,
                p.IsDelocalized,
                // ⚠ A période with no cell behind it is imported history, a délocalisation or a
                // revalidation — never something a répartition produced. Worth a column: it is the
                // difference between « le planning dit ça » and « quelqu'un l'a saisi à la main ».
                p.CohortSlotAssignmentId != null,
                p.Evaluation != null ? p.Evaluation.Id : null,
                p.Evaluation != null ? p.Evaluation.Mode : null,
                p.Evaluation != null ? p.Evaluation.TotalScore : null,
                p.Evaluation != null ? p.Evaluation.Outcome : null));
    }

    /// <summary>
    /// The planning cells each période was materialised from — one row per covered créneau, with the
    /// créneau's <em>own</em> window.
    ///
    /// <para>⚠ <b>This is what a folded run costs the document.</b> Under
    /// <see cref="StageRotationMode.SingleService"/> <c>SchedulePublisher</c> collapses the <c>kₛ</c>
    /// cells of a run into <b>one</b> <c>ServicePeriod</c> spanning them — correctly, since the
    /// student stands in one service and is marked once — so the périodes sheet showed one row where
    /// the grid authored three columns, and the axis those columns belong to disappeared from the
    /// file entirely. Measured on the live base 2026-08-31: 5MED Gynécologie Obstétrique is 833
    /// périodes each covering <b>3</b> créneaux (P4, P5, P6 — 08/12 → 07/01, 08/01 → 07/02,
    /// 08/02 → 07/03), against 5 831 grid-linked périodes covering 7 497 cells in all.</para>
    ///
    /// <para>⚠ Read through <c>ServicePeriodSlotCoverage</c>, never through
    /// <c>ServicePeriod.CohortSlotAssignmentId</c>: that FK names only the <b>first</b> cell of a run,
    /// so the trailing columns of exactly the case this exists for carry nothing pointing at them.
    /// A période with no coverage at all is an ad-hoc one — imported history, a délocalisation, a
    /// revalidation — and « 0 créneau » is the true answer for it, not a missing row.</para>
    /// </summary>
    internal static IQueryable<PeriodSlotExportRow> SlotCoverageQuery(
        IApplicationDbContext dbContext,
        int yearId,
        int? levelId,
        int? stageId,
        int? academicGroupId,
        bool onlyEvaluated)
    {
        var scoped = Scoped(dbContext, yearId, levelId, stageId, academicGroupId, onlyEvaluated)
            .Select(a => a.Id);

        return dbContext.ServicePeriodSlotCoverage
            .AsNoTracking()
            .Where(c => scoped.Contains(c.ServicePeriod.InternshipAssignmentId))
            .OrderBy(c => c.CohortSlotAssignment.StageSlot.PeriodNumber)
            .Select(c => new PeriodSlotExportRow(
                c.ServicePeriodId,
                c.CohortSlotAssignment.StageSlot.PeriodNumber,
                c.CohortSlotAssignment.StageSlot.Label,
                c.CohortSlotAssignment.StageSlot.StartDate,
                c.CohortSlotAssignment.StageSlot.EndDate));
    }

    /// <summary>
    /// The objective scores of those périodes' évaluations, flat and keyed on the évaluation.
    /// Weight travels with the row because that is what <see cref="StageScoring"/> weights by.
    /// </summary>
    internal static IQueryable<ObjectiveScoreExportRow> ObjectiveScoresQuery(
        IApplicationDbContext dbContext,
        int yearId,
        int? levelId,
        int? stageId,
        int? academicGroupId,
        bool onlyEvaluated)
    {
        var scoped = Scoped(dbContext, yearId, levelId, stageId, academicGroupId, onlyEvaluated)
            .Select(a => a.Id);

        return dbContext.ObjectiveScores
            .AsNoTracking()
            .Where(o => scoped.Contains(o.ServiceEvaluation.ServicePeriod.InternshipAssignmentId))
            .Select(o => new ObjectiveScoreExportRow(
                o.ServiceEvaluationId,
                o.Score,
                o.StageObjective.Weight));
    }
}
