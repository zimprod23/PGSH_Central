using PGSH.Application.Abstractions.Behaviors;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.AcademicYears;
using PGSH.Application.AcademicYears.Manage;
using PGSH.Application.Backups;
using PGSH.Application.Behaviors;
using PGSH.Application.Calendar;
using PGSH.Application.Hospitals.Chefs;
using PGSH.Application.Hospitals.Services;
using PGSH.Application.Stages.Cnpn;
using PGSH.Application.Stages.Curricula.SeedFromHistory;
using PGSH.Application.Stages.Cnpn.Effectivity;
using PGSH.Application.Stages.Cnpn.Targeting;
using PGSH.Application.Stages.Evaluations;
using PGSH.Application.Stages.Evaluations.Import;
using PGSH.Application.Stages.Planning;
using PGSH.Application.Stages.Progression;
using PGSH.Application.Stages.RotationCycle;
using PGSH.Application.Stages.Slots;
using PGSH.Application.Students.Registrations.Deliberation;
using PGSH.Application.Students.Registrations.Inscription;
using PGSH.Application.Students.Registrations.Reinscription;
using PGSH.Application.Students.Registrations.ReinscriptionSheet;

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

        services.AddScoped<DatabaseCensusReader>();
        services.AddScoped<SafePointTaker>();
        services.AddScoped<ServiceOccupancyCalculator>();
        services.AddScoped<ServiceIntakeCalculator>();
        services.AddScoped<ServiceLevelCapacityResolver>();
        services.AddScoped<RotationArranger>();
        services.AddScoped<PromotionPartitioning>();
        services.AddScoped<StudentAffectationService>();
        services.AddScoped<AffectationTollReader>();
        services.AddScoped<SchedulePublisher>();
        services.AddScoped<StagePeriodRunner>();
        services.AddScoped<StagePauseRunner>();
        services.AddScoped<MidStageTransferRescheduler>();
        services.AddScoped<LateArrivalScheduler>();
        services.AddScoped<CohortProvisioner>();
        services.AddScoped<ExecutionAuthorizer>();
        services.AddScoped<CnpnAssignment>();
        services.AddScoped<CurriculumHistoryReconstructor>();
        services.AddScoped<RegistrationCnpnStamper>();
        services.AddScoped<CnpnTargetPlanner>();
        services.AddScoped<CnpnEffectivityPlanner>();
        services.AddScoped<OutstandingStageFinder>();
        services.AddScoped<FinalYearGuard>();
        services.AddScoped<AcademicYearResolver>();
        services.AddScoped<AcademicYearCalendarGuard>();
        services.AddScoped<CurrentYearDesignation>();
        services.AddScoped<SlotOverlapGuard>();
        services.AddScoped<GroupScheduleConflictGuard>();
        services.AddScoped<EvaluationObjectiveResolver>();
        services.AddScoped<EvaluationImportPlanner>();
        services.AddScoped<DeliberationPlanner>();
        services.AddScoped<ReinscriptionPlanner>();
        services.AddScoped<ReinscriptionSheetPlanner>();
        services.AddScoped<InscriptionPlanner>();
        services.AddScoped<InscriptionApplier>();
        services.AddScoped<RotationCycleContext>();
        services.AddScoped<WorkingDayProvider>();
        services.AddScoped<ServiceChefProvider>();

        return services;
    }
}
