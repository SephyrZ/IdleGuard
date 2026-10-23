using System.Text.Json;
using IdleGuard.Models;

namespace IdleGuard.Services;

public sealed class SettingsStore
{
    private readonly string _path;
    public SettingsStore(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IdleGuard", "settings.json");

    public AppSettings Load() => File.Exists(_path)
        ? (JsonSerializer.Deserialize(File.ReadAllText(_path), AppJsonContext.Default.AppSettings) ?? throw new InvalidDataException("Empty preferences")).Validated()
        : new();

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings.Validated(), AppJsonContext.Default.AppSettings));
        File.Move(temporary, _path, overwrite: true);
    }
}
