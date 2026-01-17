using Microsoft.OpenApi.Models;
using Scalar.AspNetCore;

namespace PGSH.API.Extensions
{
    public static class ApplicationBuilderExtensions
    {
        public static IApplicationBuilder AddApiDocumentationUI(this WebApplication app) 
        {
            app.MapOpenApi();
            app.UseSwaggerUI(opts => opts.SwaggerEndpoint("/openapi/v1.json", "Open API V1"));
            app.MapScalarApiReference(options =>
            {
                options.Servers = [];
            });
            return app;
        }
        public static IApplicationBuilder MapHealthEndpoints(this WebApplication app)
        {
            return app;
        }
    }
}
