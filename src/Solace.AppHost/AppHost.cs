using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

bool isSelfHosted = builder.Configuration["Mode"] == "SelfHosted";

IResourceBuilder<IResourceWithConnectionString> db;
if (isSelfHosted)
{
    db = builder.AddConnectionString("EarthDb");
}
else
{
    db = builder.AddPostgres("postgres").AddDatabase("EarthDb");
}

var eventBus = builder.AddProject<Projects.Solace_EventBus_Server>("event-bus")
    .WithEndpoint(scheme: "tcp", name: "raw-tcp", env: "TCP_PORT");

var objectStoreDataDirectory = Path.GetFullPath(builder.Configuration.GetValue<string>("ObjectStore:DataDirectory", "data"));

var objectStore = builder.AddProject<Projects.Solace_ObjectStore_Server>("object-store")
    .WithEndpoint(scheme: "tcp", name: "raw-tcp", env: "TCP_PORT")
    .WithEnvironment("ObjectStore__DataDirectory", objectStoreDataDirectory);

var apiPort = builder.Configuration.GetValue<int>("ApiServer:Port", 8080);
var apiLocalLoginOnly = builder.Configuration.GetValue<bool>("ApiServer:Authentication:LocalLoginOnly", false);

var apiServer = builder.AddProject<Projects.Solace_ApiServer>("api-server")
    .WithHttpEndpoint(port: apiPort, name: "http")
    .WithReference(db)
    .WaitFor(db)
    .WithReference(eventBus)
    .WaitFor(eventBus)
    .WithReference(objectStore)
    .WaitFor(objectStore)
    .WithEnvironment("Authentication__LocalLoginOnly", apiLocalLoginOnly.ToString());

if (isSelfHosted)
{
    apiServer.WithEnvironment("DatabaseProvider", "Sqlite");
}
else
{
    apiServer.WithEnvironment("DatabaseProvider", "Postgres");
}

builder.Build().Run();
