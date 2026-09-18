namespace PGSH.Application.Hospitals;

/// <summary>
/// The column widths of the hospital catalogue — <c>Center</c>, <c>Hospital</c>, <c>Service</c> —
/// named once so a validator and its column cannot disagree.
/// </summary>
/// <remarks>
/// ⚠ <b>A validator looser than its column does not refuse; it produces a 500.</b> Both hospital and
/// both centre validators allowed a <b>200</b>-character name against a <c>varchar(100)</c> column and
/// a <b>100</b>-character city against a <c>varchar(50)</c> one, so a long name passed validation and
/// PostgreSQL answered <c>22001 string_data_right_truncation</c> — a <c>DbUpdateException</c>, mapped
/// to <b>500</b>, whose only content is the name of a constraint and whose <c>detail</c> the client
/// discards above 500 (<c>errorMiddleware</c> shows « Une erreur serveur est survenue »). The user is
/// told nothing, least of all that a name is too long.
///
/// <para>It is the same rule as « a delete asks the schema first, or the constraint answers for it »,
/// reached through a length rather than a foreign key: the schema decides, and the validator's job is
/// to say so in words <em>before</em> the write.</para>
///
/// <para>⚠ These numbers are the ones in <c>HospitalConfiguration</c>. Changing a column's width means
/// changing the constant here in the same migration — that is the whole reason they are a constant and
/// not four literals.</para>
/// </remarks>
public static class HospitalTextLengths
{
    /// <summary><c>Center.Name</c>, <c>Hospital.Name</c> and <c>Service.Name</c>.</summary>
    public const int Name = 100;

    /// <summary><c>Center.City</c> and <c>Hospital.City</c>.</summary>
    public const int City = 50;

    /// <summary><c>Hospital.Description</c> and <c>Service.Description</c>.</summary>
    public const int Description = 500;

    /// <summary><c>Hospital.Email</c>.</summary>
    public const int Email = 100;

    /// <summary><c>Service.Specialty</c>.</summary>
    public const int Specialty = 100;

    /// <summary>Each of the three <c>LocalisationMaps</c> coordinates, on all three entities.</summary>
    public const int Coordinate = 50;
}
