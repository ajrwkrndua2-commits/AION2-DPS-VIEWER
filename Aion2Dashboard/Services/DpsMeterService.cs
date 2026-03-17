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
    private int? _activeBossTargetId;
    private DateTime _activeBossLastHitUtc = DateTime.MinValue;
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
        set => _meterWindowSeconds = Math.Clamp(value, 1, 300);
    }

    public int CombatResetSeconds
    {
        get => _combatResetSeconds;
        set => _combatResetSeconds = Math.Clamp(value, 5, 180);
    }

    public int ActiveDisplaySeconds
    {
        get => _activeDisplaySeconds;
        set => _activeDisplaySeconds = Math.Clamp(value, 1, 30);
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
            throw new InvalidOperationException("관리자 권한으로 실행해야 DPS 패킷 수집이 동작합니다.");
        }

        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "PacketProcessor.dll")))
        {
            throw new FileNotFoundException("PacketProcessor.dll을 찾을 수 없습니다.");
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
            throw new InvalidOperationException($"WinDivert 시작 실패: {ex.Message}", ex);
        }

        IsRunning = true;
        _snapshotTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(1));

        var prefix = EnablePartyPacketLogging && !string.IsNullOrWhiteSpace(PartyLogFilePath)
            ? $"DPS 자동 수집 시작 / 파티 로그 저장: {PartyLogFilePath}"
            : "DPS 자동 수집 시작";
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
        StatusChanged?.Invoke(this, "DPS 수집을 중지했습니다.");
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
        _bossTargets.Clear();
        _activeBossTargetId = null;
        _activeBossLastHitUtc = DateTime.MinValue;
        _bridge?.Reset();
        _lastDamageUtc = DateTime.MinValue;
        PublishTarget(null);
        PublishSnapshot();
        StatusChanged?.Invoke(this, BuildStatusMessage("DPS 데이터를 초기화했습니다."));
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
                    TotalDamage = item.Events.Sum(hit => (long)hit.Damage),
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

    public CombatRecord? CaptureCombatRecord(string savedReason)
    {
        var participants = _actors
            .Where(pair => !_summonOwners.ContainsKey(pair.Key))
            .Select(pair => BuildParticipantRecord(pair.Key, pair.Value))
            .Where(item => item is not null)
            .Select(item => item!)
            .OrderByDescending(item => item.TotalDamage)
            .ToArray();

        if (participants.Length == 0)
        {
            return null;
        }

        var allEvents = _actors
            .Where(pair => !_summonOwners.ContainsKey(pair.Key))
            .SelectMany(pair =>
            {
                lock (pair.Value.SyncRoot)
                {
                    return pair.Value.Events.Where(hit => MatchesFilter(hit.TargetId)).ToArray();
                }
            })
            .OrderBy(hit => hit.Timestamp)
            .ToArray();

        if (allEvents.Length == 0)
        {
            return null;
        }

        var totalDamage = participants.Sum(item => item.TotalDamage);
        var target = SelectRecordTarget(allEvents);

        return new CombatRecord
        {
            StartedAtUtc = allEvents[0].Timestamp,
            EndedAtUtc = allEvents[^1].Timestamp,
            TargetName = target?.Name ?? "타겟 미상",
            IsBossTarget = target?.IsBoss == true,
            BossOnlyMode = BossOnlyMode,
            TotalDamage = totalDamage,
            DurationSeconds = Math.Max(1.0, (allEvents[^1].Timestamp - allEvents[0].Timestamp).TotalSeconds),
            SavedReason = savedReason,
            Participants = participants
        };
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

        RegisterTargetHit(targetId, now, total);
        TrackBossEncounter(targetId, now);

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
        target.LastHitUtc = DateTime.UtcNow;
        target.MobCode = mobCode;

        var mobInfo = _mobNameResolver.ResolveInfo(mobCode);
        target.IsBoss = isBoss || mobInfo?.IsBoss == true;
        if (hp > 0)
        {
            var inferredMaxHp = hp + Math.Max(0L, target.DamageTaken);
            target.MaxHp = Math.Max(target.MaxHp, Math.Max(hp, inferredMaxHp));
            target.CurrentHp = hp;
        }

        if (!string.IsNullOrWhiteSpace(mobInfo?.Name))
        {
            target.Name = mobInfo.Name;
        }

        if (target.IsBoss)
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
        RefreshBossEncounterState(DateTime.UtcNow);
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

                    var totalDamage = events.Sum(hit => (long)hit.Damage);
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
        ResetSuggested?.Invoke(this, "긴 공백 이후 새 전투가 감지되어 전체 리셋을 진행했습니다.");
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
        if (!BossOnlyMode)
        {
            return true;
        }

        RefreshBossEncounterState(DateTime.UtcNow);

        if (!_activeBossTargetId.HasValue)
        {
            var fallbackBossTargetId = SelectRecentBossTargetId();
            if (fallbackBossTargetId.HasValue)
            {
                _activeBossTargetId = fallbackBossTargetId.Value;
                _activeBossLastHitUtc = _targets.TryGetValue(fallbackBossTargetId.Value, out var fallbackTarget)
                    ? fallbackTarget.LastHitUtc
                    : DateTime.UtcNow;
            }
        }

        if (_activeBossTargetId.HasValue)
        {
            return targetId == _activeBossTargetId.Value;
        }

        return _targets.TryGetValue(targetId, out var target) && target.IsBoss;
    }

    private void RegisterTargetHit(int targetId, DateTime now, int damage)
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

        if (damage <= 0)
        {
            return;
        }

        target.DamageTaken += damage;

        if (target.MaxHp > 0)
        {
            target.CurrentHp = Math.Max(0, target.MaxHp - target.DamageTaken);
        }
        else if (target.CurrentHp > 0)
        {
            target.CurrentHp = Math.Max(0, target.CurrentHp - damage);
        }
    }

    private void TrackBossEncounter(int targetId, DateTime now)
    {
        if (!_targets.TryGetValue(targetId, out var target) || !target.IsBoss)
        {
            return;
        }

        if (_activeBossTargetId == targetId)
        {
            _activeBossLastHitUtc = now;
            return;
        }

        if (!_activeBossTargetId.HasValue)
        {
            _activeBossTargetId = targetId;
            _activeBossLastHitUtc = now;
            return;
        }

        var switchThresholdSeconds = Math.Max(3, Math.Min(15, CombatResetSeconds / 2));
        if ((now - _activeBossLastHitUtc).TotalSeconds >= switchThresholdSeconds)
        {
            _activeBossTargetId = targetId;
            _activeBossLastHitUtc = now;
        }
    }

    private void RefreshBossEncounterState(DateTime now)
    {
        if (!_activeBossTargetId.HasValue)
        {
            return;
        }

        if ((now - _activeBossLastHitUtc).TotalSeconds <= CombatResetSeconds)
        {
            return;
        }

        _activeBossTargetId = null;
        _activeBossLastHitUtc = DateTime.MinValue;
    }

    private int? SelectRecentBossTargetId()
    {
        var cutoff = DateTime.UtcNow - TimeSpan.FromSeconds(Math.Max(6, CombatResetSeconds));
        return _targets
            .Where(pair => pair.Value.IsBoss && pair.Value.LastHitUtc >= cutoff)
            .OrderByDescending(pair => pair.Value.LastHitUtc)
            .Select(pair => (int?)pair.Key)
            .FirstOrDefault();
    }

    private TargetInfo? SelectCurrentTarget()
    {
        RefreshBossEncounterState(DateTime.UtcNow);

        if (_activeBossTargetId.HasValue && _targets.TryGetValue(_activeBossTargetId.Value, out var activeBoss) && !string.IsNullOrWhiteSpace(activeBoss.Name))
        {
            return new TargetInfo
            {
                TargetId = _activeBossTargetId.Value,
                Name = activeBoss.Name,
                IsBoss = true,
                CurrentHp = activeBoss.CurrentHp,
                MaxHp = activeBoss.MaxHp,
                DamageTaken = activeBoss.DamageTaken
            };
        }

        var cutoff = DateTime.UtcNow - TargetRetention;
        return _targets
            .Where(pair => pair.Value.LastHitUtc >= cutoff && !string.IsNullOrWhiteSpace(pair.Value.Name))
            .Select(pair => new TargetInfo
            {
                TargetId = pair.Key,
                Name = pair.Value.Name,
                IsBoss = pair.Value.IsBoss,
                CurrentHp = pair.Value.CurrentHp,
                MaxHp = pair.Value.MaxHp,
                DamageTaken = pair.Value.DamageTaken
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

    private CombatRecordParticipant? BuildParticipantRecord(int actorId, ActorRuntime actor)
    {
        lock (actor.SyncRoot)
        {
            var events = actor.Events.Where(hit => MatchesFilter(hit.TargetId)).OrderBy(hit => hit.Timestamp).ToArray();
            if (events.Length == 0)
            {
                return null;
            }

            var totalDamage = events.Sum(hit => (long)hit.Damage);
            var critCount = events.Count(hit => hit.IsCritical);
            var duration = Math.Max(1.0, (events[^1].Timestamp - events[0].Timestamp).TotalSeconds);
            var maxDamage = Math.Max(1L, actor.Skills.Values
                .Select(skill => (long)skill.Events.Where(hit => MatchesFilter(hit.TargetId)).Sum(hit => hit.Damage))
                .DefaultIfEmpty(1L)
                .Max());

            var skills = actor.Skills.Values
                .Select(skill => new
                {
                    skill.SkillCode,
                    Events = skill.Events.Where(hit => MatchesFilter(hit.TargetId)).ToArray()
                })
                .Where(item => item.Events.Length > 0)
                .Select(item => new SkillUsageEntry
                {
                    SkillCode = item.SkillCode,
                    SkillName = _skillNameResolver.Resolve(item.SkillCode),
                    TotalDamage = item.Events.Sum(hit => (long)hit.Damage),
                    HitCount = item.Events.Length,
                    CritRate = item.Events.Length == 0 ? 0 : (double)item.Events.Count(hit => hit.IsCritical) / item.Events.Length,
                    MaxHit = item.Events.Max(hit => hit.Damage),
                    DamageShare = totalDamage == 0 ? 0 : (double)item.Events.Sum(hit => (long)hit.Damage) / totalDamage,
                    DamageRatio = maxDamage == 0 ? 0 : (double)item.Events.Sum(hit => (long)hit.Damage) / maxDamage
                })
                .OrderByDescending(item => item.TotalDamage)
                .ToArray();

            return new CombatRecordParticipant
            {
                ActorId = actorId,
                Name = string.IsNullOrWhiteSpace(actor.Name) ? $"#{actorId}" : actor.Name,
                TotalDamage = totalDamage,
                Dps = (long)(totalDamage / duration),
                HitCount = events.Length,
                CritRate = events.Length == 0 ? 0 : (double)critCount / events.Length,
                IsPartyCandidate = _partyCandidateActorIds.Contains(actorId),
                SkillUsages = skills
            };
        }
    }

    private TargetInfo? SelectLatestKnownTarget()
    {
        if (_activeBossTargetId.HasValue && _targets.TryGetValue(_activeBossTargetId.Value, out var activeBoss) && !string.IsNullOrWhiteSpace(activeBoss.Name))
        {
            return new TargetInfo
            {
                TargetId = _activeBossTargetId.Value,
                Name = activeBoss.Name,
                IsBoss = true,
                CurrentHp = activeBoss.CurrentHp,
                MaxHp = activeBoss.MaxHp,
                DamageTaken = activeBoss.DamageTaken
            };
        }

        return _targets
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value.Name))
            .Select(pair => new TargetInfo
            {
                TargetId = pair.Key,
                Name = pair.Value.Name,
                IsBoss = pair.Value.IsBoss,
                CurrentHp = pair.Value.CurrentHp,
                MaxHp = pair.Value.MaxHp,
                DamageTaken = pair.Value.DamageTaken
            })
            .OrderByDescending(item => _targets[item.TargetId].LastHitUtc)
            .FirstOrDefault();
    }

    private TargetInfo? SelectRecordTarget(IReadOnlyList<DamageEvent> allEvents)
    {
        if (allEvents.Count == 0)
        {
            return null;
        }

        var bestTarget = allEvents
            .GroupBy(hit => hit.TargetId)
            .Select(group => new
            {
                TargetId = group.Key,
                TotalDamage = group.Sum(hit => (long)hit.Damage),
                LastHitUtc = group.Max(hit => hit.Timestamp)
            })
            .OrderByDescending(item => item.TotalDamage)
            .ThenByDescending(item => item.LastHitUtc)
            .FirstOrDefault();

        if (bestTarget is null)
        {
            return SelectLatestKnownTarget();
        }

        if (_targets.TryGetValue(bestTarget.TargetId, out var target) && !string.IsNullOrWhiteSpace(target.Name))
        {
            return new TargetInfo
            {
                TargetId = bestTarget.TargetId,
                Name = target.Name,
                IsBoss = target.IsBoss,
                CurrentHp = target.CurrentHp,
                MaxHp = target.MaxHp,
                DamageTaken = target.DamageTaken
            };
        }

        return SelectLatestKnownTarget();
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
        public int MobCode { get; set; }
        public long CurrentHp { get; set; }
        public long MaxHp { get; set; }
        public long DamageTaken { get; set; }
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
