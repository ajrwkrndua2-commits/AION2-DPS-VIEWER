using System.IO;
using System.Text;
using System.Text.Json;

namespace Aion2Dashboard.Services;

public sealed class SkillNameResolver
{
    private readonly IReadOnlyDictionary<int, string> _skills;
    private readonly string _unknownSkillLogPath = Path.Combine(AppContext.BaseDirectory, "unknown-skills.log");
    private readonly object _logSync = new();
    private readonly HashSet<int> _loggedUnknownCodes = [];

    public SkillNameResolver()
    {
        _skills = LoadMap();
    }

    public string Resolve(int skillCode)
    {
        foreach (var candidate in EnumerateCandidates(skillCode))
        {
            if (_skills.TryGetValue(candidate, out var skillName) && !string.IsNullOrWhiteSpace(skillName))
            {
                return skillName;
            }
        }

        LogUnknownSkill(skillCode);
        return $"Skill {skillCode}";
    }

    private static IEnumerable<int> EnumerateCandidates(int skillCode)
    {
        yield return skillCode;

        var absolute = Math.Abs(skillCode);
        if (absolute != skillCode)
        {
            yield return absolute;
        }

        var offsets = new[] { 100000000, 200000000, 300000000, 400000000 };
        foreach (var offset in offsets)
        {
            if (absolute > offset)
            {
                yield return absolute - offset;
            }
        }

        if (absolute > 10000000)
        {
            yield return absolute % 100000000;
            yield return absolute % 10000000;
        }

        if (absolute > 1000000)
        {
            yield return absolute % 1000000;
        }

        if (absolute > 100000)
        {
            yield return absolute % 100000;
        }

        var asText = absolute.ToString();
        if (asText.Length > 8 && int.TryParse(asText[^8..], out var last8))
        {
            yield return last8;
        }

        if (asText.Length > 7 && int.TryParse(asText[^7..], out var last7))
        {
            yield return last7;
        }
    }

    private static IReadOnlyDictionary<int, string> LoadMap()
    {
        var merged = new Dictionary<int, string>();
        foreach (var path in GetCandidatePaths())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (raw is null)
                {
                    continue;
                }

                foreach (var pair in raw)
                {
                    if (int.TryParse(pair.Key, out var code) && !string.IsNullOrWhiteSpace(pair.Value))
                    {
                        merged[code] = pair.Value.Trim();
                    }
                }
            }
            catch
            {
            }
        }

        return merged;
    }

    private static IEnumerable<string> GetCandidatePaths()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "skills_override.json");
        yield return Path.Combine(AppContext.BaseDirectory, "skills_ko.json");
        yield return Path.Combine(AppContext.BaseDirectory, "Assets", "skills_ko.json");
        yield return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Assets", "skills_ko.json");
    }

    private void LogUnknownSkill(int skillCode)
    {
        lock (_logSync)
        {
            if (!_loggedUnknownCodes.Add(skillCode))
            {
                return;
            }

            try
            {
                File.AppendAllText(_unknownSkillLogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{skillCode}{Environment.NewLine}", Encoding.UTF8);
            }
            catch
            {
            }
        }
    }
}
