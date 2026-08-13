using PGSH.SharedKernel;

namespace PGSH.Application.Calendar;

public static class HolidayErrors
{
    public static Error NotFound(int id) =>
        Error.NotFound("Holidays.NotFound", $"Jour férié {id} introuvable.");

    public static Error Duplicate(DateOnly date, string name) => Error.Conflict(
        "Holidays.Duplicate",
        $"« {name} » est déjà enregistré au {date:dd/MM/yyyy}.");

    public static Error EndsBeforeStart => Error.Validation(
        "Holidays.EndsBeforeStart",
        "Un jour férié se termine avant de commencer.");
}
