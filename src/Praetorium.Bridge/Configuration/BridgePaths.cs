using System;
using System.IO;

namespace Praetorium.Bridge.Configuration;

/// <summary>
/// Resolves standard file system paths for Praetorium.Bridge configuration and data.
/// </summary>
public static class BridgePaths
{
    private const string AppDirectoryName = "PraetoriumBridge";
    private const string ConfigFileName = "praetorium-bridge.json";
    private const string PromptsFolderName = "prompts";

    /// <summary>
    /// Gets the root directory for Praetorium.Bridge data under the user's application data folder.
    /// </summary>
    /// <remarks>
    /// Resolves to <c>%APPDATA%\PraetoriumBridge</c> on Windows,
    /// <c>~/.config/PraetoriumBridge</c> on Linux/macOS.
    /// </remarks>
    public static string AppDataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppDirectoryName);

    /// <summary>
    /// Gets the default full path to the bridge configuration file.
    /// </summary>
    public static string DefaultConfigFilePath { get; } =
        Path.Combine(AppDataDirectory, ConfigFileName);

    /// <summary>
    /// Gets the default directory for prompt template files.
    /// </summary>
    public static string DefaultPromptsDirectory { get; } =
        Path.Combine(AppDataDirectory, PromptsFolderName);

    /// <summary>
    /// Ensures the application data directory and prompts subdirectory exist.
    /// </summary>
    public static void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(AppDataDirectory);
        Directory.CreateDirectory(DefaultPromptsDirectory);
    }
}
