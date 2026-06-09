namespace Solace.ApiServer.Types.Workshop;

public sealed record InputItem(
     Guid ItemId,
     int Quantity,
     Guid[] InstanceIds
);