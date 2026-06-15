using static Solace.ApiServer.Types.Catalog.JournalCatalog;

namespace Solace.ApiServer.Types.Catalog;

internal sealed record JournalCatalog(
    Dictionary<string, Item> Items
)
{
    internal sealed record Item(
        string ReferenceId,
        string ParentCollection,
        int OverallOrder,
        int CollectionOrder,
        string? DefaultSound,
        bool Deprecated,
        string ToolsVersion
    );
}
