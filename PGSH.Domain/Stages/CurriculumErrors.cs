using PGSH.SharedKernel;

namespace PGSH.Domain.Stages;

public static class CurriculumErrors
{
    public static Error NotFound(int levelId, int cnpnVersionId) => Error.NotFound(
        "Curriculums.NotFound",
        $"Aucune exigence enregistrée pour le niveau {levelId} dans le CNPN {cnpnVersionId}.");

    public static Error AlreadyExists(int levelId, int cnpnVersionId) => Error.Conflict(
        "Curriculums.AlreadyExists",
        $"Le niveau {levelId} figure déjà dans le CNPN {cnpnVersionId} ; modifiez-le "
        + "plutôt que d'en créer un second.");

    public static Error VersionNotFound(int cnpnVersionId) => Error.NotFound(
        "CnpnVersions.NotFound",
        $"Aucun CNPN enregistré sous l'identifiant {cnpnVersionId}.");

    public static readonly Error ProgramMismatch = Error.Validation(
        "Curriculums.ProgramMismatch",
        "Le niveau et le CNPN ne relèvent pas de la même filière.");

    /// <summary>
    /// A six-year CNPN has no seventh year. Catching this here is the point of recording
    /// <c>TotalYears</c>: without it, a level outside the programme's span is silently requirable.
    /// </summary>
    public static Error LevelOutsideProgramme(int levelYear, int totalYears) => Error.Validation(
        "Curriculums.LevelOutsideProgramme",
        $"Ce CNPN organise {totalYears} années — la {levelYear}ᵉ année n'en fait pas partie.");

    public static Error StageAlreadyRequired(int stageId) => Error.Conflict(
        "Curriculums.StageAlreadyRequired",
        $"Le stage {stageId} figure déjà dans ce CNPN.");

    public static Error StageNotRequired(int stageId) => Error.NotFound(
        "Curriculums.StageNotRequired",
        $"Le stage {stageId} ne figure pas dans ce CNPN.");

    public static Error StageNotInLevel(int stageId, int levelId) => Error.Validation(
        "Curriculums.StageNotInLevel",
        $"Le stage {stageId} n'appartient pas au niveau {levelId}.");

    public static readonly Error InvalidCoefficient = Error.Validation(
        "Curriculums.InvalidCoefficient",
        "Le coefficient doit être supérieur ou égal à 1.");

    public static readonly Error InvalidDuration = Error.Validation(
        "Curriculums.InvalidDuration",
        "La durée doit être supérieure à zéro.");

    public static readonly Error LevelMismatch = Error.Validation(
        "Curriculums.LevelMismatch",
        "Un CNPN ne peut être copié que depuis le même niveau.");

    public static readonly Error NotEmpty = Error.Conflict(
        "Curriculums.NotEmpty",
        "Ce CNPN contient déjà des stages ; la copie ne s'applique qu'à un CNPN vierge.");
}
