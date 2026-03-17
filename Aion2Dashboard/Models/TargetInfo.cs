namespace Aion2Dashboard.Models;

public sealed class TargetInfo
{
    public int TargetId { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsBoss { get; init; }
    public long CurrentHp { get; init; }
    public long MaxHp { get; init; }
    public long DamageTaken { get; init; }
}
