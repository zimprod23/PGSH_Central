using PGSH.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

// The data volume survives container restarts; without it every `dotnet run` starts from an empty
// database and the seeder re-creates everything, losing whatever was entered by hand. The named
// volume keeps the same storage across runs — delete it explicitly to start clean.
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("pgsh-postgres-data")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithPgAdmin(pgadm => pgadm.WithHostPort(5050))
    .AddDatabase("TodoDatabase");

// ⚠ Le realm est un FICHIER versionné (`keycloak/pgsh-realm.json`), pas seulement le contenu du
// volume. Le 17/09/2026 une réinitialisation de Docker Desktop a emporté le .vhdx : la base est
// revenue d'un point de sauvegarde (ils vivent hors du volume), le realm n'avait aucune copie et
// était à refaire de mémoire. C'est la branche « established in writing as independent » de
// PHASES.md §18.2 — un realm écrit n'a pas besoin d'être sauvegardé, il se reconstruit.
// ⚠ Keycloak n'importe QUE si le realm est absent : ce fichier ne réécrit jamais un realm déjà là,
// donc ce qu'un humain règle dans l'admin console lui survit. Détails : keycloak/README.md.
var keycloak = builder.AddKeycloak("keycloak", 8082)
    .WithDataVolume()
    .WithRealmImport("../keycloak")
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
