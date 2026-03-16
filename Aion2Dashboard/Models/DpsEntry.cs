namespace Aion2Dashboard.Models;

public sealed class DpsEntry
{
    public int ActorId { get; init; }
    public string Name { get; init; } = "";
    public long TotalDamage { get; init; }
    public long Dps { get; init; }
    public int HitCount { get; init; }
    public double CritRate { get; init; }
    public bool IsPartyCandidate { get; init; }
    public double PartyShareRatio { get; init; }
}
