#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Solace.Buildplate.Connector.Model;
#pragma warning restore IDE0130 // Namespace does not match folder structure

public sealed record InventoryResponse(
    InventoryResponse.Item[] Items,
    InventoryResponse.HotbarItem?[] Hotbar
)
{
    public sealed record Item(
        Guid Id,
        int Count,
        Guid? InstanceId,
        int Wear
    );

    public sealed record HotbarItem(
        Guid Id,
        int Count,
        Guid? InstanceId
    );
}
