namespace Solace.ApiServer.Types.Common;

public sealed record Effect(
    string Type,
    string? Duration,
    int? Value,
    string? Unit,
    string Targets,
    Guid[] Items,
    string[] ItemScenarios,
    string Activation,
    string? ModifiesType
);