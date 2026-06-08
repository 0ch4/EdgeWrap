using System.Text.Json;
using System.Text.Json.Serialization;

namespace EdgeWrap.Config;

public static class ConfigStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EdgeWrap");

    private static readonly string FilePath = Path.Combine(Dir, "config.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Full path to the config file (for display in the UI).</summary>
    public static string Location => FilePath;

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, Options);
                if (cfg != null)
                    return cfg;
            }
        }
        catch
        {
            // Corrupt or unreadable config: fall back to defaults rather than crash.
        }
        return new AppConfig();
    }

    public static void Save(AppConfig config)
    {
        Directory.CreateDirectory(Dir);
        var json = JsonSerializer.Serialize(config, Options);
        File.WriteAllText(FilePath, json);
    }
}
