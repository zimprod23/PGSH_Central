using PGSH.Application.Abstractions.Behaviors;
using PGSH.Application.AcademicYears;
using PGSH.Application.Behaviors;
using PGSH.Application.Calendar;
using PGSH.Application.Employees.MyServices;
using PGSH.Application.Hospitals.Services;
using PGSH.Application.Stages.Cnpn;
using PGSH.Application.Stages.Cnpn.Effectivity;
using PGSH.Application.Stages.Progression;
using PGSH.Application.Stages.Cnpn.Targeting;
using PGSH.Application.Stages.Evaluations;
using PGSH.Application.Stages.Evaluations.Import;
using PGSH.Application.Students.Registrations.Deliberation;
using PGSH.Application.Stages.RotationCycle;
using PGSH.Application.Students.Registrations.Reinscription;
using PGSH.Application.Stages.Planning;
using PGSH.Application.Stages.Slots;
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
        services.AddScoped<ServiceIntakeCalculator>();
        services.AddScoped<ServiceLevelCapacityResolver>();
        services.AddScoped<RotationArranger>();
        services.AddScoped<PromotionPartitioning>();
        services.AddScoped<StudentAffectationService>();
        services.AddScoped<SchedulePublisher>();
        services.AddScoped<StagePeriodRunner>();
        services.AddScoped<StagePauseRunner>();
        services.AddScoped<MidStageTransferRescheduler>();
        services.AddScoped<LateArrivalScheduler>();
        services.AddScoped<CohortProvisioner>();
        services.AddScoped<ExecutionAuthorizer>();
        services.AddScoped<CnpnAssignment>();
        services.AddScoped<RegistrationCnpnStamper>();
        services.AddScoped<CnpnTargetPlanner>();
        services.AddScoped<CnpnEffectivityPlanner>();
        services.AddScoped<OutstandingStageFinder>();
        services.AddScoped<FinalYearGuard>();
        services.AddScoped<AcademicYearResolver>();
        services.AddScoped<SlotOverlapGuard>();
        services.AddScoped<GroupScheduleConflictGuard>();
        services.AddScoped<EvaluationObjectiveResolver>();
        services.AddScoped<EvaluationImportPlanner>();
        services.AddScoped<DeliberationPlanner>();
        services.AddScoped<ReinscriptionPlanner>();
        services.AddScoped<RotationCycleContext>();
        services.AddScoped<WorkingDayProvider>();

        return services;
    }
}
