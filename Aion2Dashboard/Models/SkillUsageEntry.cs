namespace Aion2Dashboard.Models;

public sealed class SkillUsageEntry
{
    public int SkillCode { get; init; }
    public string SkillName { get; init; } = string.Empty;
    public long TotalDamage { get; init; }
    public int HitCount { get; init; }
    public double CritRate { get; init; }
    public long MaxHit { get; init; }
    public double DamageRatio { get; init; }
    public double DamageShare { get; init; }
}
