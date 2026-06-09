namespace Solace.ApiServer.Types.Inventory;

public sealed record NonStackableInventoryItem(
    Guid Id,
    NonStackableInventoryItem.Instance[] Instances,
    int Fragments,
    NonStackableInventoryItem.OnR Unlocked,
    NonStackableInventoryItem.OnR Seen
)
{
    public sealed record Instance(
        Guid Id,
        float Health
    );

    public sealed record OnR(
        string On
    );
}
