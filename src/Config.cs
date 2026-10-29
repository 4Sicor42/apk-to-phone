using System.Text.Json;

namespace ApkToPhone;

/// <summary>Настройки пользователя (%LOCALAPPDATA%\ApkToPhone\config.json).</summary>
internal sealed class Config
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Путь к adb.exe, если его нет в PATH и в стандартных местах.</summary>
    public string? AdbPath { get; set; }

    /// <summary>Дополнительные параметры для adb install.</summary>
    public string InstallArguments { get; set; } = string.Empty;

    public static string DirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApkToPhone");

    public static string FilePath => Path.Combine(DirectoryPath, "config.json");

    public static Config Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<Config>(json, JsonOptions) ?? new Config();
            }
        }
        catch (Exception)
        {
            // битый конфиг — берём значения по умолчанию
        }

        return new Config();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception)
        {
            // не критично
        }
    }

    public IEnumerable<string> ExtraInstallArguments()
    {
        if (string.IsNullOrWhiteSpace(InstallArguments))
        {
            yield break;
        }

        foreach (var part in InstallArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return part;
        }
    }
}
