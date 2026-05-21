using System.Text.Json.Serialization;
using PGSH.API.Infrastructure;

namespace PGSH.API
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddPresentation(this IServiceCollection services)
        {
            services.AddEndpointsApiExplorer();
            services.AddOpenApi();

            // Register the enum-as-string converter for both minimal API endpoints and MVC controllers
            // (AddControllers registers a separate JSON pipeline from ConfigureHttpJsonOptions)
            services.AddControllers()
                .AddJsonOptions(opts =>
                    opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

            services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
                options.SerializerOptions.PropertyNameCaseInsensitive = true;
            });

            services.AddExceptionHandler<GlobalExceptionHandler>();
            services.AddProblemDetails();

            return services;
        }
    }
}
