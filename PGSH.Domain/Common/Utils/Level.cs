using System.ComponentModel.DataAnnotations;

namespace PGSH.Domain.Common.Utils;

public sealed class Level
{
    public int Id { get; set; }
    public string? Label { get; set; }
    [Range(0, 10)]
    public int Year { get; set; }
    public AcademicProgram AcademicProgram { get; set; }

    /// <summary>
    /// Whether this level is a <b>promotion</b> — a year of study that a cohort of students moves
    /// through together — rather than a marker.
    ///
    /// <para>⚠ <b>Year 0 is « Retrait », and it is a status wearing a level's clothes.</b> The Access
    /// base used <c>CODE_N = 'MED00'</c> to mark a withdrawal instead of a year, and
    /// <c>LegacyImport.LevelMapper</c> deliberately kept it as a level so the registration — and the
    /// rotations already served that year — survived the import rather than being dropped. The
    /// meaning lives in <c>Registration.Status = Withdrawn</c>; the real year the student withdrew
    /// from is not recoverable, because the source overwrote it.</para>
    ///
    /// <para>Nothing is planned for a marker: it has no stages, no cohorts, and no rotation. Every
    /// path that treats a level as a promotion has to say so, or it silently offers « Retrait »
    /// alongside the 3rd year — which is how one of its rosters came to carry a partition label.
    /// <c>CnpnTargetPlanner</c> already had to special-case this by hand; this property is so the
    /// next one does not have to.</para>
    /// </summary>
    public bool IsPromotion => Year > 0;
}
