namespace Aion2Dashboard.Models;

public sealed class CharacterProfile
{
    public string Nickname { get; set; } = string.Empty;
    public string Server { get; set; } = string.Empty;
    public string Race { get; set; } = string.Empty;
    public string Job { get; set; } = string.Empty;
    public string JobImageUrl { get; set; } = string.Empty;
    public string Guild { get; set; } = string.Empty;
    public string TitleSummary { get; set; } = string.Empty;
    public int TitleCount { get; set; }
    public int StigmaShards { get; set; }
    public int Level { get; set; }
    public long CombatPower { get; set; }
    public long CombatScore { get; set; }
    public int CombatRank { get; set; }
    public int CombatScoreRank { get; set; }
    public long DaevanionPoint { get; set; }
    public IReadOnlyList<string> MajorTitles { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> MajorStigmas { get; set; } = Array.Empty<string>();
    public IReadOnlyList<SkillUsageEntry> SkillUsages { get; set; } = Array.Empty<SkillUsageEntry>();
}
