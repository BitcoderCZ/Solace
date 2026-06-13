using Projects;

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

var eventBus = builder.AddProject<Solace_EventBus_Server>("event-bus");

var objectStore = builder.AddProject<Solace_ObjectStore_Server>("object-store");

var adminPanel = builder.AddProject<Solace_AdminPanel>("admin-panel")
    .WithReference(db)
    .WithReference(eventBus)
    .WithReference(objectStore);

builder.Build().Run();
