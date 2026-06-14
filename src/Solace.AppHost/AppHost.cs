using Microsoft.Extensions.Configuration;
using Solace.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

bool useSqlite = builder.Configuration.GetValue<bool>("Shared:UseSqlite");

IResourceBuilder<IResourceWithConnectionString> db;
if (useSqlite)
{
    db = builder.AddSqlite("EarthDb", "data", "earth.db");
}
else
{
    var postgres = builder.AddPostgres("postgres").WithPgAdmin();
    db = postgres.AddDatabase("EarthDb");
}

var eventBus = builder.AddProject<Projects.Solace_EventBus_Server>("event-bus")
    .WithEndpoint(scheme: "tcp", name: "raw-tcp", env: "TCP_PORT");

var objectStoreDataDirectory = Path.GetFullPath(builder.Configuration.GetValue<string>("ObjectStore:DataDirectory", "data"));

var objectStore = builder.AddProject<Projects.Solace_ObjectStore_Server>("object-store")
    .WithEndpoint(scheme: "tcp", name: "raw-tcp", env: "TCP_PORT")
    .WithEnvironment("ObjectStore__DataDirectory", objectStoreDataDirectory);

var locatorPort = builder.Configuration.GetValue<int>("Locator:Port", 8088);

var apiPort = builder.Configuration.GetValue<int>("ApiServer:Port", 8088);

var apiServer = builder.AddProject<Projects.Solace_ApiServer>("api-server")
    .WithHttpEndpoint(port: apiPort, name: "http")
    .WithReference(db)
    .WaitFor(db)
    .WithReference(eventBus)
    .WaitFor(eventBus)
    .WithReference(objectStore)
    .WaitFor(objectStore)
    .WithEnvironmentFromSection(builder.Configuration, "ApiServer:Authentication", "ApiServer:")
    .WithEnvironment("StaticDataPath", builder.Configuration["Shared:StaticDataPath"]);

if (useSqlite)
{
    apiServer.WithEnvironment("DatabaseProvider", "Sqlite");
}
else
{
    apiServer.WithEnvironment("DatabaseProvider", "Postgres");
}

var locator = builder.AddProject<Projects.Solace_Locator>("locator")
    .WithHttpEndpoint(port: locatorPort, name: "http")
    .WithReference(apiServer)
    .WaitFor(apiServer);

builder.Build().Run();
