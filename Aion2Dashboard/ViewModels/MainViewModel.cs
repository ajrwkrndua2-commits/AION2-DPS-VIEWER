using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Aion2Dashboard.Infrastructure;
using Aion2Dashboard.Models;
using Aion2Dashboard.Services;

namespace Aion2Dashboard.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private const string DefaultLogUploadUrl = "https://discord.com/api/webhooks/1483389759488262305/sCsRiD4kK7-cAx7JNUlzmZYgOFq7Be0x0KKfrN5fNpDqHapHbrzo4dzVAinBZMqcP6CF";

    private readonly AtoolApiClient _atoolApiClient;
    private readonly DpsMeterService _dpsMeterService;
    private readonly OverlaySettingsStore _settingsStore;
    private readonly AdBannerService _adBannerService;
    private readonly UpdateCheckerService _updateCheckerService;
    private readonly UpdateInstallerService _updateInstallerService;
    private readonly LogUploadService _logUploadService;
    private readonly CombatRecordStore _combatRecordStore;
    private readonly DpsSnapshotLogStore _dpsSnapshotLogStore;
    private readonly bool _isDistributionBuild;
    private readonly Dictionary<int, DpsPlayerRow> _rowsByActorId = new();
    private readonly Dictionary<string, DpsPlayerRow> _rowsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CharacterProfile> _profileCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, DetectedCharacterInfo> _detectedCharactersByActorId = new();
    private readonly Dictionary<int, PartyMemberCard> _partyApplicantCardsByActorId = new();
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
    private string _resetHotkey = "Ctrl+R";
    private string _fullResetHotkey = "Ctrl+F1";
    private string _manualSelfName = string.Empty;
    private int _meterWindowSeconds = 300;
    private int _combatResetSeconds = 180;
    private int _searchResultSeconds = 180;
    private int _activeDisplaySeconds = 30;
    private int _combatRecordRetentionSeconds = 1800;
    private bool _bossOnlyMode;
    private bool _partyPacketLoggingEnabled;
    private bool _dpsSnapshotLoggingEnabled;
    private bool _strongHitAnalysisLoggingEnabled;
    private bool _isCompactMode;
    private bool _autoUpdateCheckEnabled = true;
    private string _logUploadUrl = string.Empty;
    private bool _preserveRowsAfterMeterReset;
    private string _currentTargetName = "타겟 대기 중";
    private string _currentTargetHealthText = string.Empty;
    private bool _isBossTarget;
    private TargetInfo? _currentTargetInfo;
    private DpsPlayerRow? _pinnedSearchPlayer;
    private DateTime _pinnedSearchSetUtc = DateTime.MinValue;
    private bool _isAdBannerVisible;
    private string _adBadge = "AD";
    private string _adTitle = string.Empty;
    private string _adDescription = string.Empty;
    private string _adButtonText = "열기";
    private string _adUrl = string.Empty;
    private bool _hasUpdateNotification;
    private string _updateButtonToolTip = "업데이트";
    private string _lastSavedCombatRecordKey = string.Empty;
    private const int CombatRecordSaveDelayMilliseconds = 1500;
    private DateTime _lastSnapshotLogUtc = DateTime.MinValue;
    private int _isUploadingLogs;
    private DateTime _lastDetectionSignalUtc = DateTime.MinValue;
    private readonly DispatcherTimer _detectionStateTimer;

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
        _updateCheckerService = new UpdateCheckerService();
        _updateInstallerService = new UpdateInstallerService();
        _logUploadService = new LogUploadService();
        _combatRecordStore = new CombatRecordStore();
        _dpsSnapshotLogStore = new DpsSnapshotLogStore();
        _isDistributionBuild = isDistributionBuild;

        SearchCommand = new AsyncRelayCommand(SearchAsync, CanSearch);
        RefreshServersCommand = new AsyncRelayCommand(LoadServersAsync);
        CheckUpdateCommand = new AsyncRelayCommand(() => RunAutoUpdateAsync(showLatestMessage: true));
        UploadLogsCommand = new AsyncRelayCommand(UploadLogsAsync, () => !string.IsNullOrWhiteSpace(LogUploadUrl));
        ResetMeterCommand = new RelayCommand(ResetMeter);
        ResetAllCommand = new RelayCommand(ResetAll);
        OpenAdLinkCommand = new RelayCommand(OpenAdLink, () => !string.IsNullOrWhiteSpace(AdUrl));

        LoadSettings();
        LoadAdBanner();
        ApplyMeterOptions();

        _detectionStateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _detectionStateTimer.Tick += (_, _) =>
        {
            RaisePropertyChanged(nameof(DetectionStatus));
            RaisePropertyChanged(nameof(DetectionIndicatorBrush));
        };
        _detectionStateTimer.Start();

        _dpsMeterService.SnapshotUpdated += OnSnapshotUpdated;
        _dpsMeterService.StatusChanged += OnMeterStatusChanged;
        _dpsMeterService.CharacterDetected += OnCharacterDetected;
        _dpsMeterService.TargetChanged += OnTargetChanged;
        _dpsMeterService.ResetSuggested += OnResetSuggested;
        _dpsMeterService.CombatRecordCompleted += OnCombatRecordCompleted;
        PartyApplicants.CollectionChanged += OnPartyApplicantsChanged;
    }

    public ObservableCollection<ServerOption> Servers { get; } = [];
    public ObservableCollection<ServerOption> FilteredServers { get; } = [];
    public ObservableCollection<DpsPlayerRow> LivePlayers { get; } = [];
    public ObservableCollection<DpsPlayerRow> DisplayPlayers { get; } = [];
    public ObservableCollection<PartyMemberCard> PartyApplicants { get; } = [];

    public AsyncRelayCommand SearchCommand { get; }
    public AsyncRelayCommand RefreshServersCommand { get; }
    public AsyncRelayCommand CheckUpdateCommand { get; }
    public AsyncRelayCommand UploadLogsCommand { get; }
    public RelayCommand ResetMeterCommand { get; }
    public RelayCommand ResetAllCommand { get; }
    public RelayCommand OpenAdLinkCommand { get; }
    public bool HasPartyApplicants => PartyApplicants.Count > 0;

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

    public string DetectionStatus
    {
        get
        {
            if (!_dpsMeterService.IsRunning || CombatPort <= 0)
            {
                return "감지 안됨";
            }

            if (_lastDetectionSignalUtc == DateTime.MinValue)
            {
                return "감지 지연";
            }

            return DateTime.UtcNow - _lastDetectionSignalUtc <= TimeSpan.FromSeconds(5)
                ? "감지중"
                : "감지 지연";
        }
    }

    public Brush DetectionIndicatorBrush => DetectionStatus switch
    {
        "감지중" => new SolidColorBrush(Color.FromRgb(102, 224, 147)),
        "감지 지연" => new SolidColorBrush(Color.FromRgb(255, 201, 87)),
        _ => new SolidColorBrush(Color.FromRgb(255, 101, 101))
    };
    public long TotalPartyDps => LivePlayers.Sum(row => row.Dps);
    public string MeterModeText => BossOnlyMode ? "보스만" : "전체";
    public bool ShowPartyLoggingSetting => !_isDistributionBuild;
    public bool DpsSnapshotLoggingEnabled
    {
        get => _dpsSnapshotLoggingEnabled;
        set
        {
            if (SetProperty(ref _dpsSnapshotLoggingEnabled, value))
            {
                SaveSettings();
                if (value)
                {
                    StatusText = $"DPS 로그 저장 활성화: {_dpsSnapshotLogStore.DirectoryPath}";
                }
            }
        }
    }

    public bool StrongHitAnalysisLoggingEnabled
    {
        get => _strongHitAnalysisLoggingEnabled;
        set
        {
            if (SetProperty(ref _strongHitAnalysisLoggingEnabled, value))
            {
                ApplyMeterOptions();
                SaveSettings();
                if (value)
                {
                    StatusText = "DOUBLE 분석 로그 저장 활성화: logs/strong-hit-analysis-YYYYMMDD.csv";
                }
            }
        }
    }
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

    public string CurrentTargetHealthText
    {
        get => _currentTargetHealthText;
        set => SetProperty(ref _currentTargetHealthText, value);
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
            var normalized = NormalizeHotkey(value, "Ctrl+R");
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
            var normalized = NormalizeHotkey(value, "Ctrl+F1");
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
            var normalized = Math.Clamp(value, 1, 300);
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
            var normalized = Math.Clamp(value, 1, 30);
            if (SetProperty(ref _activeDisplaySeconds, normalized))
            {
                ApplyMeterOptions();
                SaveSettings();
            }
        }
    }

    public bool HasUpdateNotification
    {
        get => _hasUpdateNotification;
        set => SetProperty(ref _hasUpdateNotification, value);
    }

    public string UpdateButtonToolTip
    {
        get => _updateButtonToolTip;
        set => SetProperty(ref _updateButtonToolTip, value);
    }

    public int CombatRecordRetentionSeconds
    {
        get => _combatRecordRetentionSeconds;
        set
        {
            var normalized = Math.Clamp(value, 60, 1800);
            if (SetProperty(ref _combatRecordRetentionSeconds, normalized))
            {
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
                UpdateDisplayedPlayers();
                SaveSettings();
            }
        }
    }

    public bool AutoUpdateCheckEnabled
    {
        get => _autoUpdateCheckEnabled;
        set
        {
            if (SetProperty(ref _autoUpdateCheckEnabled, value))
            {
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
        _ = CheckForUpdatesImprovedAsync();
    }

    public string LogUploadUrl
    {
        get => _logUploadUrl;
        set
        {
            var normalized = value?.Trim() ?? string.Empty;
            if (SetProperty(ref _logUploadUrl, normalized))
            {
                UploadLogsCommand.NotifyCanExecuteChanged();
                SaveSettings();
            }
        }
    }

    public IReadOnlyList<CombatRecord> LoadCombatRecords()
    {
        return _combatRecordStore.LoadRecent(retentionSeconds: CombatRecordRetentionSeconds);
    }

    public async Task CheckForUpdatesAsync(bool showLatestMessage = false)
    {
        if (!AutoUpdateCheckEnabled && !showLatestMessage)
        {
            return;
        }

        try
        {
            var result = await _updateCheckerService.CheckForUpdateAsync();
            if (result.IsUpdateAvailable)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    StatusText = $"새 버전 {result.LatestVersion} 이 있습니다.";
                    var answer = MessageBox.Show(
                        $"새 버전 {result.LatestVersion} 이 있습니다.\n현재 버전: v{AppVersion.Current}\n\n릴리즈 페이지를 열까요?",
                        "업데이트 확인",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (answer == MessageBoxResult.Yes && !string.IsNullOrWhiteSpace(result.ReleaseUrl))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = result.ReleaseUrl,
                            UseShellExecute = true
                        });
                    }
                });
                return;
            }

            if (showLatestMessage)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    StatusText = $"최신 버전입니다. v{AppVersion.Current}";
                    MessageBox.Show(
                        $"현재 최신 버전입니다.\n버전: v{AppVersion.Current}",
                        "업데이트 확인",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                });
            }
        }
        catch (Exception ex)
        {
            if (showLatestMessage)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    StatusText = $"업데이트 확인 실패: {ex.Message}";
                    MessageBox.Show(
                        $"업데이트 확인에 실패했습니다.\n{ex.Message}",
                        "업데이트 확인",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                });
            }
        }
    }

    public void TriggerResetHotkey()
    {
        ResetMeter();
        StatusText = $"DPS를 초기화했습니다. 단축키: {ResetHotkey}";
    }

    public async Task CheckForUpdatesImprovedAsync(bool showLatestMessage = false)
    {
        if (!AutoUpdateCheckEnabled && !showLatestMessage)
        {
            return;
        }

        try
        {
            var result = await _updateCheckerService.CheckForUpdateAsync();
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                HasUpdateNotification = result.IsUpdateAvailable;
                UpdateButtonToolTip = result.IsUpdateAvailable
                    ? $"새 버전 {result.LatestVersion}"
                    : "업데이트";
                StatusText = result.Message;
            });

            if (!result.IsUpdateAvailable)
            {
                if (showLatestMessage)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show(
                            $"현재 최신 버전입니다.\n버전: v{AppVersion.Current}",
                            "업데이트 확인",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    });
                }

                return;
            }

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var answer = MessageBox.Show(
                    $"새 버전 {result.LatestVersion} 이 있습니다.\n현재 버전: v{AppVersion.Current}\n\n릴리즈 페이지를 열까요?",
                    "업데이트 확인",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (answer == MessageBoxResult.Yes && !string.IsNullOrWhiteSpace(result.ReleaseUrl))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = result.ReleaseUrl,
                        UseShellExecute = true
                    });
                }
            });
        }
        catch (Exception ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                StatusText = $"업데이트 확인 실패: {ex.Message}";
                if (showLatestMessage)
                {
                    MessageBox.Show(
                        $"업데이트 확인에 실패했습니다.\n{ex.Message}",
                        "업데이트 확인",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            });
        }
    }

    public async Task RunAutoUpdateAsync(bool showLatestMessage = false)
    {
        try
        {
            var result = await _updateCheckerService.CheckForUpdateAsync();
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                HasUpdateNotification = result.IsUpdateAvailable;
                UpdateButtonToolTip = result.IsUpdateAvailable
                    ? $"새 버전 {result.LatestVersion}"
                    : "업데이트";
                StatusText = result.Message;
            });

            if (!result.IsUpdateAvailable)
            {
                if (showLatestMessage)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show(
                            $"현재 최신 버전입니다.\n버전: v{AppVersion.Current}",
                            "자동 업데이트",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    });
                }

                return;
            }

            var shouldInstall = false;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var answer = MessageBox.Show(
                    $"새 버전 {result.LatestVersion} 이 있습니다.\n현재 버전: v{AppVersion.Current}\n\n자동 업데이트를 시작할까요?",
                    "자동 업데이트",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);
                shouldInstall = answer == MessageBoxResult.Yes;
            });

            if (!shouldInstall)
            {
                return;
            }

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                StatusText = $"업데이트 파일 다운로드 중... {result.LatestVersion}";
            });

            var installResult = await _updateInstallerService.InstallAsync(result);
            if (!installResult.Success)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    StatusText = $"자동 업데이트 실패: {installResult.Message}";
                    MessageBox.Show(
                        $"자동 업데이트에 실패했습니다.\n{installResult.Message}",
                        "자동 업데이트",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                });
                return;
            }

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                StatusText = installResult.Message;
                MessageBox.Show(
                    "자동 업데이트를 시작합니다.\n프로그램을 종료한 뒤 새 버전으로 다시 실행합니다.",
                    "자동 업데이트",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Application.Current.Shutdown();
            });
        }
        catch (Exception ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                StatusText = $"자동 업데이트 실패: {ex.Message}";
                if (showLatestMessage)
                {
                    MessageBox.Show(
                        $"자동 업데이트에 실패했습니다.\n{ex.Message}",
                        "자동 업데이트",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            });
        }
    }

    public void SetStatusMessage(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            StatusText = message;
        }
    }

    public void TriggerFullResetHotkey()
    {
        ResetAll();
        StatusText = $"전체 초기화를 완료했습니다. 단축키: {FullResetHotkey}";
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
        _detectionStateTimer.Stop();
        PartyApplicants.CollectionChanged -= OnPartyApplicantsChanged;
        SaveCombatRecordIfAvailable("프로그램 종료");
        _dpsMeterService.SnapshotUpdated -= OnSnapshotUpdated;
        _dpsMeterService.StatusChanged -= OnMeterStatusChanged;
        _dpsMeterService.CharacterDetected -= OnCharacterDetected;
        _dpsMeterService.TargetChanged -= OnTargetChanged;
        _dpsMeterService.ResetSuggested -= OnResetSuggested;
        _dpsMeterService.CombatRecordCompleted -= OnCombatRecordCompleted;
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

        var runtimeName = ParseRuntimeEntityName(payload.Name);
        if (string.IsNullOrWhiteSpace(runtimeName.DisplayName))
        {
            return;
        }

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var resolvedServerName = !string.IsNullOrWhiteSpace(runtimeName.ServerName)
                ? runtimeName.ServerName
                : TryResolveServerName(payload.ServerId) ?? string.Empty;
            _detectedCharactersByActorId[payload.ActorId] = new DetectedCharacterInfo(
                runtimeName.DisplayName,
                payload.ServerId,
                resolvedServerName,
                payload.JobCode);
        });

        var hasVisibleRow = payload.ActorId > 0 && _rowsByActorId.ContainsKey(payload.ActorId);
        if (!hasVisibleRow && !_dpsMeterService.HasRecentDamage(payload.ActorId))
        {
            return;
        }

        DpsPlayerRow? row = null;
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            row = GetOrCreateRow(payload.ActorId, runtimeName.DisplayName);
            row.Name = runtimeName.DisplayName;
            row.IsConfirmedIdentity = true;
            row.ServerId = payload.ServerId;
            ApplyDetectedJob(row, payload.JobCode);
            TryMarkSelf(row, payload.ActorId);

            if (!string.IsNullOrWhiteSpace(runtimeName.ServerName))
            {
                row.Server = runtimeName.ServerName;
            }
            else if (payload.ServerId > 0)
            {
                row.Server = TryResolveServerName(payload.ServerId) ?? row.Server;
            }
        });

        if (row is null)
        {
            return;
        }

        await Application.Current.Dispatcher.InvokeAsync(ReorderRows);
    }

    private void OnSnapshotUpdated(object? sender, IReadOnlyList<DpsEntry> entries)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _lastDetectionSignalUtc = DateTime.UtcNow;
            CombatPort = _dpsMeterService.CombatPort;
            var partyActorIds = _dpsMeterService.PartyCandidateActorIds.ToHashSet();
            var activeActorIds = entries.Select(entry => entry.ActorId).Where(id => id > 0).ToHashSet();
            if (entries.Count > 0)
            {
                _preserveRowsAfterMeterReset = false;
            }

            foreach (var row in LivePlayers)
            {
                row.Dps = 0;
                row.TotalDamage = 0;
                row.CritRate = 0;
                row.DoubleRate = 0;
                row.HitCount = 0;
                row.PartyShareRatio = 0;
                row.IsPartyCandidate = false;
            }

            foreach (var entry in entries)
            {
                var runtimeName = ParseRuntimeEntityName(entry.Name);
                var normalizedName = string.IsNullOrWhiteSpace(runtimeName.DisplayName) ? entry.Name : runtimeName.DisplayName;
                var row = GetOrCreateRow(entry.ActorId, normalizedName);

                if ((!runtimeName.IsOwnerDerived || !row.IsConfirmedIdentity) && ShouldReplaceName(row.Name, normalizedName))
                {
                    row.Name = normalizedName;
                }

                TryMarkSelf(row, entry.ActorId);

                if (!string.IsNullOrWhiteSpace(runtimeName.ServerName))
                {
                    row.Server = runtimeName.ServerName;
                    row.ServerId = TryResolveServerId(runtimeName.ServerName) ?? row.ServerId;
                }

                row.Dps = entry.Dps;
                row.TotalDamage = entry.TotalDamage;
                row.CritRate = entry.CritRate;
                row.DoubleRate = entry.DoubleRate;
                row.HitCount = entry.HitCount;
                row.IsPartyCandidate = entry.IsPartyCandidate || partyActorIds.Contains(entry.ActorId);
                row.PartyShareRatio = entry.PartyShareRatio;

            }

            if (!_preserveRowsAfterMeterReset || activeActorIds.Count > 0)
            {
                RemoveInactiveRows(activeActorIds);
            }

            UpdatePinnedSearchVisibility();
            ApplyPartyHints();
            ReorderRows();
            RaisePropertyChanged(nameof(TotalPartyDps));
            RaisePropertyChanged(nameof(DetectionStatus));
            RaisePropertyChanged(nameof(DetectionIndicatorBrush));

            TryAppendDpsSnapshotLog();
        });
    }

    private void OnMeterStatusChanged(object? sender, string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _lastDetectionSignalUtc = DateTime.UtcNow;
            CombatPort = _dpsMeterService.CombatPort;
            StatusText = message;
            RaisePropertyChanged(nameof(DetectionStatus));
            RaisePropertyChanged(nameof(DetectionIndicatorBrush));
        });
    }

    private void OnTargetChanged(object? sender, TargetInfo? target)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _currentTargetInfo = target;
            CurrentTargetName = target?.Name ?? "타겟 대기 중";
            IsBossTarget = target?.IsBoss == true;
            CurrentTargetHealthText = FormatTargetHealth(target);
            ApplyPartyHints();
            ReorderRows();
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

    private void OnCombatRecordCompleted(object? sender, CombatRecord record)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            EnrichCombatRecord(record, _rowsByActorId.ToDictionary(
                pair => pair.Key,
                pair => ParticipantSnapshot.FromRow(pair.Value)), _currentTargetInfo);
            SaveCombatRecordCore(record);
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
        SaveCombatRecordIfAvailable("DPS 리셋");
        _dpsMeterService.Reset();
        _preserveRowsAfterMeterReset = true;

        foreach (var row in LivePlayers)
        {
            row.Dps = 0;
            row.TotalDamage = 0;
            row.CritRate = 0;
            row.DoubleRate = 0;
            row.HitCount = 0;
            row.PartyShareRatio = 0;
        }

        _pendingProfileKeys.Clear();
        if (_selfActorId > 0 && _rowsByActorId.TryGetValue(_selfActorId, out var selfRow))
        {
            selfRow.IsSelf = true;
        }

        CurrentTargetName = "타겟 대기 중";
        CurrentTargetHealthText = string.Empty;
        _currentTargetInfo = null;
        IsBossTarget = false;
        StatusText = "DPS 데이터만 초기화했습니다. 이름과 아툴 정보는 유지됩니다.";
        RaisePropertyChanged(nameof(TotalPartyDps));
        UpdatePinnedSearchVisibility();
        ReorderRows();
    }

    private void ResetAll()
    {
        SaveCombatRecordIfAvailable("전체 리셋");
        _dpsMeterService.Reset();
        _preserveRowsAfterMeterReset = false;
        _rowsByActorId.Clear();
        _rowsByName.Clear();
        _profileCache.Clear();
        _detectedCharactersByActorId.Clear();
        _partyApplicantCardsByActorId.Clear();
        _pendingProfileKeys.Clear();
        LivePlayers.Clear();
        DisplayPlayers.Clear();
        PartyApplicants.Clear();
        _selfActorId = 0;
        CurrentTargetName = "타겟 대기 중";
        CurrentTargetHealthText = string.Empty;
        _currentTargetInfo = null;
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
                goto CreateNewRow;
            }

            if (byName.ActorId > 0 && actorId > 0 && byName.ActorId != actorId)
            {
                goto CreateNewRow;
            }

            if (actorId > 0)
            {
                byName.ActorId = actorId;
                _rowsByActorId[actorId] = byName;
            }

            return byName;
        }

CreateNewRow:
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
        if (!string.IsNullOrWhiteSpace(profile.Nickname))
        {
            row.Name = profile.Nickname;
        }

        if (!string.IsNullOrWhiteSpace(profile.Job) &&
            !string.Equals(profile.Job, "미확인", StringComparison.OrdinalIgnoreCase))
        {
            row.Job = profile.Job;
        }

        if (!string.IsNullOrWhiteSpace(profile.JobImageUrl))
        {
            row.JobImageUrl = profile.JobImageUrl;
        }

        if (!string.IsNullOrWhiteSpace(profile.Server))
        {
            row.Server = profile.Server;
        }

        row.ServerId = 0;
        row.CombatScore = profile.CombatScore;
        row.MajorTitles = profile.MajorTitles.Take(3).Select(ShortTitle).ToArray();
        row.MajorStigmas = profile.MajorStigmas.Take(4).ToArray();
        row.IsConfirmedIdentity = true;
    }

    private static void ApplyProfile(PartyMemberCard card, CharacterProfile profile)
    {
        card.Nickname = profile.Nickname;
        card.Server = profile.Server;

        if (!string.IsNullOrWhiteSpace(profile.Job))
        {
            card.Job = profile.Job;
        }

        if (!string.IsNullOrWhiteSpace(profile.JobImageUrl))
        {
            card.JobImageUrl = profile.JobImageUrl;
        }

        card.CombatScore = profile.CombatScore;
        card.MajorTitles.Clear();
        foreach (var title in profile.MajorTitles.Take(3).Select(ShortTitle))
        {
            card.MajorTitles.Add(title);
        }

        card.MajorStigmas.Clear();
        foreach (var stigma in profile.MajorStigmas.Take(4))
        {
            card.MajorStigmas.Add(stigma);
        }
    }

    private static string ShortTitle(string value)
    {
        var text = value?.Trim() ?? string.Empty;
        return text.Length <= 4 ? text : text[..4];
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

        UpdateDisplayedPlayers();
    }

    private void UpdateDisplayedPlayers()
    {
        var source = IsCompactMode ? LivePlayers.Take(4) : LivePlayers.AsEnumerable();
        DisplayPlayers.Clear();
        foreach (var row in source)
        {
            DisplayPlayers.Add(row);
        }
    }

    private void RefreshPartyApplicantCards()
    {
        _partyApplicantCardsByActorId.Clear();
        PartyApplicants.Clear();
    }

    private static string FormatTargetHealth(TargetInfo? target)
    {
        if (target is null)
        {
            return string.Empty;
        }

        if (target.MaxHp <= 0)
        {
            return target.DamageTaken > 0
                ? $"HP 추적 중 / 누적 피해 {target.DamageTaken:N0}"
                : string.Empty;
        }

        var current = Math.Max(0L, target.CurrentHp);
        var max = Math.Max(1L, target.MaxHp);
        var ratio = (double)current / max;
        return $"HP {current:N0}/{max:N0} ({ratio:P0})";
    }

    private void ApplyPartyHints()
    {
        var partyActorIds = _dpsMeterService.PartyCandidateActorIds.ToHashSet();
        foreach (var row in LivePlayers)
        {
            row.IsPartyCandidate = partyActorIds.Contains(row.ActorId);
        }

        var selfRow = LivePlayers.FirstOrDefault(row => row.IsSelf);
        var desiredPartyCount = selfRow is null ? 0 : 1;
        desiredPartyCount += Math.Clamp(_dpsMeterService.PartySlotHintCount, 0, 3);
        if (partyActorIds.Count > 0)
        {
            desiredPartyCount = Math.Max(desiredPartyCount, Math.Min(4, partyActorIds.Count + (selfRow is null ? 0 : 1)));
        }

        if (desiredPartyCount > 0)
        {
            var inferredPartyRows = LivePlayers
                .Where(row => row.IsSelf || row.IsPartyCandidate)
                .OrderByDescending(row => row.TotalDamage)
                .ToList();

            if (inferredPartyRows.Count < desiredPartyCount)
            {
                var fillRows = LivePlayers
                    .Where(row => !row.IsSelf && !row.IsPartyCandidate && row.IsResolvedName)
                    .OrderByDescending(row => row.TotalDamage)
                    .ThenByDescending(row => row.Dps)
                    .Take(desiredPartyCount - inferredPartyRows.Count)
                    .ToList();

                foreach (var row in fillRows)
                {
                    row.IsPartyCandidate = true;
                }
            }
        }

        var targetMaxHp = _currentTargetInfo?.MaxHp ?? 0;
        var targetDamageTaken = _currentTargetInfo?.DamageTaken ?? 0;
        var visibleDamage = Math.Max(1L, LivePlayers.Sum(row => row.TotalDamage));
        var contributionBase = targetMaxHp > 0
            ? targetMaxHp
            : targetDamageTaken > 0
                ? Math.Max(targetDamageTaken, visibleDamage)
                : visibleDamage;

        foreach (var row in LivePlayers)
        {
            row.PartyShareRatio = row.TotalDamage <= 0
                ? 0
                : Math.Min(1.0, (double)row.TotalDamage / contributionBase);
        }
    }

    private void ApplyMeterOptions()
    {
        _dpsMeterService.MeterWindowSeconds = MeterWindowSeconds;
        _dpsMeterService.CombatResetSeconds = CombatResetSeconds;
        _dpsMeterService.ActiveDisplaySeconds = ActiveDisplaySeconds;
        _dpsMeterService.BossOnlyMode = BossOnlyMode;
        _dpsMeterService.EnablePartyPacketLogging = PartyPacketLoggingEnabled;
        _dpsMeterService.EnableStrongHitAnalysisLogging = !_isDistributionBuild && StrongHitAnalysisLoggingEnabled;
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

    private void SaveCombatRecordIfAvailable(string reason)
    {
        CombatRecord? record;
        try
        {
            record = _dpsMeterService.CaptureCombatRecord(reason);
        }
        catch (Exception ex)
        {
            StatusText = $"전투 기록 저장 중 오류: {ex.Message}";
            return;
        }

        if (record is null || record.TotalDamage <= 0 || record.Participants.Count == 0)
        {
            return;
        }

        var rowSnapshots = _rowsByActorId.ToDictionary(
            pair => pair.Key,
            pair => ParticipantSnapshot.FromRow(pair.Value));
        var currentTargetSnapshot = _currentTargetInfo;
        _ = SaveCombatRecordAsync(
            record,
            reason,
            rowSnapshots,
            currentTargetSnapshot);
    }

    private async Task SaveCombatRecordAsync(
        CombatRecord record,
        string reason,
        IReadOnlyDictionary<int, ParticipantSnapshot> rowSnapshots,
        TargetInfo? currentTargetSnapshot)
    {
        try
        {
            if (!string.Equals(reason, "프로그램 종료", StringComparison.Ordinal))
            {
                await Task.Delay(CombatRecordSaveDelayMilliseconds);
            }

            EnrichCombatRecord(record, rowSnapshots, currentTargetSnapshot);
            SaveCombatRecordCore(record);
        }
        catch (Exception ex)
        {
            StatusText = $"전투 기록 후처리 중 오류: {ex.Message}";
        }
    }

    private void EnrichCombatRecord(
        CombatRecord record,
        IReadOnlyDictionary<int, ParticipantSnapshot> rowSnapshots,
        TargetInfo? currentTargetSnapshot)
    {
        if (IsPlaceholderTarget(record.TargetName)
            && currentTargetSnapshot is not null
            && currentTargetSnapshot.TargetId > 0
            && (record.TargetId <= 0 || currentTargetSnapshot.TargetId == record.TargetId)
            && !IsPlaceholderTarget(currentTargetSnapshot.Name))
        {
            record.TargetId = currentTargetSnapshot.TargetId;
            record.TargetName = currentTargetSnapshot.Name;
            record.IsBossTarget = currentTargetSnapshot.IsBoss;
        }

        var totalDamage = Math.Max(1L, record.TotalDamage > 0 ? record.TotalDamage : record.Participants.Sum(item => item.TotalDamage));
        foreach (var participant in record.Participants)
        {
            participant.PartyShareRatio = (double)participant.TotalDamage / totalDamage;

            if (participant.ActorId > 0 && rowSnapshots.TryGetValue(participant.ActorId, out var liveRow))
            {
                if (IsPlaceholderActor(participant.Name) && liveRow.IsResolvedName)
                {
                    participant.Name = liveRow.Name;
                }

                ApplyParticipantSnapshot(participant, liveRow);
                continue;
            }

            var cached = _profileCache.Values.FirstOrDefault(profile =>
                string.Equals(profile.Nickname, participant.Name, StringComparison.OrdinalIgnoreCase));
            if (cached is not null)
            {
                participant.Name = cached.Nickname;
                participant.Server = cached.Server;
                participant.Job = cached.Job;
                participant.JobImageUrl = cached.JobImageUrl;
                participant.CombatScore = cached.CombatScore;
                participant.MajorTitles = cached.MajorTitles.Take(3).Select(ShortTitle).ToArray();
                participant.MajorStigmas = cached.MajorStigmas.Take(4).ToArray();
            }
        }
    }

    private static void ApplyParticipantRow(CombatRecordParticipant participant, DpsPlayerRow row)
    {
        participant.Server = row.Server;
        participant.Job = row.Job;
        participant.JobImageUrl = row.JobImageUrl;
        participant.CombatScore = row.CombatScore;
        participant.IsSelf = row.IsSelf;
        participant.IsPartyCandidate = row.IsPartyCandidate;
        participant.MajorTitles = row.MajorTitles;
        participant.MajorStigmas = row.MajorStigmas;
    }

    private static void ApplyParticipantSnapshot(CombatRecordParticipant participant, ParticipantSnapshot row)
    {
        participant.Server = row.Server;
        participant.Job = row.Job;
        participant.JobImageUrl = row.JobImageUrl;
        participant.CombatScore = row.CombatScore;
        participant.IsSelf = row.IsSelf;
        participant.IsPartyCandidate = row.IsPartyCandidate;
        participant.MajorTitles = row.MajorTitles;
        participant.MajorStigmas = row.MajorStigmas;
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

    private static bool IsPlaceholderActor(string? name)
    {
        return string.IsNullOrWhiteSpace(name)
            || name.StartsWith("#", StringComparison.Ordinal)
            || string.Equals(name, "이름 확인중", StringComparison.Ordinal);
    }

    private static bool IsPlaceholderTarget(string? name)
    {
        return string.IsNullOrWhiteSpace(name)
            || name.StartsWith("#", StringComparison.Ordinal)
            || string.Equals(name, "타겟 대기 중", StringComparison.Ordinal)
            || string.Equals(name, "타겟 확인중", StringComparison.Ordinal)
            || string.Equals(name, "타겟 미상", StringComparison.Ordinal);
    }

    private void SaveCombatRecordCore(CombatRecord record)
    {
        var recordKey = record.Id;
        if (string.Equals(_lastSavedCombatRecordKey, recordKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastSavedCombatRecordKey = recordKey;
        _combatRecordStore.Save(record, retentionSeconds: CombatRecordRetentionSeconds);
    }

    private sealed record ParticipantSnapshot(
        string Name,
        string Server,
        string Job,
        string JobImageUrl,
        long CombatScore,
        bool IsSelf,
        bool IsPartyCandidate,
        IReadOnlyList<string> MajorTitles,
        IReadOnlyList<string> MajorStigmas,
        bool IsResolvedName)
    {
        public static ParticipantSnapshot FromRow(DpsPlayerRow row) =>
            new(
                row.Name,
                row.Server,
                row.Job,
                row.JobImageUrl,
                row.CombatScore,
                row.IsSelf,
                row.IsPartyCandidate,
                row.MajorTitles.ToArray(),
                row.MajorStigmas.ToArray(),
                row.IsResolvedName);
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

    private RuntimeEntityName ParseRuntimeEntityName(string input)
    {
        var value = input.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return new RuntimeEntityName(value, null, false);
        }

        var bracketMatch = Regex.Match(value, @"^(?<name>.+?)\s*[\[\(](?<suffix>.+?)[\]\)]$");
        if (!bracketMatch.Success)
        {
            return new RuntimeEntityName(value, null, false);
        }

        var name = bracketMatch.Groups["name"].Value.Trim();
        var suffix = bracketMatch.Groups["suffix"].Value.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(suffix))
        {
            return new RuntimeEntityName(value, null, false);
        }

        if (IsKnownServerName(suffix))
        {
            return new RuntimeEntityName(name, suffix, false);
        }

        if (IsLikelyOwnedEntityName(name))
        {
            return new RuntimeEntityName(suffix, null, true);
        }

        return new RuntimeEntityName(value, null, false);
    }

    private bool IsKnownServerName(string value)
    {
        return Servers.Any(server => string.Equals(server.Name, value, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLikelyOwnedEntityName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string[] keywords =
        [
            "소환", "정령", "분신", "환영", "덫", "함정", "토템", "기운", "결계", "구체",
            "마법", "화살", "폭발", "사격", "표식", "장판", "파동", "타격", ":"
        ];

        return keywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private string? TryResolveServerName(int serverId)
    {
        return Servers.FirstOrDefault(item => item.Id == serverId)?.Name;
    }

    private int? TryResolveServerId(string? serverName)
    {
        if (string.IsNullOrWhiteSpace(serverName))
        {
            return null;
        }

        return Servers.FirstOrDefault(item => string.Equals(item.Name, serverName, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    private static void ApplyDetectedJob(DpsPlayerRow row, int jobCode)
    {
        row.JobCode = jobCode;
        var (jobName, jobImageUrl) = ResolveJobInfo(jobCode);
        if (!string.IsNullOrWhiteSpace(jobName) && (string.IsNullOrWhiteSpace(row.Job) || row.Job == "-" || row.Job == "미확인"))
        {
            row.Job = jobName;
        }

        if (!string.IsNullOrWhiteSpace(jobImageUrl) && string.IsNullOrWhiteSpace(row.JobImageUrl))
        {
            row.JobImageUrl = jobImageUrl;
        }
    }

    private static void ApplyDetectedJob(PartyMemberCard card, int jobCode)
    {
        var (jobName, jobImageUrl) = ResolveJobInfo(jobCode);
        if (!string.IsNullOrWhiteSpace(jobName) && (string.IsNullOrWhiteSpace(card.Job) || card.Job == "-" || card.Job == "미확인"))
        {
            card.Job = jobName;
        }

        if (!string.IsNullOrWhiteSpace(jobImageUrl) && string.IsNullOrWhiteSpace(card.JobImageUrl))
        {
            card.JobImageUrl = jobImageUrl;
        }
    }

    private static (string JobName, string JobImageUrl) ResolveJobInfo(int jobCode)
    {
        return jobCode switch
        {
            0 => ("검성", "https://assets.playnccdn.com/static-aion2/characters/img/class/class_icon_gladiator.png"),
            1 => ("수호성", "https://assets.playnccdn.com/static-aion2/characters/img/class/class_icon_templar.png"),
            2 => ("살성", "https://assets.playnccdn.com/static-aion2/characters/img/class/class_icon_assassin.png"),
            3 => ("궁성", "https://assets.playnccdn.com/static-aion2/characters/img/class/class_icon_ranger.png"),
            4 => ("치유성", "https://assets.playnccdn.com/static-aion2/characters/img/class/class_icon_cleric.png"),
            5 => ("호법성", "https://assets.playnccdn.com/static-aion2/characters/img/class/class_icon_chanter.png"),
            6 => ("마도성", "https://assets.playnccdn.com/static-aion2/characters/img/class/class_icon_sorcerer.png"),
            7 => ("정령성", "https://assets.playnccdn.com/static-aion2/characters/img/class/class_icon_elementalist.png"),
            _ => (string.Empty, string.Empty)
        };
    }

    private void LoadSettings()
    {
        var settings = _settingsStore.Load();
        var useLegacyDefaults =
            string.Equals(settings.ResetHotkey, "Ctrl+F1", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(settings.FullResetHotkey, "Ctrl+R", StringComparison.OrdinalIgnoreCase);
        _windowOpacity = settings.WindowOpacity;
        _overlayOpacity = settings.OverlayOpacity;
        _cardOpacity = settings.CardOpacity;
        _isTopMost = settings.TopMost;
        _meterServerPort = settings.ServerPort;
        _resetHotkey = NormalizeHotkey(useLegacyDefaults ? "Ctrl+R" : settings.ResetHotkey, "Ctrl+R");
        _fullResetHotkey = NormalizeHotkey(useLegacyDefaults ? "Ctrl+F1" : settings.FullResetHotkey, "Ctrl+F1");
        _manualSelfName = settings.ManualSelfName?.Trim() ?? string.Empty;
        _meterWindowSeconds = Math.Clamp(
            settings.MeterWindowSeconds > 0 ? settings.MeterWindowSeconds : settings.MeterWindowMinutes * 60,
            1,
            300);
        _combatResetSeconds = Math.Clamp(settings.CombatResetSeconds, 5, 180);
        _searchResultSeconds = Math.Clamp(settings.SearchResultSeconds, 5, 180);
        _activeDisplaySeconds = Math.Clamp(settings.ActiveDisplaySeconds, 1, 30);
        _combatRecordRetentionSeconds = Math.Clamp(settings.CombatRecordRetentionSeconds, 60, 1800);
        _bossOnlyMode = settings.BossOnlyMode;
        _partyPacketLoggingEnabled = settings.PartyPacketLoggingEnabled;
        _dpsSnapshotLoggingEnabled = settings.DpsSnapshotLoggingEnabled;
        _strongHitAnalysisLoggingEnabled = _isDistributionBuild ? false : settings.StrongHitAnalysisLoggingEnabled;
        _isCompactMode = settings.CompactMode;
        _autoUpdateCheckEnabled = settings.AutoUpdateCheckEnabled;
        _logUploadUrl = string.IsNullOrWhiteSpace(settings.LogUploadUrl)
            ? DefaultLogUploadUrl
            : settings.LogUploadUrl.Trim();
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
            CombatRecordRetentionSeconds = CombatRecordRetentionSeconds,
            BossOnlyMode = BossOnlyMode,
            PartyPacketLoggingEnabled = PartyPacketLoggingEnabled,
            DpsSnapshotLoggingEnabled = DpsSnapshotLoggingEnabled,
            StrongHitAnalysisLoggingEnabled = StrongHitAnalysisLoggingEnabled,
            CompactMode = IsCompactMode,
            AutoUpdateCheckEnabled = AutoUpdateCheckEnabled,
            LogUploadUrl = LogUploadUrl
        });
    }

    private void TryAppendDpsSnapshotLog()
    {
        if (!DpsSnapshotLoggingEnabled || LivePlayers.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (_lastSnapshotLogUtc != DateTime.MinValue && (now - _lastSnapshotLogUtc).TotalMilliseconds < 900)
        {
            return;
        }

        _lastSnapshotLogUtc = now;
        _dpsSnapshotLogStore.AppendSnapshot(
            now,
            GetSnapshotTargetName(),
            IsBossTarget,
            LivePlayers.Where(row => row.IsConfirmedIdentity && row.HitCount > 0 && (row.Dps > 0 || row.TotalDamage > 0)).ToArray());
    }

    private string GetSnapshotTargetName()
    {
        if (_currentTargetInfo is not null && !IsPlaceholderTarget(_currentTargetInfo.Name))
        {
            return _currentTargetInfo.Name;
        }

        return IsPlaceholderTarget(CurrentTargetName) ? "타겟 확인중" : CurrentTargetName;
    }

    private readonly record struct RuntimeEntityName(string DisplayName, string? ServerName, bool IsOwnerDerived);

    public async Task UploadLogsAsync()
    {
        if (Interlocked.Exchange(ref _isUploadingLogs, 1) == 1)
        {
            return;
        }

        try
        {
            StatusText = "로그 전송 준비 중...";
            var result = await _logUploadService.UploadLogsAsync(LogUploadUrl);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                StatusText = result.Message;
                MessageBox.Show(
                    result.Message,
                    "로그 전송",
                    MessageBoxButton.OK,
                    result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            });
        }
        catch (Exception ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                StatusText = $"로그 전송 실패: {ex.Message}";
                MessageBox.Show(
                    $"로그 전송 실패\n{ex.Message}",
                    "로그 전송",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            });
        }
        finally
        {
            Interlocked.Exchange(ref _isUploadingLogs, 0);
        }
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

    private void OnPartyApplicantsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(HasPartyApplicants));
    }

    private sealed record DetectedCharacterInfo(
        string Name,
        int ServerId,
        string ServerName,
        int JobCode);
}



