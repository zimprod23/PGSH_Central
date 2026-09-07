using PGSH.SharedKernel;

namespace PGSH.Domain.Calendar;

public static class PromotionPauseErrors
{
    public static Error NotFound(int id) =>
        Error.NotFound("PromotionPauses.NotFound", $"Suspension {id} introuvable.");

    public static Error EndsBeforeItStarts(DateOnly start, DateOnly end) => Error.Validation(
        "PromotionPauses.EndsBeforeItStarts",
        $"La suspension se termine le {end:dd/MM/yyyy}, avant son début le {start:dd/MM/yyyy}.");

    public static Error ReasonRequired => Error.Validation(
        "PromotionPauses.ReasonRequired",
        "Une suspension déplace le calendrier d'une promotion entière : dites laquelle et pourquoi.");

    public static Error SpanTooLong(int days, int maximum) => Error.Validation(
        "PromotionPauses.SpanTooLong",
        $"La suspension couvre {days} jours ; le maximum est {maximum}. Une fermeture plus longue "
        + "qu'un trimestre n'est pas une suspension d'examens — déclarez-la comme vacances "
        + "universitaires dans le calendrier de la faculté.");

    public static Error OutsideAcademicYear(
        DateOnly start, DateOnly end, string yearLabel, DateOnly yearStart, DateOnly yearEnd) =>
        Error.Validation(
            "PromotionPauses.OutsideAcademicYear",
            $"La fenêtre du {start:dd/MM/yyyy} au {end:dd/MM/yyyy} sort de l'année {yearLabel} "
            + $"({yearStart:dd/MM/yyyy} – {yearEnd:dd/MM/yyyy}).");

    /// <summary>
    /// ⚠ The same reason <c>AcademicYearCalendarGuard</c> refuses overlapping years: every day in the
    /// overlap would be subtracted twice from every duration measured across it.
    /// </summary>
    public static Error OverlapsAnotherPause(
        string levelLabel, string existingReason, DateOnly existingStart, DateOnly existingEnd) =>
        Error.Conflict(
            "PromotionPauses.Overlap",
            $"{levelLabel} est déjà suspendue du {existingStart:dd/MM/yyyy} au {existingEnd:dd/MM/yyyy} "
            + $"« {existingReason} ». Deux fenêtres qui se chevauchent retireraient deux fois les mêmes "
            + "jours de chaque durée.");
}
