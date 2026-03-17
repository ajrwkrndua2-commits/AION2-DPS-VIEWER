using System.Windows.Media;
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
        set
        {
            if (SetProperty(ref _job, value))
            {
                RaisePropertyChanged(nameof(JobAccentBrush));
                RaisePropertyChanged(nameof(JobAccentBorderBrush));
                RaisePropertyChanged(nameof(JobAccentText));
            }
        }
    }

    public string JobImageUrl
    {
        get => _jobImageUrl;
        set => SetProperty(ref _jobImageUrl, value);
    }

    public string Server
    {
        get => _server;
        set
        {
            if (SetProperty(ref _server, value))
            {
                RaisePropertyChanged(nameof(CompactServerLabel));
            }
        }
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

    public string CompactServerLabel =>
        string.IsNullOrWhiteSpace(Server) || Server == "-"
            ? string.Empty
            : $"[{Server}]";

    public Brush JobAccentBrush => ResolveJobBrush(Job);

    public Brush JobAccentBorderBrush => ResolveJobBorderBrush(Job);

    public string JobAccentText => ResolveJobShortText(Job);

    private static Brush ResolveJobBrush(string job)
    {
        var (background, _) = ResolveJobColors(job);
        return background;
    }

    private static Brush ResolveJobBorderBrush(string job)
    {
        var (_, border) = ResolveJobColors(job);
        return border;
    }

    private static (Brush Background, Brush Border) ResolveJobColors(string job)
    {
        if (job.Contains("검성", StringComparison.OrdinalIgnoreCase) || job.Contains("gladiator", StringComparison.OrdinalIgnoreCase))
        {
            return (BrushFromHex("#734E2F"), BrushFromHex("#E2A66D"));
        }

        if (job.Contains("수호", StringComparison.OrdinalIgnoreCase) || job.Contains("templar", StringComparison.OrdinalIgnoreCase))
        {
            return (BrushFromHex("#3C536E"), BrushFromHex("#8EB5E8"));
        }

        if (job.Contains("살성", StringComparison.OrdinalIgnoreCase) || job.Contains("assassin", StringComparison.OrdinalIgnoreCase))
        {
            return (BrushFromHex("#5A366B"), BrushFromHex("#E3A1FF"));
        }

        if (job.Contains("궁성", StringComparison.OrdinalIgnoreCase) || job.Contains("ranger", StringComparison.OrdinalIgnoreCase))
        {
            return (BrushFromHex("#2E6A53"), BrushFromHex("#87E7BB"));
        }

        if (job.Contains("치유", StringComparison.OrdinalIgnoreCase) || job.Contains("cleric", StringComparison.OrdinalIgnoreCase))
        {
            return (BrushFromHex("#5A7B2F"), BrushFromHex("#C6F07B"));
        }

        if (job.Contains("호법", StringComparison.OrdinalIgnoreCase) || job.Contains("chanter", StringComparison.OrdinalIgnoreCase))
        {
            return (BrushFromHex("#62522B"), BrushFromHex("#F0D37C"));
        }

        if (job.Contains("마도", StringComparison.OrdinalIgnoreCase) || job.Contains("sorcerer", StringComparison.OrdinalIgnoreCase))
        {
            return (BrushFromHex("#2F477A"), BrushFromHex("#7EB6FF"));
        }

        if (job.Contains("정령", StringComparison.OrdinalIgnoreCase) || job.Contains("elementalist", StringComparison.OrdinalIgnoreCase))
        {
            return (BrushFromHex("#2D665A"), BrushFromHex("#84E1CF"));
        }

        return (BrushFromHex("#48566C"), BrushFromHex("#A6BDD8"));
    }

    private static string ResolveJobShortText(string job)
    {
        if (job.Contains("검성", StringComparison.OrdinalIgnoreCase) || job.Contains("gladiator", StringComparison.OrdinalIgnoreCase))
        {
            return "검";
        }

        if (job.Contains("수호", StringComparison.OrdinalIgnoreCase) || job.Contains("templar", StringComparison.OrdinalIgnoreCase))
        {
            return "수";
        }

        if (job.Contains("살성", StringComparison.OrdinalIgnoreCase) || job.Contains("assassin", StringComparison.OrdinalIgnoreCase))
        {
            return "살";
        }

        if (job.Contains("궁성", StringComparison.OrdinalIgnoreCase) || job.Contains("ranger", StringComparison.OrdinalIgnoreCase))
        {
            return "궁";
        }

        if (job.Contains("치유", StringComparison.OrdinalIgnoreCase) || job.Contains("cleric", StringComparison.OrdinalIgnoreCase))
        {
            return "치";
        }

        if (job.Contains("호법", StringComparison.OrdinalIgnoreCase) || job.Contains("chanter", StringComparison.OrdinalIgnoreCase))
        {
            return "호";
        }

        if (job.Contains("마도", StringComparison.OrdinalIgnoreCase) || job.Contains("sorcerer", StringComparison.OrdinalIgnoreCase))
        {
            return "마";
        }

        if (job.Contains("정령", StringComparison.OrdinalIgnoreCase) || job.Contains("elementalist", StringComparison.OrdinalIgnoreCase))
        {
            return "정";
        }

        return "-";
    }

    private static SolidColorBrush BrushFromHex(string hex)
    {
        return (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    }
}
