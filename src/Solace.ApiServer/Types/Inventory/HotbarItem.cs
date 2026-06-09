namespace Solace.ApiServer.Types.Inventory;

public sealed record HotbarItem(
     Guid Id,
     int Count,
     Guid? InstanceId,
     float? Health
);
