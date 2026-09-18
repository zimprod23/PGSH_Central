using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OpenTelemetry.Trace;
using PGSH.Domain.Users;
using PGSH.Infrastructure.Database;
using System.Diagnostics;

namespace PGSH.MigrationService;

public class Worker(
    IServiceProvider serviceProvider,
    IConfiguration configuration,
    IHostApplicationLifetime hostApplicationLifetime
    ) : BackgroundService
{
    public const string ActivitySourceName = "Migrations";

    /// <summary>
    /// Whether to inject the Bogus sample data. Defaults to true so development is unchanged, but it
    /// MUST be false on any database carrying real records: the seeder runs on every Aspire start, and
    /// its Levels and AcademicYears collide with the imported ones on their unique indexes.
    /// </summary>
    public const string SeedingEnabledKey = "Seeding:Enabled";

    /// <summary>
    /// Whether to ensure the three fixed accounts exist. Independent of <see cref="SeedingEnabledKey"/>
    /// on purpose: they are the only way into the application, so a database holding real imported
    /// records still needs them even though it must never receive the sample data. Each is created only
    /// when its e-mail is absent, so this is safe to run on every start.
    /// </summary>
    public const string SeedingStaticUsersKey = "Seeding:StaticUsers";

    private static ActivitySource s_activitySource = new(ActivitySourceName);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var activity = s_activitySource.StartActivity("Migrating Database", ActivityKind.Client);
        try
        {
            using var scope = serviceProvider.CreateScope();

            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Worker>>();

            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            //Launch operations
            await EnsureDatabaseCreated(dbContext, stoppingToken);//To Remove later
            await RefuseToBuildAnEmptyBaseAsync(dbContext, logger, stoppingToken);
            await RunMigrationAsync(dbContext, stoppingToken);

            if (configuration.GetValue(SeedingStaticUsersKey, defaultValue: true))
            {
                await Seeder.SeedStaticUsersOnlyAsync(dbContext, logger, stoppingToken);
            }

            if (configuration.GetValue(SeedingEnabledKey, defaultValue: true))
            {
                await Seeder.SeedAsync(dbContext, logger, stoppingToken);
            }
            else
            {
                logger.LogInformation(
                    "Sample seeding disabled ({Key}=false) — migrations only, no fixture data written.",
                    SeedingEnabledKey);
            }
        }
        catch(Exception ex) 
        {
            activity?.RecordException(ex);
            throw;
        }
        hostApplicationLifetime.StopApplication();
    }

    private static async Task EnsureDatabaseCreated(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        var dbCreator = dbContext.GetService<IRelationalDatabaseCreator>();

        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            if (!await dbCreator.ExistsAsync(cancellationToken)) 
            {
                await dbCreator.CreateAsync(cancellationToken);
            }
        });
    }

    /// <summary>
    /// Stops, with a sentence, when the chain is about to be run against a base that has never been
    /// migrated — because it cannot succeed there.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Three of the migrations are <i>data</i> migrations</b> — the arrêté 1650.25
    /// requirement sets — and they read the catalogue the legacy import writes. On a base with no
    /// <c>Levels</c> they <c>RAISE EXCEPTION</c> <b>by design</b>: « Refusing costs an apply; a wrong
    /// requirement set costs a promotion planned against stages it does not owe. » So « migrate then
    /// import » is not a way to build this database, and never was — <c>docs/operations.md</c> §1.
    /// (It is also why the Testcontainers tier builds its schema with <c>EnsureCreated</c>.)</para>
    ///
    /// <para>⚠ <b>Why refuse rather than let it fail on its own.</b> It already failed on its own, on
    /// 17/09/2026, and what it produced was a <c>PostgresException</c> stack trace ending in
    /// <c>MigrateAsync</c> — accurate, and saying neither that the state was expected nor what to do
    /// about it. PostgreSQL has transactional DDL, so EF had additionally rolled the <b>whole chain</b>
    /// back: the operator was left with one empty table and a stack trace, reading the situation as a
    /// broken build rather than as an empty database. Same rule as <c>DatabaseOutage</c> — an
    /// infrastructure state must not look like a defect.</para>
    ///
    /// <para>The condition is deliberately the narrow one: <b>no migration has ever been applied</b>.
    /// A base mid-chain is a different question (a real drift) and still gets the provider's own
    /// error, because this class has nothing useful to add to it.</para>
    /// </remarks>
    private static async Task RefuseToBuildAnEmptyBaseAsync(
        ApplicationDbContext dbContext, ILogger<Worker> logger, CancellationToken cancellationToken)
    {
        var applied = await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken);
        if (applied.Any())
            return;

        const string explanation =
            "Cette base n'a jamais été migrée, et la chaîne de migrations ne peut pas la construire : " +
            "trois d'entre elles sont des migrations de DONNÉES (les jeux d'exigences de l'arrêté " +
            "1650.25) qui lisent le catalogue écrit par l'import hérité, et refusent une base vide " +
            "par conception — « Aucun niveau \"3ᵉ année Médecine\" : le catalogue des niveaux doit " +
            "exister avant les stages. » Ce n'est pas un défaut de l'application. " +
            "Pour repartir : restaurez un point de sauvegarde — scripts/pgsh-restore.ps1 -List, puis " +
            "-Id <point> — ou rejouez la procédure de reconstruction depuis Medecine.mdb " +
            "(docs/operations.md). Un volume Docker perdu se restaure, il ne se re-migre pas.";

        logger.LogError("{Explanation}", explanation);
        throw new EmptyDatabaseCannotBeMigratedException(explanation);
    }

    private static async Task RunMigrationAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            // Run migration in a transaction to avoid partial migration if it fails.
            //await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await dbContext.Database.MigrateAsync(cancellationToken);
            //await transaction.CommitAsync(cancellationToken);
        });
    }

    private static async Task SeedDataAsync(ApplicationDbContext dbContext, ILogger<Worker> logger, CancellationToken cancellationToken)
    {
        User user = new User
        {
            FirstName = "Ezzoubeir",
            LastName = "Elasraoui",
            Email = "elassraouiezzoubeir@gmail.com",
            Gender = Gender.Male,
            //PasswordHash = "JSIJAZIODIOANIIFOEZ-jdioahdiozajonjopdaJOPDA12"
        };
        logger.LogInformation("User Created"+user);
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () => 
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await dbContext.Users.AddAsync(user);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync();
        });
    }
}
