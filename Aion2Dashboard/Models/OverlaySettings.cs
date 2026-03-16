namespace Aion2Dashboard.Models;

public sealed class OverlaySettings
{
    public double WindowOpacity { get; set; } = 0.98;
    public double OverlayOpacity { get; set; } = 0.88;
    public double CardOpacity { get; set; } = 0.82;
    public bool TopMost { get; set; } = true;
    public int ServerPort { get; set; } = 13328;
    public string ResetHotkey { get; set; } = "Ctrl+F1";
    public string FullResetHotkey { get; set; } = "Ctrl+R";
    public string ManualSelfName { get; set; } = string.Empty;
    public int MeterWindowMinutes { get; set; } = 5;
    public int ActiveDisplaySeconds { get; set; } = 2;
    public bool BossOnlyMode { get; set; }
    public bool PartyPacketLoggingEnabled { get; set; }
    public bool CompactMode { get; set; }
}
