using PGSH.Domain.Common.Utils;

namespace PGSH.Application.Hospitals;

internal static class LocalizationMapper
{
    public static Localization? FromCoordinates(string? x, string? y, string? z) =>
        x is not null || y is not null ? new Localization(x!, y!, z) : null;
}
