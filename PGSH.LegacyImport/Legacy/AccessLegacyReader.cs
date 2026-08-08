using System.Data.OleDb;
using System.Runtime.Versioning;

namespace PGSH.LegacyImport.Legacy;

/// <summary>
/// Reads the seven tables that matter out of the Access file. Deliberately dumb: every value comes
/// across as stored, and all interpretation happens in the mapping layer, which is testable without
/// a copy of the .mdb.
/// </summary>
/// <remarks>
/// Needs the Microsoft ACE OLEDB provider and a bitness matching this process (64-bit here). Note the
/// driver speaks ANSI-92 wildcards through OleDb — <c>%</c> and <c>_</c>, not Access's <c>*</c>/<c>#</c>.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class AccessLegacyReader(string filePath)
{
    private const string Provider = "Microsoft.ACE.OLEDB.16.0";
    private const string FallbackProvider = "Microsoft.ACE.OLEDB.12.0";

    public LegacyDatabase Read()
    {
        using var connection = Open();

        return new LegacyDatabase(
            AcademicYears: Query(connection,
                "SELECT anneeuniv, encours FROM anneeuniv",
                r => new LegacyAcademicYear(
                    Str(r, 0) ?? "",
                    string.Equals(Str(r, 1), "O", StringComparison.OrdinalIgnoreCase))),

            Niveaux: Query(connection,
                "SELECT CodeN, Niveau, [option], rang FROM Niveaux",
                r => new LegacyNiveau(Str(r, 0) ?? "", Str(r, 1) ?? "", Str(r, 2), Int(r, 3) ?? 0)),

            Stages: Query(connection,
                "SELECT codest, CodeN, stage, coef, duree FROM stages",
                r => new LegacyStage(
                    Int(r, 0) ?? 0, Str(r, 1) ?? "", Str(r, 2) ?? "", Int(r, 3) ?? 1, Int(r, 4) ?? 30)),

            Services: Query(connection,
                "SELECT CODES, SERVICE FROM SERVICES",
                r => new LegacyService(Int(r, 0) ?? 0, Str(r, 1) ?? "")),

            Students: Query(connection,
                "SELECT NO_ORDRE, Nom, CNE, CIN, SEXE, DAT_NAISS, LIEU_NAISS, VILLE, ADRESSE_P, ANNEE_BAC, MILITAIRE FROM ETUDIANT",
                r => new LegacyStudent(
                    Int(r, 0) ?? 0, Str(r, 1) ?? "", Str(r, 2), Str(r, 3), Str(r, 4),
                    Date(r, 5), Str(r, 6), Str(r, 7), Str(r, 8), Str(r, 9), Str(r, 10))),

            Registrations: Query(connection,
                "SELECT Numins, NO_ORDRE, ANNEE_UNIV, coden, GROUPE_STG, STATUT, Fraud FROM Inscription",
                r => new LegacyRegistration(
                    Int(r, 0) ?? 0, Int(r, 1) ?? 0, Str(r, 2) ?? "", Str(r, 3) ?? "",
                    Int(r, 4), Str(r, 5), Bool(r, 6))),

            StageAssignments: Query(connection,
                "SELECT NUMINS, CODEST, CodeS, PER1, PER2, Note, revalide FROM AffectStage",
                r => new LegacyStageAssignment(
                    Int(r, 0) ?? 0, Int(r, 1) ?? 0, Int(r, 2) ?? 0,
                    Str(r, 3), Str(r, 4), Dec(r, 5), Str(r, 6))));
    }

    private OleDbConnection Open()
    {
        Exception? lastFailure = null;

        // Every provider is caught, including the last: filtering on `provider == Provider` let a
        // failure on the fallback escape as a raw OleDbException, so the message below explaining the
        // real requirement could never be reached. The half-open connection is disposed either way.
        foreach (string provider in new[] { Provider, FallbackProvider })
        {
            var connection = new OleDbConnection($"Provider={provider};Data Source={filePath};");
            try
            {
                connection.Open();
                return connection;
            }
            catch (Exception ex)
            {
                lastFailure = ex;
                connection.Dispose();
            }
        }

        throw new InvalidOperationException(
            $"Could not open '{filePath}'. The Microsoft ACE OLEDB provider must be installed and "
            + "match this process's bitness (64-bit).", lastFailure);
    }

    private static List<T> Query<T>(OleDbConnection connection, string sql, Func<OleDbDataReader, T> map)
    {
        using var command = new OleDbCommand(sql, connection);
        using var reader = command.ExecuteReader();

        var rows = new List<T>();
        while (reader.Read()) rows.Add(map(reader));
        return rows;
    }

    private static string? Str(OleDbDataReader r, int i)
    {
        if (r.IsDBNull(i)) return null;
        string value = r.GetValue(i).ToString() ?? "";
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static int? Int(OleDbDataReader r, int i) =>
        r.IsDBNull(i) ? null : Convert.ToInt32(Convert.ToDouble(r.GetValue(i)));

    private static decimal? Dec(OleDbDataReader r, int i) =>
        r.IsDBNull(i) ? null : Convert.ToDecimal(r.GetValue(i));

    private static bool Bool(OleDbDataReader r, int i) =>
        !r.IsDBNull(i) && Convert.ToBoolean(r.GetValue(i));

    private static DateTime? Date(OleDbDataReader r, int i) =>
        r.IsDBNull(i) ? null : Convert.ToDateTime(r.GetValue(i));
}
