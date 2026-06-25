using PGSH.Application.Abstractions.Behaviors;
using PGSH.Application.Behaviors;
using PGSH.Application.Employees.MyServices;
using PGSH.Application.Stages.Planning;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(config =>
        {
            config.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly);

            config.AddOpenBehavior(typeof(RequestLoggingPipelineBehavior<,>));
            config.AddOpenBehavior(typeof(ValidationPipelineBehavior<,>));
            config.AddOpenBehavior(typeof(AuditLogPipelineBehavior<,>));
        });

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        services.AddScoped<ServiceOccupancyCalculator>();
        services.AddScoped<RotationArranger>();
        services.AddScoped<StudentAffectationService>();
        services.AddScoped<SchedulePublisher>();
        services.AddScoped<StagePeriodRunner>();
        services.AddScoped<MidStageTransferRescheduler>();
        services.AddScoped<CohortProvisioner>();
        services.AddScoped<ExecutionAuthorizer>();

        return services;
    }
}
