using Microsoft.OpenApi.Models;
using Scalar.AspNetCore;

namespace PGSH.API.Extensions
{
    public static class ApplicationBuilderExtensions
    {
        public static IApplicationBuilder AddApiDocumentationUI(this WebApplication app) 
        {
            //app.MapOpenApi();
            //app.UseSwagger();
            //app.UseSwaggerUI(opts => {
            //    opts.SwaggerEndpoint("/swagger/v1/swagger.json", "API v1"); //SwaggerEndpoint("/openapi/v1.json", "Open API V1");
            //    opts.OAuthClientId("pgsh-swagger");
            //    opts.OAuthAppName("PGSH Swagger UI");
            //    opts.OAuthUsePkce();
            //    opts.OAuthScopeSeparator(" ");
            //});

            // Generates the /openapi/v1.json (New .NET 9)
            app.MapOpenApi();
            app.UseSwaggerUI(opts => {
                // POINT TO SWAGGER DOC, NOT OPENAPI DOC
                opts.SwaggerEndpoint("/openapi/v1.json", "API v1");

                opts.OAuthClientId("pgsh-swagger");
                opts.OAuthAppName("PGSH Swagger UI");
                opts.OAuthUsePkce();
            });
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
