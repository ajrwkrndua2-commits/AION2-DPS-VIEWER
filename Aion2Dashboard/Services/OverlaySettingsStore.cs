using System.IO;
using System.Text.Json;
using Aion2Dashboard.Models;

namespace Aion2Dashboard.Services;

public sealed class OverlaySettingsStore
{
    private readonly string _settingsPath;

    public OverlaySettingsStore()
    {
        _settingsPath = Path.Combine(AppContext.BaseDirectory, "overlay-settings.json");
    }

    public OverlaySettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new OverlaySettings();
            }

            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<OverlaySettings>(json) ?? new OverlaySettings();
        }
        catch
        {
            return new OverlaySettings();
        }
    }

    public void Save(OverlaySettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }
        catch
        {
        }
    }
}
