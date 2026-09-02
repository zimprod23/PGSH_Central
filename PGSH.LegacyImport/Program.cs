using Microsoft.EntityFrameworkCore;
using PGSH.Infrastructure.Database;
using PGSH.LegacyImport;
using PGSH.Application.Stages.Cnpn;
using PGSH.Application.Stages.Cnpn.SeedFromHistory;
using PGSH.Application.Stages.Curricula.SeedFromHistory;
using PGSH.LegacyImport.Legacy;
using PGSH.LegacyImport.Mapping;

var options = CliOptions.Parse(args);
if (options is null)
{
    Console.WriteLine("""
        PGSH legacy import — reads the VB.NET/Access database and writes it into PGSH.

          --seed-curricula         Reconstruct past CNPN records from the stages actually served,
                                   against --connection. Needs no .mdb. Add --apply to write.
          --stamp-cnpn             Attribute a governing CNPN to every imported student and every
                                   registration, against --connection. Needs no .mdb. Add --apply.
          --source <file.mdb>      Path to the legacy Access file (required unless a pass above)
          --connection <string>    PostgreSQL connection string (required with --apply)
          --apply                  Actually write. Omitted, the import is a dry run.
          --review                 Print the reconstructed hospital tree for checking, then exit
          --email-domain <domain>  Address domain for generated e-mails (default: um5.ac.ma)
          --allow-nonempty         Permit writing into a database that already holds data

        A dry run reads the whole file, builds the entire graph and reports it, without
        opening a database connection. Run it first — always.

        Hospital city and service type are not in the legacy data — they are inferred from
        the service name strings. Use --review to check them before importing.

        ORDER, rebuilding a database from scratch. The three CNPN data migrations refuse to
        run against an empty base — they need the Levels and Stages the import creates — so
        the chain is not simply "migrate, then import":

            1. dotnet ef database update 20260830143914_PriorEnrolment
            2. --source Medecine.mdb --connection <cs> --apply
            3. --seed-curricula --connection <cs> --apply
            4. dotnet ef database update            (the remaining CNPN migrations)
            5. --stamp-cnpn --connection <cs> --apply

        Step 5 is the one nothing else does. The student attribution and the registration
        backfill were written as one-off UPDATEs inside migrations that have already been
        marked applied, and replayed against a base rebuilt in this order they stamp nobody —
        silently, because a null CNPN is what every reader falls back on.
        """);
    return 1;
}

// Curriculum reconstruction reads PGSH's own data, not the Access file — it is the pass that follows
// a legacy import, deriving each year's CNPN from the cohorts that import created.
if (options.SeedCurricula)
{
    if (string.IsNullOrWhiteSpace(options.Connection))
    {
        Console.Error.WriteLine("--seed-curricula needs --connection.");
        return 1;
    }

    return await SeedCurriculaAsync(options.Connection, options.Apply);
}

// The other post-import pass, and the one a rebuilt database silently goes without: see
// CnpnHistoryAttributor for why the migrations that first did this cannot do it again.
if (options.StampCnpn)
{
    if (string.IsNullOrWhiteSpace(options.Connection))
    {
        Console.Error.WriteLine("--stamp-cnpn needs --connection.");
        return 1;
    }

    return await StampCnpnAsync(options.Connection, options.Apply);
}

if (!File.Exists(options.Source))
{
    Console.Error.WriteLine($"Source file not found: {options.Source}");
    return 1;
}

Console.WriteLine($"Reading {options.Source} …");
var legacy = new AccessLegacyReader(options.Source).Read();

Console.WriteLine($"  {legacy.Students.Count,7:N0} ETUDIANT");
Console.WriteLine($"  {legacy.Registrations.Count,7:N0} Inscription");
Console.WriteLine($"  {legacy.StageAssignments.Count,7:N0} AffectStage");
Console.WriteLine($"  {legacy.Services.Count,7:N0} SERVICES");
Console.WriteLine();

Console.WriteLine("Planning …");
var plan = new LegacyImportPlanner(options.EmailDomain).Plan(legacy);

if (options.Review)
{
    ReviewHospitals(plan);
    return 0;
}

Report(plan.Report);

if (!options.Apply)
{
    Console.WriteLine();
    Console.WriteLine("Dry run — nothing was written. Re-run with --apply to commit.");
    return 0;
}

if (string.IsNullOrWhiteSpace(options.Connection))
{
    Console.Error.WriteLine("--apply needs --connection.");
    return 1;
}

// No IServiceScopeFactory: ApplicationDbContext only publishes domain events when it has one, and an
// import must not fire 100k history/notification handlers for events that happened years ago.
var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseNpgsql(options.Connection)
    .Options;

await using var db = new ApplicationDbContext(dbOptions);

if (!options.AllowNonEmpty)
{
    // Every table the import writes, not just the big ones. The seeder creates Levels and
    // AcademicYears too, and those carry unique indexes — UNIQUE(Year, AcademicProgram) and
    // UNIQUE(Label) — so a database holding only seeded reference data would clear a students-only
    // check and then fail on a constraint halfway through writing.
    var occupied = new List<(string Table, int Count)>
    {
        ("Levels",                await db.Levels.CountAsync()),
        ("AcademicYears",         await db.AcademicYears.CountAsync()),
        ("AcademicGroups",        await db.AcademicGroups.CountAsync()),
        ("Centers",               await db.Centers.CountAsync()),
        ("Hospitals",             await db.Hospitals.CountAsync()),
        ("Services",              await db.Services.CountAsync()),
        ("Stages",                await db.Stages.CountAsync()),
        ("Cohorts",               await db.Cohorts.CountAsync()),
        ("Students",              await db.Students.CountAsync()),
        ("Registrations",         await db.Registrations.CountAsync()),
        ("InternshipAssignments", await db.InternshipAssignments.CountAsync()),
    };

    var nonEmpty = occupied.Where(t => t.Count > 0).ToList();
    if (nonEmpty.Count > 0)
    {
        Console.Error.WriteLine("Target is not empty — the import creates rows, it never reconciles them:");
        foreach (var (table, count) in nonEmpty)
            Console.Error.WriteLine($"    {count,9:N0}  {table}");

        Console.Error.WriteLine();
        Console.Error.WriteLine(
            "Levels and AcademicYears carry unique indexes, so importing on top of seeded reference "
            + "data fails part-way through. Disable the sample seeder (Seeding:Enabled=false in "
            + "PGSH.MigrationService) and start from an empty database, or pass --allow-nonempty if "
            + "you are certain.");
        return 1;
    }
}

Console.WriteLine();
Console.WriteLine("Writing …");
await LegacyImportWriter.WriteAsync(db, plan, Console.Out);

Console.WriteLine();
Console.WriteLine("Done.");
return 0;

// City and service type are the two fields the legacy catalogue simply does not have. They are
// inferred, so they are the two worth a human glance before 148 services land in the tree.
static void ReviewHospitals(LegacyImportPlan plan)
{
    Console.WriteLine();
    Console.WriteLine("Reconstructed hospital tree — check City and Type, neither is in the source.");

    foreach (var hospital in plan.Hospitals.OrderBy(h => h.Name, StringComparer.CurrentCulture))
    {
        var services = plan.Services.Where(s => s.Hospital == hospital).ToList();
        string flag = plan.HospitalsWithAssumedCity.Contains(hospital.Name) ? " ?" : "";

        Console.WriteLine();
        Console.WriteLine($"  {hospital.Name}  [{hospital.City}{flag} · {hospital.HospitalType}]  — {services.Count} service(s)");

        foreach (var service in services.OrderBy(s => s.Name, StringComparer.CurrentCulture))
            Console.WriteLine($"      {service.ServiceType,-10} {service.Name}");
    }

    Console.WriteLine();
    Console.WriteLine("Service types by count:");
    foreach (var group in plan.Services.GroupBy(s => s.ServiceType).OrderByDescending(g => g.Count()))
        Console.WriteLine($"  {group.Count(),4} {group.Key}");

    Console.WriteLine();
    Console.WriteLine($"  ? = city attributed, not stated in the source ({plan.HospitalsWithAssumedCity.Count} of {plan.Hospitals.Count}).");
    Console.WriteLine("  Service type is inferred from the name in every case — the source has no such column.");
}

static async Task<int> SeedCurriculaAsync(string connection, bool apply)
{
    var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options;
    await using var db = new ApplicationDbContext(dbOptions);

    var result = await new CurriculumHistoryReconstructor(db, new CnpnAssignment(db))
        .ReconstructAsync(dryRun: !apply, default);
    if (result.IsFailure)
    {
        Console.Error.WriteLine($"{result.Error.Code}: {result.Error.Description}");
        return 1;
    }

    var report = result.Value;
    Console.WriteLine($"  {report.CurriculaCreated,7:N0} Curriculum");
    Console.WriteLine($"  {report.StageEntriesCreated,7:N0} CurriculumStage");
    Console.WriteLine($"  {report.CurriculaSkippedBecauseTheyExist,7:N0} already recorded, left alone");
    Console.WriteLine();
    Console.WriteLine(report.DryRun
        ? "Dry run — nothing was written. Re-run with --apply to commit."
        : "Done.");

    return 0;
}

/// <summary>
/// Attributes a governing CNPN to every student and every registration. ⚠ Run it <b>after</b> the
/// remaining CNPN migrations, not before: they are what create the texts a student can be attributed
/// to, and an attribution pass over a base holding none reports every student unresolved.
/// </summary>
static async Task<int> StampCnpnAsync(string connection, bool apply)
{
    var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options;
    await using var db = new ApplicationDbContext(dbOptions);

    var result = await new CnpnHistoryAttributor(db, new CnpnAssignment(db))
        .AttributeAsync(dryRun: !apply, default);

    if (result.IsFailure)
    {
        Console.Error.WriteLine($"{result.Error.Code}: {result.Error.Description}");
        return 1;
    }

    var report = result.Value;
    Console.WriteLine($"  {report.StudentsConsidered,7:N0} students with a registration on record");
    Console.WriteLine($"  {report.StudentsStamped,7:N0} stamped   ({report.StudentsInferred:N0} from a deduced entry)");
    Console.WriteLine($"  {report.StudentsAlreadySettled,7:N0} already confirmed, left alone");
    Console.WriteLine($"  {report.StudentsUnresolved,7:N0} unresolved — no text covers their intake");
    Console.WriteLine($"  {report.RegistrationsBackfilled,7:N0} registrations backfilled");
    Console.WriteLine($"  {report.RegistrationsRefusedByAggregate,7:N0} registrations refused by the aggregate (expected 0)");
    Console.WriteLine();
    Console.WriteLine(report.DryRun
        ? "Dry run — nothing was written. Re-run with --apply to commit."
        : "Done.");

    return 0;
}

static void Report(LegacyImportReport r)
{
    Console.WriteLine($"  {r.Centers,7:N0} Center");
    Console.WriteLine($"  {r.Hospitals,7:N0} Hospital");
    Console.WriteLine($"  {r.Services,7:N0} Service");
    Console.WriteLine($"  {r.Levels,7:N0} Level");
    Console.WriteLine($"  {r.Stages,7:N0} Stage");
    Console.WriteLine($"  {r.AcademicYears,7:N0} AcademicYear");
    Console.WriteLine($"  {r.AcademicGroups,7:N0} AcademicGroup");
    Console.WriteLine($"  {r.Students,7:N0} Student   ({r.StudentsWithoutCne:N0} with no CNE in the source)");
    Console.WriteLine($"  {r.Registrations,7:N0} Registration");
    Console.WriteLine($"  {r.Cohorts,7:N0} Cohort");
    Console.WriteLine($"  {r.Assignments,7:N0} InternshipAssignment");
    Console.WriteLine($"  {r.ServicePeriods,7:N0} ServicePeriod");
    Console.WriteLine($"  {r.Evaluations,7:N0} ServiceEvaluation");

    if (r.Problems.Count == 0) return;

    Console.WriteLine();
    Console.WriteLine($"Notes ({r.Problems.Count:N0}):");
    foreach (var (kind, count) in r.ProblemsByKind())
        Console.WriteLine($"  {count,7:N0} {kind}");
}

internal sealed record CliOptions(
    string Source, string? Connection, bool Apply, bool Review, string EmailDomain, bool AllowNonEmpty,
    bool SeedCurricula, bool StampCnpn)
{
    public static CliOptions? Parse(string[] args)
    {
        string? source = null, connection = null, domain = LegacyIdentityMapper.DefaultDomain;
        bool apply = false, review = false, allowNonEmpty = false;
        bool seedCurricula = false, stampCnpn = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--source" when i + 1 < args.Length: source = args[++i]; break;
                case "--connection" when i + 1 < args.Length: connection = args[++i]; break;
                case "--email-domain" when i + 1 < args.Length: domain = args[++i]; break;
                case "--apply": apply = true; break;
                case "--review": review = true; break;
                case "--allow-nonempty": allowNonEmpty = true; break;
                case "--seed-curricula": seedCurricula = true; break;
                case "--stamp-cnpn": stampCnpn = true; break;
                default: return null;
            }
        }

        // The post-import passes read PGSH's own tables, so neither needs the .mdb.
        if (source is null && !seedCurricula && !stampCnpn) return null;

        return new CliOptions(
            source ?? "", connection, apply, review, domain, allowNonEmpty, seedCurricula, stampCnpn);
    }
}
