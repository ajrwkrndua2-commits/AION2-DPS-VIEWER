using System.Collections.Concurrent;
using System.IO;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Aion2Dashboard.Models;

namespace Aion2Dashboard.Services;

public sealed class DpsMeterService : IDisposable
{
    private static readonly TimeSpan MaxRetention = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan TargetRetention = TimeSpan.FromSeconds(12);

    private readonly ConcurrentDictionary<int, ActorRuntime> _actors = new();
    private readonly ConcurrentDictionary<int, int> _summonOwners = new();
    private readonly ConcurrentDictionary<int, bool> _bossTargets = new();
    private readonly ConcurrentDictionary<int, TargetRuntime> _targets = new();
    private readonly Timer _snapshotTimer;
    private readonly SkillNameResolver _skillNameResolver = new();
    private readonly MobNameResolver _mobNameResolver = new();

    private PacketProcessorBridge? _bridge;
    private WfpCapturer? _capturer;
    private PacketCaptureLogger? _packetLogger;
    private int _meterWindowSeconds = 300;
    private int _combatResetSeconds = 20;
    private int _activeDisplaySeconds = 2;
    private bool _bossOnlyMode;
    private string _lastPublishedTargetKey = string.Empty;
    private DateTime _lastPartyGuessRefreshUtc = DateTime.MinValue;
    private DateTime _lastDamageUtc = DateTime.MinValue;
    private DateTime _lastAutoResetSuggestUtc = DateTime.MinValue;
    private HashSet<int> _partyCandidateActorIds = [];
    private int _partySlotHintCount;

    public DpsMeterService()
    {
        _snapshotTimer = new Timer(_ => PublishSnapshot(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public bool IsRunning { get; private set; }
    public int CombatPort { get; private set; } = -1;
    public bool EnablePartyPacketLogging { get; set; }
    public string? PartyLogFilePath => _packetLogger?.FilePath;
    public int PartySlotHintCount => _partySlotHintCount;

    public int MeterWindowSeconds
    {
        get => _meterWindowSeconds;
        set => _meterWindowSeconds = Math.Clamp(value, 1, 180);
    }

    public int CombatResetSeconds
    {
        get => _combatResetSeconds;
        set => _combatResetSeconds = Math.Clamp(value, 5, 180);
    }

    public int ActiveDisplaySeconds
    {
        get => _activeDisplaySeconds;
        set => _activeDisplaySeconds = Math.Clamp(value, 1, 10);
    }

    public bool BossOnlyMode
    {
        get => _bossOnlyMode;
        set => _bossOnlyMode = value;
    }

    public event EventHandler<IReadOnlyList<DpsEntry>>? SnapshotUpdated;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<(int ActorId, string Name, int ServerId, int JobCode)>? CharacterDetected;
    public event EventHandler<TargetInfo?>? TargetChanged;
    public event EventHandler<string>? ResetSuggested;

    public void Start(int serverPort = 13328)
    {
        if (IsRunning)
        {
            return;
        }

        if (!IsAdministrator())
        {
            throw new InvalidOperationException("愿由ъ옄 沅뚰븳?쇰줈 ?ㅽ뻾?댁빞 DPS ?⑦궥 ?섏쭛???숈옉?⑸땲??");
        }

        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "PacketProcessor.dll")))
        {
            throw new FileNotFoundException("PacketProcessor.dll??李얠쓣 ???놁뒿?덈떎.");
        }

        _bridge = new PacketProcessorBridge(serverPort);
        _bridge.DamageReceived += OnDamageReceived;
        _bridge.MobSpawnReceived += OnMobSpawnReceived;
        _bridge.SummonReceived += OnSummonReceived;
        _bridge.UserInfoReceived += OnUserInfoReceived;
        _bridge.LogReceived += (_, message) => StatusChanged?.Invoke(this, message);

        _packetLogger = EnablePartyPacketLogging ? new PacketCaptureLogger() : null;

        try
        {
            _capturer = new WfpCapturer(_bridge, _packetLogger);
            _capturer.Start();
        }
        catch (Exception ex)
        {
            _bridge.Dispose();
            _bridge = null;
            _packetLogger?.Dispose();
            _packetLogger = null;
            throw new InvalidOperationException($"WinDivert ?쒖옉 ?ㅽ뙣: {ex.Message}", ex);
        }

        IsRunning = true;
        _snapshotTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(1));

        var prefix = EnablePartyPacketLogging && !string.IsNullOrWhiteSpace(PartyLogFilePath)
            ? $"DPS ?먮룞 ?섏쭛 ?쒖옉 / ?뚰떚 濡쒓렇 ??? {PartyLogFilePath}"
            : "DPS ?먮룞 ?섏쭛 ?쒖옉";
        StatusChanged?.Invoke(this, BuildStatusMessage(prefix));
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        _snapshotTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _capturer?.Dispose();
        _bridge?.Dispose();
        _packetLogger?.Dispose();
        _capturer = null;
        _bridge = null;
        _packetLogger = null;
        IsRunning = false;
        CombatPort = -1;
        _lastPublishedTargetKey = string.Empty;
        StatusChanged?.Invoke(this, "DPS ?섏쭛??以묒??덉뒿?덈떎.");
    }

    public void Reset()
    {
        foreach (var actor in _actors.Values)
        {
            lock (actor.SyncRoot)
            {
                actor.Events.Clear();
                actor.Skills.Clear();
            }
        }

        _targets.Clear();
        _bridge?.Reset();
        _lastDamageUtc = DateTime.MinValue;
        PublishTarget(null);
        PublishSnapshot();
        StatusChanged?.Invoke(this, BuildStatusMessage("DPS ?곗씠?곕? 珥덇린?뷀뻽?듬땲??"));
    }

    public IReadOnlyList<SkillUsageEntry> GetSkillUsage(int actorId, string? name = null)
    {
        var actor = ResolveActor(actorId, name);
        if (actor is null)
        {
            return Array.Empty<SkillUsageEntry>();
        }

        var windowStart = DateTime.UtcNow - TimeSpan.FromSeconds(MeterWindowSeconds);
        lock (actor.SyncRoot)
        {
            TrimEvents(actor, windowStart - MaxRetention);

            var filteredSkills = actor.Skills.Values
                .Select(skill => new
                {
                    Skill = skill,
                    Events = skill.Events.Where(hit => hit.Timestamp >= windowStart && MatchesFilter(hit.TargetId)).ToArray()
                })
                .Where(item => item.Events.Length > 0)
                .Select(item => new
                {
                    item.Skill.SkillCode,
                    TotalDamage = item.Events.Sum(hit => hit.Damage),
                    HitCount = item.Events.Length,
                    CritCount = item.Events.Count(hit => hit.IsCritical),
                    MaxHit = item.Events.Max(hit => hit.Damage)
                })
                .OrderByDescending(item => item.TotalDamage)
                .ToArray();

            var actorTotalDamage = Math.Max(1L, filteredSkills.Sum(item => item.TotalDamage));
            var maxDamage = filteredSkills.Length == 0 ? 0L : filteredSkills.Max(item => item.TotalDamage);

            return filteredSkills
                .Select(item => new SkillUsageEntry
                {
                    SkillCode = item.SkillCode,
                    SkillName = _skillNameResolver.Resolve(item.SkillCode),
                    TotalDamage = item.TotalDamage,
                    HitCount = item.HitCount,
                    CritRate = item.HitCount == 0 ? 0 : (double)item.CritCount / item.HitCount,
                    MaxHit = item.MaxHit,
                    DamageRatio = maxDamage == 0 ? 0 : (double)item.TotalDamage / maxDamage,
                    DamageShare = (double)item.TotalDamage / actorTotalDamage
                })
                .ToArray();
        }
    }

    public void Dispose()
    {
        Stop();
        _snapshotTimer.Dispose();
    }

    public bool HasRecentDamage(int actorId)
    {
        actorId = ResolveOwnerActorId(actorId);
        if (!_actors.TryGetValue(actorId, out var actor))
        {
            return false;
        }

        var cutoff = DateTime.UtcNow - ActiveDisplayRetention;
        lock (actor.SyncRoot)
        {
            return actor.Events.Count > 0 && actor.Events[^1].Timestamp >= cutoff;
        }
    }

    private void OnDamageReceived(int actorId, int targetId, int skillCode, byte damageType, int damage, uint flags, int multiHitCount, int multiHitDamage, int healAmount, int isDot)
    {
        var total = damage + multiHitDamage;
        if (total <= 0 || actorId == targetId)
        {
            return;
        }

        var now = DateTime.UtcNow;
        TrySuggestAutoReset(now);
        actorId = ResolveOwnerActorId(actorId);
        var actor = _actors.GetOrAdd(actorId, _ => new ActorRuntime());
        var hit = new DamageEvent(now, total, (flags & 0x0100) != 0, targetId);
        _lastDamageUtc = now;

        RegisterTargetHit(targetId, now);

        lock (actor.SyncRoot)
        {
            actor.Events.Add(hit);
            var skill = actor.Skills.GetOrAdd(skillCode, code => new SkillRuntime(code));
            skill.Events.Add(hit);
            TrimEvents(actor, now - MaxRetention);
        }
    }

    private void OnMobSpawnReceived(int mobId, int mobCode, int hp, bool isBoss)
    {
        if (mobId <= 0)
        {
            return;
        }

        var target = _targets.GetOrAdd(mobId, _ => new TargetRuntime());
        target.IsBoss = isBoss;
        target.LastHitUtc = DateTime.UtcNow;

        var name = _mobNameResolver.Resolve(mobCode);
        if (!string.IsNullOrWhiteSpace(name))
        {
            target.Name = name;
        }

        if (isBoss)
        {
            _bossTargets[mobId] = true;
        }
    }

    private void OnSummonReceived(int ownerActorId, int summonActorId)
    {
        if (ownerActorId <= 0 || summonActorId <= 0 || ownerActorId == summonActorId)
        {
            return;
        }

        _summonOwners[summonActorId] = ResolveOwnerActorId(ownerActorId);
    }

    private void OnUserInfoReceived(int actorId, string name, int serverId, int jobCode)
    {
        if (_summonOwners.ContainsKey(actorId) || IsSummonName(name))
        {
            return;
        }

        var actor = _actors.GetOrAdd(actorId, _ => new ActorRuntime());
        lock (actor.SyncRoot)
        {
            actor.Name = name;
        }

        CharacterDetected?.Invoke(this, (actorId, name, serverId, jobCode));
    }

    private void PublishSnapshot()
    {
        RefreshPartyCandidates();
        CombatPort = _bridge?.GetCombatPort() ?? -1;
        var activeCutoff = DateTime.UtcNow - ActiveDisplayRetention;
        var windowStart = DateTime.UtcNow - TimeSpan.FromSeconds(MeterWindowSeconds);

        var snapshot = _actors
            .Where(pair => !_summonOwners.ContainsKey(pair.Key))
            .Select(pair =>
            {
                var actor = pair.Value;
                lock (actor.SyncRoot)
                {
                    TrimEvents(actor, windowStart - MaxRetention);
                    var events = actor.Events.Where(hit => hit.Timestamp >= windowStart && MatchesFilter(hit.TargetId)).ToArray();
                    if (events.Length == 0)
                    {
                        return null;
                    }

                    if (events[^1].Timestamp < activeCutoff)
                    {
                        return null;
                    }

                    var totalDamage = events.Sum(hit => hit.Damage);
                    var critCount = events.Count(hit => hit.IsCritical);
                    var duration = Math.Max(1.0, (events[^1].Timestamp - events[0].Timestamp).TotalSeconds);

                    return new DpsEntry
                    {
                        ActorId = pair.Key,
                        Name = string.IsNullOrWhiteSpace(actor.Name) ? $"#{pair.Key}" : actor.Name,
                        TotalDamage = totalDamage,
                        Dps = (long)(totalDamage / duration),
                        HitCount = events.Length,
                        CritRate = events.Length == 0 ? 0 : (double)critCount / events.Length,
                        IsPartyCandidate = _partyCandidateActorIds.Contains(pair.Key)
                    };
                }
            })
            .Where(item => item is not null)
            .Select(item => item!)
            .OrderByDescending(item => !item.Name.StartsWith("#", StringComparison.Ordinal))
            .ThenByDescending(item => item.TotalDamage)
            .ThenByDescending(item => item.Dps)
            .Take(30)
            .ToArray();

        PublishTarget(SelectCurrentTarget());
        SnapshotUpdated?.Invoke(this, snapshot);
    }

    private void RefreshPartyCandidates()
    {
        if (!EnablePartyPacketLogging || string.IsNullOrWhiteSpace(PartyLogFilePath) || !File.Exists(PartyLogFilePath))
        {
            _partyCandidateActorIds.Clear();
            _partySlotHintCount = 0;
            return;
        }

        var now = DateTime.UtcNow;
        if ((now - _lastPartyGuessRefreshUtc).TotalSeconds < 2)
        {
            return;
        }

        _lastPartyGuessRefreshUtc = now;

        try
        {
            var lines = File.ReadLines(PartyLogFilePath)
                .Reverse()
                .Take(500)
                .Reverse()
                .ToArray();

            var slotMarkers = new Dictionary<string, int>
            {
                ["451C921A"] = 0,
                ["451C926B"] = 0,
                ["3F1C9288"] = 0
            };

            var counts = new Dictionary<int, int>();
            foreach (var line in lines)
            {
                if (!line.Contains("|src=13328|", StringComparison.Ordinal))
                {
                    continue;
                }

                var hexIndex = line.IndexOf("|hex=", StringComparison.Ordinal);
                if (hexIndex < 0)
                {
                    continue;
                }

                var hex = line[(hexIndex + 5)..];
                foreach (var marker in slotMarkers.Keys.ToArray())
                {
                    if (hex.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    {
                        slotMarkers[marker]++;
                    }
                }

                if (hex.Length < 40 || !hex.Contains("50690F00", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (Match match in Regex.Matches(hex, @"38(?<id>[0-9A-F]{6})00?50690F00", RegexOptions.IgnoreCase))
                {
                    var actorId = ParseLittleEndianActorId(match.Groups["id"].Value);
                    if (actorId <= 0)
                    {
                        continue;
                    }

                    counts[actorId] = counts.TryGetValue(actorId, out var current) ? current + 1 : 1;
                }
            }

            _partyCandidateActorIds = counts
                .Where(pair => pair.Value >= 3)
                .Select(pair => pair.Key)
                .ToHashSet();
            _partySlotHintCount = slotMarkers.Count(pair => pair.Value >= 2);
        }
        catch
        {
            _partySlotHintCount = 0;
        }
    }

    private static int ParseLittleEndianActorId(string hex)
    {
        if (hex.Length != 6)
        {
            return 0;
        }

        var b0 = Convert.ToByte(hex[..2], 16);
        var b1 = Convert.ToByte(hex.Substring(2, 2), 16);
        var b2 = Convert.ToByte(hex.Substring(4, 2), 16);
        return b0 | (b1 << 8) | (b2 << 16);
    }

    private void TrySuggestAutoReset(DateTime nowUtc)
    {
        if (_lastDamageUtc == DateTime.MinValue)
        {
            return;
        }

        if ((nowUtc - _lastDamageUtc).TotalSeconds < CombatResetSeconds)
        {
            return;
        }

        if ((nowUtc - _lastAutoResetSuggestUtc).TotalSeconds < 15)
        {
            return;
        }

        var hasRecordedDamage = _actors.Values.Any(actor =>
        {
            lock (actor.SyncRoot)
            {
                return actor.Events.Count > 0;
            }
        });

        if (!hasRecordedDamage)
        {
            return;
        }

        _lastAutoResetSuggestUtc = nowUtc;
        ResetSuggested?.Invoke(this, "湲?怨듬갚 ?????꾪닾媛 媛먯??섏뼱 ?꾩껜 由ъ뀑??吏꾪뻾?덉뒿?덈떎.");
    }

    private TimeSpan ActiveDisplayRetention => TimeSpan.FromSeconds(ActiveDisplaySeconds);

    private ActorRuntime? ResolveActor(int actorId, string? name)
    {
        if (actorId > 0)
        {
            actorId = ResolveOwnerActorId(actorId);
            if (_actors.TryGetValue(actorId, out var actorById))
            {
                return actorById;
            }
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            return _actors.Values.FirstOrDefault(actor =>
            {
                lock (actor.SyncRoot)
                {
                    return string.Equals(actor.Name, name, StringComparison.OrdinalIgnoreCase);
                }
            });
        }

        return null;
    }

    private int ResolveOwnerActorId(int actorId)
    {
        while (_summonOwners.TryGetValue(actorId, out var ownerActorId) && ownerActorId > 0 && ownerActorId != actorId)
        {
            actorId = ownerActorId;
        }

        return actorId;
    }

    private bool MatchesFilter(int targetId)
    {
        return !BossOnlyMode || _bossTargets.IsEmpty || _bossTargets.ContainsKey(targetId);
    }

    private void RegisterTargetHit(int targetId, DateTime now)
    {
        if (targetId <= 0)
        {
            return;
        }

        var target = _targets.GetOrAdd(targetId, _ => new TargetRuntime());
        target.LastHitUtc = now;
        if (_bossTargets.ContainsKey(targetId))
        {
            target.IsBoss = true;
        }
    }

    private TargetInfo? SelectCurrentTarget()
    {
        var cutoff = DateTime.UtcNow - TargetRetention;
        return _targets
            .Where(pair => pair.Value.LastHitUtc >= cutoff && !string.IsNullOrWhiteSpace(pair.Value.Name))
            .Select(pair => new TargetInfo
            {
                TargetId = pair.Key,
                Name = pair.Value.Name,
                IsBoss = pair.Value.IsBoss
            })
            .OrderByDescending(item => item.IsBoss)
            .ThenByDescending(item => _targets[item.TargetId].LastHitUtc)
            .FirstOrDefault();
    }

    private void PublishTarget(TargetInfo? target)
    {
        var nextKey = target is null ? string.Empty : $"{target.TargetId}:{target.Name}:{target.IsBoss}";
        if (string.Equals(_lastPublishedTargetKey, nextKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastPublishedTargetKey = nextKey;
        TargetChanged?.Invoke(this, target);
    }

    private string BuildStatusMessage(string prefix)
    {
        var modeText = BossOnlyMode ? "보스만" : "전체";
        return $"{prefix}. {MeterWindowSeconds}초 집계 / {modeText}";
    }

    private static bool IsSummonName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return name.StartsWith("소환:", StringComparison.Ordinal)
            || name.StartsWith("소환 :", StringComparison.Ordinal)
            || name.Contains("환영분신", StringComparison.Ordinal)
            || name.Contains("환영 분신", StringComparison.Ordinal);
    }

    private static void TrimEvents(ActorRuntime actor, DateTime cutoff)
    {
        actor.Events.RemoveAll(hit => hit.Timestamp < cutoff);
        foreach (var skill in actor.Skills.Values.ToArray())
        {
            skill.Events.RemoveAll(hit => hit.Timestamp < cutoff);
            if (skill.Events.Count == 0)
            {
                actor.Skills.TryRemove(skill.SkillCode, out _);
            }
        }
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private sealed class ActorRuntime
    {
        public object SyncRoot { get; } = new();
        public string Name { get; set; } = string.Empty;
        public List<DamageEvent> Events { get; } = [];
        public ConcurrentDictionary<int, SkillRuntime> Skills { get; } = new();
    }

    private sealed class SkillRuntime
    {
        public SkillRuntime(int skillCode)
        {
            SkillCode = skillCode;
        }

        public int SkillCode { get; }
        public List<DamageEvent> Events { get; } = [];
    }

    private sealed class TargetRuntime
    {
        public string Name { get; set; } = string.Empty;
        public bool IsBoss { get; set; }
        public DateTime LastHitUtc { get; set; }
    }

    private sealed class PacketCaptureLogger : IPacketCaptureLogger, IDisposable
    {
        private readonly object _syncRoot = new();
        private readonly StreamWriter _writer;

        public PacketCaptureLogger()
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(directory);
            FilePath = Path.Combine(directory, $"party-capture-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            _writer = new StreamWriter(File.Open(FilePath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                AutoFlush = true
            };
        }

        public string FilePath { get; }

        public void LogPacket(int srcPort, int dstPort, uint seqNum, byte[] payload)
        {
            if (payload.Length == 0)
            {
                return;
            }

            var hex = Convert.ToHexString(payload);
            lock (_syncRoot)
            {
                _writer.WriteLine($"{DateTime.Now:O}|src={srcPort}|dst={dstPort}|seq={seqNum}|len={payload.Length}|hex={hex}");
            }
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                _writer.Dispose();
            }
        }
    }

    private readonly record struct DamageEvent(DateTime Timestamp, int Damage, bool IsCritical, int TargetId);
}
