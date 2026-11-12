using System;
using System.IO;
using System.Text;
using System.Text.Json;
using DayTodo.Services;

namespace DayTodo.Mcp;

/// <summary>
/// Persists the MCP on/off switch next to tasks.json (a separate file so the task
/// data itself stays clean). Kept deliberately tiny: no registry, no settings UI.
/// </summary>
public sealed class McpSettings
{
    public bool Enabled { get; set; } = true;
    public int Port { get; set; } = 8765;
    public string Token { get; set; } = string.Empty;

    public static string FilePath => Path.Combine(DataStore.DataDir, "mcp-settings.json");

    public static McpSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new McpSettings();
            var s = JsonSerializer.Deserialize<McpSettings>(
                File.ReadAllText(FilePath, Encoding.UTF8)) ?? new McpSettings();
            if (s.Port < 1 || s.Port > 65535) s.Port = 8765;
            s.Token ??= string.Empty;
            return s;
        }
        catch (Exception) { return new McpSettings(); }
    }

    public static void Save(McpSettings settings)
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
