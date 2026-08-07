using FluentAssertions;
using PGSH.Application.Students.GetMany;
using PGSH.Domain.Students;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

// The student search backs the group lookup and the student list. It must match any of the identifying
// fields, ignore the case the user typed, and survive a pasted value with stray whitespace.
public class StudentSearchTests
{
    private static async Task SeedAsync(ApplicationDbContext db)
    {
        db.SeedCatalog();
        Add("Youssef", "Alaoui",  "CNE100001", "AP2200A");
        Add("Salma",   "Alaoui",  "CNE100002", "AP2200B");
        Add("Karim",   "Benjelloun", "CNE100003", "AP2200C");
        await db.SaveChangesAsync();

        void Add(string first, string last, string cne, string appogee) =>
            db.Users.Add(new Student
            {
                Id = Guid.NewGuid(), FirstName = first, LastName = last,
                Email = $"{first}.{last}@um5.ac.ma".ToLowerInvariant(),
                CNE = cne, Appogee = appogee, BacYear = "2022",
            });
    }

    private static async Task<List<StudentSummaryResponse>> SearchAsync(ApplicationDbContext db, string term)
    {
        var result = await new GetStudentsQueryHandler(db)
            .Handle(new GetStudentsQuery(term, null, null, null), default);

        result.IsSuccess.Should().BeTrue();
        return result.Value.Items.ToList();
    }

    [Fact]
    public async Task A_surname_returns_every_student_who_carries_it()
    {
        await using var db = TestHarness.NewContext("search-surname");
        await SeedAsync(db);

        var found = await SearchAsync(db, "Alaoui");

        found.Should().HaveCount(2, "both Alaouis match — the caller decides which one is meant");
        found.Select(s => s.FirstName).Should().Contain(["Youssef", "Salma"]);
    }

    [Theory]
    [InlineData("alaoui")]
    [InlineData("ALAOUI")]
    [InlineData("AlAoUi")]
    public async Task A_name_matches_whatever_case_was_typed(string term)
    {
        await using var db = TestHarness.NewContext($"search-case-{term}");
        await SeedAsync(db);

        (await SearchAsync(db, term)).Should().HaveCount(2);
    }

    [Theory]
    [InlineData("ap2200a")]
    [InlineData("AP2200A")]
    public async Task An_apogee_number_matches_in_either_case(string term)
    {
        await using var db = TestHarness.NewContext($"search-appogee-{term}");
        await SeedAsync(db);

        var found = await SearchAsync(db, term);

        found.Should().ContainSingle().Which.FirstName.Should().Be("Youssef");
    }

    [Fact]
    public async Task A_cne_finds_exactly_its_owner()
    {
        await using var db = TestHarness.NewContext("search-cne");
        await SeedAsync(db);

        var found = await SearchAsync(db, "CNE100003");

        found.Should().ContainSingle().Which.LastName.Should().Be("Benjelloun");
    }

    [Theory]
    [InlineData("  Alaoui")]
    [InlineData("Alaoui  ")]
    [InlineData("  Alaoui  ")]
    public async Task A_pasted_term_with_stray_whitespace_still_matches(string term)
    {
        await using var db = TestHarness.NewContext($"search-trim-{term.Trim()}-{term.Length}");
        await SeedAsync(db);

        (await SearchAsync(db, term)).Should().HaveCount(2, "a pasted value carries spaces the user cannot see");
    }

    [Fact]
    public async Task A_partial_name_matches_from_the_middle()
    {
        await using var db = TestHarness.NewContext("search-partial");
        await SeedAsync(db);

        (await SearchAsync(db, "jell")).Should().ContainSingle().Which.LastName.Should().Be("Benjelloun");
    }

    [Fact]
    public async Task An_email_matches()
    {
        await using var db = TestHarness.NewContext("search-email");
        await SeedAsync(db);

        (await SearchAsync(db, "salma.alaoui@um5")).Should().ContainSingle();
    }

    [Fact]
    public async Task A_term_matching_nobody_returns_an_empty_page_rather_than_failing()
    {
        await using var db = TestHarness.NewContext("search-none");
        await SeedAsync(db);

        var result = await new GetStudentsQueryHandler(db)
            .Handle(new GetStudentsQuery("zzzz", null, null, null), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().BeEmpty();
        result.Value.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Results_are_ordered_by_surname_so_the_list_is_stable()
    {
        await using var db = TestHarness.NewContext("search-order");
        await SeedAsync(db);

        var found = await SearchAsync(db, "cne1000");

        found.Select(s => s.LastName).Should().BeInAscendingOrder();
    }
}
