namespace PGSH.Application.Hospitals.Centers.GetById;

public record CenterDetailResponse(
    int Id,
    string Name,
    string CenterType,
    string? City,
    string? LocalizationX,
    string? LocalizationY,
    string? LocalizationZ,
    List<HospitalInCenterResponse> Hospitals);

/// <summary>
/// Un hôpital tel qu'il apparaît <em>dans la fiche de son centre</em> : de quoi le nommer et cliquer
/// dessus, rien de plus.
/// </summary>
/// <remarks>
/// ⚠ <b>Nommé ainsi parce qu'il s'appelait <c>HospitalSummaryResponse</c>, comme
/// <see cref="PGSH.Application.Hospitals.GetMany.HospitalSummaryResponse"/>, qui est une autre forme
/// dans un autre espace de noms.</b> Deux types d'un même nom rendent invisible la question « quel
/// est celui que le formulaire d'édition relit ? » — et c'est très exactement la question que
/// personne n'a posée le jour où le <c>HospitalSummaryResponse</c> de <c>GetMany</c> omettait
/// <c>Description</c> : le formulaire renvoyait consciencieusement <c>''</c>, et <b>modifier un
/// hôpital effaçait sa description</b>.
///
/// <para>Celui-ci est délibérément incomplet, et c'est licite : rien ne le réécrit. Ce qui ne l'était
/// pas, c'est de ne pas pouvoir le dire sans préciser l'espace de noms.</para>
///
/// <para>⚠ Le nom du type n'apparaît pas dans le JSON — la propriété reste <c>hospitals</c> — donc le
/// renommage ne change rien pour le client.</para>
/// </remarks>
public record HospitalInCenterResponse(
    int Id,
    string Name,
    string City,
    string HospitalType);
