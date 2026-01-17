using PGSH.Infrastructure.Database;
using PGSH.MigrationService;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddHostedService<Worker>();

builder.Services.AddOpenTelemetry()
        .WithTracing(tracing => tracing.AddSource(Worker.ActivitySourceName));

builder.AddNpgsqlDbContext<ApplicationDbContext>("TodoDatabase");

var host = builder.Build();
host.Run();
