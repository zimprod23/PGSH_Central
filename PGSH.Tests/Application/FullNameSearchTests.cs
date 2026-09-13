using FluentAssertions;
using PGSH.Application.AcademicGroups.GetById;
using PGSH.Application.Employees.GetMany;
using PGSH.Application.Search;
using PGSH.Application.Students.GetMany;
using PGSH.Domain.Employees;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// « Je tape le nom complet et rien ne sort » — signalé par la faculté le 12/09/2026.
///
/// <para>Chaque écran comparait le terme <b>entier</b> à chaque colonne : « Mohamed Alami » ne
/// figure ni dans le prénom ni dans le nom, et aucune colonne ne porte les deux, donc la recherche
/// la plus naturelle qui existe ne rendait <b>personne</b>. Un terme est désormais une conjonction
/// de mots — voir <c>SearchTerms</c> et <c>StudentSearch</c>.</para>
///
/// <para>⚠ <b>La règle élargit strictement l'ancienne</b>, et les témoins ci-dessous le vérifient :
/// si la chaîne entière se trouvait dans une colonne, chacun de ses mots s'y trouve aussi. Aucune
/// saisie qui marchait ne cesse de marcher — et <c>StudentSearchTests</c>, à côté, garde intacts les
/// cas d'avant : la casse, l'Apogée, l'e-mail, un terme collé avec ses espaces, un fragment pris au
/// milieu d'un nom.</para>
/// </summary>
public class FullNameSearchTests
{
    private static ApplicationDbContext SeedRoll(string name)
    {
        var db = TestHarness.NewContext(name);
        db.SeedCatalog();

        db.SeedRegistration("Mohamed", "Alami");
        db.SeedRegistration("Fatima", "Alami");
        db.SeedRegistration("Mohamed", "Benali");
        db.SeedRegistration("Zoubair", "Chraibi");
        db.SeedRegistration("Saïd", "Daoudi");

        db.SaveChanges();
        return db;
    }

    private static async Task<List<string>> FoundAsync(ApplicationDbContext db, string? term)
    {
        var result = await new GetStudentsQueryHandler(db).Handle(
            new GetStudentsQuery(term, null, null, null, PageSize: 50), default);

        result.IsSuccess.Should().BeTrue();

        return [.. result.Value.Items.Select(s => $"{s.FirstName} {s.LastName}")];
    }

    /// <summary>L'acte demandé : le nom complet, tapé comme on le prononce.</summary>
    [Fact]
    public async Task A_full_name_finds_the_one_student_who_carries_both_words()
    {
        await using var db = SeedRoll(nameof(A_full_name_finds_the_one_student_who_carries_both_words));

        (await FoundAsync(db, "Mohamed Alami")).Should().Equal("Mohamed Alami");
    }

    /// <summary>
    /// ⚠ <b>L'ordre des mots ne compte pas.</b> La faculté écrit « ALAMI Mohamed » sur ses listes et
    /// « Mohamed Alami » en parlant ; une recherche qui n'accepte qu'une des deux formes se lit comme
    /// un étudiant absent.
    /// </summary>
    [Fact]
    public async Task The_words_may_come_in_either_order()
    {
        await using var db = SeedRoll(nameof(The_words_may_come_in_either_order));

        (await FoundAsync(db, "Alami Mohamed")).Should().Equal("Mohamed Alami");
    }

    /// <summary>
    /// ⚠ <b>Le témoin de l'élargissement.</b> Un seul mot rend exactement ce qu'il rendait avant —
    /// les deux Alami, et les deux Mohamed.
    /// </summary>
    [Fact]
    public async Task One_word_still_matches_every_student_who_carries_it()
    {
        await using var db = SeedRoll(nameof(One_word_still_matches_every_student_who_carries_it));

        (await FoundAsync(db, "alami")).Should().BeEquivalentTo(["Mohamed Alami", "Fatima Alami"]);
        (await FoundAsync(db, "MOHAMED")).Should().BeEquivalentTo(["Mohamed Alami", "Mohamed Benali"]);
    }

    /// <summary>
    /// Une conjonction, pas une disjonction : chaque mot doit se retrouver sur le <b>même</b>
    /// étudiant. Sans cela « Mohamed Chraibi » rendrait trois personnes qui ne s'appellent pas ainsi,
    /// et la recherche ne servirait plus à distinguer qui que ce soit.
    /// </summary>
    [Fact]
    public async Task A_word_that_names_nobody_narrows_to_nobody()
    {
        await using var db = SeedRoll(nameof(A_word_that_names_nobody_narrows_to_nobody));

        (await FoundAsync(db, "Mohamed Chraibi")).Should().BeEmpty();
    }

    /// <summary>
    /// Un nom recopié d'une liste arrive « ALAMI, Mohamed ». La virgule est un séparateur, sans quoi
    /// le premier mot est « alami, » et ne figure dans aucune colonne.
    /// </summary>
    [Fact]
    public async Task A_name_pasted_with_its_comma_still_finds_its_student()
    {
        await using var db = SeedRoll(nameof(A_name_pasted_with_its_comma_still_finds_its_student));

        (await FoundAsync(db, "ALAMI, Mohamed")).Should().Equal("Mohamed Alami");
    }

    /// <summary>
    /// ⚠ <b>Un accent tapé retrouve une colonne qui n'en porte pas</b> — l'import Access a saisi les
    /// prénoms sans accent, les saisies récentes en portent, et l'opérateur ne sait pas laquelle il
    /// regarde.
    /// </summary>
    [Fact]
    public async Task An_accented_word_finds_a_name_stored_without_the_accent()
    {
        await using var db = SeedRoll(nameof(An_accented_word_finds_a_name_stored_without_the_accent));

        (await FoundAsync(db, "Zoubaïr")).Should().Equal("Zoubair Chraibi");
    }

    /// <summary>
    /// ⚠ <b>Et le repli n'est qu'une orthographe de plus.</b> Replier le terme seul ferait perdre
    /// « Saïd » à qui le tape correctement — une régression sur la saisie la plus soignée.
    /// </summary>
    [Fact]
    public async Task An_accented_name_is_still_found_when_it_is_typed_with_its_accent()
    {
        await using var db = SeedRoll(nameof(An_accented_name_is_still_found_when_it_is_typed_with_its_accent));

        (await FoundAsync(db, "Saïd")).Should().Equal("Saïd Daoudi");
    }

    /// <summary>
    /// Les identifiants restent cherchables, y compris mêlés au nom — c'est ce que fait quelqu'un qui
    /// colle une ligne entière de son fichier.
    /// </summary>
    [Fact]
    public async Task An_identifier_is_still_found_and_may_accompany_the_name()
    {
        await using var db = SeedRoll(nameof(An_identifier_is_still_found_and_may_accompany_the_name));

        var alami = db.Students.Single(s => s.LastName == "Alami" && s.FirstName == "Mohamed");

        (await FoundAsync(db, alami.Appogee)).Should().Equal("Mohamed Alami");
        (await FoundAsync(db, $"{alami.Appogee} Alami")).Should().Equal("Mohamed Alami");
    }

    /// <summary>
    /// ⚠ <b>Les colonnes sont les mêmes sur tous les écrans.</b> Le détail d'un groupe cherchait dans
    /// cinq colonnes et les occupants d'un service dans trois : un étudiant trouvé sur son CIN depuis
    /// la liste ne l'était pas depuis son groupe, ce qui se lit comme une absence du groupe.
    /// </summary>
    [Fact]
    public async Task A_roster_search_reaches_the_same_columns_as_the_list()
    {
        await using var db = TestHarness.NewContext(nameof(A_roster_search_reaches_the_same_columns_as_the_list));
        var stage = db.SeedCatalog();
        var cohort = db.SeedCohort(stage, 10, "A");

        var registration = db.SeedRegistration("Mohamed", "Alami", cohort.AcademicGroup);
        registration.Student.CIN = "BE889977";
        db.SeedRegistration("Fatima", "Benali", cohort.AcademicGroup);

        await db.SaveChangesAsync();

        var byCin = await new GetGroupByIdQueryHandler(db).Handle(
            new GetGroupByIdQuery(cohort.AcademicGroupId, SearchTerm: "be889977"), default);

        byCin.IsSuccess.Should().BeTrue();
        byCin.Value.Students.Items.Should().HaveCount(1);

        var byFullName = await new GetGroupByIdQueryHandler(db).Handle(
            new GetGroupByIdQuery(cohort.AcademicGroupId, SearchTerm: "Mohamed Alami"), default);

        byFullName.IsSuccess.Should().BeTrue();
        byFullName.Value.Students.Items.Should().HaveCount(1);
    }

    /// <summary>
    /// La même règle sur la boîte d'où l'on nomme un chef de service : le nom complet d'un
    /// professeur ne se trouvait pas davantage que celui d'un étudiant.
    /// </summary>
    [Fact]
    public async Task An_employee_is_found_by_his_full_name_too()
    {
        await using var db = TestHarness.NewContext(nameof(An_employee_is_found_by_his_full_name_too));

        db.Users.Add(new Employee
        {
            Id = Guid.NewGuid(), FirstName = "Mohamed", LastName = "Alami",
            Email = "m.alami@fmp.ma", PPR = "P445566",
        });
        db.Users.Add(new Employee
        {
            Id = Guid.NewGuid(), FirstName = "Fatima", LastName = "Benali",
            Email = "f.benali@fmp.ma", PPR = "P112233",
        });

        await db.SaveChangesAsync();

        var result = await new GetEmployeesQueryHandler(db).Handle(
            new GetEmployeesQuery("Alami Mohamed", null, null, null, null), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(1);
    }

    /// <summary>
    /// Ce qu'un terme devient, sans base. ⚠ La borne sur le nombre de mots <b>élargit</b> — une
    /// conjonction plus courte retient plus de monde — donc elle ne peut pas cacher la personne
    /// cherchée ; et ni le tiret ni l'apostrophe ne coupent un nom.
    /// </summary>
    [Fact]
    public void A_term_is_split_into_at_most_five_distinct_words()
    {
        SearchTerms.Split("   ").Should().BeEmpty();
        SearchTerms.Split(null).Should().BeEmpty();

        SearchTerms.Split("Alami alami").Select(w => w.Text).Should().Equal("alami");

        SearchTerms.Split("un deux trois quatre cinq six sept")
            .Select(w => w.Text)
            .Should().Equal("un", "deux", "trois", "quatre", "cinq");

        SearchTerms.Split("EL-AMRANI D'ALAMI")
            .Select(w => w.Text)
            .Should().Equal("el-amrani", "d'alami");
    }
}
