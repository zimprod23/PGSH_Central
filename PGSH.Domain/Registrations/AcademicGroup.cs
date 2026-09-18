using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;
using PGSH.SharedKernel;

namespace PGSH.Domain.Registrations;

/// <summary>
/// A roster: the fixed set of students who move together through a year. Nothing more.
///
/// ⚠ "Groupe" in conversation usually means something narrower than this class. Three distinct
/// things share the word, and confusing them is how the year-scoping bugs got written:
///
/// <list type="bullet">
/// <item><b><see cref="AcademicGroup"/></b> — the roster, per (year, level). No stage, no service.</item>
/// <item><b><see cref="Cohort"/></b> — that roster <i>doing one stage</i>: (group × stage). This is
/// what a stage's "groups" are, and why a stage accumulates one per year it runs.</item>
/// <item><b><see cref="CohortSlotAssignment"/></b> — that cohort <i>in one period, in one service</i>:
/// (cohort × slot → service). When someone says "the group in Cardiologie in P2", this is the row.</item>
/// </list>
///
/// So a group is not "in a service" — it is in a <i>sequence</i> of them, one per period, and the
/// service lives two levels out. Reaching for <see cref="AcademicGroup"/> when you mean one cell of
/// the rotation grid is a category error the compiler cannot catch.
///
/// The year is constitutive, not decoration: a roster outside a year is not a roster, which is why
/// <see cref="AcademicYearId"/> is non-nullable.
/// </summary>
public sealed class AcademicGroup
{
    /// <summary>
    /// EF's constructor. Not for callers — use <see cref="ForPromotion(int, int, int, string, string?, string?, string?)"/>
    /// or <see cref="AsUnassignedBucket(int, string, string?, string?)"/>.
    /// </summary>
    private AcademicGroup() { }

    /// <summary>
    /// A roster of one promotion: (année, niveau, numéro), all three demanded.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>The identity is <c>IX_AcademicGroup_Year_Level_Number</c>, and until 2026-08-13 it
    /// was (année, numéro) alone.</b> <c>GROUPE_STG</c> restarts at 1 for each promotion, so the 3ᵉ
    /// année's 1-80 and the 5ᵉ année's 1-60 collapsed into one set of rows and 80 of the 100 rosters
    /// of 2025-2026 ended up carrying four or five promotions at once. A roster is the unit
    /// <c>GroupScheduleConflictGuard</c> forbids from being in two places, so one promotion's spring
    /// placements then refused another's, and a répartition came out with two of its nine columns
    /// filled.</para>
    ///
    /// <para>The three keys are <c>private set</c>: moving a roster to another promotion is not a
    /// correction, it is a different roster, and its cohortes, its cells and its students' whole year
    /// would follow it in silence. The label, the zone, the partition and the purpose stay open —
    /// those are what <c>UpdateGroupCommand</c> legitimately edits.</para>
    /// </remarks>
    public static Result<AcademicGroup> ForPromotion(
        int academicYearId, int levelId, int groupNumber, string label,
        string? geographicZone = null, string? rotationGroup = null, string? purpose = null)
    {
        if (academicYearId <= 0)
            return Result.Failure<AcademicGroup>(AcademicGroupErrors.RosterNeedsAcademicYear);

        // ⚠ Ce qui sépare un groupe de promotion du panier, c'est la promotion — justement. Sous une
        // fabrique unique à niveau nullable, oublier la promotion et vouloir « Non réparti » sont le
        // même appel, et c'est de là que vient l'incident des 4 725 étudiants.
        if (levelId <= 0)
            return Result.Failure<AcademicGroup>(AcademicGroupErrors.RosterNeedsPromotion);

        if (groupNumber <= 0)
            return Result.Failure<AcademicGroup>(AcademicGroupErrors.RosterNeedsNumber);

        return new AcademicGroup
        {
            AcademicYearId = academicYearId,
            LevelId        = levelId,
            GroupNumber    = groupNumber,
            Label          = label,
            GeographicZone = geographicZone,
            RotationGroup  = rotationGroup,
            Purpose        = NormalisePurpose(purpose),
        };
    }

    /// <summary>
    /// The same demand, for a graph whose année and niveau have no key yet — <c>LegacyImportPlanner</c>
    /// builds all three in one pass and lets the store number them.
    /// </summary>
    /// <remarks>
    /// ⚠ It exists so that path does not have to go round the factory: the id overload would be
    /// satisfied there by two zeros, which is precisely the row this class refuses to make.
    /// </remarks>
    public static Result<AcademicGroup> ForPromotion(
        AcademicYear academicYear, Level level, int groupNumber, string label)
    {
        if (academicYear is null)
            return Result.Failure<AcademicGroup>(AcademicGroupErrors.RosterNeedsAcademicYear);

        if (level is null)
            return Result.Failure<AcademicGroup>(AcademicGroupErrors.RosterNeedsPromotion);

        if (groupNumber <= 0)
            return Result.Failure<AcademicGroup>(AcademicGroupErrors.RosterNeedsNumber);

        return new AcademicGroup
        {
            AcademicYear = academicYear,
            Level        = level,
            GroupNumber  = groupNumber,
            Label        = label,
        };
    }

    /// <summary>
    /// « Non réparti » — the one roster of a year that deliberately belongs to <i>no</i> promotion.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>That this is a construction path of its own is the point of the pair.</b> The bucket
    /// holds every promotion's unassigned registrations at once — 4 725 of them in 2025-2026 — so it
    /// is a holding pen, not a roster: no niveau, no number, and the two acts that would turn it into
    /// one are refused by <see cref="AcademicGroupErrors.UnassignedRosterCannotBePartitioned"/> and
    /// by <c>StageErrors.CohortOnUnassignedRoster</c>. A partition label pulls the whole bucket into
    /// <c>CohortProvisioner</c>; a cohorte puts it in one service.</para>
    ///
    /// <para>Under one factory with a nullable <c>levelId</c>, <i>forgetting</i> the promotion and
    /// <i>meaning</i> the bucket are the same call. Here, forgetting does not compile and meaning it
    /// has to be said. <see cref="GroupNumber"/> stays <c>0</c> for the reason it is not asked for:
    /// the bucket is not the year's group zero, it is outside the numbering.</para>
    ///
    /// <para>One per année, never one per promotion — splitting it would invent a roster per niveau
    /// that nobody is a member of.</para>
    ///
    /// <para>⚠ <b>It takes no <c>rotationGroup</c>, and that is deliberate.</b> Naming a partition on
    /// the bucket is the act <see cref="AcademicGroupErrors.UnassignedRosterCannotBePartitioned"/>
    /// refuses at runtime; here there is simply no parameter to pass it through. The zone and the
    /// purpose stay available because they are read by people and change nothing.</para>
    /// </remarks>
    public static Result<AcademicGroup> AsUnassignedBucket(
        int academicYearId, string label, string? geographicZone = null, string? purpose = null)
    {
        if (academicYearId <= 0)
            return Result.Failure<AcademicGroup>(AcademicGroupErrors.RosterNeedsAcademicYear);

        return new AcademicGroup
        {
            AcademicYearId = academicYearId,
            Label          = label,
            GeographicZone = geographicZone,
            Purpose        = NormalisePurpose(purpose),
        };
    }

    /// <inheritdoc cref="AsUnassignedBucket(int, string, string?, string?)"/>
    public static Result<AcademicGroup> AsUnassignedBucket(AcademicYear academicYear, string label)
    {
        if (academicYear is null)
            return Result.Failure<AcademicGroup>(AcademicGroupErrors.RosterNeedsAcademicYear);

        return new AcademicGroup { AcademicYear = academicYear, Label = label };
    }

    public int Id { get; set; }
    public string Label { get; set; } = default!; // e.g., "G22 - Temara Cluster"

    /// <summary>
    /// Its number inside its promotion. <c>0</c> on « Non réparti » alone, which is outside the
    /// numbering rather than first in it — see <see cref="AsUnassignedBucket(int, string, string?, string?)"/>.
    /// </summary>
    public int GroupNumber { get; private set; }

    public string? GeographicZone { get; set; }
    public string? RotationGroup  { get; set; } // Persistent partition label (A, B, C…) across all stages

    /// <summary>
    /// Why this roster exists, in the faculty's own words — « Volontaires Kénitra (GST), formulaire
    /// du 12/09 », « étudiants militaires ».
    /// </summary>
    /// <remarks>
    /// ⚠ Nothing else records it. The only evidence that roster 102 was the military one is the
    /// pattern of its cells; a year later nobody can tell that from a coincidence, and a re-découpage
    /// dissolves it without anything saying what was lost. Free text on purpose — not an entity, not
    /// a rule engine: it is read by people, never by the arranger, which must keep deciding on
    /// capacity and order alone.
    /// </remarks>
    public string? Purpose { get; set; }

    /// <summary>
    /// What an entered purpose means once stored: trimmed, and blank collapsed to « none ».
    /// </summary>
    /// <remarks>
    /// ⚠ Shared by the create and the update because a whitespace-only value has to mean the same on
    /// both paths. Stored raw on one and normalised on the other, a roster edited without touching
    /// the field would come back carrying «&#160; » — which reads, in every list, exactly like a
    /// purpose somebody wrote.
    /// </remarks>
    public static string? NormalisePurpose(string? purpose) =>
        string.IsNullOrWhiteSpace(purpose) ? null : purpose.Trim();

    public int AcademicYearId { get; private set; }
    public AcademicYear AcademicYear { get; set; } = default!;

    /// <summary>
    /// The promotion this roster belongs to. Null on « Non réparti » and nowhere else — a null here
    /// is the bucket's signature, which is what lets every planning read exclude it by construction
    /// rather than by a special case.
    /// </summary>
    public int? LevelId { get; private set; }
    public Level? Level { get; set; }

    // The 20 fixed students
    public ICollection<Registration> Registrations { get; set; } = new List<Registration>();

    // The "Buses" this group takes for various stages
    public ICollection<Cohort> Cohorts { get; set; } = new List<Cohort>();
}
