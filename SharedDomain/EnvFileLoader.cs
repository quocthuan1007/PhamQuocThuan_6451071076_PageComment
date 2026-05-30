using System.Text;

namespace SharedDomain;

public static class EnvFileLoader
{
    public static void LoadFromRepoRoot(string fileName = ".env")
    {
        var envPath = FindFileUpwards(Directory.GetCurrentDirectory(), fileName);
        if (envPath is null || !File.Exists(envPath))
        {
            return;
        }

        foreach (var rawLine in File.ReadAllLines(envPath, Encoding.UTF8))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();

            if (value.Length >= 2)
            {
                var wrappedInDoubleQuotes = value.StartsWith('"') && value.EndsWith('"');
                var wrappedInSingleQuotes = value.StartsWith('\'') && value.EndsWith('\'');
                if (wrappedInDoubleQuotes || wrappedInSingleQuotes)
                {
                    value = value[1..^1];
                }
            }

            Environment.SetEnvironmentVariable(key, value);
        }
    }

    private static string? FindFileUpwards(string startDirectory, string fileName)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
