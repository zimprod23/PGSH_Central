using Application;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using PGSH.API;
using PGSH.API.Extensions;
using PGSH.Application.Abstractions.Data;
using PGSH.Infrastructure;
using PGSH.Infrastructure.Database;
using Scalar.AspNetCore;
using Serilog;
using System.Reflection;

var MyAllowSpecificOrigins = "AllowAllForDev";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options => {
    options.AddPolicy("AllowAllForDev", policy => {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.AddServiceDefaults();

builder.Host.UseSerilog((context, loggerConfig) => loggerConfig.ReadFrom.Configuration(context.Configuration));

//builder.Services.AddSwaggerGenWithAuth();

//builder.AddRedisDistributedCache("cache");
//this one is new
//builder.Services.AddAuthentication()
//        .AddKeycloakJwtBearer("Keycloak",realm: "fmpr", options =>
//        {
//            options.RequireHttpsMetadata = false;
//            //options.Authority = builder.Configuration["Keycloak:Authority"];
//            //options.RequireHttpsMetadata = false;
//            options.Audience = "account";
//        });
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
 .AddKeycloakJwtBearer(
    serviceName: "keycloak",
    realm: "pgsh",
    configureOptions: options =>
    {
        //options.Audience = "pgsh.api";
        options.Audience = "account";
        options.RequireHttpsMetadata = false;
    }
    );
builder.Services.AddAuthorization();
//.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
//{
//    // Reading configuration from appsettings.json/appsettings.Development.json
//    // Keycloak:Authority must be the URL to your Keycloak server and realm 
//    // (e.g., "http://keycloak:8080/realms/fmpr")
//    options.Authority = builder.Configuration["Keycloak:Authority"];

//    // Keycloak:Audience must match the Client ID of your API client in Keycloak
//    options.Audience = builder.Configuration["Keycloak:Audience"];

//    // Set to false for development/local Keycloak over HTTP
//    options.RequireHttpsMetadata = builder.Environment.IsProduction();

//    // Optional: Configure claim mapping if your user ID is not in 'sub'
//    // options.TokenValidationParameters.NameClaimType = "sub"; 
//});


builder.Services
    .AddInfrastructure(builder.Configuration)
    .AddApplication()
    .AddPresentation();

//builder.Services.AddOpenApi();

builder.Services.AddApiDocumentation();

builder.Services.AddEndpoints(Assembly.GetExecutingAssembly());
builder.AddNpgsqlDbContext<ApplicationDbContext>(connectionName: "TodoDatabase");

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
// ONLY for development!

WebApplication app = builder.Build();
app.MapDefaultEndpoints();

//app.UseSwaggerUI(c =>
//{
//    c.SwaggerEndpoint("/openapi/v1.json", "PGSH API v1");

//    // OAuth2 PKCE
//    c.OAuthClientId("pgsh-swagger");         // client in Keycloak
//    c.OAuthAppName("PGSH Swagger UI");
//    c.OAuthUsePkce();
//    c.OAuthScopeSeparator(" ");
//});



if (app.Environment.IsDevelopment())
{
    app.AddApiDocumentationUI();
    //app.ApplyMigrations();
}

// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
//{
//app.MapOpenApi();
//app.UseSwaggerUI(opts => opts.SwaggerEndpoint("/openapi/v1.json", "Open API V1"));
//app.MapScalarApiReference();
//}
//app.MapHealthChecks("health-2", new HealthCheckOptions
//{
//    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
//});

app.UseHttpsRedirection();

app.UseCors(MyAllowSpecificOrigins);

app.UseRequestLoggingMiddleware();

app.UseSerilogRequestLogging();

app.UseExceptionHandler();

app.UseAuthentication();

app.UseAuthorization();

app.MapEndpoints();

app.MapControllers();

await app.RunAsync();
