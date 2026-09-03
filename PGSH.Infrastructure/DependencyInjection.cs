using PGSH.Application.Abstractions.Authentication;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Exports;
using PGSH.Application.Stages.Evaluations.Import;
using PGSH.Application.Students.Registrations.Deliberation;
using PGSH.Application.Students.Registrations.Inscription;
using PGSH.Application.Students.Registrations.ReinscriptionSheet;
using PGSH.Application.Backups;
using PGSH.Infrastructure.Authentication;
using PGSH.Infrastructure.Backups;
using PGSH.Infrastructure.Evaluations;
using PGSH.Infrastructure.Exports;
using PGSH.Infrastructure.Registrations;
using PGSH.Infrastructure.Authorization;
using PGSH.Infrastructure.Database;
using PGSH.Infrastructure.Time;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PGSH.SharedKernel;
using Microsoft.AspNetCore.Authentication;

namespace PGSH.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration) =>
        services
            .AddServices()
            .AddDatabase()
            .AddBackups(configuration)
            .AddAuthenticationInternal()
            .AddAuthorizationInternal();

    private static IServiceCollection AddServices(this IServiceCollection services)
    {
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        // Adapters for the Application's sheet ports — stateless, so singletons.
        services.AddSingleton<IEvaluationSheetParser, ClosedXmlEvaluationSheetParser>();
        services.AddSingleton<IDeliberationSheetParser, ClosedXmlDeliberationSheetParser>();
        services.AddSingleton<IInscriptionSheetParser, ClosedXmlInscriptionSheetParser>();
        services.AddSingleton<IReinscriptionSheetParser, ClosedXmlReinscriptionSheetParser>();
        services.AddSingleton<IExportWorkbookWriter, ClosedXmlExportWorkbookWriter>();
        return services;
    }

    private static IServiceCollection AddDatabase(this IServiceCollection services)
    {
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        return services;
    }

    /// <summary>
    /// The safe-point archive and the timer that feeds it.
    /// </summary>
    /// <remarks>
    /// The archive is a singleton: it caches the docker probe every confirmation dialog reads, and it
    /// holds the gate that keeps two <c>pg_dump</c>s off the live base at once. The fingerprint
    /// provider is scoped because it reads the migrations table through the context.
    ///
    /// <para>⚠ <see cref="ScheduledBackupService"/> is registered three ways on purpose — one instance,
    /// reachable as the hosted service that runs it and as the <c>IBackupScheduleClock</c> the status
    /// endpoint reads « prochaine sauvegarde » from. Registered twice it would run twice, and the
    /// screen would be reading a timer that is not the one taking the dumps.</para>
    /// </remarks>
    private static IServiceCollection AddBackups(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BackupOptions>(configuration.GetSection(BackupOptions.SectionName));

        services.AddSingleton<IBackupArchive, PgDumpBackupArchive>();
        services.AddScoped<ISchemaFingerprintProvider, EfSchemaFingerprintProvider>();

        services.AddSingleton<ScheduledBackupService>();
        services.AddSingleton<IBackupScheduleClock>(sp => sp.GetRequiredService<ScheduledBackupService>());
        services.AddHostedService(sp => sp.GetRequiredService<ScheduledBackupService>());

        return services;
    }

    private static IServiceCollection AddAuthenticationInternal(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddMemoryCache();
        services.AddScoped<IUserContext, UserContext>();
        return services;
    }

    private static IServiceCollection AddAuthorizationInternal(this IServiceCollection services)
    {
        services.AddAuthorization();
        services.AddScoped<PermissionProvider>();
        services.AddTransient<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddTransient<IAuthorizationPolicyProvider, PermissionAuthorizationPolicyProvider>();
        services.AddTransient<IClaimsTransformation, KeycloakRoleTransformer>();
        return services;
    }
}
