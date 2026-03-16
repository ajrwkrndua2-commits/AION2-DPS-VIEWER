using Aion2Dashboard.Infrastructure;

namespace Aion2Dashboard.Models;

public sealed class DpsPlayerRow : ObservableObject
{
    private int _actorId;
    private string _name = "Unknown";
    private string _job = "-";
    private string _jobImageUrl = string.Empty;
    private string _server = "-";
    private long _combatScore;
    private long _dps;
    private long _totalDamage;
    private double _critRate;
    private int _hitCount;
    private IReadOnlyList<string> _majorTitles = Array.Empty<string>();
    private IReadOnlyList<string> _majorStigmas = Array.Empty<string>();
    private double _dpsRatio;
    private double _totalDamageRatio;
    private double _partyShareRatio;
    private bool _isSelf;
    private bool _isPartyCandidate;

    public int ActorId
    {
        get => _actorId;
        set => SetProperty(ref _actorId, value);
    }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                RaisePropertyChanged(nameof(DisplayName));
                RaisePropertyChanged(nameof(IsResolvedName));
            }
        }
    }

    public string DisplayName => IsResolvedName ? Name : "이름 확인중";

    public bool IsResolvedName => !Name.StartsWith("#", StringComparison.Ordinal);

    public string Job
    {
        get => _job;
        set => SetProperty(ref _job, value);
    }

    public string JobImageUrl
    {
        get => _jobImageUrl;
        set => SetProperty(ref _jobImageUrl, value);
    }

    public string Server
    {
        get => _server;
        set => SetProperty(ref _server, value);
    }

    public long CombatScore
    {
        get => _combatScore;
        set => SetProperty(ref _combatScore, value);
    }

    public long Dps
    {
        get => _dps;
        set => SetProperty(ref _dps, value);
    }

    public long TotalDamage
    {
        get => _totalDamage;
        set => SetProperty(ref _totalDamage, value);
    }

    public double CritRate
    {
        get => _critRate;
        set => SetProperty(ref _critRate, value);
    }

    public int HitCount
    {
        get => _hitCount;
        set => SetProperty(ref _hitCount, value);
    }

    public IReadOnlyList<string> MajorTitles
    {
        get => _majorTitles;
        set => SetProperty(ref _majorTitles, value);
    }

    public IReadOnlyList<string> MajorStigmas
    {
        get => _majorStigmas;
        set => SetProperty(ref _majorStigmas, value);
    }

    public double DpsRatio
    {
        get => _dpsRatio;
        set => SetProperty(ref _dpsRatio, value);
    }

    public double TotalDamageRatio
    {
        get => _totalDamageRatio;
        set => SetProperty(ref _totalDamageRatio, value);
    }

    public double PartyShareRatio
    {
        get => _partyShareRatio;
        set => SetProperty(ref _partyShareRatio, value);
    }

    public bool IsSelf
    {
        get => _isSelf;
        set => SetProperty(ref _isSelf, value);
    }

    public bool IsPartyCandidate
    {
        get => _isPartyCandidate;
        set => SetProperty(ref _isPartyCandidate, value);
    }
}
