using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aion2Dashboard.Models;

namespace Aion2Dashboard.Services;

public sealed class AtoolApiClient
{
    private static readonly IReadOnlyDictionary<string, string> JobIconMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["검성"] = "https://assets.playnccdn.com/static-aion2/characters/img/class/class_icon_gladiator.png",
        ["수호성"] = "https://assets.playnccdn.com/static-aion2/characters/img/class/class_icon_templar.png",
        ["살성"] = "https://assets.playnccdn.com/static-aion2/characters/img/class/class_icon_assassin.png",
        ["궁성"] = "https://assets.playnccdn.com/static-aion2/characters/img/class/class_icon_ranger.png",
        ["치유성"] = "https://assets.playnccdn.com/static-aion2/characters/img/class/class_icon_cleric.png",
        ["호법성"] = "https://assets.playnccdn.com/static-aion2/characters/img/class/class_icon_chanter.png",
        ["마도성"] = "https://assets.playnccdn.com/static-aion2/characters/img/class/class_icon_sorcerer.png",
        ["정령성"] = "https://assets.playnccdn.com/static-aion2/characters/img/class/class_icon_elementalist.png",
        ["gladiator"] = "https://assets.playnccdn.com/static-aion2/characters/img/class/class_icon_gladiator.png",
        ["templar"] = "https://assets.playnccdn.com/static-aion2/characters/img/class/class_icon_templar.png",
        ["assassin"] = "https://assets.playnccdn.com/static-aion2/characters/img/class/class_icon_assassin.png",
        ["ranger"] = "https://assets.playnccdn.com/static-aion2/characters/img/class/class_icon_ranger.png",
        ["cleric"] = "https://assets.playnccdn.com/static-aion2/characters/img/class/class_icon_cleric.png",
        ["chanter"] = "https://assets.playnccdn.com/static-aion2/characters/img/class/class_icon_chanter.png",
        ["sorcerer"] = "https://assets.playnccdn.com/static-aion2/characters/img/class/class_icon_sorcerer.png",
        ["elementalist"] = "https://assets.playnccdn.com/static-aion2/characters/img/class/class_icon_elementalist.png"
    };

    private readonly HttpClient _httpClient;
    private IReadOnlyList<ServerOption>? _cachedServers;

    public AtoolApiClient()
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://aion2tool.com")
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 DPSVIEWER");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Referrer = new Uri("https://aion2tool.com/");
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://aion2tool.com");
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
    }

    public async Task<IReadOnlyList<ServerOption>> LoadServersAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedServers is not null)
        {
            return _cachedServers;
        }

        var html = await _httpClient.GetStringAsync("/compare", cancellationToken);
        var match = Regex.Match(html, "<script type=\"application/json\" id=\"server-data\">(?<json>.*?)</script>", RegexOptions.Singleline);
        if (!match.Success)
        {
            return Array.Empty<ServerOption>();
        }

        using var document = JsonDocument.Parse(match.Groups["json"].Value);
        var servers = new List<ServerOption>();
        AddServerGroup(document.RootElement, "elyos", 1, servers);
        AddServerGroup(document.RootElement, "asmodian", 2, servers);
        _cachedServers = servers.OrderBy(s => s.Id).ThenBy(s => s.Race).ToArray();
        return _cachedServers;
    }

    public async Task<CharacterProfile> SearchCharacterSmartAsync(ServerOption server, string keyword, IReadOnlyList<ServerOption> allServers, CancellationToken cancellationToken = default)
    {
        var candidates = new List<(int Race, int ServerId)>
        {
            (1, server.Id),
            (2, server.Id)
        };

        foreach (var sibling in allServers.Where(item => item.Name == server.Name && item.Id != server.Id))
        {
            candidates.Add((1, sibling.Id));
            candidates.Add((2, sibling.Id));
        }

        foreach (var candidate in candidates.Distinct())
        {
            try
            {
                return await SearchCharacterAsync(candidate.Race, candidate.ServerId, keyword, cancellationToken);
            }
            catch
            {
            }
        }

        throw new InvalidOperationException("캐릭터를 찾을 수 없습니다.");
    }

    public async Task<CharacterProfile> SearchCharacterAcrossAllServersAsync(string keyword, IReadOnlyList<ServerOption> allServers, CancellationToken cancellationToken = default)
    {
        foreach (var server in allServers)
        {
            try
            {
                return await SearchCharacterSmartAsync(server, keyword, allServers, cancellationToken);
            }
            catch
            {
            }
        }

        throw new InvalidOperationException("모든 서버에서 캐릭터를 찾지 못했습니다.");
    }

    public async Task<CharacterProfile> SearchCharacterAsync(int race, int serverId, string keyword, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/character/search")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { race, server_id = serverId, keyword }),
                Encoding.UTF8,
                "application/json")
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(payload);

        if (!response.IsSuccessStatusCode ||
            !document.RootElement.TryGetProperty("success", out var successNode) ||
            !successNode.GetBoolean())
        {
            throw new InvalidOperationException(TryGetString(document.RootElement, "error") ?? "캐릭터를 찾을 수 없습니다.");
        }

        var data = document.RootElement.GetProperty("data");
        var job = FirstString(data, "job", "job_name", "class_name") ?? "미확인";

        var profile = new CharacterProfile
        {
            Nickname = FirstString(data, "nickname", "name") ?? keyword,
            Server = FirstString(data, "server", "server_name") ?? string.Empty,
            Race = NormalizeRace(FirstString(data, "race", "race_name") ?? race.ToString(CultureInfo.InvariantCulture)),
            Job = job,
            JobImageUrl = NormalizeJobImage(job, FirstString(data, "job_image_url", "jobImageUrl") ?? string.Empty),
            Guild = FirstString(data, "legion_name", "guild", "guild_name") ?? "-",
            TitleSummary = ExtractTitleSummary(data),
            TitleCount = ExtractTitleCount(data),
            Level = FirstInt(data, "level"),
            CombatPower = FirstLong(data, "combat_power", "power", "combatPower"),
            CombatScore = FirstRoundedLong(data, "combat_score", "score", "combatScore", "combat_score_max"),
            MajorTitles = ExtractTitleNames(data).Take(3).ToArray(),
            MajorStigmas = ExtractStigmaNames(data).Take(4).ToArray()
        };

        await PopulateRankingInfoAsync(profile, cancellationToken);
        return profile;
    }

    public void ClearCache() => _cachedServers = null;

    private async Task PopulateRankingInfoAsync(CharacterProfile profile, CancellationToken cancellationToken)
    {
        try
        {
            using var rankingResponse = await _httpClient.GetAsync(
                $"/api/character/ranking?nickname={Uri.EscapeDataString(profile.Nickname)}&server={Uri.EscapeDataString(profile.Server)}&race={Uri.EscapeDataString(profile.Race)}",
                cancellationToken);
            var rankingPayload = await rankingResponse.Content.ReadAsStringAsync(cancellationToken);
            using var rankingDocument = JsonDocument.Parse(rankingPayload);
            if (rankingResponse.IsSuccessStatusCode &&
                rankingDocument.RootElement.TryGetProperty("success", out var rankingSuccess) &&
                rankingSuccess.GetBoolean())
            {
                var ranking = rankingDocument.RootElement.GetProperty("data");
                profile.CombatRank = FirstInt(ranking, "combat_power_rank", "rank");
                profile.CombatScoreRank = FirstInt(ranking, "combat_score_rank", "score_rank");
                profile.DaevanionPoint = FirstLong(ranking, "daevanion_crystal", "daevanion_crystal_ariel", "daevanion_crystal_aspel", "daevanion_crystal_markutan");
                profile.TitleCount = Math.Max(profile.TitleCount, FirstInt(ranking, "title_count"));
                profile.StigmaShards = FirstInt(ranking, "stigma_shards");
                if (string.IsNullOrWhiteSpace(profile.TitleSummary) && profile.TitleCount > 0)
                {
                    profile.TitleSummary = $"타이틀 {profile.TitleCount:N0}개";
                }
            }
        }
        catch
        {
        }

        try
        {
            using var rankingInfoResponse = await _httpClient.GetAsync(
                $"/api/character/ranking-info?nickname={Uri.EscapeDataString(profile.Nickname)}&server={Uri.EscapeDataString(profile.Server)}&race={Uri.EscapeDataString(profile.Race)}",
                cancellationToken);
            var rankingInfoPayload = await rankingInfoResponse.Content.ReadAsStringAsync(cancellationToken);
            using var rankingInfoDocument = JsonDocument.Parse(rankingInfoPayload);
            if (rankingInfoResponse.IsSuccessStatusCode &&
                rankingInfoDocument.RootElement.TryGetProperty("success", out var rankingInfoSuccess) &&
                rankingInfoSuccess.GetBoolean())
            {
                var rankingInfo = rankingInfoDocument.RootElement.GetProperty("data");
                profile.TitleCount = Math.Max(profile.TitleCount, FirstInt(rankingInfo, "title_count"));
                profile.StigmaShards = Math.Max(profile.StigmaShards, FirstInt(rankingInfo, "stigma_shards"));
                if (profile.TitleCount > 0)
                {
                    profile.TitleSummary = $"타이틀 {profile.TitleCount:N0}개";
                }
            }
        }
        catch
        {
        }
    }

    private static void AddServerGroup(JsonElement root, string propertyName, int race, ICollection<ServerOption> target)
    {
        if (!root.TryGetProperty(propertyName, out var group) || group.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in group.EnumerateArray())
        {
            var id = FirstInt(item, "id");
            var name = FirstString(item, "name");
            if (id > 0 && !string.IsNullOrWhiteSpace(name))
            {
                target.Add(new ServerOption { Id = id, Name = name!, Race = race });
            }
        }
    }

    private static string NormalizeRace(string race) => race switch
    {
        "1" => "천족",
        "2" => "마족",
        _ => race
    };

    private static string NormalizeJobImage(string job, string originalUrl)
    {
        if (!string.IsNullOrWhiteSpace(originalUrl) &&
            originalUrl.Contains("assets.playnccdn.com/static-aion2/characters/img/class/", StringComparison.OrdinalIgnoreCase))
        {
            return originalUrl;
        }

        foreach (var pair in JobIconMap)
        {
            if (job.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return originalUrl;
    }

    private static string ExtractTitleSummary(JsonElement element)
    {
        var direct = FirstString(element, "title", "title_name");
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct!;
        }

        if (element.TryGetProperty("title_summary", out var summary) && summary.ValueKind == JsonValueKind.Object)
        {
            var owned = FirstInt(summary, "owned_count", "ownedCount");
            var total = FirstInt(summary, "total_count", "totalCount");
            if (total > 0)
            {
                return $"타이틀 {owned:N0}/{total:N0}";
            }
        }

        return string.Empty;
    }

    private static int ExtractTitleCount(JsonElement element)
    {
        if (element.TryGetProperty("title_summary", out var summary) && summary.ValueKind == JsonValueKind.Object)
        {
            return FirstInt(summary, "owned_count", "ownedCount");
        }

        return 0;
    }

    private static IReadOnlyList<string> ExtractTitleNames(JsonElement element)
    {
        if (!element.TryGetProperty("titles", out var titles) || titles.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var values = new List<string>();
        foreach (var item in titles.EnumerateArray())
        {
            var name = FirstString(item, "name", "title", "title_name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                values.Add(name!);
            }
        }

        return values;
    }

    private static IReadOnlyList<string> ExtractStigmaNames(JsonElement element)
    {
        if (!element.TryGetProperty("stigmas", out var stigmas) || stigmas.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var values = new List<string>();
        foreach (var item in stigmas.EnumerateArray())
        {
            var name = FirstString(item, "name", "skill_name", "title");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var level = FirstInt(item, "level_int", "level");
            values.Add(level > 0 ? $"{name} Lv.{level}" : name!);
        }

        return values;
    }

    private static string? FirstString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            var value = TryGetString(element, name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? TryGetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.ToString(),
            _ => null
        };
    }

    private static int FirstInt(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
            {
                return number;
            }

            if (property.ValueKind == JsonValueKind.String &&
                int.TryParse(property.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out number))
            {
                return number;
            }
        }

        return 0;
    }

    private static long FirstLong(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var number))
            {
                return number;
            }

            if (property.ValueKind == JsonValueKind.String &&
                long.TryParse(property.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out number))
            {
                return number;
            }
        }

        return 0;
    }

    private static long FirstRoundedLong(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Number)
            {
                if (property.TryGetInt64(out var longValue))
                {
                    return longValue;
                }

                if (property.TryGetDouble(out var doubleValue))
                {
                    return (long)Math.Round(doubleValue, MidpointRounding.AwayFromZero);
                }
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                var text = property.GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                text = text.Replace(",", string.Empty, StringComparison.Ordinal);
                if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var doubleValue))
                {
                    return (long)Math.Round(doubleValue, MidpointRounding.AwayFromZero);
                }
            }
        }

        return 0;
    }
}
