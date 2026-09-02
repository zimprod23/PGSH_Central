namespace PGSH.Application.Stages.InternshipAssignments.Fiche;

/// <summary>
/// Print-ready payload for a stage's fiche de validation, available only once every period is
/// evaluated and the whole stage is validated. The frontend renders it into a printable page and
/// styles the (deliberately empty) header/footer band — that attestation template is configured later.
/// Each objective carries its mark; a period the chef only validated (no scoring) lists no objectives,
/// so the frontend simply shows the period mark (10).
/// </summary>
public sealed record FicheDeValidationResponse(
    string StudentFullName,
    string StudentAppogee,
    string? StudentCne,
    int StageId,
    string StageName,
    string? LevelLabel,
    string CohortLabel,
    string? GroupLabel,
    decimal FinalMark,
    IReadOnlyList<FichePeriod> Periods);

public sealed record FichePeriod(
    string ServiceName,
    string HospitalName,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal Mark,
    IReadOnlyList<FicheObjective> Objectives);

public sealed record FicheObjective(string Label, decimal Mark);
