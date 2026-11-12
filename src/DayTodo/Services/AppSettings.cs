using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace DayTodo.Services;

/// <summary>
/// User preferences that are not tied to a single feature. The MCP switch keeps its
/// own file (mcp-settings.json) so the MCP section stays self-contained.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// Closing the main window hides it to the tray instead of quitting.
    /// When false, the close button quits DayTodo for real.
    /// </summary>
    public bool MinimizeToTrayOnClose { get; set; } = true;

    public static string FilePath => Path.Combine(DataStore.DataDir, "app-settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            var s = JsonSerializer.Deserialize<AppSettings>(
                File.ReadAllText(FilePath, Encoding.UTF8)) ?? new AppSettings();
            return s;
        }
        catch (Exception) { return new AppSettings(); }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(DataStore.DataDir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings,
                new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.Move(tmp, FilePath, true);
        }
        catch (Exception) { }
    }
}
