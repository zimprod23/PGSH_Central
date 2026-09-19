using AcademicProgram = PGSH.Domain.Common.Utils.AcademicProgram;
using Level = PGSH.Domain.Common.Utils.Level;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.Sqlite;
using NSubstitute;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.AcademicYears;
using PGSH.Application.Audit;
using PGSH.Application.Stages.Delocalization;
using PGSH.Application.Stages.Delocalization.Bulk;
using PGSH.Application.Stages.Evaluations;
using PGSH.Application.Stages.Planning;
using PGSH.Application.Stages.Slots;
using PGSH.Domain.Calendar;
using PGSH.Domain.Employees;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;
using PGSH.SharedKernel;
using PGSH.Infrastructure.Database;
using PGSH.Application.AcademicGroups.BulkAssignment;
using PGSH.Application.AcademicGroups.GroupChange;
using PGSH.Application.Students.Selection;
using PGSH.Application.Stages.InternshipAssignments.Sheet;
using PGSH.Application.Stages.InternshipAssignments.Sheet.Reversal;

namespace PGSH.Tests;

/// <summary>
/// Shared scaffolding for application-layer tests: an isolated in-memory context, a stubbed identity,
/// and builders for the reference graph (year → level → stage → group → cohort → registration) that
/// nearly every handler needs before it has anything to act on.
/// </summary>
/// <remarks>
/// The in-memory provider does not enforce foreign keys, unique indexes or <c>OnDelete</c> behaviour,
/// and never checks that a query is translatable to SQL — constraint and translation defects are
/// invisible here and need the integration suite instead.
/// </remarks>
public static class TestHarness
{
    public const int CurrentYearId  = 1;
    public const int PreviousYearId = 2;
    public const int LevelId        = 1;
    public const int StageId        = 1;
    public const int HospitalId     = 1;

    /// <summary>⚠ 99, not 1: a fixture naming its own centre must not collide with this one.</summary>
    public const int DefaultCenterId = 99;

    /// <summary>The superseded seven-year text. Recorded but governing no intake, so it never wins
    /// version selection — tests that want it name it explicitly.</summary>
    public const int OldCnpnId = 91;

    /// <summary>The six-year text in force, governing entrants from <see cref="CurrentYearId"/>.</summary>
    public const int NewCnpnId = 92;

    /// <remarks>
    /// ⚠ <b>The warning suppression is a statement, not a workaround.</b> The in-memory store has no
    /// transactions, so <c>ExecuteAtomicallyAsync</c> — which is how the macro plan stops a dropped
    /// connection leaving half a plan behind — would throw here rather than run. Ignoring the warning
    /// makes it a no-op instead, which is the honest reading: <b>this suite cannot prove atomicity</b>,
    /// exactly as it cannot prove a FK, a unique index or an <c>OnDelete</c>. It can still prove the
    /// steps inside the unit of work, and that is what the tests here assert.
    /// </remarks>
    public static ApplicationDbContext NewContext(string name) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"{name}-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    /// <summary>
    /// A context on the <b>Npgsql</b> provider that never opens a connection — for asking whether a
    /// query can be turned into SQL at all.
    /// </summary>
    /// <remarks>
    /// ⚠ The in-memory provider runs LINQ against objects and translates nothing, so an untranslatable
    /// query is invisible to every other test in this project until it throws on the real base.
    /// Translation happens at query <i>compile</i> time, before any connection is opened, so this
    /// context needs no database and no Testcontainer: build the query, call <c>ToQueryString()</c>,
    /// and either SQL comes back or the provider says why not. See <c>SqlTranslationTests</c>.
    ///
    /// <para>The connection string is deliberately unusable. Anything that actually executes against
    /// this context is a test that has misunderstood what it is for.</para>
    /// </remarks>
    public static ApplicationDbContext NewNpgsqlContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=translation-only;Username=none;Password=none")
            .Options);

    /// <summary>
    /// A context on <b>SQLite in-memory</b>, schema created — for the acts the in-memory provider
    /// cannot run at all.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Why it had to exist.</b> <c>ExecuteDelete</c> and <c>ExecuteUpdate</c> are
    /// <i>refused outright</i> by the in-memory provider — « not supported by the current database
    /// provider ». So a handler that writes only through them has a success path that <b>no test in
    /// this repository could reach</b>, by any route: the handler tests could only assert its
    /// refusals, and the endpoint tests 500 before they touch it. That is exactly how
    /// <c>DeleteAllGroupsCommand</c> and <c>EmptyAllYearGroupsCommand</c> carried
    /// <c>IAuditableCommand</c> while writing no journal entry at all, from phase 20 to 10/09/2026.</para>
    ///
    /// <para>SQLite is relational, so it runs both, and it also enforces foreign keys and unique
    /// indexes. ⚠ <b>It is still not PostgreSQL</b> — no filtered indexes, no <c>NULLS NOT
    /// DISTINCT</c>, different type affinities — so this is a way to <i>execute</i> those acts, never
    /// a proof about the real schema. Translatability stays <c>SqlTranslationTests</c>'s question and
    /// the real base stays Testcontainers' (still unbuilt).</para>
    ///
    /// <para>⚠ The connection is kept open by the caller: an SQLite in-memory database lives exactly
    /// as long as its connection, so disposing it early empties the store mid-test.</para>
    /// </remarks>
    public static ApplicationDbContext NewSqliteContext(SqliteConnection connection)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options);

        db.Database.EnsureCreated();
        return db;
    }

    /// <summary>An open SQLite in-memory connection, to be disposed after the context.</summary>
    public static SqliteConnection OpenSqlite()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        return connection;
    }

    /// <summary>
    /// A clock stopped on <paramref name="on"/> — midday UTC, so no test straddles a date boundary by
    /// timezone.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Shared because « quel jour est-il ? » is now a question several reads ask</b>, and because
    /// the alternative is the habit that made the retired pause untestable: <c>DateTime.UtcNow</c> read
    /// at the bottom of a class, so « cette promotion est-elle en examens ? » could only ever be asked
    /// about the day the suite happened to run.
    /// </remarks>
    public static IDateTimeProvider ClockOn(DateOnly on)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(on.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc));
        return clock;
    }

    /// <summary>A caller identified by <paramref name="keycloakId"/> holding exactly <paramref name="roles"/>.</summary>
    public static IUserContext UserContext(Guid keycloakId, params string[] roles)
    {
        var ctx = Substitute.For<IUserContext>();
        ctx.UserId.Returns(keycloakId);
        ctx.IsInRole(Arg.Any<string>()).Returns(ci => roles.Contains((string)ci[0]));
        return ctx;
    }

    /// <summary>
    /// An authorizer for a Scolarité caller — the scope most handlers are exercised under, since an
    /// administrative user bypasses the per-service scoping. Tests that care about the scoping itself
    /// build their own with <see cref="UserContext"/>.
    /// </summary>
    internal static ExecutionAuthorizer AdminAuthorizer(this ApplicationDbContext db) =>
        new(db, UserContext(Guid.NewGuid(), Roles.Scolarite));

    /// <summary>An authorizer for a caller holding no role at all — neither admin, chef nor student.</summary>
    internal static ExecutionAuthorizer StrangerAuthorizer(this ApplicationDbContext db) =>
        new(db, UserContext(Guid.NewGuid()));

    /// <summary>
    /// The arranger with its real collaborators. One place, so a new dependency does not have to be
    /// threaded through every planning test — and so no test can accidentally exercise it against a
    /// stubbed partitioning, which is precisely the thing the promotion-wide cut has to be read from.
    /// </summary>
    internal static RotationArranger Arranger(this ApplicationDbContext db) =>
        new(db, new ServiceOccupancyCalculator(db), new PromotionPartitioning(db),
            new GroupScheduleConflictGuard(db));

    /// <summary>
    /// The délocalisation handler with its real collaborators, so a test cannot miss the verdict
    /// writer resolving objectives or the year resolver reading the registration's own year.
    /// </summary>
    internal static DelocalizeStudentCommandHandler DelocalizeHandler(
        this ApplicationDbContext db, ExecutionAuthorizer? authorizer = null)
    {
        var scope = authorizer ?? db.AdminAuthorizer();
        return new DelocalizeStudentCommandHandler(
            db,
            new DelocalizationVerdictWriter(new EvaluationObjectiveResolver(db), scope),
            new AcademicYearResolver(db),
            scope);
    }

    internal static BulkDelocalizationPlanner BulkPlanner(this ApplicationDbContext db) =>
        new(db, new StudentSelectionResolver(db), new AcademicYearResolver(db));

    internal static ApplyBulkDelocalizationCommandHandler BulkDelocalizeHandler(
        this ApplicationDbContext db, ExecutionAuthorizer? authorizer = null) =>
        new(db, db.BulkPlanner(), authorizer ?? db.AdminAuthorizer());

    internal static PreviewBulkDelocalizationQueryHandler BulkDelocalizePreview(
        this ApplicationDbContext db, ExecutionAuthorizer? authorizer = null) =>
        new(db.BulkPlanner(), authorizer ?? db.AdminAuthorizer());

    /// <summary>
    /// The nominative roster assignment with its real collaborators — the same relocator « changement
    /// de groupe » runs and the same affectation service « affecter à un groupe » runs.
    /// </summary>
    /// <remarks>
    /// ⚠ Stubbing either would make the bulk act pass on rules it does not actually share with the
    /// single ones, which is the whole claim the act rests on.
    /// </remarks>
    internal static BulkRosterAssignmentPlanner RosterAssignmentPlanner(this ApplicationDbContext db) =>
        new(db, new StudentSelectionResolver(db), new AcademicYearResolver(db));

    internal static ApplyBulkRosterAssignmentCommandHandler AssignToRosterHandler(
        this ApplicationDbContext db, ExecutionAuthorizer? authorizer = null) =>
        new(db,
            db.RosterAssignmentPlanner(),
            new StudentGroupRelocator(db, new AffectationTollReader(db), new CohortMemberScheduler(db)),
            new StudentAffectationService(db),
            new LateArrivalScheduler(db),
            authorizer ?? db.AdminAuthorizer());

    internal static PreviewBulkRosterAssignmentQueryHandler AssignToRosterPreview(
        this ApplicationDbContext db, ExecutionAuthorizer? authorizer = null) =>
        new(db.RosterAssignmentPlanner(), authorizer ?? db.AdminAuthorizer());

    /// <summary>
    /// The canevas des affectations with its real planner. ⚠ The audit trail is handed back with the
    /// handler rather than built inside it: this act's whole claim is that it says <i>how much</i> it
    /// destroyed, and a constat nobody can read is a constat nobody can test.
    /// </summary>
    internal static (ApplyAffectationSheetCommandHandler Handler, RecordingAuditTrail Trail)
        AffectationSheetHandler(this ApplicationDbContext db, ExecutionAuthorizer? authorizer = null)
    {
        var trail = new RecordingAuditTrail();
        return (new ApplyAffectationSheetCommandHandler(
            db, db.AffectationSheetPlanner(), authorizer ?? db.AdminAuthorizer(), trail), trail);
    }

    /// <summary>
    /// The three grid-cell acts with their real guards. ⚠ Real, not stubbed: what these tests assert is
    /// precisely the interplay between the publication guard and the overlap/conflict ones, and a
    /// stubbed guard would make the ordering untestable.
    /// </summary>
    /// <summary>
    /// Une date antérieure à toute fenêtre qu'une fixture pose, donc « rien n'a encore commencé ».
    /// ⚠ Nommée plutôt que glissée en littéral : depuis que la mobilité est <b>datée</b>
    /// (<c>ServicePeriodLifecycle.MovableOn</c>), le jour où l'acte est joué décide de ce qu'il
    /// accepte, et un test qui ne le dit pas dépend du calendrier de la machine.
    /// </summary>
    internal static readonly DateOnly BeforeAnyWindow = new(2000, 1, 1);

    internal static UpdateStageSlotCommandHandler UpdateSlotHandler(
        this ApplicationDbContext db, DateOnly? on = null) =>
        new(db, new SlotOverlapGuard(db), new GroupScheduleConflictGuard(db),
            new PublishedPeriodShifter(db), new RecordingAuditTrail(),
            ClockOn(on ?? BeforeAnyWindow));

    internal static SetCohortSlotAssignmentCommandHandler SetCellHandler(this ApplicationDbContext db) =>
        new(db, new GroupScheduleConflictGuard(db), new RecordingAuditTrail());

    internal static ClearCohortSlotAssignmentCommandHandler ClearCellHandler(this ApplicationDbContext db) =>
        new(db, new RecordingAuditTrail());

    internal static AffectationSheetPlanner AffectationSheetPlanner(this ApplicationDbContext db) =>
        new(db, new AcademicYearResolver(db));

    /// <summary>
    /// The undo, with the same trail handed back for the same reason: its whole claim is that it says
    /// how much it removed and how much it put back.
    /// </summary>
    internal static (ReverseAffectationImportCommandHandler Handler, RecordingAuditTrail Trail)
        ReverseImportHandler(this ApplicationDbContext db, ExecutionAuthorizer? authorizer = null)
    {
        var trail = new RecordingAuditTrail();
        return (new ReverseAffectationImportCommandHandler(
            db, new AffectationImportReversalPlanner(db), authorizer ?? db.AdminAuthorizer(), trail), trail);
    }

    internal static GetOrphanedAffectationImportsQueryHandler OrphanedImportsHandler(
        this ApplicationDbContext db, ExecutionAuthorizer? authorizer = null) =>
        new(db, new AcademicYearResolver(db), authorizer ?? db.AdminAuthorizer());

    /// <summary>⚠ The trail comes back with it: this act's whole claim is that it says what it removed,
    /// and it is the one deletion the business register does not keep.</summary>
    internal static (PurgeOrphanedAffectationImportsCommandHandler Handler, RecordingAuditTrail Trail)
        PurgeImportsHandler(this ApplicationDbContext db, ExecutionAuthorizer? authorizer = null)
    {
        var trail = new RecordingAuditTrail();
        return (new PurgeOrphanedAffectationImportsCommandHandler(
            db, new AcademicYearResolver(db), authorizer ?? db.AdminAuthorizer(), trail), trail);
    }

    internal static PreviewAffectationImportReversalQueryHandler ReverseImportPreview(
        this ApplicationDbContext db, ExecutionAuthorizer? authorizer = null) =>
        new(new AffectationImportReversalPlanner(db), authorizer ?? db.AdminAuthorizer());

    internal static PreviewAffectationSheetQueryHandler AffectationSheetPreview(
        this ApplicationDbContext db, ExecutionAuthorizer? authorizer = null) =>
        new(db.AffectationSheetPlanner(), authorizer ?? db.AdminAuthorizer());

    /// <summary>The current academic year plus the level and stage every cohort hangs off.</summary>
    public static Stage SeedCatalog(this ApplicationDbContext db, DateOnly? yearStart = null, DateOnly? yearEnd = null)
    {
        db.AcademicYears.Add(new AcademicYear
        {
            Id = CurrentYearId, Label = "2025-2026", IsCurrent = true,
            StartDate = yearStart ?? new DateOnly(2025, 9, 1),
            EndDate   = yearEnd   ?? new DateOnly(2026, 8, 31),
        });

        // Program stated rather than left to default: AcademicProgram's zero value is Master, so an
        // unset level silently disagreed with every Médecine CNPN.
        var level = new Level
        {
            Id = LevelId, Label = "3ème année", Year = 3, AcademicProgram = AcademicProgram.Medecine,
        };
        var stage = new Stage { Id = StageId, Name = "Cardiologie", LevelId = LevelId, Level = level, Coefficient = 2 };
        db.Levels.Add(level);
        db.Stages.Add(stage);

        // Two texts, as the real data now has them: one superseded and governing nobody, one in force.
        db.SeedCnpnVersion(OldCnpnId, "2174.18", totalYears: 7, appliesFromAcademicYearId: null);
        db.SeedCnpnVersion(NewCnpnId, "1650.25", totalYears: 6);

        return stage;
    }

    /// <summary>
    /// A CNPN text. <paramref name="appliesFromAcademicYearId"/> is the first intake it governs;
    /// null records a text kept for citation that never governed one (as arrêté 2175.22 became).
    /// </summary>
    public static CnpnVersion SeedCnpnVersion(
        this ApplicationDbContext db, int id, string code, int totalYears,
        AcademicProgram program = AcademicProgram.Medecine,
        int? appliesFromAcademicYearId = CurrentYearId)
    {
        var version = new CnpnVersion
        {
            Id = id, Code = code, Label = $"CNPN {code} ({totalYears} ans)",
            AcademicProgram = program, TotalYears = totalYears,
            AppliesToEntrantsFromAcademicYearId = appliesFromAcademicYearId,
            // The nav, not just the key: version selection reads StartDate through it, and the
            // in-memory provider does not fix up a reference from a foreign key alone.
            AppliesToEntrantsFromAcademicYear = appliesFromAcademicYearId is { } yearId
                ? db.AcademicYears.Local.FirstOrDefault(y => y.Id == yearId)
                : null,
        };
        db.CnpnVersions.Add(version);
        return version;
    }

    /// <summary>
    /// « Ce texte régit tel niveau à partir de telle année ». Navigations are set explicitly for the
    /// same reason <see cref="SeedCnpnVersion"/> does it: resolution compares
    /// <c>FromAcademicYear.StartDate</c>, and the in-memory provider will not fix up a reference from
    /// a foreign key alone before the first save.
    /// </summary>
    public static CnpnLevelEffectivity SeedEffectivity(
        this ApplicationDbContext db, int id, int cnpnVersionId, int levelId, int fromAcademicYearId)
    {
        var effectivity = new CnpnLevelEffectivity
        {
            Id = id,
            CnpnVersionId = cnpnVersionId,
            LevelId = levelId,
            FromAcademicYearId = fromAcademicYearId,
            RecordedOn = DateTime.UtcNow,
            CnpnVersion = db.CnpnVersions.Local.FirstOrDefault(v => v.Id == cnpnVersionId)!,
            Level = db.Levels.Local.FirstOrDefault(l => l.Id == levelId)!,
            FromAcademicYear = db.AcademicYears.Local.FirstOrDefault(y => y.Id == fromAcademicYearId)!,
        };
        db.CnpnLevelEffectivities.Add(effectivity);
        return effectivity;
    }

    /// <summary>
    /// An earlier academic year, for the repeating student: the same level registered twice, once per
    /// year. Defaults to the year before <see cref="SeedCatalog"/>'s.
    /// </summary>
    public static AcademicYear SeedAcademicYear(
        this ApplicationDbContext db, int id, string label, DateOnly start, DateOnly end)
    {
        var year = new AcademicYear { Id = id, Label = label, StartDate = start, EndDate = end };
        db.AcademicYears.Add(year);
        return year;
    }

    /// <summary>An additional stage on the shared level — a level is a set of stages, not just one.</summary>
    public static Stage SeedStage(
        this ApplicationDbContext db, int stageId, string name, int coefficient = 1, int levelId = LevelId)
    {
        var level = db.Levels.Local.First(l => l.Id == levelId);
        var stage = new Stage
        {
            Id = stageId, Name = name, LevelId = levelId, Level = level, Coefficient = coefficient,
        };
        db.Stages.Add(stage);
        return stage;
    }

    /// <summary>
    /// A second promotion, for the rules that only bite when two of them meet: a service's per-level
    /// quotas, and the physical ceiling they share. A <see cref="Level"/> is (programme × année), so
    /// this is how "1ère année Pharmacie" is expressed.
    /// </summary>
    public static Level SeedLevel(
        this ApplicationDbContext db, int levelId, string label, int year,
        AcademicProgram program = AcademicProgram.Medecine)
    {
        var level = new Level { Id = levelId, Label = label, Year = year, AcademicProgram = program };
        db.Levels.Add(level);
        return level;
    }

    /// <summary>
    /// Grants <paramref name="service"/> an intake quota for one level. ⚠ The first call also
    /// <i>restricts</i> the service: from then on it admits no level without a row of its own.
    /// </summary>
    public static ServiceLevelCapacity SeedLevelCapacity(
        this ApplicationDbContext db, Service service, int levelId, int capacity)
    {
        var quota = new ServiceLevelCapacity { Service = service, LevelId = levelId, Capacity = capacity };
        service.LevelCapacities.Add(quota);
        db.ServiceLevelCapacities.Add(quota);
        return quota;
    }

    /// <summary>
    /// A second hospital. Most fixtures need only the default one <see cref="SeedService"/> creates,
    /// but anything asking « où ce groupe est-il placé » needs at least two — with one hospital every
    /// placement is trivially at it, and a rule that reads « tout au militaire » would pass without
    /// ever distinguishing anything.
    /// </summary>
    public static Hospital SeedHospital(
        this ApplicationDbContext db, int hospitalId, string name, string city = "Rabat")
    {
        var hospital = new Hospital
        {
            Id = hospitalId, Name = name, City = city, CenterId = db.DefaultCenter().Id,
        };
        db.Hospitals.Add(hospital);
        return hospital;
    }

    /// <summary>
    /// The centre every seeded hospital hangs off, created once per context.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>It was missing, and only a relational provider could say so.</b> <c>Hospital.CenterId</c>
    /// is a non-nullable foreign key, and the fixtures left it at <c>0</c> — a hospital in a centre
    /// that does not exist. The in-memory provider ignores foreign keys, so every fixture in this
    /// project was building a graph PostgreSQL would refuse, and nothing said so until
    /// <see cref="NewSqliteContext"/> ran one. Idempotent, and it never takes an id a fixture might
    /// have chosen for a centre of its own.
    /// </remarks>
    public static Center DefaultCenter(this ApplicationDbContext db)
    {
        var existing = db.Centers.Local.FirstOrDefault(c => c.Id == DefaultCenterId);
        if (existing is not null) return existing;

        var centre = new Center { Id = DefaultCenterId, Name = "CHU Ibn Sina", City = "Rabat" };
        db.Centers.Add(centre);
        return centre;
    }

    /// <summary>
    /// A hospital service, optionally led by <paramref name="chef"/> (who is added to its staff first)
    /// and optionally in a hospital other than the default one — see <see cref="SeedHospital"/>.
    /// </summary>
    public static Service SeedService(
        this ApplicationDbContext db, int serviceId, string name, Employee? chef = null,
        int? hospitalId = null, bool isExternal = false)
    {
        int wanted = hospitalId ?? HospitalId;

        var hospital = db.Hospitals.Local.FirstOrDefault(h => h.Id == wanted);
        if (hospital is null)
        {
            hospital = new Hospital
            {
                Id = wanted, Name = "CHU Ibn Sina", City = "Rabat", CenterId = db.DefaultCenter().Id,
            };
            db.Hospitals.Add(hospital);
        }

        // The foreign key is stated as well as the navigation: a query reading Service.HospitalId
        // before SaveChanges has fixed it up would see 0 and place every service in no hospital.
        var service = new Service
        {
            Id = serviceId, Name = name, Description = "",
            HospitalId = hospital.Id, Hospital = hospital, Capacity = 20,
            IsExternal = isExternal,
        };

        if (chef is not null)
        {
            service.AddStaff(chef);
            service.AssignChef(chef);
        }

        db.Services.Add(service);
        return service;
    }

    /// <summary>
    /// Adds <paramref name="services"/> to a stage's allowed-services whitelist.
    /// </summary>
    /// <remarks>
    /// ⚠ The empty list is not the neutral state: <c>SetCohortSlotAssignmentCommandHandler</c>
    /// enforces the whitelist « when configured », so a stage with none is open to every service. A
    /// fixture that never calls this is therefore asserting « personne n'a saisi la liste », which is
    /// a real state of the catalogue and a different one from « ce service n'est pas autorisé ».
    /// </remarks>
    public static Stage Allow(this ApplicationDbContext db, Stage stage, params Service[] services)
    {
        foreach (var service in services)
            stage.AllowedServices.Add(service);

        return stage;
    }

    /// <summary>
    /// Authorises <paramref name="services"/> <b>and</b> ranks them in the order given — the order
    /// <c>RotationArranger</c> then walks them in.
    /// </summary>
    /// <remarks>
    /// ⚠ <c>Allow</c> deliberately leaves the rank at 0, i.e. « personne n'a choisi l'ordre », which
    /// is the state of every stage before somebody authors one and the reason the arranger falls
    /// back to id order. A fixture that wants to assert an authored order has to say so, or it is
    /// asserting the fallback.
    /// </remarks>
    public static Stage AllowInOrder(
        this ApplicationDbContext db, Stage stage, params Service[] services)
    {
        // ⚠ The join row only, never also stage.AllowedServices.Add — the navigation and the
        // payload entity are two ways of writing the same row, and doing both makes EF track two
        // instances of one key. The skip navigation is fixed up from the join once both ends are
        // tracked, which is what every read here goes through.
        for (int i = 0; i < services.Length; i++)
            db.StageAllowedServices.Add(new StageAllowedService
            {
                StageId = stage.Id, ServiceId = services[i].Id, Rank = i + 1,
            });

        return stage;
    }

    /// <summary>
    /// Holds an already-authorised service for named rosters: the rotation stops drawing it, and only
    /// a pinned cell puts anybody there.
    /// </summary>
    /// <remarks>
    /// ⚠ Written onto the join row the authorisation already has, never as a second row — the mode is
    /// a property of « ce service est autorisé pour ce stage », not a second authorisation. A fixture
    /// that adds one instead makes EF track two instances of one key.
    /// </remarks>
    public static Stage Reserve(this ApplicationDbContext db, Stage stage, Service service)
    {
        var authorisation = db.StageAllowedServices.Local
            .FirstOrDefault(a => a.StageId == stage.Id && a.ServiceId == service.Id)
            ?? db.StageAllowedServices
                .FirstOrDefault(a => a.StageId == stage.Id && a.ServiceId == service.Id)
            ?? throw new InvalidOperationException(
                $"Service {service.Id} is not authorised for stage {stage.Id}: authorise it first.");

        authorisation.PlacementMode = ServicePlacementMode.Reserved;
        return stage;
    }

    public static Employee SeedChef(this ApplicationDbContext db, Guid keycloakId, string email = "chef@pgsh.ma")
    {
        var chef = new Employee { Id = Guid.NewGuid(), Email = email, Position = Position.ServiceChef };
        chef.LinkIdentity(keycloakId.ToString());
        db.Users.Add(chef);
        return chef;
    }

    /// <summary>A group and its cohort for <paramref name="stage"/> — the pair a rotation is planned against.</summary>
    public static Cohort SeedCohort(
        this ApplicationDbContext db, Stage stage, int groupId, string groupLabel,
        int academicYearId = CurrentYearId)
    {
        // The roster takes the stage's promotion. A roster is identified by (year, level, number) and
        // a cohorte cannot straddle two promotions, so a level-less group here would seed a state the
        // handlers now refuse to create — and quietly exempt every test built on it from the rule.
        var group = NewGroup(groupId, academicYearId, stage.LevelId, groupId, groupLabel);
        var cohort = NewCohort(groupId, stage, group, groupLabel);
        db.AcademicGroups.Add(group);
        db.Cohorts.Add(cohort);
        return cohort;
    }

    /// <summary>
    /// A roster on its own, with an explicit number and partition label. <see cref="SeedCohort"/>
    /// covers the common case of one group taking one stage; a level whose groups rotate through
    /// several stages needs the roster separate from the cohorts hanging off it.
    /// </summary>
    /// <param name="levelId">
    /// ⚠ The promotion, and it is not optional in the sense a default makes it look: a roster is
    /// keyed (année, niveau, numéro), so two fixtures sharing a number across two promotions are two
    /// rosters and sharing it inside one is a collision. A test covering two promotions passes it.
    /// There is deliberately no null to pass — a roster without a promotion is « Non réparti », which
    /// has <see cref="SeedUnassignedBucket"/>.
    /// </param>
    public static AcademicGroup SeedGroup(
        this ApplicationDbContext db, int groupId, int groupNumber, string? rotationGroup = null,
        int academicYearId = CurrentYearId, int levelId = LevelId, string? label = null)
    {
        var group = NewGroup(
            groupId, academicYearId, levelId, groupNumber, label ?? $"G{groupNumber}", rotationGroup);
        db.AcademicGroups.Add(group);
        return group;
    }

    /// <summary>Attaches an existing roster to <paramref name="stage"/> — one cohort per (group, stage).</summary>
    public static Cohort SeedCohortFor(
        this ApplicationDbContext db, Stage stage, AcademicGroup group, int cohortId)
    {
        var cohort = NewCohort(cohortId, stage, group, $"{stage.Name} · {group.Label}");
        db.Cohorts.Add(cohort);
        return cohort;
    }

    /// <summary>
    /// The year's « Non réparti » — the roster that belongs to no promotion, seeded through the
    /// factory that says so.
    /// </summary>
    /// <remarks>
    /// ⚠ It gets its own helper for the reason it has its own factory: a fixture reaching for
    /// <see cref="SeedGroup"/> and leaving the level out would be seeding the bucket <i>by
    /// accident</i>, which is the shape of the incident the pair exists to prevent. A test asserting
    /// that the bucket is excluded from something has to be able to say it is seeding one.
    /// </remarks>
    public static AcademicGroup SeedUnassignedBucket(
        this ApplicationDbContext db, int groupId, int academicYearId = CurrentYearId,
        string label = "Non réparti")
    {
        var made = AcademicGroup.AsUnassignedBucket(academicYearId, label);
        Ensure(made);

        made.Value.Id = groupId;
        db.AcademicGroups.Add(made.Value);
        return made.Value;
    }

    /// <summary>
    /// Un groupe de promotion, avec son identifiant fixé — ce que <c>AcademicGroup.ForPromotion</c>
    /// ne donne pas, puisqu'un identifiant est au magasin et non au domaine.
    /// </summary>
    /// <remarks>
    /// ⚠ Passe par la fabrique comme le code de production, et c'est le but : une fixture qui
    /// construirait un groupe autrement pourrait en poser un que l'application ne sait pas produire.
    /// Le niveau est <c>int?</c> parce que <c>Stage.LevelId</c> l'est, et un <c>null</c> y est refusé
    /// bruyamment plutôt que traduit en « Non réparti » — c'est exactement la confusion que
    /// <see cref="SeedUnassignedBucket"/> existe pour empêcher.
    /// </remarks>
    public static AcademicGroup NewGroup(
        int groupId, int academicYearId, int? levelId, int groupNumber, string label,
        string? rotationGroup = null, string? geographicZone = null, string? purpose = null)
    {
        if (levelId is not { } promotion)
            throw new InvalidOperationException(
                "Fixture invalide — un groupe de promotion porte un niveau. Un groupe sans "
                + "promotion est « Non réparti » : utilisez SeedUnassignedBucket.");

        var made = AcademicGroup.ForPromotion(
            academicYearId, promotion, groupNumber, label, geographicZone, rotationGroup, purpose);

        Ensure(made);
        made.Value.Id = groupId;
        return made.Value;
    }

    /// <summary>
    /// Une cohorte, avec son identifiant fixé. Même raison et même forme que <see cref="NewSlot"/>.
    /// </summary>
    /// <remarks>
    /// ⚠ Les deux navigations sont posées en plus des clés : le fournisseur en mémoire les résout
    /// depuis le change tracker, mais une fixture qui construit son graphe sans l'ajouter au contexte
    /// ne lui donne rien à résoudre.
    /// </remarks>
    public static Cohort NewCohort(int cohortId, Stage stage, AcademicGroup group, string label)
    {
        var cohort = NewCohort(cohortId, stage.Id, group.Id, label);
        cohort.Stage = stage;
        cohort.AcademicGroup = group;
        return cohort;
    }

    /// <summary>
    /// The same, by key alone — for a fixture whose stage and roster are rows it added rather than
    /// objects it holds. The navigations are then EF's to fix up.
    /// </summary>
    public static Cohort NewCohort(int cohortId, int stageId, int academicGroupId, string label)
    {
        var made = Cohort.For(stageId, academicGroupId, label);
        Ensure(made);

        made.Value.Id = cohortId;
        return made.Value;
    }

    /// <summary>
    /// ⚠ Une fixture qui viole l'identité d'un objet de planification est un <b>bug de la fixture</b>,
    /// pas un cas de test : elle poserait une ligne qu'aucun chemin réel ne peut produire, et le test
    /// bâti dessus ne prouverait rien. Elle échoue donc bruyamment, ici, plutôt qu'en assertion.
    /// </summary>
    private static void Ensure<T>(Result<T> made)
    {
        if (made.IsFailure)
            throw new InvalidOperationException(
                $"Fixture invalide — {made.Error.Code} : {made.Error.Description}");
    }

    /// <summary>A student with this year's registration, optionally attached to <paramref name="group"/>.</summary>
    public static Registration SeedRegistration(
        this ApplicationDbContext db, string firstName, string lastName, AcademicGroup? group = null,
        int academicYearId = CurrentYearId, int levelId = LevelId)
    {
        var student = new Student
        {
            Id = Guid.NewGuid(), FirstName = firstName, LastName = lastName,
            Email = $"{firstName}.{lastName}@etu.ma".ToLowerInvariant(),
            CNE = $"CNE{Guid.NewGuid():N}"[..10], Appogee = $"AP{Guid.NewGuid():N}"[..8], BacYear = "2022",
            // Stated for the same reason SeedCatalog states the level's: AcademicProgram's zero value
            // is Master, so a student left to the default silently disagrees with the Médecine level
            // he is registered in — which reads as a réorientation to anything comparing the two.
            AcademicProgram = AcademicProgram.Medecine,
        };
        var registration = new Registration
        {
            Id = Guid.NewGuid(), AcademicYearId = academicYearId, LevelId = levelId,
            StudentId = student.Id, Student = student, AcademicGroupId = group?.Id,
        };
        db.Users.Add(student);
        db.Registrations.Add(registration);
        return registration;
    }

    /// <summary>An assignment on <paramref name="cohort"/> carrying its initial membership record.</summary>
    public static InternshipAssignment SeedAssignment(
        this ApplicationDbContext db, Registration registration, Cohort cohort, DateOnly? enrolledOn = null)
    {
        var assignment = new InternshipAssignment
        {
            Id = Guid.NewGuid(),
            RegistrationId = registration.Id, Registration = registration,
            CurrentCohortId = cohort.Id, Cohort = cohort,
        };
        assignment.MembershipHistory.Add(new CohortMembership
        {
            Id = Guid.NewGuid(), InternshipAssignmentId = assignment.Id,
            CohortId = cohort.Id, StartDate = enrolledOn ?? new DateOnly(2025, 9, 1),
        });
        db.InternshipAssignments.Add(assignment);
        return assignment;
    }

    /// <summary>A rotation of <paramref name="assignment"/> in <paramref name="service"/>.</summary>
    public static ServicePeriod SeedPeriod(
        this ApplicationDbContext db, InternshipAssignment assignment, Service service,
        DateOnly start, DateOnly end, bool started = true, bool complete = false)
    {
        var period = new ServicePeriod
        {
            Id = Guid.NewGuid(),
            InternshipAssignmentId = assignment.Id, InternshipAssignment = assignment,
            ServiceId = service.Id, Service = service,
            StartDate = start, EndDate = end,
            IsStarted = started, IsComplete = complete,
        };
        assignment.ServicePeriods.Add(period);
        db.ServicePeriods.Add(period);
        return period;
    }

    /// <summary>
    /// A rotation standing in the store as <b>suspended</b>: the flag <i>and</i> its open
    /// <see cref="PeriodPause"/> row, which are one state and not two.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>There is deliberately no act that produces this any more.</b> The stage-scoped
    /// pause was retired on 18/09/2026 — it extended a période by <i>calendar</i> days, moved the
    /// student's dates without the grid, and accumulated on a replay. What still produces the state
    /// is an import reversal putting back a période that already carried it
    /// (<c>RestoredPeriod.IsPaused</c>), so the readers must keep showing it and this is how a test
    /// poses it.</para>
    ///
    /// <para>⚠ <b>Setting the flag alone would be a state no path can produce</b> — a période
    /// suspended with nothing saying since when or why — and a test built on it would prove nothing.
    /// That is the whole reason this is a helper rather than two lines per file.</para>
    /// </remarks>
    public static ServicePeriod SeedPausedPeriod(
        this ApplicationDbContext db, InternshipAssignment assignment, Service service,
        DateOnly start, DateOnly end, DateOnly pausedOn, PauseKind kind, string? reason)
    {
        var period = db.SeedPeriod(assignment, service, start, end);
        period.IsPaused = true;
        period.Pauses.Add(new PeriodPause
        {
            ServicePeriodId = period.Id,
            StartDate       = pausedOn,
            Kind            = kind,
            Reason          = reason,
        });
        return period;
    }

    /// <summary>
    /// An assignment carried all the way to a verdict: one rotation, started, closed, then marked
    /// <paramref name="mark"/> out of 20. Goes through the real lifecycle rather than back-filling
    /// <c>FinalScore</c>/<c>Result</c> — those have private setters precisely so the verdict can only
    /// come from marks the domain actually rolled up. A mark of 10 or more yields <c>Validé</c>.
    /// </summary>
    public static InternshipAssignment SeedGradedAssignment(
        this ApplicationDbContext db, Registration registration, Cohort cohort, Service service,
        decimal mark, DateOnly? from = null)
    {
        var assignment = db.SeedAssignment(registration, cohort);
        var start = from ?? new DateOnly(2025, 10, 1);
        var period = db.SeedPeriod(assignment, service, start, start.AddDays(30), started: false);

        assignment.Start();
        assignment.CompletePeriod(period.Id);

        // No pre-set Id: the evaluation joins an already-tracked assignment, where a store-generated
        // key makes EF classify it Modified instead of Added.
        assignment.SubmitEvaluation(period.Id, new ServiceEvaluation
        {
            ServicePeriodId = period.Id,
            Mode            = EvaluationMode.Numeric,
            TotalScore      = mark,
        });

        return assignment;
    }

    /// <summary>
    /// A period of the stage's grid (P1, P2…) — the window every cohort is routed through. Slots are
    /// per (stage, year): <paramref name="academicYearId"/> defaults to the current one so the common
    /// case stays a one-liner, and a test covering two promotions passes it explicitly.
    /// </summary>
    public static StageSlot SeedSlot(
        this ApplicationDbContext db, Stage stage, int slotId, int periodNumber, DateOnly start, DateOnly end,
        int? academicYearId = null)
    {
        var slot = NewSlot(slotId, stage.Id, academicYearId ?? CurrentYearId, periodNumber, start, end);
        db.StageSlots.Add(slot);
        return slot;
    }

    /// <summary>
    /// Un créneau, avec son identifiant fixé — ce que <c>StageSlot.For</c> ne donne pas, puisqu'un
    /// identifiant est au magasin et non au domaine.
    /// </summary>
    /// <remarks>
    /// ⚠ Passe par <c>StageSlot.For</c> comme le code de production, et c'est le but : une fixture
    /// qui construirait un créneau autrement pourrait en fabriquer un que l'application ne sait pas
    /// produire — et c'est arrivé, <c>MidStageTransferReschedulerTests</c> en posait un **sans
    /// année**, ce qu'aucun chemin réel ne peut plus faire.
    /// </remarks>
    public static StageSlot NewSlot(
        int slotId, int stageId, int academicYearId, int periodNumber,
        DateOnly start, DateOnly end, string? label = null)
    {
        var made = StageSlot.For(stageId, academicYearId, periodNumber, start, end, label);
        Ensure(made);

        made.Value.Id = slotId;
        return made.Value;
    }

    /// <summary>One cell of the planning grid: this cohort spends this slot in this service.</summary>
    /// <param name="source">
    /// ⚠ <c>Arranged</c> by default, which is what the rotation writes. A fixture asserting that a
    /// hand-authored placement survives has to say <c>Pinned</c>, or it is asserting nothing.
    /// </param>
    public static CohortSlotAssignment SeedSlotAssignment(
        this ApplicationDbContext db, int id, Cohort cohort, StageSlot slot, Service service,
        CellSource source = CellSource.Arranged)
    {
        var assignment = new CohortSlotAssignment
        {
            Id = id, CohortId = cohort.Id, Cohort = cohort,
            StageSlotId = slot.Id, StageSlot = slot,
            ServiceId = service.Id, Service = service,
            Source = source,
        };
        db.CohortSlotAssignments.Add(assignment);
        return assignment;
    }

    /// <summary>
    /// Marks <paramref name="cell"/> as published by <paramref name="period"/> — both the foreign key
    /// and the coverage row, which is what a real publish writes.
    ///
    /// <para>⚠ The two are not interchangeable. <c>ServicePeriod.CohortSlotAssignmentId</c> names the
    /// <b>first</b> cell of a run; <c>ServicePeriodSlotCoverage</c> carries one row per covered cell,
    /// and it is the one every guard reads. A test that sets only the FK proves nothing about a run
    /// under <c>SingleService</c>, where the trailing cells have no FK pointing at them.</para>
    /// </summary>
    public static ServicePeriodSlotCoverage SeedCoverage(
        this ApplicationDbContext db, ServicePeriod period, CohortSlotAssignment cell, bool leadCell = true)
    {
        if (leadCell)
        {
            period.CohortSlotAssignmentId = cell.Id;
            period.CohortSlotAssignment = cell;
        }

        var coverage = new ServicePeriodSlotCoverage
        {
            ServicePeriodId = period.Id, ServicePeriod = period,
            CohortSlotAssignmentId = cell.Id, CohortSlotAssignment = cell,
        };

        period.SlotCoverage.Add(coverage);
        db.ServicePeriodSlotCoverage.Add(coverage);
        return coverage;
    }

    /// <summary>
    /// One day of présence against <paramref name="period"/> — the fact that makes a rotation
    /// un-movable and un-rewritable without announcing itself anywhere.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>The quiet half of « ce que rien ne remet ».</b> A mark shows on every screen; a journée de
    /// présence is invisible until the day somebody needs it, and <c>AttendanceRecord</c> cascades from
    /// <c>ServicePeriod</c> — so an act that rewrites a rotation deletes them in silence. Any fixture
    /// exercising a guard that must refuse over attendance goes through this rather than reaching for
    /// the entity, so the guards and the reports read one shape.
    /// </remarks>
    public static AttendanceRecord SeedAttendance(
        this ApplicationDbContext db, ServicePeriod period, DateOnly? date = null,
        AttendanceStatus status = AttendanceStatus.Present)
    {
        var record = new AttendanceRecord
        {
            Id = Guid.NewGuid(),
            ServicePeriodId = period.Id,
            ServicePeriod = period,
            Date = date ?? period.StartDate,
            Status = status,
        };

        period.Attendance.Add(record);
        db.AttendanceRecords.Add(record);
        return record;
    }

    /// <summary>
    /// Requires <paramref name="stage"/> of the text <paramref name="cnpnVersionId"/> — the row that
    /// makes « ce que l'étudiant doit » a fact about a CNPN rather than about the stage.
    /// </summary>
    /// <remarks>
    /// ⚠ <c>CurriculumStages.StageId</c> is <c>RESTRICT</c>, so this is also what a stage delete has
    /// to refuse over. The in-memory provider holds no foreign key, so the refusal has to be the
    /// handler's — see <c>DeleteStageGuardTests</c>.
    /// </remarks>
    public static CurriculumStage SeedCurriculumStage(
        this ApplicationDbContext db, int cnpnVersionId, Stage stage,
        int coefficient = 1, int durationInDays = 30)
    {
        var curriculum = db.Curriculums.Local
            .FirstOrDefault(c => c.CnpnVersionId == cnpnVersionId && c.LevelId == stage.LevelId);

        if (curriculum is null)
        {
            curriculum = new Curriculum
            {
                Id = cnpnVersionId * 1000 + stage.LevelId,
                LevelId = stage.LevelId,
                CnpnVersionId = cnpnVersionId,
            };
            db.Curriculums.Add(curriculum);
        }

        var required = new CurriculumStage
        {
            CurriculumId = curriculum.Id, Curriculum = curriculum,
            StageId = stage.Id, Stage = stage,
            Coefficient = coefficient, DurationInDays = durationInDays,
        };

        db.CurriculumStages.Add(required);
        return required;
    }

    public static StageObjective SeedObjective(
        this ApplicationDbContext db, Stage stage, int id, string label, int weight, bool mandatory = false)
    {
        var objective = new StageObjective
        {
            Id = id, StageId = stage.Id, Stage = stage,
            Label = label, Weight = weight, IsMandatory = mandatory,
        };
        db.StageObjectives.Add(objective);
        return objective;
    }

    /// <summary>
    /// A non-working stretch. <paramref name="days"/> defaults to one, so the common single-day férié stays
    /// a one-liner while Aïd (two days) and vacances (a fortnight) pass a count.
    /// </summary>
    public static Holiday SeedHoliday(
        this ApplicationDbContext db, DateOnly start, string name, int days = 1,
        HolidayKind kind = HolidayKind.National, bool confirmed = true,
        bool countsAsWorkingDay = false)
    {
        var holiday = new Holiday
        {
            StartDate = start,
            EndDate = start.AddDays(days - 1),
            Name = name,
            Kind = kind,
            IsConfirmed = confirmed,
            CountsAsWorkingDay = countsAsWorkingDay,
        };
        db.Holidays.Add(holiday);
        return holiday;
    }

    /// <summary>
    /// A window during which one promotion is out of its services. ⚠ Keyed on (année, niveau) — a level
    /// alone is not a promotion, and a pause on one is a pause on none.
    /// </summary>
    public static PromotionPause SeedPromotionPause(
        this ApplicationDbContext db, DateOnly start, DateOnly end, string reason = "Examens",
        int levelId = LevelId, int academicYearId = CurrentYearId,
        PauseKind kind = PauseKind.Exam, bool confirmed = true)
    {
        var pause = new PromotionPause
        {
            AcademicYearId = academicYearId,
            LevelId = levelId,
            StartDate = start,
            EndDate = end,
            Reason = reason,
            Kind = kind,
            IsConfirmed = confirmed,
            RecordedOn = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc),
        };

        db.PromotionPauses.Add(pause);
        return pause;
    }
}


/// <summary>
/// Une piste d'audit qui retient ce qu'on lui dit, sans rien derrière.
/// </summary>
/// <remarks>
/// <para>Un test de handler appelle le handler <b>directement</b>, donc
/// <c>AuditLogPipelineBehavior</c> n'a rien ouvert et la vraie <c>AuditTrail</c> serait
/// silencieuse — ce qui est exactement son comportement voulu hors d'un acte auditable, et
/// exactement ce qui rendrait le constat intestable ici.</para>
///
/// <para>⚠ <b>Ce double ne prouve pas que l'entrée est écrite</b>, seulement que le handler a dit
/// ce qu'il a fait. Que la ligne atteigne la base est la question de <c>PGSH.Tests/Integration/</c>,
/// et c'est là qu'elle se pose : c'est précisément le trou par lequel « Supprimer les groupes » a
/// pu porter <c>IAuditableCommand</c> pendant des semaines sans jamais rien écrire.</para>
/// </remarks>
public sealed class RecordingAuditTrail : IAuditTrail
{
    private readonly Dictionary<string, object?> _fields = [];

    /// <summary>Le dernier constat déposé pour chaque clé, dans l'ordre où les clés sont apparues.</summary>
    public IReadOnlyDictionary<string, object?> Fields => _fields;

    public void RecordOutcome(params (string Key, object? Value)[] fields)
    {
        foreach (var (key, value) in fields)
            _fields[key] = value;
    }

    /// <summary>
    /// Exécute l'opération telle quelle. ⚠ <b>Aucune transaction, et c'est honnête</b> : ce double
    /// sert aux tests de handler, qui tournent sur le fournisseur en mémoire — lequel n'honore aucune
    /// transaction de toute façon. L'atomicité se vérifie sur SQLite, dans
    /// <c>AtomicUnitOfWorkTests</c>.
    /// </summary>
    public Task<Result<T>> RunAtomicallyAsync<T>(
        Func<CancellationToken, Task<Result<T>>> operation, CancellationToken cancellationToken = default) =>
        operation(cancellationToken);
}
