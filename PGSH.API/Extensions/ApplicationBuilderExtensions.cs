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
            //    opts.OAuthClientId(ApiDocumentationAuth.ClientId);
            //    opts.OAuthAppName("PGSH Swagger UI");
            //    opts.OAuthUsePkce();
            //    opts.OAuthScopeSeparator(" ");
            //});

            // Generates the /openapi/v1.json (New .NET 9)
            app.MapOpenApi();
            app.UseSwaggerUI(opts => {
                // POINT TO SWAGGER DOC, NOT OPENAPI DOC
                opts.SwaggerEndpoint("/openapi/v1.json", "API v1");

                // ⚠ The client is named once, in ApiDocumentationAuth — « pgsh-swagger » was a string
                // literal here pointing at a client the versioned realm does not declare.
                opts.OAuthClientId(ApiDocumentationAuth.ClientId);
                opts.OAuthAppName("PGSH Swagger UI");
                opts.OAuthUsePkce();
            });
            app.MapScalarApiReference(options =>
            {
                options.Servers = [];

                // Without these, « Authorize » opens Keycloak with no client_id at all and the realm
                // answers « Client not found » — a dead end that reads as a broken button.
                options
                    .WithPreferredScheme(ApiDocumentationAuth.SchemeName)
                    .WithOAuth2Authentication(oauth =>
                    {
                        oauth.ClientId = ApiDocumentationAuth.ClientId;
                        oauth.Scopes = ApiDocumentationAuth.Scopes;
                    });
            });
            return app;
        }
        public static IApplicationBuilder MapHealthEndpoints(this WebApplication app)
        {
            return app;
        }
    }
}
