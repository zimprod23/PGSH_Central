using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.OpenApi.Models;
using System.Reflection;

namespace PGSH.API.Extensions
{
    internal static class ServiceCollectionExtensions
    {
        //    internal static IServiceCollection AddSwaggerGenWithAuth(this IServiceCollection services) 
        //    {
        //        //services.AddSwaggerGen(o =>
        //        //    {  
        //        //        o.CustomSchemaIds(id => id.FullName!.Replace('+', '-'));
        //        //        var securityScheme = new OpenApiSecurityScheme
        //        //        {
        //        //            Name = "Jwt Authentication",
        //        //            Description = "Enter your JWT token in this field",
        //        //            In = ParameterLocation.Header,
        //        //            Type = SecuritySchemeType.Http,
        //        //            Scheme = JwtBearerDefaults.AuthenticationScheme,
        //        //            BearerFormat = "JWT"
        //        //        };
        //        //        o.AddSecurityDefinition(JwtBearerDefaults.AuthenticationScheme, securityScheme);
        //        //        var securityRequirement = new OpenApiSecurityRequirement
        //        //        {
        //        //             {
        //        //                new OpenApiSecurityScheme
        //        //                {
        //        //                    Reference = new OpenApiReference
        //        //                    {
        //        //                        Type = ReferenceType.SecurityScheme,
        //        //                        Id = JwtBearerDefaults.AuthenticationScheme
        //        //                    }
        //        //                },
        //        //                []
        //        //            }
        //        //        };
        //        //        o.AddSecurityRequirement(securityRequirement);
        //        //    }
        //        //    );
        //        services.AddSwaggerGen(o =>
        //        {
        //            o.CustomSchemaIds(id => id.FullName!.Replace('+', '-'));

        //            o.AddSecurityDefinition("keycloak", new OpenApiSecurityScheme
        //            {
        //                Type = SecuritySchemeType.OAuth2,
        //                Description = "Keycloak OAuth2",
        //                Flows = new OpenApiOAuthFlows
        //                {
        //                    AuthorizationCode = new OpenApiOAuthFlow
        //                    {
        //                        AuthorizationUrl = new Uri(
        //                            "http://localhost:8082/realms/pgsh/protocol/openid-connect/auth"),
        //                        TokenUrl = new Uri(
        //                            "http://localhost:8082/realms/pgsh/protocol/openid-connect/token"),
        //                        Scopes = new Dictionary<string, string>
        //            {
        //                { "openid", "OpenID" },
        //                { "profile", "Profile" }
        //            }
        //                    }
        //                }
        //            });

        //            o.AddSecurityRequirement(new OpenApiSecurityRequirement
        //{
        //    {
        //        new OpenApiSecurityScheme
        //        {
        //            Reference = new OpenApiReference
        //            {
        //                Type = ReferenceType.SecurityScheme,
        //                Id = "keycloak"
        //            }
        //        },
        //        new[] { "openid", "profile" }
        //    }
        //});
        //        });
        //        return services;
        //    }


internal static IServiceCollection AddApiDocumentation(this IServiceCollection services)
    {
        // 1. Use the NEW native .NET 9/10 OpenApi service
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, context, cancellationToken) =>
            {
                // Define the Security Scheme (Keycloak)
                var scheme = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.OAuth2,
                    Flows = new OpenApiOAuthFlows
                    {
                        AuthorizationCode = new OpenApiOAuthFlow
                        {
                            AuthorizationUrl = new Uri("http://localhost:8082/realms/pgsh/protocol/openid-connect/auth"),
                            TokenUrl = new Uri("http://localhost:8082/realms/pgsh/protocol/openid-connect/token"),
                            Scopes = new Dictionary<string, string>
                        {
                            { "openid", "openid" },
                            { "profile", "profile" }
                        }
                        }
                    }
                };

                // Add the scheme to the document components
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes.Add("keycloak", scheme);

                // Apply the security requirement globally
                var requirement = new OpenApiSecurityRequirement
                {
                    [new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Id = "keycloak", // MUST match the name in your SecuritySchemes.Add(...)
                            Type = ReferenceType.SecurityScheme
                        }
                    }] = new List<string>() // Add "openid", "profile" here if you want to be specific
                };

                document.SecurityRequirements.Add(requirement);
                return Task.CompletedTask;
            });
        });

        return services;
    }
}
}
