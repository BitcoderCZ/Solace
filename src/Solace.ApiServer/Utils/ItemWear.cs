using Solace.StaticData;

namespace Solace.ApiServer.Utils;

public static partial class ItemWear
{
    public static float WearToHealth(string itemId, int wear, Catalog.ItemsCatalogR itemsCatalog, ILogger logger)
    {
        Catalog.ItemsCatalogR.Item? catalogItem = itemsCatalog.GetItem(itemId);

        if (catalogItem is null || catalogItem.ToolInfo is null)
        {
            LogHealthForNonTool(logger, itemId);
            return 100.0f;
        }

        return ((catalogItem.ToolInfo.MaxWear - wear) / (float)catalogItem.ToolInfo.MaxWear) * 100.0f;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Attempt to get item health for non-tool item '{ItemId}'")]
    private static partial void LogHealthForNonTool(ILogger logger, string ItemId);
}
