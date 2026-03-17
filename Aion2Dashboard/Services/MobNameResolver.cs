using System.IO;
using System.Text.Json;

namespace Aion2Dashboard.Services;

public sealed class MobNameResolver
{
    private readonly Lazy<IReadOnlyDictionary<int, MobMetadata>> _mobs;

    public MobNameResolver()
    {
        _mobs = new Lazy<IReadOnlyDictionary<int, MobMetadata>>(LoadMobs, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string? Resolve(int mobCode)
    {
        return ResolveInfo(mobCode)?.Name;
    }

    public MobMetadata? ResolveInfo(int mobCode)
    {
        return _mobs.Value.TryGetValue(mobCode, out var mob) ? mob : null;
    }

    private static IReadOnlyDictionary<int, MobMetadata> LoadMobs()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "mobs.json");
        if (!File.Exists(path))
        {
            return new Dictionary<int, MobMetadata>();
        }

        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);

        var values = new Dictionary<int, MobMetadata>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!int.TryParse(property.Name, out var mobCode))
            {
                continue;
            }

            if (!property.Value.TryGetProperty("name", out var nameNode))
            {
                continue;
            }

            var name = nameNode.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var isBoss = property.Value.TryGetProperty("isBoss", out var bossNode) && bossNode.ValueKind == JsonValueKind.True;
            values[mobCode] = new MobMetadata(name, isBoss);
        }

        return values;
    }
}

public sealed record MobMetadata(string Name, bool IsBoss);
