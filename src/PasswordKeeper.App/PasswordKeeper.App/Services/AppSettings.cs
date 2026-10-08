using System.Text.Json;
using System.Text.Json.Serialization;

namespace PasswordKeeper.App.Services;

/// <summary>Non-secret preferences, currently just which vault file to use.</summary>
public sealed class AppSettings
{
    public string? VaultPath { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PasswordKeeper", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize(File.ReadAllText(FilePath), AppSettingsJson.Default.AppSettings) ?? new()
                : new();
        }
        catch
        {
            return new();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, AppSettingsJson.Default.AppSettings));
    }
}

[JsonSerializable(typeof(AppSettings))]
internal partial class AppSettingsJson : JsonSerializerContext
{
}
