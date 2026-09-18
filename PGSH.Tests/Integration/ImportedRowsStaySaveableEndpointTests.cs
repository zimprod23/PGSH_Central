using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Students;
using PGSH.Domain.Users;
using Xunit;
using Roles = PGSH.Application.Abstractions.Authentication.Roles;

namespace PGSH.Tests.Integration;

/// <summary>
/// Un étudiant que l'import a écrit peut-il encore être <b>enregistré</b>&nbsp;?
///
/// <para>⚠ <b>Un validateur décrit ce qu'une <em>sauvegarde</em> doit satisfaire, pas à quoi ressemble
/// une bonne fiche.</b> Chaque règle d'un chemin de modification s'applique à des lignes qui sont déjà
/// là : une règle que la donnée importée ne satisfait pas ne décrit rien du tout — elle rend ces
/// lignes <b>lisibles seulement</b>, et le refus nomme un champ que personne n'était en train de
/// corriger, donc il se lit comme un bouton cassé plutôt que comme une règle.</para>
///
/// <para>C'est arrivé cinq fois. L'ancienne expression régulière du CNE refusait <b>5 646 des 10 204
/// étudiants</b> ; <c>Objectives.NotEmpty()</c> refusait <b>27 stages sur 27</b> ; et les trois règles
/// mesurées ici — sexe, date de naissance, longueur du nom — refusaient tout ce que le fichier Access
/// ne renseignait pas. Les trois valeurs sont écrites <em>exprès</em> par les deux chemins d'écriture
/// en masse : <c>LegacyIdentityMapper.MapGender</c> écrit <c>None</c> pour les 1 050 lignes à sexe vide
/// (« None is the honest answer; it is not a guess ») et <c>InscriptionPlanner</c> fait de même pour
/// chaque canevas dont la colonne Sexe est vide — donc la base continue d'en produire.</para>
///
/// <para>⚠ <b>Aucun test de handler ne peut voir ceci.</b> Le validateur tourne dans
/// <c>ValidationPipelineBehavior</c> : un test qui appelle le handler passe la commande malformée
/// telle quelle. Un test de validateur ne le voit pas mieux — il construit l'objet lui-même et saute
/// la liaison de modèle. Il faut la vraie route.</para>
/// </summary>
public class ImportedRowsStaySaveableEndpointTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private static readonly Guid ImportedStudentId = new("11111111-1111-1111-1111-111111111111");

    /// <summary>Le nom réel le plus long que le format autorise — 100 caractères, la largeur de la colonne.</summary>
    private static readonly string LongName = new('A', 100);

    private readonly ApiFactory _factory;

    public ImportedRowsStaySaveableEndpointTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetAsync();
        await SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Un étudiant tel que l'import en a écrit des milliers : aucun sexe, aucune date de naissance, et
    /// un prénom vide parce que son nom d'origine ne tenait qu'en un mot.
    /// </summary>
    private async Task SeedAsync() => await _factory.SeedAsync(db =>
    {
        db.SeedCatalog();

        db.Students.Add(new Student
        {
            Id = ImportedStudentId,
            Email = "bennani@um5.ac.ma",
            FirstName = "",
            LastName = "Bennani",
            CNE = "R130896",
            Appogee = "20140001",
            Gender = Gender.None,
            DateOfBirth = null,
            BacYear = "2014",
            AcademicProgram = AcademicProgram.Medecine,
            Status = new Status(CivilStatus.Civil, NationalityStatus.Marocaine),
        });
    });

    private HttpClient Client() => _factory.CreateApiClient(null, Roles.Scolarite);

    private static object Body(
        Gender gender = Gender.None,
        DateOnly? dateOfBirth = null,
        string firstName = "",
        string lastName = "Bennani",
        string cne = "R130896") => new
        {
            email = "bennani@um5.ac.ma",
            firstName,
            lastName,
            cin = (string?)null,
            cne,
            appogee = "20140001",
            accessGrade = 12.5m,
            academicProgram = nameof(AcademicProgram.Medecine),
            bacSeries = nameof(BacSeries.SVT),
            bacYear = "2014",
            gender = gender.ToString(),
            civilStatus = nameof(CivilStatus.Civil),
            nationalityStatus = nameof(NationalityStatus.Marocaine),
            placeOfBirth = (string?)null,
            fullAddress = (string?)null,
            dateOfBirth,
            academy = (Academy?)null,
            province = (Province?)null,
            ranking = (int?)null,
        };

    private Task<HttpResponseMessage> SaveAsync(object body) =>
        Client().PutAsJsonAsync($"/api/students/{ImportedStudentId}", body);

    /// <summary>
    /// ⚠ Le cas complet, et celui qui compte : la fiche revient telle qu'elle est stockée, avec une
    /// seule correction — le CNE — et elle doit s'enregistrer. Les trois champs fautifs sont ceux que
    /// l'opérateur n'a pas touchés.
    /// </summary>
    [Fact]
    public async Task An_imported_student_can_be_corrected_without_inventing_what_the_base_never_recorded()
    {
        var response = await SaveAsync(Body(cne: "R 13089613"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "corriger un CNE ne doit pas exiger qu'on invente un sexe et une date de naissance");

        var saved = await _factory.QueryAsync(db =>
            db.Students.AsNoTracking().FirstAsync(s => s.Id == ImportedStudentId));

        saved.CNE.Should().Be("R 13089613");
        saved.Gender.Should().Be(Gender.None, "la correction ne devait toucher que le CNE");
        saved.DateOfBirth.Should().BeNull();
    }

    /// <summary>
    /// <c>Gender.None</c> est la valeur que les deux chemins d'écriture produisent — 1 053 lignes sur
    /// le rôle réel. La refuser demandait à l'opérateur d'inventer un sexe pour corriger un nom.
    /// </summary>
    [Fact]
    public async Task A_student_whose_sex_the_base_never_recorded_can_still_be_saved()
    {
        var response = await SaveAsync(Body(gender: Gender.None));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// <c>Student.DateOfBirth</c> est <c>DateOnly?</c> dans le domaine, et les deux chemins stockent
    /// <c>null</c> quand la source ne porte pas de date. <c>NotEmpty()</c> contredisait le schéma.
    /// </summary>
    [Fact]
    public async Task A_student_with_no_recorded_date_of_birth_can_still_be_saved()
    {
        var response = await SaveAsync(Body(dateOfBirth: null));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// <c>SplitName</c> rend <c>("", "Bennani")</c> pour un nom d'un seul mot — à dessein, puisque la
    /// moitié à laquelle ce mot appartient n'est pas décidable. Exiger les deux noms rendait chacun de
    /// ces étudiants illisible en écriture.
    /// </summary>
    [Fact]
    public async Task A_student_carrying_only_one_of_the_two_names_can_still_be_saved()
    {
        var response = await SaveAsync(Body(firstName: "", lastName: "Bennani"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// ⚠ 100 est la largeur de la colonne, et c'est à elle que le validateur doit se tenir : plafonné
    /// à 50 alors que l'import tronque à 100, un nom de 51 à 100 caractères s'importait puis ne se
    /// sauvegardait plus jamais.
    /// </summary>
    [Fact]
    public async Task A_name_as_long_as_the_column_allows_is_accepted()
    {
        var response = await SaveAsync(Body(lastName: LongName));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// ⚠ <b>Premier témoin.</b> Sans lui, tout ce qui précède passerait aussi bien si le validateur
    /// n'existait plus du tout. Au-delà de la colonne, le refus doit venir du validateur <b>en
    /// mots</b> — et non de PostgreSQL, qui répondrait <c>22001</c>, donc un 500 dont l'écran ne peut
    /// rien faire.
    /// </summary>
    [Fact]
    public async Task A_name_longer_than_the_column_is_refused_in_words()
    {
        var response = await SaveAsync(Body(lastName: LongName + "B"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "un nom trop long est une demande malformée, pas une panne du serveur");
    }

    /// <summary>
    /// ⚠ <b>Second témoin.</b> Un étudiant doit porter <em>au moins</em> un nom : relâcher les deux
    /// règles aurait laissé enregistrer une fiche que plus aucun écran ne sait nommer.
    /// </summary>
    [Fact]
    public async Task A_student_with_neither_name_is_refused()
    {
        var response = await SaveAsync(Body(firstName: "", lastName: ""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// ⚠ <b>Troisième témoin.</b> La date de naissance devient facultative, pas ininspectée : une date
    /// effectivement fournie reste soumise à la règle d'âge.
    /// </summary>
    [Fact]
    public async Task A_date_of_birth_that_is_supplied_is_still_checked()
    {
        var response = await SaveAsync(Body(dateOfBirth: DateOnly.FromDateTime(DateTime.Now.AddYears(-3))));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "rendre le champ facultatif ne veut pas dire cesser de lire ce qu'on y écrit");
    }
}
