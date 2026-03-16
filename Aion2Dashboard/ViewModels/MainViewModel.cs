using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using Aion2Dashboard.Infrastructure;
using Aion2Dashboard.Models;
using Aion2Dashboard.Services;

namespace Aion2Dashboard.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly AtoolApiClient _atoolApiClient;
    private readonly DpsMeterService _dpsMeterService;
    private readonly OverlaySettingsStore _settingsStore;
    private readonly AdBannerService _adBannerService;
    private readonly bool _isDistributionBuild;
    private readonly Dictionary<int, DpsPlayerRow> _rowsByActorId = new();
    private readonly Dictionary<string, DpsPlayerRow> _rowsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CharacterProfile> _profileCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingProfileKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly string[] _raceOptions = ["천족", "마족"];

    private int _selfActorId;
    private ServerOption? _selectedServer;
    private string _selectedRace = "천족";
    private string _searchKeyword = string.Empty;
    private string _statusText = "준비 완료";
    private int _combatPort = -1;
    private double _windowOpacity = 0.98;
    private double _overlayOpacity = 0.88;
    private double _cardOpacity = 0.82;
    private bool _isTopMost = true;
    private int _meterServerPort = 13328;
    private string _resetHotkey = "Ctrl+F1";
    private string _fullResetHotkey = "Ctrl+R";
    private string _manualSelfName = string.Empty;
    private int _meterWindowSeconds = 300;
    private int _combatResetSeconds = 20;
    private int _searchResultSeconds = 30;
    private int _activeDisplaySeconds = 2;
    private bool _bossOnlyMode;
    private bool _partyPacketLoggingEnabled;
    private bool _isCompactMode;
    private string _currentTargetName = "타겟 대기 중";
    private bool _isBossTarget;
    private DpsPlayerRow? _pinnedSearchPlayer;
    private DateTime _pinnedSearchSetUtc = DateTime.MinValue;
    private bool _isAdBannerVisible;
    private string _adBadge = "AD";
    private string _adTitle = string.Empty;
    private string _adDescription = string.Empty;
    private string _adButtonText = "열기";
    private string _adUrl = string.Empty;

    public MainViewModel(
        AtoolApiClient atoolApiClient,
        DpsMeterService dpsMeterService,
        OverlaySettingsStore settingsStore,
        bool isDistributionBuild = false)
    {
        _atoolApiClient = atoolApiClient;
        _dpsMeterService = dpsMeterService;
        _settingsStore = settingsStore;
        _adBannerService = new AdBannerService();
        _isDistributionBuild = isDistributionBuild;

        SearchCommand = new AsyncRelayCommand(SearchAsync, CanSearch);
        RefreshServersCommand = new AsyncRelayCommand(LoadServersAsync);
        ResetMeterCommand = new RelayCommand(ResetMeter);
        ResetAllCommand = new RelayCommand(ResetAll);
        OpenAdLinkCommand = new RelayCommand(OpenAdLink, () => !string.IsNullOrWhiteSpace(AdUrl));

        LoadSettings();
        LoadAdBanner();
        ApplyMeterOptions();

        _dpsMeterService.SnapshotUpdated += OnSnapshotUpdated;
        _dpsMeterService.StatusChanged += OnMeterStatusChanged;
        _dpsMeterService.CharacterDetected += OnCharacterDetected;
        _dpsMeterService.TargetChanged += OnTargetChanged;
        _dpsMeterService.ResetSuggested += OnResetSuggested;
    }

    public ObservableCollection<ServerOption> Servers { get; } = [];
    public ObservableCollection<ServerOption> FilteredServers { get; } = [];
    public ObservableCollection<DpsPlayerRow> LivePlayers { get; } = [];

    public AsyncRelayCommand SearchCommand { get; }
    public AsyncRelayCommand RefreshServersCommand { get; }
    public RelayCommand ResetMeterCommand { get; }
    public RelayCommand ResetAllCommand { get; }
    public RelayCommand OpenAdLinkCommand { get; }

    public ServerOption? SelectedServer
    {
        get => _selectedServer;
        set
        {
            if (SetProperty(ref _selectedServer, value))
            {
                SearchCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public IReadOnlyList<string> RaceOptions => _raceOptions;

    public string SelectedRace
    {
        get => _selectedRace;
        set
        {
            if (SetProperty(ref _selectedRace, value))
            {
                RefreshFilteredServers();
                SearchCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string SearchKeyword
    {
        get => _searchKeyword;
        set
        {
            if (SetProperty(ref _searchKeyword, value))
            {
                SearchCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public int CombatPort
    {
        get => _combatPort;
        set
        {
            if (SetProperty(ref _combatPort, value))
            {
                RaisePropertyChanged(nameof(DetectionStatus));
            }
        }
    }

    public string DetectionStatus => _dpsMeterService.IsRunning ? "감지 중" : "대기 중";
    public long TotalPartyDps => LivePlayers.Sum(row => row.Dps);
    public string MeterModeText => BossOnlyMode ? "보스만" : "전체";
    public bool ShowPartyLoggingSetting => !_isDistributionBuild;
    public bool IsAdBannerVisible
    {
        get => _isAdBannerVisible;
        set => SetProperty(ref _isAdBannerVisible, value);
    }

    public string AdBadge
    {
        get => _adBadge;
        set => SetProperty(ref _adBadge, value);
    }

    public string AdTitle
    {
        get => _adTitle;
        set => SetProperty(ref _adTitle, value);
    }

    public string AdDescription
    {
        get => _adDescription;
        set => SetProperty(ref _adDescription, value);
    }

    public string AdButtonText
    {
        get => _adButtonText;
        set => SetProperty(ref _adButtonText, value);
    }

    public string AdUrl
    {
        get => _adUrl;
        set
        {
            if (SetProperty(ref _adUrl, value))
            {
                OpenAdLinkCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string CompactButtonText => IsCompactMode ? "기본 보기" : "간소화";

    public string CurrentTargetName
    {
        get => _currentTargetName;
        set => SetProperty(ref _currentTargetName, value);
    }

    public bool IsBossTarget
    {
        get => _isBossTarget;
        set
        {
            if (SetProperty(ref _isBossTarget, value))
            {
                RaisePropertyChanged(nameof(TargetBadgeText));
                RaisePropertyChanged(nameof(TargetBrush));
                RaisePropertyChanged(nameof(TargetBorderBrush));
            }
        }
    }

    public string TargetBadgeText => IsBossTarget ? "BOSS" : "TARGET";

    public double WindowOpacityValue
    {
        get => _windowOpacity;
        set
        {
            if (SetProperty(ref _windowOpacity, value))
            {
                SaveSettings();
            }
        }
    }

    public double OverlayOpacity
    {
        get => _overlayOpacity;
        set
        {
            if (SetProperty(ref _overlayOpacity, value))
            {
                RaiseVisualProperties();
                SaveSettings();
            }
        }
    }

    public double CardOpacity
    {
        get => _cardOpacity;
        set
        {
            if (SetProperty(ref _cardOpacity, value))
            {
                RaiseVisualProperties();
                SaveSettings();
            }
        }
    }

    public bool IsTopMost
    {
        get => _isTopMost;
        set
        {
            if (SetProperty(ref _isTopMost, value))
            {
                SaveSettings();
            }
        }
    }

    public int MeterServerPort
    {
        get => _meterServerPort;
        set
        {
            var normalized = Math.Max(1, value);
            if (SetProperty(ref _meterServerPort, normalized))
            {
                SaveSettings();
            }
        }
    }

    public string ResetHotkey
    {
        get => _resetHotkey;
        set
        {
            var normalized = NormalizeHotkey(value, "Ctrl+F1");
            if (SetProperty(ref _resetHotkey, normalized))
            {
                SaveSettings();
            }
        }
    }

    public string FullResetHotkey
    {
        get => _fullResetHotkey;
        set
        {
            var normalized = NormalizeHotkey(value, "Ctrl+R");
            if (SetProperty(ref _fullResetHotkey, normalized))
            {
                SaveSettings();
            }
        }
    }

    public int MeterWindowSeconds
    {
        get => _meterWindowSeconds;
        set
        {
            var normalized = Math.Clamp(value, 1, 180);
            if (SetProperty(ref _meterWindowSeconds, normalized))
            {
                ApplyMeterOptions();
                SaveSettings();
            }
        }
    }

    public int CombatResetSeconds
    {
        get => _combatResetSeconds;
        set
        {
            var normalized = Math.Clamp(value, 5, 180);
            if (SetProperty(ref _combatResetSeconds, normalized))
            {
                ApplyMeterOptions();
                SaveSettings();
            }
        }
    }

    public int SearchResultSeconds
    {
        get => _searchResultSeconds;
        set
        {
            var normalized = Math.Clamp(value, 5, 180);
            if (SetProperty(ref _searchResultSeconds, normalized))
            {
                SaveSettings();
            }
        }
    }

    public int ActiveDisplaySeconds
    {
        get => _activeDisplaySeconds;
        set
        {
            var normalized = Math.Clamp(value, 1, 10);
            if (SetProperty(ref _activeDisplaySeconds, normalized))
            {
                ApplyMeterOptions();
                SaveSettings();
            }
        }
    }

    public bool BossOnlyMode
    {
        get => _bossOnlyMode;
        set
        {
            if (SetProperty(ref _bossOnlyMode, value))
            {
                ApplyMeterOptions();
                RaisePropertyChanged(nameof(MeterModeText));
                SaveSettings();
            }
        }
    }

    public bool PartyPacketLoggingEnabled
    {
        get => _partyPacketLoggingEnabled;
        set
        {
            if (SetProperty(ref _partyPacketLoggingEnabled, value))
            {
                ApplyMeterOptions();
                SaveSettings();
            }
        }
    }

    public bool IsCompactMode
    {
        get => _isCompactMode;
        set
        {
            if (SetProperty(ref _isCompactMode, value))
            {
                RaisePropertyChanged(nameof(CompactButtonText));
                SaveSettings();
            }
        }
    }

    public string ManualSelfName
    {
        get => _manualSelfName;
        set
        {
            var normalized = value?.Trim() ?? string.Empty;
            if (SetProperty(ref _manualSelfName, normalized))
            {
                ApplyManualSelfName();
                _ = TryLoadManualSelfProfileAsync();
                SaveSettings();
            }
        }
    }

    public DpsPlayerRow? PinnedSearchPlayer
    {
        get => _pinnedSearchPlayer;
        set
        {
            if (SetProperty(ref _pinnedSearchPlayer, value))
            {
                _pinnedSearchSetUtc = value is null ? DateTime.MinValue : DateTime.UtcNow;
            }
        }
    }

    public Brush OverlayBrush => CreateBrush(11, 17, 28, OverlayOpacity);
    public Brush TitleBarBrush => CreateBrush(20, 39, 83, Math.Min(1.0, OverlayOpacity + 0.08));
    public Brush SearchRowBrush => CreateBrush(14, 18, 29, Math.Min(1.0, OverlayOpacity + 0.02));
    public Brush CardBrush => CreateBrush(27, 23, 32, CardOpacity);
    public Brush FooterBrush => CreateBrush(11, 14, 22, OverlayOpacity * 0.92);
    public Brush TargetBrush => IsBossTarget ? new SolidColorBrush(Color.FromRgb(85, 38, 38)) : new SolidColorBrush(Color.FromRgb(25, 53, 76));
    public Brush TargetBorderBrush => IsBossTarget ? new SolidColorBrush(Color.FromRgb(224, 138, 88)) : new SolidColorBrush(Color.FromRgb(108, 169, 230));

    public async Task InitializeAsync()
    {
        await LoadServersAsync();
        await TryLoadManualSelfProfileAsync();
        StartMeter();
    }

    public void TriggerResetHotkey()
    {
        ResetMeter();
        StatusText = $"미터를 초기화했습니다. 단축키 {ResetHotkey}";
    }

    public void TriggerFullResetHotkey()
    {
        ResetAll();
        StatusText = $"전체 초기화를 완료했습니다. 단축키 {FullResetHotkey}";
    }

    public void ExecuteSearch()
    {
        if (SearchCommand.CanExecute(null))
        {
            SearchCommand.Execute(null);
        }
    }

    public async Task LoadServersAsync()
    {
        try
        {
            StatusText = "아툴 서버 목록을 불러오는 중...";
            _atoolApiClient.ClearCache();
            var allServers = await _atoolApiClient.LoadServersAsync();

            Application.Current.Dispatcher.Invoke(() =>
            {
                var currentId = SelectedServer?.Id;
                Servers.Clear();
                foreach (var server in allServers)
                {
                    Servers.Add(server);
                }

                RefreshFilteredServers();
                SelectedServer = FilteredServers.FirstOrDefault(s => s.Id == currentId) ?? FilteredServers.FirstOrDefault();
                StatusText = "실시간 DPS 감지 준비 완료";
            });
        }
        catch (Exception ex)
        {
            StatusText = $"서버 목록 로드 실패: {ex.Message}";
        }
    }

    public async Task<CharacterProfile?> LoadCharacterDetailAsync(DpsPlayerRow? row, bool forceLookup = false)
    {
        if (row is null || string.IsNullOrWhiteSpace(row.Name) || row.Name.StartsWith("#", StringComparison.Ordinal))
        {
            return null;
        }

        var profileKey = GetProfileKey(row.Name, row.Server);
        if (!forceLookup && _profileCache.TryGetValue(profileKey, out var cached))
        {
            cached.SkillUsages = _dpsMeterService.GetSkillUsage(row.ActorId, row.Name);
            return cached;
        }

        try
        {
            StatusText = $"{row.Name} 상세 정보를 불러오는 중...";
            var profile = await ResolveProfileAsync(row.Name, 0, row.Server);
            ApplyProfile(row, profile);
            profile.SkillUsages = _dpsMeterService.GetSkillUsage(row.ActorId, row.Name);
            _profileCache[GetProfileKey(profile.Nickname, profile.Server)] = profile;
            StatusText = $"{row.Name} 상세 정보를 불러왔습니다.";
            return profile;
        }
        catch (Exception ex)
        {
            StatusText = $"{row.Name} 상세 조회 실패: {ex.Message}";
            return null;
        }
    }

    public void Dispose()
    {
        _dpsMeterService.SnapshotUpdated -= OnSnapshotUpdated;
        _dpsMeterService.StatusChanged -= OnMeterStatusChanged;
        _dpsMeterService.CharacterDetected -= OnCharacterDetected;
        _dpsMeterService.TargetChanged -= OnTargetChanged;
        _dpsMeterService.ResetSuggested -= OnResetSuggested;
        _dpsMeterService.Dispose();
    }

    private bool CanSearch() => !string.IsNullOrWhiteSpace(SearchKeyword);

    private async Task SearchAsync()
    {
        var keyword = SearchKeyword.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return;
        }

        try
        {
            StatusText = $"{keyword} 검색 중...";
            var (nickname, serverName) = ParseSearchInput(keyword);
            CharacterProfile profile;

            if (!string.IsNullOrWhiteSpace(serverName))
            {
                profile = await ResolveProfileAsync(nickname, 0, serverName);
            }
            else if (SelectedServer is not null)
            {
                profile = await _atoolApiClient.SearchCharacterSmartAsync(SelectedServer, nickname, Servers);
            }
            else
            {
                profile = await _atoolApiClient.SearchCharacterAcrossAllServersAsync(nickname, Servers);
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                var row = CreateProfileRow(profile);
                ApplyProfile(row, profile);
                PinnedSearchPlayer = row;
                _profileCache[GetProfileKey(profile.Nickname, profile.Server)] = profile;
                SearchKeyword = string.Empty;
                StatusText = $"{profile.Nickname} 정보를 불러왔습니다.";
            });
        }
        catch (Exception ex)
        {
            StatusText = $"검색 실패: {ex.Message}";
        }
    }

    private async void OnCharacterDetected(object? sender, (int ActorId, string Name, int ServerId, int JobCode) payload)
    {
        if (string.IsNullOrWhiteSpace(payload.Name) || payload.Name.StartsWith("#", StringComparison.Ordinal))
        {
            return;
        }

        var (nickname, parsedServerName) = ParseSearchInput(payload.Name);
        if (string.IsNullOrWhiteSpace(nickname))
        {
            return;
        }

        var hasVisibleRow = payload.ActorId > 0 && _rowsByActorId.ContainsKey(payload.ActorId);
        if (!hasVisibleRow && !_dpsMeterService.HasRecentDamage(payload.ActorId))
        {
            return;
        }

        DpsPlayerRow? row = null;
        CharacterProfile? cached = null;

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            row = GetOrCreateRow(payload.ActorId, nickname);
            row.Name = nickname;
            TryMarkSelf(row, payload.ActorId);

            if (!string.IsNullOrWhiteSpace(parsedServerName))
            {
                row.Server = parsedServerName;
            }

            _profileCache.TryGetValue(GetProfileKey(nickname, row.Server), out cached);
        });

        if (row is null)
        {
            return;
        }

        if (cached is not null)
        {
            await Application.Current.Dispatcher.InvokeAsync(() => ApplyProfile(row, cached));
            return;
        }

        try
        {
            var profile = await ResolveProfileAsync(nickname, payload.ServerId, string.IsNullOrWhiteSpace(parsedServerName) ? row.Server : parsedServerName);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ApplyProfile(row, profile);
                _profileCache[GetProfileKey(profile.Nickname, profile.Server)] = profile;
                StatusText = $"{profile.Nickname} 자동 조회 완료";
                ReorderRows();
            });
        }
        catch
        {
            await Application.Current.Dispatcher.InvokeAsync(() => TryQueueProfileLookup(row));
        }
    }

    private void OnSnapshotUpdated(object? sender, IReadOnlyList<DpsEntry> entries)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            CombatPort = _dpsMeterService.CombatPort;
            var activeActorIds = entries.Select(entry => entry.ActorId).Where(id => id > 0).ToHashSet();

            foreach (var row in LivePlayers)
            {
                row.Dps = 0;
                row.TotalDamage = 0;
                row.CritRate = 0;
                row.HitCount = 0;
                row.PartyShareRatio = 0;
                row.IsPartyCandidate = false;
            }

            foreach (var entry in entries)
            {
                var (nickname, parsedServerName) = ParseSearchInput(entry.Name);
                var normalizedName = string.IsNullOrWhiteSpace(nickname) ? entry.Name : nickname;
                var row = GetOrCreateRow(entry.ActorId, normalizedName);

                if (ShouldReplaceName(row.Name, normalizedName))
                {
                    row.Name = normalizedName;
                }

                TryMarkSelf(row, entry.ActorId);

                if (!string.IsNullOrWhiteSpace(parsedServerName))
                {
                    row.Server = parsedServerName;
                }

                row.Dps = entry.Dps;
                row.TotalDamage = entry.TotalDamage;
                row.CritRate = entry.CritRate;
                row.HitCount = entry.HitCount;
                row.IsPartyCandidate = entry.IsPartyCandidate;
                row.PartyShareRatio = entry.PartyShareRatio;

                TryQueueProfileLookup(row);
            }

            RemoveInactiveRows(activeActorIds);
            UpdatePinnedSearchVisibility();
            ApplyPartyHints();
            ReorderRows();
            RaisePropertyChanged(nameof(TotalPartyDps));
        });
    }

    private void OnMeterStatusChanged(object? sender, string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            CombatPort = _dpsMeterService.CombatPort;
            StatusText = message;
            RaisePropertyChanged(nameof(DetectionStatus));
        });
    }

    private void OnTargetChanged(object? sender, TargetInfo? target)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            CurrentTargetName = target?.Name ?? "타겟 대기 중";
            IsBossTarget = target?.IsBoss == true;
        });
    }

    private void OnResetSuggested(object? sender, string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            ResetAll();
            StatusText = message;
        });
    }

    private void StartMeter()
    {
        try
        {
            ApplyMeterOptions();
            _dpsMeterService.Start(MeterServerPort);
            StatusText = $"DPS 수집 시작. {MeterWindowSeconds}초 집계 / {MeterModeText}";
            if (PartyPacketLoggingEnabled && !string.IsNullOrWhiteSpace(_dpsMeterService.PartyLogFilePath))
            {
                StatusText = $"파티 로그 수집 중: {_dpsMeterService.PartyLogFilePath}";
            }

            RaisePropertyChanged(nameof(DetectionStatus));
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
    }

    private void ResetMeter()
    {
        _dpsMeterService.Reset();

        foreach (var row in LivePlayers)
        {
            row.Dps = 0;
            row.TotalDamage = 0;
            row.CritRate = 0;
            row.HitCount = 0;
            row.PartyShareRatio = 0;
        }

        _pendingProfileKeys.Clear();
        if (_selfActorId > 0 && _rowsByActorId.TryGetValue(_selfActorId, out var selfRow))
        {
            selfRow.IsSelf = true;
        }

        CurrentTargetName = "타겟 대기 중";
        IsBossTarget = false;
        StatusText = "DPS 데이터만 초기화했습니다. 이름과 아툴 정보는 유지됩니다.";
        RaisePropertyChanged(nameof(TotalPartyDps));
        UpdatePinnedSearchVisibility();
        ReorderRows();
    }

    private void ResetAll()
    {
        _dpsMeterService.Reset();
        _rowsByActorId.Clear();
        _rowsByName.Clear();
        _profileCache.Clear();
        _pendingProfileKeys.Clear();
        LivePlayers.Clear();
        _selfActorId = 0;
        CurrentTargetName = "타겟 대기 중";
        IsBossTarget = false;
        StatusText = "전체 리셋을 완료했습니다. DPS 목록과 자동 조회 캐시를 모두 비웠습니다.";
        PinnedSearchPlayer = null;
        RaisePropertyChanged(nameof(TotalPartyDps));
    }

    private DpsPlayerRow GetOrCreateRow(int actorId, string name)
    {
        if (actorId > 0 && _rowsByActorId.TryGetValue(actorId, out var byActor))
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                _rowsByName[name] = byActor;
            }

            return byActor;
        }

        if (!string.IsNullOrWhiteSpace(name) && _rowsByName.TryGetValue(name, out var byName))
        {
            if (actorId > 0)
            {
                byName.ActorId = actorId;
                _rowsByActorId[actorId] = byName;
            }

            return byName;
        }

        var row = new DpsPlayerRow
        {
            ActorId = actorId,
            Name = string.IsNullOrWhiteSpace(name) ? $"#{actorId}" : name
        };

        LivePlayers.Add(row);
        if (actorId > 0)
        {
            _rowsByActorId[actorId] = row;
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            _rowsByName[name] = row;
        }

        return row;
    }

    private static DpsPlayerRow CreateProfileRow(CharacterProfile profile)
    {
        var row = new DpsPlayerRow();
        ApplyProfile(row, profile);
        return row;
    }

    private void RemoveInactiveRows(HashSet<int> activeActorIds)
    {
        var staleRows = LivePlayers
            .Where(row => row.ActorId > 0 && !activeActorIds.Contains(row.ActorId) && !ReferenceEquals(row, PinnedSearchPlayer))
            .ToList();

        foreach (var row in staleRows)
        {
            LivePlayers.Remove(row);
            _rowsByActorId.Remove(row.ActorId);
            if (!string.IsNullOrWhiteSpace(row.Name))
            {
                _rowsByName.Remove(row.Name);
            }
        }
    }

    private async Task<CharacterProfile> ResolveProfileAsync(string name, int serverId = 0, string? serverName = null)
    {
        if (Servers.Count == 0)
        {
            await LoadServersAsync();
        }

        if (serverId > 0)
        {
            var matched = Servers.FirstOrDefault(item => item.Id == serverId);
            if (matched is not null)
            {
                return await _atoolApiClient.SearchCharacterSmartAsync(matched, name, Servers);
            }
        }

        if (!string.IsNullOrWhiteSpace(serverName))
        {
            var exactServer = Servers.FirstOrDefault(item => string.Equals(item.Name, serverName, StringComparison.OrdinalIgnoreCase));
            if (exactServer is not null)
            {
                try
                {
                    return await _atoolApiClient.SearchCharacterSmartAsync(exactServer, name, Servers);
                }
                catch
                {
                }
            }
        }

        if (SelectedServer is not null)
        {
            try
            {
                return await _atoolApiClient.SearchCharacterSmartAsync(SelectedServer, name, Servers);
            }
            catch
            {
            }
        }

        return await _atoolApiClient.SearchCharacterAcrossAllServersAsync(name, Servers);
    }

    private static void ApplyProfile(DpsPlayerRow row, CharacterProfile profile)
    {
        row.Name = profile.Nickname;
        row.Job = profile.Job;
        row.JobImageUrl = profile.JobImageUrl;
        row.Server = profile.Server;
        row.CombatScore = profile.CombatScore;
        row.MajorTitles = profile.MajorTitles.Take(3).Select(ShortTitle).ToArray();
        row.MajorStigmas = profile.MajorStigmas.Take(4).ToArray();
    }

    private static string ShortTitle(string value)
    {
        var text = value?.Trim() ?? string.Empty;
        return text.Length <= 4 ? text : text[..4];
    }

    private void TryQueueProfileLookup(DpsPlayerRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Name) || row.Name.StartsWith("#", StringComparison.Ordinal))
        {
            return;
        }

        var profileKey = GetProfileKey(row.Name, row.Server);
        if (_profileCache.ContainsKey(profileKey) || !_pendingProfileKeys.Add(profileKey))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var profile = await ResolveProfileAsync(row.Name, 0, row.Server);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ApplyProfile(row, profile);
                    _profileCache[GetProfileKey(profile.Nickname, profile.Server)] = profile;
                    ReorderRows();
                });
            }
            catch
            {
            }
            finally
            {
                await Application.Current.Dispatcher.InvokeAsync(() => _pendingProfileKeys.Remove(profileKey));
            }
        });
    }

    private void ReorderRows()
    {
        var ordered = LivePlayers
            .OrderByDescending(row => row.PartyShareRatio)
            .ThenByDescending(row => row.IsSelf)
            .ThenByDescending(row => row.IsPartyCandidate)
            .ThenByDescending(row => row.IsResolvedName)
            .ThenByDescending(row => row.TotalDamage)
            .ThenByDescending(row => row.Dps)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .Take(30)
            .ToList();

        var maxDps = Math.Max(1L, ordered.Count == 0 ? 1 : ordered.Max(row => row.Dps));
        var maxTotalDamage = Math.Max(1L, ordered.Count == 0 ? 1 : ordered.Max(row => row.TotalDamage));
        foreach (var row in ordered)
        {
            row.DpsRatio = (double)row.Dps / maxDps;
            row.TotalDamageRatio = (double)row.TotalDamage / maxTotalDamage;
        }

        LivePlayers.Clear();
        foreach (var row in ordered)
        {
            LivePlayers.Add(row);
        }
    }

    private void ApplyPartyHints()
    {
        var selfRow = LivePlayers.FirstOrDefault(row => row.IsSelf);
        var desiredPartyCount = selfRow is null ? 0 : 1;
        desiredPartyCount += Math.Clamp(_dpsMeterService.PartySlotHintCount, 0, 3);

        var partyRows = LivePlayers
            .Where(row => row.IsSelf || row.IsPartyCandidate)
            .OrderByDescending(row => row.TotalDamage)
            .ToList();

        if (desiredPartyCount > 0 && partyRows.Count < desiredPartyCount)
        {
            var fillRows = LivePlayers
                .Where(row => !row.IsSelf && !row.IsPartyCandidate && row.IsResolvedName)
                .OrderByDescending(row => row.TotalDamage)
                .ThenByDescending(row => row.Dps)
                .Take(desiredPartyCount - partyRows.Count)
                .ToList();

            foreach (var row in fillRows)
            {
                row.IsPartyCandidate = true;
                partyRows.Add(row);
            }
        }

        var limitedPartyRows = partyRows
            .OrderByDescending(row => row.IsSelf)
            .ThenByDescending(row => row.TotalDamage)
            .Take(4)
            .ToList();

        var totalPartyDamage = Math.Max(1L, limitedPartyRows.Sum(row => row.TotalDamage));
        foreach (var row in LivePlayers)
        {
            row.PartyShareRatio = limitedPartyRows.Contains(row)
                ? (double)row.TotalDamage / totalPartyDamage
                : 0;
        }
    }

    private void ApplyMeterOptions()
    {
        _dpsMeterService.MeterWindowSeconds = MeterWindowSeconds;
        _dpsMeterService.CombatResetSeconds = CombatResetSeconds;
        _dpsMeterService.ActiveDisplaySeconds = ActiveDisplaySeconds;
        _dpsMeterService.BossOnlyMode = BossOnlyMode;
        _dpsMeterService.EnablePartyPacketLogging = PartyPacketLoggingEnabled;
        RaisePropertyChanged(nameof(MeterModeText));
    }

    private void UpdatePinnedSearchVisibility()
    {
        if (PinnedSearchPlayer is null)
        {
            return;
        }

        var hasMatchingLiveRow = LivePlayers.Any(row =>
            !string.IsNullOrWhiteSpace(row.Name) &&
            string.Equals(row.Name, PinnedSearchPlayer.Name, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(PinnedSearchPlayer.Server) ||
             string.IsNullOrWhiteSpace(row.Server) ||
             string.Equals(row.Server, PinnedSearchPlayer.Server, StringComparison.OrdinalIgnoreCase)));

        if (hasMatchingLiveRow)
        {
            return;
        }

        if ((DateTime.UtcNow - _pinnedSearchSetUtc).TotalSeconds >= SearchResultSeconds)
        {
            PinnedSearchPlayer = null;
        }
    }

    private void LoadAdBanner()
    {
        var config = _adBannerService.Load();
        IsAdBannerVisible = config.Enabled;
        AdBadge = string.IsNullOrWhiteSpace(config.Badge) ? "AD" : config.Badge.Trim();
        AdTitle = config.Title?.Trim() ?? string.Empty;
        AdDescription = config.Description?.Trim() ?? string.Empty;
        AdButtonText = string.IsNullOrWhiteSpace(config.ButtonText) ? "열기" : config.ButtonText.Trim();
        AdUrl = config.Url?.Trim() ?? string.Empty;
    }

    private void OpenAdLink()
    {
        if (string.IsNullOrWhiteSpace(AdUrl))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = AdUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusText = $"배너 링크 열기 실패: {ex.Message}";
        }
    }

    private void RefreshFilteredServers()
    {
        var raceValue = SelectedRace == "마족" ? 2 : 1;
        var currentId = SelectedServer?.Id;

        FilteredServers.Clear();
        foreach (var server in Servers.Where(server => server.Race == raceValue))
        {
            FilteredServers.Add(server);
        }

        SelectedServer = FilteredServers.FirstOrDefault(server => server.Id == currentId) ?? FilteredServers.FirstOrDefault();
    }

    private void TryMarkSelf(DpsPlayerRow row, int actorId)
    {
        if (actorId <= 0)
        {
            return;
        }

        if (IsManualSelfMatch(row) || IsPinnedSearchMatch(row))
        {
            SetSelfActor(actorId);
        }
        else if (_selfActorId == 0 && row.IsResolvedName && LivePlayers.Count <= 1)
        {
            SetSelfActor(actorId);
        }

        row.IsSelf = actorId == _selfActorId;
    }

    private static bool ShouldReplaceName(string currentName, string nextName)
    {
        if (string.IsNullOrWhiteSpace(nextName))
        {
            return false;
        }

        var currentIsPlaceholder = currentName.StartsWith("#", StringComparison.Ordinal);
        var nextIsPlaceholder = nextName.StartsWith("#", StringComparison.Ordinal);

        if (!currentIsPlaceholder && nextIsPlaceholder)
        {
            return false;
        }

        return true;
    }

    private static string GetProfileKey(string nickname, string? server)
    {
        return $"{server ?? string.Empty}|{nickname}";
    }

    private static (string Nickname, string? ServerName) ParseSearchInput(string input)
    {
        var value = input.Trim();

        var bracketMatch = Regex.Match(value, @"^(?<name>.+?)\s*[\[\(](?<server>.+?)[\]\)]$");
        if (bracketMatch.Success)
        {
            return (bracketMatch.Groups["name"].Value.Trim(), bracketMatch.Groups["server"].Value.Trim());
        }

        var slashMatch = Regex.Match(value, @"^(?<server>[^/\s]+)\s*/\s*(?<name>.+)$");
        if (slashMatch.Success)
        {
            return (slashMatch.Groups["name"].Value.Trim(), slashMatch.Groups["server"].Value.Trim());
        }

        return (value, null);
    }

    private void LoadSettings()
    {
        var settings = _settingsStore.Load();
        _windowOpacity = settings.WindowOpacity;
        _overlayOpacity = settings.OverlayOpacity;
        _cardOpacity = settings.CardOpacity;
        _isTopMost = settings.TopMost;
        _meterServerPort = settings.ServerPort;
        _resetHotkey = NormalizeHotkey(settings.ResetHotkey, "Ctrl+F1");
        _fullResetHotkey = NormalizeHotkey(settings.FullResetHotkey, "Ctrl+R");
        _manualSelfName = settings.ManualSelfName?.Trim() ?? string.Empty;
        _meterWindowSeconds = Math.Clamp(
            settings.MeterWindowSeconds > 0 ? settings.MeterWindowSeconds : settings.MeterWindowMinutes * 60,
            1,
            180);
        _combatResetSeconds = Math.Clamp(settings.CombatResetSeconds, 5, 180);
        _searchResultSeconds = Math.Clamp(settings.SearchResultSeconds, 5, 180);
        _activeDisplaySeconds = Math.Clamp(settings.ActiveDisplaySeconds, 1, 10);
        _bossOnlyMode = settings.BossOnlyMode;
        _partyPacketLoggingEnabled = settings.PartyPacketLoggingEnabled;
        _isCompactMode = settings.CompactMode;
    }

    private void SaveSettings()
    {
        _settingsStore.Save(new OverlaySettings
        {
            WindowOpacity = WindowOpacityValue,
            OverlayOpacity = OverlayOpacity,
            CardOpacity = CardOpacity,
            TopMost = IsTopMost,
            ServerPort = MeterServerPort,
            ResetHotkey = ResetHotkey,
            FullResetHotkey = FullResetHotkey,
            ManualSelfName = ManualSelfName,
            MeterWindowSeconds = MeterWindowSeconds,
            CombatResetSeconds = CombatResetSeconds,
            SearchResultSeconds = SearchResultSeconds,
            MeterWindowMinutes = Math.Max(1, MeterWindowSeconds / 60),
            ActiveDisplaySeconds = ActiveDisplaySeconds,
            BossOnlyMode = BossOnlyMode,
            PartyPacketLoggingEnabled = PartyPacketLoggingEnabled,
            CompactMode = IsCompactMode
        });
    }

    private static string NormalizeHotkey(string? value, string fallback = "Ctrl+F1")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private void RaiseVisualProperties()
    {
        RaisePropertyChanged(nameof(OverlayBrush));
        RaisePropertyChanged(nameof(TitleBarBrush));
        RaisePropertyChanged(nameof(SearchRowBrush));
        RaisePropertyChanged(nameof(CardBrush));
        RaisePropertyChanged(nameof(FooterBrush));
        RaisePropertyChanged(nameof(TargetBrush));
        RaisePropertyChanged(nameof(TargetBorderBrush));
    }

    private void ApplyManualSelfName()
    {
        if (string.IsNullOrWhiteSpace(ManualSelfName))
        {
            return;
        }

        foreach (var row in EnumerateKnownRows())
        {
            if (string.Equals(row.Name, ManualSelfName, StringComparison.OrdinalIgnoreCase))
            {
                SetSelfActor(row.ActorId);
                break;
            }
        }

        ReorderRows();
    }

    private async Task TryLoadManualSelfProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(ManualSelfName))
        {
            return;
        }

        var (nickname, serverName) = ParseSearchInput(ManualSelfName);
        if (string.IsNullOrWhiteSpace(nickname))
        {
            return;
        }

        try
        {
            var profile = await ResolveProfileAsync(nickname, 0, serverName);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var row = CreateProfileRow(profile);
                ApplyProfile(row, profile);
                PinnedSearchPlayer = row;
                _profileCache[GetProfileKey(profile.Nickname, profile.Server)] = profile;
                ApplyManualSelfName();
                ReorderRows();
            });
        }
        catch
        {
        }
    }

    private void SetSelfActor(int actorId)
    {
        if (actorId <= 0)
        {
            return;
        }

        _selfActorId = actorId;
        foreach (var row in EnumerateKnownRows())
        {
            row.IsSelf = row.ActorId == actorId;
        }
    }

    private bool IsManualSelfMatch(DpsPlayerRow row)
    {
        return !string.IsNullOrWhiteSpace(ManualSelfName) &&
               string.Equals(row.Name, ManualSelfName, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsPinnedSearchMatch(DpsPlayerRow row)
    {
        if (PinnedSearchPlayer is null || string.IsNullOrWhiteSpace(PinnedSearchPlayer.Name))
        {
            return false;
        }

        if (!string.Equals(row.Name, PinnedSearchPlayer.Name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(PinnedSearchPlayer.Server) ||
               string.IsNullOrWhiteSpace(row.Server) ||
               string.Equals(row.Server, PinnedSearchPlayer.Server, StringComparison.OrdinalIgnoreCase);
    }

    private IEnumerable<DpsPlayerRow> EnumerateKnownRows()
    {
        var seen = new HashSet<DpsPlayerRow>();

        if (PinnedSearchPlayer is not null && seen.Add(PinnedSearchPlayer))
        {
            yield return PinnedSearchPlayer;
        }

        foreach (var row in LivePlayers)
        {
            if (seen.Add(row))
            {
                yield return row;
            }
        }

        foreach (var row in _rowsByActorId.Values)
        {
            if (seen.Add(row))
            {
                yield return row;
            }
        }

        foreach (var row in _rowsByName.Values)
        {
            if (seen.Add(row))
            {
                yield return row;
            }
        }
    }

    private static SolidColorBrush CreateBrush(byte r, byte g, byte b, double opacity)
    {
        return new SolidColorBrush(Color.FromArgb((byte)(Math.Clamp(opacity, 0.0, 1.0) * 255), r, g, b));
    }
}
