using PGSH.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin(pgadm => pgadm.WithHostPort(5050))
    .AddDatabase("TodoDatabase");

var keycloak = builder.AddKeycloak("keycloak", 8082)
    .WithDataVolume()
    .WithExternalHttpEndpoints();

//var redis = builder.AddRedis("cache");

builder.AddProject<Projects.PGSH_API>("pgsh-api")
    .WithReference(postgres)
    .WithReference(keycloak)
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

builder.AddProject<Projects.PGSH_MigrationService>("migrations")
        .WithReference(postgres)
        .WaitFor(postgres);

builder.Build().Run();
