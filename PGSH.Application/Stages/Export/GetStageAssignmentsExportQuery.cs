using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Exports;

namespace PGSH.Application.Stages.Export;

/// <summary>
/// The stage record as a document: who did which stage, in which service, over which périodes, and
/// how it ended.
///
/// <para><b>This is the post-validation export</b> — the one drawn after the évaluations are in, so
/// it carries the note and the verdict. It deliberately does <em>not</em> hide the rows that have no
/// verdict yet: a document whose whole purpose is « où en est la promotion » must show the holes, or
/// a missing évaluation reads as a student who was never planned. <c>OnlyEvaluated</c> narrows it to
/// the settled rows for the day the file is a PV rather than a worklist.</para>
///
/// <para><b>Three sheets, because two questions are being asked at once.</b> « Stages » is one row per
/// <em>attempt</em> — the unit that carries a note and a verdict, and therefore the unit a PV is
/// drawn from. « Périodes » is one row per <em>période</em>, joined back by the same key, for the
/// service-by-service detail. « Synthèse » counts the verdicts per stage. Folding the detail into the
/// first sheet would either lose it or make every row a paragraph; leaving the first sheet out would
/// hand a reader several lines per student and no place to read a verdict.</para>
///
/// <para>⚠ <b>Scoped by the registration's level, not the stage's.</b> The file is « la promotion et
/// ce qu'elle a fait cette année », and a 6ᵉ année student revalidating a 3ᵉ année stage belongs on
/// the 6ᵉ année's document — his own. Both levels are printed, so a rattrapage is visible as the row
/// where they differ rather than as a row in somebody else's file.</para>
///
/// <para>⚠ <b>And the year is read, never inferred from dates</b> —
/// <c>a.Registration.AcademicYearId</c>. Measured 2026-08-30, the two rules disagree on 7 030 of
/// 105 626 périodes, and the registration is right every time: a date rule cannot tell a year that
/// ran late from the next year's work.</para>
/// </summary>
public sealed record GetStageAssignmentsExportQuery(
    int? AcademicYearId = null,
    int? LevelId = null,
    int? StageId = null,
    int? AcademicGroupId = null,
    bool OnlyEvaluated = false) : IQuery<ExportFile>;
