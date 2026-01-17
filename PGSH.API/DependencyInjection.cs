using PGSH.API.Infrastructure;

namespace PGSH.API
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddPresentation(this IServiceCollection services) 
        {
            services.AddEndpointsApiExplorer();
            services.AddOpenApi();
            // REMARK: If you want to use Controllers, you'll need this.
            services.AddControllers();

            services.AddExceptionHandler<GlobalExceptionHandler>();
            services.AddProblemDetails();

            return services;
        }
    }
}
