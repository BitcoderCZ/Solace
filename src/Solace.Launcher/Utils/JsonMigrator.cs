using System.Text.Json.Nodes;

namespace Solace.Launcher.Utils;

internal static class JsonMigrator
{
    public static JsonNode? MergeNodes(JsonNode? currentDefault, JsonNode? currentValue, JsonNode? nextDefault)
    {
        if (nextDefault is null)
        {
            if (currentDefault is null && currentValue is not null)
            {
                return currentValue.DeepClone();
            }
            
            return null;
        }

        if (currentDefault is not null && GetJsonNodeType(currentDefault) != GetJsonNodeType(nextDefault))
        {
            return nextDefault.DeepClone();
        }

        if (nextDefault is JsonObject nextObj)
        {
            var resultObj = new JsonObject();
            var currentObj = currentDefault as JsonObject;
            var userObj = currentValue as JsonObject;

            var allKeys = nextObj.Select(p => p.Key)
                .Union(userObj?.Select(p => p.Key) ?? []);

            foreach (var key in allKeys)
            {
                nextObj.TryGetPropertyValue(key, out var nVal);

                JsonNode? cVal = null;
                currentObj?.TryGetPropertyValue(key, out cVal);

                JsonNode? uVal = null;
                userObj?.TryGetPropertyValue(key, out uVal);

                var mergedVal = MergeNodes(cVal, uVal, nVal);
                if (mergedVal != null)
                {
                    resultObj[key] = mergedVal;
                }
            }

            return resultObj;
        }

        if (nextDefault is JsonArray nextArr)
        {
            if (currentValue is JsonArray userArr && !JsonNode.DeepEquals(currentDefault, currentValue))
            {
                return userArr.DeepClone();
            }

            return nextArr.DeepClone();
        }

        bool currentValueModified = !JsonNode.DeepEquals(currentDefault, currentValue);
        if (currentValueModified && currentValue is not null && GetJsonNodeType(currentValue) == GetJsonNodeType(nextDefault))
        {
            return currentValue.DeepClone();
        }

        return nextDefault.DeepClone();
    }

    private static Type GetJsonNodeType(JsonNode node)
        => node switch
        {
            JsonObject => typeof(JsonObject),
            JsonArray => typeof(JsonArray),
            _ => typeof(JsonValue) // int, string, null, ...
        };
}