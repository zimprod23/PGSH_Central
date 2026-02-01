using PGSH.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin(pgadm => pgadm.WithHostPort(5050))
    .AddDatabase("TodoDatabase");

var keycloak = builder.AddKeycloak("keycloak", 8082)
    .WithDataVolume()
    .WithExternalHttpEndpoints();

//var redis = builder.AddRedis("cache");

var apiService = builder.AddProject<Projects.PGSH_API>("pgsh-api")
    .WithReference(postgres)
    .WithReference(keycloak)
    //.WithHealthCheck("/health")
    //.WithEnvironment("Keycloak:Authority", keycloak.GetEndpoint("http") + "/realms/fmpr") // <-- NEW

    //// Inject Keycloak Audience (Client ID)
    //.WithEnvironment("Keycloak:Audience", "account")
    //.WithReference(redis)   
    .WaitFor(postgres)
    .WaitFor(keycloak)
    //.WaitFor(redis)
    //.WithSwaggerUI()
    .WithScalarUI()
    .WithSwaggerUI();

builder.AddViteApp(name: "pgsh-frontend", workingDirectory: "../PGSH.Frontend")
    .WithEndpoint("http", endpoint =>
    {
        endpoint.Port = 5173; // Force the PORT to 5173 every time
    })
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    //.WithReference(keycloak) // This is the key!
    //.WithEnvironment("VITE_KEYCLOAK_URL", keycloak.GetEndpoint("http"))
    .WaitFor(apiService)
    .WithNpmPackageInstallation();

builder.AddProject<Projects.PGSH_MigrationService>("migrations")
        .WithReference(postgres)
        .WaitFor(postgres);

builder.Build().Run();
