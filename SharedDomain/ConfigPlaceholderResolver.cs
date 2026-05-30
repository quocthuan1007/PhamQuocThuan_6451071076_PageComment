using Microsoft.Extensions.Configuration;

namespace SharedDomain;

public static class ConfigPlaceholderResolver
{
    public static void Apply(ConfigurationManager configuration)
    {
        var resolved = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in configuration.AsEnumerable())
        {
            if (pair.Value is null)
            {
                continue;
            }

            resolved[pair.Key] = ResolvePlaceholders(pair.Value);
        }

        configuration.AddInMemoryCollection(resolved);
    }

    private static string ResolvePlaceholders(string value)
    {
        var resolved = value;
        var startIndex = resolved.IndexOf("${", StringComparison.Ordinal);

        while (startIndex >= 0)
        {
            var endIndex = resolved.IndexOf('}', startIndex + 2);
            if (endIndex < 0)
            {
                break;
            }

            var variableName = resolved[(startIndex + 2)..endIndex];
            var envValue = Environment.GetEnvironmentVariable(variableName) ?? string.Empty;
            resolved = resolved[..startIndex] + envValue + resolved[(endIndex + 1)..];
            startIndex = resolved.IndexOf("${", StringComparison.Ordinal);
        }

        return resolved;
    }
}
