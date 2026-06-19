namespace Solace.ApiServer.Types.Catalog;

internal sealed record RecipesCatalog(
    RecipesCatalog.CraftingRecipe[] Crafting,
    RecipesCatalog.SmeltingRecipe[] Smelting
)
{
    internal sealed record CraftingRecipe(
        Guid Id,
        string Category,
        string Duration,
        CraftingRecipe.Ingredient[] Ingredients,
        CraftingRecipe.OutputR Output,
        CraftingRecipe.ReturnItem[] ReturnItems,
        bool Deprecated
    )
    {
        internal sealed record Ingredient(
            Guid[] Items,
            int Quantity
        );

        internal sealed record OutputR(
            Guid ItemId,
            int Quantity
        );

        internal sealed record ReturnItem(
            Guid Id,
            int Amount
        );
    }

    internal sealed record SmeltingRecipe(
        Guid Id,
        int HeatRequired,
        Guid InputItemId,
        SmeltingRecipe.OutputR Output,
        SmeltingRecipe.ReturnItem[] ReturnItems,
        bool Deprecated
    )
    {
        internal sealed record OutputR(
            Guid ItemId,
            int Quantity
        );

        internal sealed record ReturnItem(
            Guid Id,
            int Amount
        );
    }
}
