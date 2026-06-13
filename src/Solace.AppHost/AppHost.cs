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

var objectStoreDataDirectory = builder.Configuration.GetValue<string>("ObjectStore:DataDirectory", "data");

var objectStore = builder.AddProject<Projects.Solace_ObjectStore_Server>("object-store")
    .WithEndpoint(scheme: "tcp", name: "raw-tcp", env: "TCP_PORT")
    .WithEnvironment("ObjectStore__DataDirectory", objectStoreDataDirectory);

// var adminPanel = builder.AddProject<Projects.Solace_AdminPanel>("admin-panel")
//     .WithReference(db)
//     .WithReference(eventBus);
    // .WithReference(objectStore);

builder.Build().Run();
