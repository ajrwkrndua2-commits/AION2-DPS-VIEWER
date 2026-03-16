using System.Collections.ObjectModel;
using Aion2Dashboard.Infrastructure;

namespace Aion2Dashboard.Models;

public sealed class PartyMemberCard : ObservableObject
{
    private string _nickname = string.Empty;
    private string _server = string.Empty;
    private string _race = string.Empty;
    private string _job = "Unknown";
    private string _jobImageUrl = string.Empty;
    private string _guild = "-";
    private int _level;
    private long _combatPower;
    private long _combatScore;
    private int _combatRank;
    private int _combatScoreRank;
    private long _dps;
    private long _totalDamage;
    private double _critRate;

    public string Nickname { get => _nickname; set => SetProperty(ref _nickname, value); }

    public string Server
    {
        get => _server;
        set
        {
            if (SetProperty(ref _server, value))
            {
                RaiseDerivedProperties();
            }
        }
    }

    public string Race
    {
        get => _race;
        set
        {
            if (SetProperty(ref _race, value))
            {
                RaiseDerivedProperties();
            }
        }
    }

    public string Job
    {
        get => _job;
        set
        {
            if (SetProperty(ref _job, value))
            {
                RaiseDerivedProperties();
            }
        }
    }

    public string JobImageUrl
    {
        get => _jobImageUrl;
        set => SetProperty(ref _jobImageUrl, value);
    }

    public string Guild
    {
        get => _guild;
        set => SetProperty(ref _guild, value);
    }

    public int Level
    {
        get => _level;
        set => SetProperty(ref _level, value);
    }

    public long CombatPower { get => _combatPower; set => SetProperty(ref _combatPower, value); }
    public long CombatScore { get => _combatScore; set => SetProperty(ref _combatScore, value); }
    public int CombatRank { get => _combatRank; set => SetProperty(ref _combatRank, value); }
    public int CombatScoreRank { get => _combatScoreRank; set => SetProperty(ref _combatScoreRank, value); }

    public long Dps
    {
        get => _dps;
        set
        {
            if (SetProperty(ref _dps, value))
            {
                RaiseDerivedProperties();
            }
        }
    }

    public long TotalDamage
    {
        get => _totalDamage;
        set
        {
            if (SetProperty(ref _totalDamage, value))
            {
                RaiseDerivedProperties();
            }
        }
    }

    public double CritRate
    {
        get => _critRate;
        set
        {
            if (SetProperty(ref _critRate, value))
            {
                RaiseDerivedProperties();
            }
        }
    }

    public ObservableCollection<string> MajorTitles { get; } = [];
    public ObservableCollection<string> MajorStigmas { get; } = [];

    public string HeaderSubtitle => $"{Server} / {Race}";
    public string DpsLine => $"DPS {Dps:N0}   누적 {TotalDamage:N0}   치명 {CritRate:P0}";

    private void RaiseDerivedProperties()
    {
        RaisePropertyChanged(nameof(HeaderSubtitle));
        RaisePropertyChanged(nameof(DpsLine));
    }
}
