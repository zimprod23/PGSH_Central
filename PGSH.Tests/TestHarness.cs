using AcademicProgram = PGSH.Domain.Common.Utils.AcademicProgram;
using Level = PGSH.Domain.Common.Utils.Level;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Stages.Planning;
using PGSH.Application.Stages.Slots;
using PGSH.Domain.Calendar;
using PGSH.Domain.Employees;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;
using PGSH.Infrastructure.Database;

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

    /// <summary>A hospital service, optionally led by <paramref name="chef"/> (who is added to its staff first).</summary>
    public static Service SeedService(this ApplicationDbContext db, int serviceId, string name, Employee? chef = null)
    {
        var hospital = db.Hospitals.Local.FirstOrDefault(h => h.Id == HospitalId);
        if (hospital is null)
        {
            hospital = new Hospital { Id = HospitalId, Name = "CHU Ibn Sina", City = "Rabat" };
            db.Hospitals.Add(hospital);
        }

        var service = new Service { Id = serviceId, Name = name, Description = "", Hospital = hospital, Capacity = 20 };
        if (chef is not null)
        {
            service.AddStaff(chef);
            service.AssignChef(chef);
        }

        db.Services.Add(service);
        return service;
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
        var group = new AcademicGroup
        {
            Id = groupId, Label = groupLabel, GroupNumber = groupId, AcademicYearId = academicYearId,
            LevelId = stage.LevelId,
        };
        var cohort = new Cohort
        {
            Id = groupId, Label = groupLabel, StageId = stage.Id, Stage = stage,
            AcademicGroupId = groupId, AcademicGroup = group,
        };
        db.AcademicGroups.Add(group);
        db.Cohorts.Add(cohort);
        return cohort;
    }

    /// <summary>
    /// A roster on its own, with an explicit number and partition label. <see cref="SeedCohort"/>
    /// covers the common case of one group taking one stage; a level whose groups rotate through
    /// several stages needs the roster separate from the cohorts hanging off it.
    /// </summary>
    public static AcademicGroup SeedGroup(
        this ApplicationDbContext db, int groupId, int groupNumber, string? rotationGroup = null,
        int academicYearId = CurrentYearId)
    {
        var group = new AcademicGroup
        {
            Id = groupId, Label = $"G{groupNumber}", GroupNumber = groupNumber,
            RotationGroup = rotationGroup, AcademicYearId = academicYearId, LevelId = LevelId,
        };
        db.AcademicGroups.Add(group);
        return group;
    }

    /// <summary>Attaches an existing roster to <paramref name="stage"/> — one cohort per (group, stage).</summary>
    public static Cohort SeedCohortFor(
        this ApplicationDbContext db, Stage stage, AcademicGroup group, int cohortId)
    {
        var cohort = new Cohort
        {
            Id = cohortId, Label = $"{stage.Name} · {group.Label}", StageId = stage.Id, Stage = stage,
            AcademicGroupId = group.Id, AcademicGroup = group,
        };
        db.Cohorts.Add(cohort);
        return cohort;
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
        var slot = new StageSlot
        {
            Id = slotId, StageId = stage.Id, AcademicYearId = academicYearId ?? CurrentYearId,
            PeriodNumber = periodNumber, StartDate = start, EndDate = end,
        };
        db.StageSlots.Add(slot);
        return slot;
    }

    /// <summary>One cell of the planning grid: this cohort spends this slot in this service.</summary>
    public static CohortSlotAssignment SeedSlotAssignment(
        this ApplicationDbContext db, int id, Cohort cohort, StageSlot slot, Service service)
    {
        var assignment = new CohortSlotAssignment
        {
            Id = id, CohortId = cohort.Id, Cohort = cohort,
            StageSlotId = slot.Id, StageSlot = slot,
            ServiceId = service.Id, Service = service,
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
        HolidayKind kind = HolidayKind.National, bool confirmed = true)
    {
        var holiday = new Holiday
        {
            StartDate = start,
            EndDate = start.AddDays(days - 1),
            Name = name,
            Kind = kind,
            IsConfirmed = confirmed,
        };
        db.Holidays.Add(holiday);
        return holiday;
    }
}
