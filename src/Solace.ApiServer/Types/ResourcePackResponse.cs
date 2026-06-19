namespace Solace.ApiServer.Types;

internal sealed record ResourcePackResponse(int Order, int[] ParsedResourcePackVersion, string RelativePath, string ResourcePackVersion, string ResourcePackId);