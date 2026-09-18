namespace PGSH.Domain.Students;

/// <summary>
/// La série du baccalauréat d'un étudiant.
/// </summary>
/// <remarks>
/// ⚠ <b><see cref="NonRenseigne"/> est ajouté en <em>dernier</em>, et cela compte.</b> La colonne est
/// un <c>integer</c> sans conversion, donc la valeur de chaque membre existant est déjà écrite dans
/// 10 203 lignes : réordonner reclasserait silencieusement toute la base. Un membre neuf se met à la
/// fin, jamais au milieu, et <see cref="BacFrançais"/> reste 0 pour cette seule raison.
///
/// <para>⚠ <b>Et 0 ne veut pas dire « non renseigné », ce qui était tout le problème.</b>
/// <c>LegacyImportPlanner</c> n'écrit jamais ce champ — la colonne <c>SERIE</c> de <c>ETUDIANT</c>
/// existe pourtant — donc les 10 203 étudiants importés portent la valeur par défaut de l'enum et se
/// lisent « Bac Français » sur tous les écrans. Pire, <c>InscriptionPlanner</c> écrivait
/// <see cref="SVT"/> pour toute ligne de canevas dont la colonne est vide : non pas un défaut de
/// l'enum mais une <em>supposition choisie</em>, trois lignes après que le même fichier eut écrit
/// <c>Gender.None</c> en expliquant que « None is the honest answer; it is not a guess ».</para>
///
/// <para>⚠ <b>Ce membre ne répare pas les lignes déjà écrites</b> : un 0 stocké recouvre « importé,
/// jamais renseigné » et « quelqu'un a bien choisi Bac Français », que rien ne distingue après coup.
/// Le rattrapage est un acte sur la base vivante, donc un clic de l'utilisateur — voir l'item 0cb.</para>
/// </remarks>
public enum BacSeries
{
    BacFrançais,
    BacMission,
    MathA,
    MathB,
    Physique,
    SVT,
    Etrangaire,

    /// <summary>La faculté n'a pas la série de cet étudiant. Se dit, ne se devine pas.</summary>
    NonRenseigne,
}
