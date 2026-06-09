namespace Solace.ApiServer.Types.Catalog;

public sealed record RecipesCatalog(
    RecipesCatalog.CraftingRecipe[] Crafting,
    RecipesCatalog.SmeltingRecipe[] Smelting
)
{
    public sealed record CraftingRecipe(
        Guid Id,
        string Category,
        string Duration,
        CraftingRecipe.Ingredient[] Ingredients,
        CraftingRecipe.OutputR Output,
        CraftingRecipe.ReturnItem[] ReturnItems,
        bool Deprecated
    )
    {
        public sealed record Ingredient(
            Guid[] Items,
            int Quantity
        );

        public sealed record OutputR(
            Guid ItemId,
            int Quantity
        );

        public sealed record ReturnItem(
            Guid Id,
            int Amount
        );
    }

    public sealed record SmeltingRecipe(
        Guid Id,
        int HeatRequired,
        Guid InputItemId,
        SmeltingRecipe.OutputR Output,
        SmeltingRecipe.ReturnItem[] ReturnItems,
        bool Deprecated
    )
    {
        public sealed record OutputR(
            Guid ItemId,
            int Quantity
        );

        public sealed record ReturnItem(
            Guid Id,
            int Amount
        );
    }
}
