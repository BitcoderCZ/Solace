namespace Solace.ApiServer.Types.Inventory;

internal sealed record StackableInventoryItem(
    Guid Id,
    int Owned,
    int Fragments,
    StackableInventoryItem.OnR Unlocked,
    StackableInventoryItem.OnR Seen
)
{
    internal sealed record OnR(
        string On
    );
}
