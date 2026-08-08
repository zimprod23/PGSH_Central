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
