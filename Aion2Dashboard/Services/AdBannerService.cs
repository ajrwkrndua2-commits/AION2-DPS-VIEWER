using System.IO;
using System.Text.Json;
using Aion2Dashboard.Models;

namespace Aion2Dashboard.Services;

public sealed class AdBannerService
{
    private const string FileName = "ad-banner.json";

    public AdBannerConfig Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, FileName);
        if (!File.Exists(path))
        {
            return new AdBannerConfig { Enabled = false };
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AdBannerConfig>(json) ?? new AdBannerConfig { Enabled = false };
        }
        catch
        {
            return new AdBannerConfig { Enabled = false };
        }
    }
}
