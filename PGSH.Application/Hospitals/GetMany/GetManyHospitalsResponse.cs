namespace PGSH.Application.Hospitals.GetMany;

/// <summary>
/// ⚠ Carries <b>every field the edit form writes back</b>, not only the columns the table renders.
/// The admin form is populated from this row, so anything missing here is sent back empty and the
/// stored value is destroyed — which is what happened to <c>Description</c> on every hospital
/// anyone edited.
/// </summary>
public record HospitalSummaryResponse(
    int Id,
    string Name,
    int CenterId,
    string CenterName,
    string HospitalType,
    string City,
    string? Email,
    string? Description,
    string? X,
    string? Y,
    string? Z);
