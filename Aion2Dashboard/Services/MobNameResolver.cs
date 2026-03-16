using System.IO;
using System.Text.Json;

namespace Aion2Dashboard.Services;

public sealed class MobNameResolver
{
    private readonly Lazy<IReadOnlyDictionary<int, string>> _names;

    public MobNameResolver()
    {
        _names = new Lazy<IReadOnlyDictionary<int, string>>(LoadNames, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string? Resolve(int mobCode)
    {
        return _names.Value.TryGetValue(mobCode, out var name) ? name : null;
    }

    private static IReadOnlyDictionary<int, string> LoadNames()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "mobs.json");
        if (!File.Exists(path))
        {
            return new Dictionary<int, string>();
        }

        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);

        var values = new Dictionary<int, string>();
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

            var name = nameNode.GetString();
            if (!string.IsNullOrWhiteSpace(name))
            {
                values[mobCode] = name.Trim();
            }
        }

        return values;
    }
}
