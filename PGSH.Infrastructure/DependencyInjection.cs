using System.Text;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Application.Abstractions.Data;
using PGSH.Infrastructure.Authentication;
using PGSH.Infrastructure.Authorization;
using PGSH.Infrastructure.Database;
using PGSH.Infrastructure.Time;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using PGSH.SharedKernel;
using MediatR;
using Microsoft.AspNetCore.Authentication;

namespace PGSH.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration) =>
        services
            .AddServices()
            .AddDatabase(configuration)
            //.AddHealthChecks(configuration)
            .AddAuthenticationInternal(configuration)
            .AddAuthorizationInternal();

    private static IServiceCollection AddServices(this IServiceCollection services)
    {
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        return services;
    }

    private static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        //string connectionString = configuration.GetConnectionString("TodoDatabase") ?? throw new InvalidOperationException("The Postgres database connection string was not provided");

        //services.AddDbContext<ApplicationDbContext>
        //(
        //options => options

        //    .UseNpgsql(connectionString)
        // //, npgsqlOptions =>
        // //    npgsqlOptions.MigrationsHistoryTable(HistoryRepository.DefaultTableName, Schemas.Default))
        // //.UseSnakeCaseNamingConvention()
        // );

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        return services;
    }

    //private static IServiceCollection AddHealthChecks(this IServiceCollection services, IConfiguration configuration)
    //{
    //    services
    //        .AddHealthChecks()
    //        .AddNpgSql(configuration.GetConnectionString("TodoDatabase")!);

    //    return services;
    //}

    private static IServiceCollection AddAuthenticationInternal(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        //services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        //    .AddJwtBearer(o =>
        //    {
        //        o.RequireHttpsMetadata = false;
        //        o.TokenValidationParameters = new TokenValidationParameters
        //        {
        //            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Secret"]!)),
        //            ValidIssuer = configuration["Jwt:Issuer"],
        //            ValidAudience = configuration["Jwt:Audience"],
        //            ClockSkew = TimeSpan.Zero
        //        };
        //    });

        services.AddHttpContextAccessor();
        services.AddMemoryCache();
        services.AddScoped<IUserContext, UserContext>();
        //services.AddSingleton<IPasswordHasher, PasswordHasher>();
        //services.AddSingleton<ITokenProvider, TokenProvider>();

        return services;
    }

    private static IServiceCollection AddAuthorizationInternal(this IServiceCollection services)
    {
        services.AddAuthorization();

        services.AddScoped<PermissionProvider>();

        services.AddTransient<IAuthorizationHandler, PermissionAuthorizationHandler>();

        services.AddTransient<IAuthorizationPolicyProvider, PermissionAuthorizationPolicyProvider>();

        services.AddTransient<IClaimsTransformation, KeycloakRoleTransformer>();

        return services;
    }
}
