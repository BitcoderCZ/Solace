using Microsoft.Extensions.Configuration;

namespace Solace.AppHost;

internal static class AspireExtension
{
    public static IResourceBuilder<T> WithEnvironmentFromSection<T>(
        this IResourceBuilder<T> builder,
        IConfiguration config,
        string sectionPath,
        string prefixToRemove = "")
            where T : IResourceWithEnvironment
    {
        var section = config.GetSection(sectionPath);

        foreach (var kvp in section.AsEnumerable())
        {
            if (kvp.Value is null)
            {
                continue;
            }

            var envName = kvp.Key;
            if (!string.IsNullOrEmpty(prefixToRemove) && envName.StartsWith(prefixToRemove, StringComparison.Ordinal))
            {
                envName = envName[prefixToRemove.Length..];
            }

            envName = envName.Replace(":", "__", StringComparison.Ordinal);
            builder.WithEnvironment(envName, kvp.Value);
        }

        return builder;
    }
}