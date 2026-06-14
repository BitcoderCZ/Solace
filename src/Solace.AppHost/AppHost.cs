using Microsoft.Extensions.Configuration;

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
    .WithEnvironment("Authentication__LocalLoginOnly", apiLocalLoginOnly.ToString())
    .WithEnvironment("Authentication__Login__SoapHeaderValidityMinutes", builder.Configuration["ApiServer:Authentication:Login:SoapHeaderValidityMinutes"])
    .WithEnvironment("Authentication__Login__UserTokenValidityMinutes", builder.Configuration["ApiServer:Authentication:Login:UserTokenValidityMinutes"])
    .WithEnvironment("Authentication__Login__DeviceTokenValidityMinutes", builder.Configuration["ApiServer:Authentication:Login:DeviceTokenValidityMinutes"])
    .WithEnvironment("Authentication__Login__XboxTokenValidityMinutes", builder.Configuration["ApiServer:Authentication:Login:XboxTokenValidityMinutes"])
    .WithEnvironment("Authentication__XboxLive__TokenValidityMinutes", builder.Configuration["ApiServer:Authentication:XboxLive:TokenValidityMinutes"])
    .WithEnvironment("Authentication__PlayfabApi__EntityTokenValidityMinutes", builder.Configuration["ApiServer:Authentication:PlayfabApi:EntityTokenValidityMinutes"])
    .WithEnvironment("Authentication__PlayfabApi__SessionTicketValidityMinutes", builder.Configuration["ApiServer:Authentication:PlayfabApi:SessionTicketValidityMinutes"])
    .WithEnvironment("StaticDataPath", builder.Configuration["Shared:StaticDataPath"]);

if (useSqlite)
{
    apiServer.WithEnvironment("DatabaseProvider", "Sqlite");
}
else
{
    apiServer.WithEnvironment("DatabaseProvider", "Postgres");
}

builder.Build().Run();
