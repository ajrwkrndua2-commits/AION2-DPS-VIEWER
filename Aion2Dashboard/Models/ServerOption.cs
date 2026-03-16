namespace Aion2Dashboard.Models;

public sealed class ServerOption
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required int Race { get; init; }

    public string DisplayName => $"{Name} ({(Race == 1 ? "천족" : "마족")})";

    public override string ToString() => DisplayName;
}
