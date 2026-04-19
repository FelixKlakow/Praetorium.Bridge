using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Praetorium.Bridge.CopilotProvider;

/// <summary>
/// Locates the Copilot CLI executable by searching well-known installation paths.
/// Checked in order: explicit option, PATH, WinGet, npm global, scoop, brew.
/// </summary>
internal static class CopilotCliLocator
{
    private const string CliExecutable = "copilot.exe";
    private const string CliExecutableUnix = "copilot";

    /// <summary>
    /// Returns the path to the Copilot CLI executable, or null if not found.
    /// </summary>
    /// <param name="explicitPath">An explicit path set by the user. Takes highest priority.</param>
    internal static string? Locate(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            if (!File.Exists(explicitPath))
                throw new InvalidOperationException(
                    $"Copilot CLI not found at the configured CliPath '{explicitPath}'.");
            return explicitPath;
        }

        foreach (var candidate in GetSearchPaths())
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static IEnumerable<string> GetSearchPaths()
    {
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? CliExecutable : CliExecutableUnix;

        // 1. Search PATH entries
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            yield return Path.Combine(dir, exeName);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // 2. WinGet installation path pattern
            var winGetBase = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
            if (Directory.Exists(winGetBase))
            {
                foreach (var dir in Directory.GetDirectories(winGetBase, "GitHub.Copilot*"))
                    yield return Path.Combine(dir, exeName);
            }

            // 3. npm global installation
            yield return Path.Combine(appData, "npm", exeName);

            // 4. Scoop
            yield return Path.Combine(userProfile, "scoop", "shims", exeName);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // 5. Homebrew (Apple Silicon and Intel)
            yield return "/opt/homebrew/bin/copilot";
            yield return "/usr/local/bin/copilot";
        }
        else
        {
            // 6. Linux common bin paths
            yield return "/usr/local/bin/copilot";
            yield return "/usr/bin/copilot";
        }
    }
}
