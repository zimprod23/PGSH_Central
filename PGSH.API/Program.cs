using Application;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using PGSH.API;
using PGSH.API.Extensions;
using PGSH.API.Infrastructure;
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

// ⚠ `WriteTo.Console()` is applied BEFORE the configuration, and it is not decoration. `UseSerilog`
// replaces the default logger providers, and `ReadFrom.Configuration` on a file with no `Serilog`
// section produces a logger with **no sinks at all** — which is what this project had: every
// `ILogger` call in the API went nowhere, silently, and an operator reading the resource logs saw
// only what Aspire itself printed. Found 17/09/2026, when a LogCritical explaining why the API
// refused to start did not appear anywhere. A configured sink in appsettings adds to this one rather
// than replacing it, so the console can never go quiet again by editing a JSON file.
builder.Host.UseSerilog((context, loggerConfig) => loggerConfig
    .WriteTo.Console()
    .ReadFrom.Configuration(context.Configuration));

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
//    c.OAuthClientId(ApiDocumentationAuth.ClientId);         // client in Keycloak
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
var apiGroup = app.MapGroup("api");

app.UseHttpsRedirection();

app.UseCors(MyAllowSpecificOrigins);

app.UseSerilogRequestLogging();

app.UseExceptionHandler();

app.UseAuthentication();

app.UseAuthorization();

app.UseRequestLoggingMiddleware();

app.MapEndpoints(apiGroup);

app.MapControllers();

// ⚠ Asked once, here, rather than discovered as a 500 on every screen: a database that answers and
// holds no schema has never been built, and the remedy is a restore. An unreachable database is a
// different state and still boots — DatabaseOutage answers it with a 503. See SchemaPresenceCheck.
// ⚠ It returns instead of throwing: the explanation is already in the log, and a stack trace would
// dress an ordinary infrastructure state up as a crash.
if (!await app.EnsureSchemaExistsAsync())
    return;

await app.RunAsync();

/// <summary>
/// Top-level statements compile to an <c>internal</c> <c>Program</c>, which
/// <c>WebApplicationFactory&lt;T&gt;</c> cannot reach. Declaring the partial makes it public so
/// <c>PGSH.Tests/Integration/</c> can host this exact pipeline — the routes, the model binding, the
/// authentication, the exception handler and the problem-details mapping — instead of a
/// reconstruction of it that can agree with the tests while disagreeing with production.
/// </summary>
public partial class Program;
