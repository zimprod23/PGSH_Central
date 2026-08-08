using PGSH.Application.Abstractions.Messaging;

namespace PGSH.Application.Stages.Curricula.GetCurriculum;

/// <summary>
/// What one CNPN requires of one level — "ce que le CNPN 1650.25 exige de la 3e année Médecine".
/// Keyed on the text rather than on an academic year: from 2026-2027 two texts govern the same
/// year, so the year cannot identify a requirement set.
/// </summary>
public sealed record GetCurriculumQuery(int LevelId, int CnpnVersionId) : IQuery<CurriculumResponse>;

public sealed record CurriculumResponse(
    int     Id,
    int     LevelId,
    string? LevelLabel,
    int     CnpnVersionId,
    string  CnpnVersionCode,
    string  CnpnVersionLabel,
    int     TotalYears,
    string? Reference,
    IReadOnlyList<CurriculumStageResponse> Stages);

public sealed record CurriculumStageResponse(
    int    StageId,
    string StageName,
    int    Coefficient,
    int    DurationInDays);
