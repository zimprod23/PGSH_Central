namespace PGSH.Application.Students.Selection;

/// <summary>
/// The three ways the faculty names a group of students for a bulk act, unioned — because that is
/// how the question is actually answered: « le G3 au complet, plus ces douze-là, plus la liste du
/// formulaire ».
/// </summary>
/// <remarks>
/// <para>⚠ <b><see cref="AcademicGroupIds"/> is by id, never by label.</b> A partition label repeats
/// in every promotion, and a label-scoped act reaches into past years — the defect that made
/// publish, auto-arrange and close do exactly that.</para>
///
/// <para>⚠ <b><see cref="Identifiers"/> accepts a CNE <i>or</i> an Apogée per line</b>, because the
/// paste comes from a Google Form and the two are mixed there. Both columns are matched, and the
/// Apogée is the one always present: 46% of the roll carried no real CNE until the placeholders were
/// cleared.</para>
///
/// <para>Shared rather than restated per act. The délocalisation asked this question first; the
/// nominative roster assignment asks the identical one, and the FIFO choice will ask it a third
/// time. Two acts answering one question separately is how one of them ends up with the right
/// year-scoping and the other does not.</para>
/// </remarks>
public sealed record StudentTargets(
    IReadOnlyList<int>?    AcademicGroupIds = null,
    IReadOnlyList<Guid>?   RegistrationIds  = null,
    IReadOnlyList<string>? Identifiers      = null)
{
    /// <summary>
    /// Whether the caller named anybody at all. ⚠ An empty selection is not an act on nobody, it is
    /// a request that lost its payload — the validators refuse it rather than reporting « 0 étudiant
    /// concerné », which reads as a promotion that happens to be empty.
    /// </summary>
    public bool NamesNobody =>
        (AcademicGroupIds is null || AcademicGroupIds.Count == 0)
     && (RegistrationIds  is null || RegistrationIds.Count  == 0)
     && (Identifiers      is null || Identifiers.Count      == 0);
}

/// <summary>Why a line of the selection reached no registration in the year asked for.</summary>
/// <remarks>
/// ⚠ The two are deliberately different answers. « Je ne le trouve pas » and « il est en 5ᵉ, pas en
/// 6ᵉ » are two different corrections — one is a typo in the file, the other is a student on the
/// wrong list — which is why the identifier lookup is <b>not</b> scoped by year and the year
/// comparison happens afterwards.
/// </remarks>
public enum TargetResolution
{
    /// <summary>No student carries this identifier, or no such registration exists.</summary>
    NotFound,

    /// <summary>The student is known, but not through a registration of the year asked for.</summary>
    WrongYear,
}

/// <summary>
/// One line of the selection that named nobody usable, in the vocabulary the resolver owns — each
/// act maps it onto its own row type.
/// </summary>
/// <remarks>
/// ⚠ <b>A line that resolves to nobody comes back as a row, never as silence.</b> Dropping it is the
/// defect that quietly lost 182 students of a réinscription roll: the file was applied, the report
/// said nothing, and the only trace was a spreadsheet nobody re-read.
/// </remarks>
public sealed record UnresolvedTarget(
    Guid?   RegistrationId,
    string  StudentName,
    string? Cne,
    string? Appogee,
    TargetResolution Reason,
    string  Message,
    /// <summary>The identifier that produced this line, when it came from a pasted list — so an
    /// unmatched line can be found in the file it was typed in.</summary>
    string? SourceIdentifier = null);

/// <summary>Who the operator meant, and every line that named nobody.</summary>
/// <param name="RegistrationIds">
/// The registrations to act on, each mapped to the identifier it was typed as — null when it was
/// named by id or reached through a roster. It travels so an unmatched line can be traced back.
/// </param>
public sealed record StudentSelection(
    IReadOnlyDictionary<Guid, string?> RegistrationIds,
    IReadOnlyList<UnresolvedTarget>    Unresolved);
