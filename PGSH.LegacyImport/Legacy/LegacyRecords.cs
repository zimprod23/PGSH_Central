namespace PGSH.LegacyImport.Legacy;

/// <summary>
/// The legacy tables as plain data, exactly as Access stores them — text dates, sentinel marks and
/// all. Nothing is interpreted here; every rule lives in the mapping layer so it can be tested
/// without a copy of the .mdb (which is gitignored, since it carries real personal data).
/// </summary>
public sealed record LegacyDatabase(
    IReadOnlyList<LegacyAcademicYear> AcademicYears,
    IReadOnlyList<LegacyNiveau> Niveaux,
    IReadOnlyList<LegacyStage> Stages,
    IReadOnlyList<LegacyService> Services,
    IReadOnlyList<LegacyStudent> Students,
    IReadOnlyList<LegacyRegistration> Registrations,
    IReadOnlyList<LegacyStageAssignment> StageAssignments);

/// <summary>`anneeuniv` — label is "2015/2016".</summary>
public sealed record LegacyAcademicYear(string Label, bool IsOpen);

/// <summary>`Niveaux` — `CodeN` is the level code referenced by `Inscription.coden` and `stages.CodeN`.</summary>
public sealed record LegacyNiveau(string CodeN, string Label, string? Option, int Rang);

/// <summary>`stages` — `duree` is in days, `coef` the weighting.</summary>
public sealed record LegacyStage(int CodeSt, string CodeN, string Name, int Coefficient, int DurationInDays);

/// <summary>`SERVICES` — the hospital is embedded in <paramref name="Name"/>, there is no FK.</summary>
public sealed record LegacyService(int CodeS, string Name);

/// <summary>`ETUDIANT` — `Nom` is one field holding "NOM PRENOM", surname first.</summary>
public sealed record LegacyStudent(
    int       NoOrdre,
    string    Nom,
    string?   Cne,
    string?   Cin,
    string?   Sexe,
    DateTime? DateOfBirth,
    string?   PlaceOfBirth,
    string?   City,
    string?   Address,
    string?   BacYear,
    string?   Militaire);

/// <summary>`Inscription` — one row per student per academic year. `Numins` is the PK the stages hang off.</summary>
public sealed record LegacyRegistration(
    int     NumIns,
    int     NoOrdre,
    string  AcademicYear,
    string  LevelCode,
    int?    GroupNumber,
    string? Statut,
    bool    Fraud);

/// <summary>
/// `AffectStage` — one row per rotation, NOT per stage. The stage is implicit in
/// (<paramref name="NumIns"/>, <paramref name="CodeSt"/>); several rows sharing that pair are the
/// several services one stage was served across. `Note` is -1 or null when ungraded.
/// </summary>
public sealed record LegacyStageAssignment(
    int      NumIns,
    int      CodeSt,
    int      CodeS,
    string?  Per1,
    string?  Per2,
    decimal? Note,
    string?  Revalide);
