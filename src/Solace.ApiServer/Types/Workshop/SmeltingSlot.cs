using Solace.ApiServer.Types.Common;

namespace Solace.ApiServer.Types.Workshop;

public record SmeltingSlot(
    SmeltingSlot.FuelR? Fuel,
    SmeltingSlot.BurningR? Burning,
    string? SessionId,
    Guid? RecipeId,
    OutputItem? Output,
    InputItem[]? Escrow,
    int Completed,
    int Available,
    int Total,
    string? NextCompletionUtc,
    string? TotalCompletionUtc,
    State State,
    BoostState? BoostState,
    UnlockPrice? UnlockPrice,
    int StreamVersion
)
{
    public sealed record FuelR(
        BurnRate BurnRate,
        Guid ItemId,
        int Quantity,
        Guid[] ItemInstanceIds
    );

    public sealed record BurningR(
        string? BurnStartTime,
        string? BurnsUntil,
        string? RemainingBurnTime,
        float? HeatDepleted,
        FuelR Fuel
    );
}
