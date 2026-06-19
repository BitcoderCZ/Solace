namespace Solace.ApiServer.Types.Inventory;

internal sealed record NonStackableInventoryItem(
    Guid Id,
    NonStackableInventoryItem.Instance[] Instances,
    int Fragments,
    NonStackableInventoryItem.OnR Unlocked,
    NonStackableInventoryItem.OnR Seen
)
{
    internal sealed record Instance(
        Guid Id,
        float Health
    );

    internal sealed record OnR(
        string On
    );
}
