using System.IO;
using System.Text.Json;
using Aion2Dashboard.Models;

namespace Aion2Dashboard.Services;

public sealed class CombatRecordStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _recordsDirectory;

    public CombatRecordStore()
    {
        _recordsDirectory = Path.Combine(AppContext.BaseDirectory, "records");
    }

    public IReadOnlyList<CombatRecord> LoadRecent(int maxCount = 50, int retentionSeconds = 1800)
    {
        if (!Directory.Exists(_recordsDirectory))
        {
            return Array.Empty<CombatRecord>();
        }

        TrimExpiredRecords(retentionSeconds);

        return Directory.GetFiles(_recordsDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(TryReadRecord)
            .Where(record => record is not null)
            .Select(record => record!)
            .Where(record => !IsExpired(record, retentionSeconds))
            .OrderByDescending(record => record.EndedAtUtc)
            .Take(maxCount)
            .ToArray();
    }

    public void Save(CombatRecord record, int maxCount = 50, int retentionSeconds = 1800)
    {
        Directory.CreateDirectory(_recordsDirectory);
        var safeTarget = string.Concat(record.TargetName.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)));
        if (string.IsNullOrWhiteSpace(safeTarget))
        {
            safeTarget = "combat";
        }

        DeleteExistingRecordsWithSameId(record.Id);
        DeleteOverlappingRecords(record);

        var fileName = $"{record.StartedAtUtc:yyyyMMdd-HHmmss}-{safeTarget}-{record.Id[..8]}.json";
        var path = Path.Combine(_recordsDirectory, fileName);
        var json = JsonSerializer.Serialize(record, JsonOptions);
        File.WriteAllText(path, json);
        TrimExpiredRecords(retentionSeconds);
        TrimOldRecords(maxCount);
    }

    private CombatRecord? TryReadRecord(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<CombatRecord>(json);
        }
        catch
        {
            return null;
        }
    }

    private void TrimOldRecords(int maxCount)
    {
        var files = Directory.GetFiles(_recordsDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToArray();

        foreach (var stale in files.Skip(maxCount))
        {
            try
            {
                stale.Delete();
            }
            catch
            {
            }
        }
    }

    private void TrimExpiredRecords(int retentionSeconds)
    {
        if (!Directory.Exists(_recordsDirectory))
        {
            return;
        }

        foreach (var path in Directory.GetFiles(_recordsDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            var record = TryReadRecord(path);
            if (record is null || !IsExpired(record, retentionSeconds))
            {
                continue;
            }

            try
            {
                File.Delete(path);
            }
            catch
            {
            }
        }
    }

    private void DeleteExistingRecordsWithSameId(string recordId)
    {
        if (!Directory.Exists(_recordsDirectory))
        {
            return;
        }

        foreach (var path in Directory.GetFiles(_recordsDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var record = TryReadRecord(path);
                if (record is null || !string.Equals(record.Id, recordId, StringComparison.Ordinal))
                {
                    continue;
                }

                File.Delete(path);
            }
            catch
            {
            }
        }
    }

    private void DeleteOverlappingRecords(CombatRecord incoming)
    {
        if (!Directory.Exists(_recordsDirectory))
        {
            return;
        }

        foreach (var path in Directory.GetFiles(_recordsDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var existing = TryReadRecord(path);
                if (existing is null || !ShouldMerge(existing, incoming))
                {
                    continue;
                }

                File.Delete(path);
            }
            catch
            {
            }
        }
    }

    private static bool ShouldMerge(CombatRecord existing, CombatRecord incoming)
    {
        if (string.Equals(existing.Id, incoming.Id, StringComparison.Ordinal))
        {
            return true;
        }

        if (!existing.IsBossTarget || !incoming.IsBossTarget)
        {
            return false;
        }

        var sameTargetId = existing.TargetId > 0 && incoming.TargetId > 0 && existing.TargetId == incoming.TargetId;
        var sameMobCode = existing.MobCode > 0 && incoming.MobCode > 0 && existing.MobCode == incoming.MobCode;
        var sameName = NormalizeName(existing.TargetName) == NormalizeName(incoming.TargetName);
        if (!sameTargetId && !sameMobCode && !sameName)
        {
            return false;
        }

        var startsClose = Math.Abs((existing.StartedAtUtc - incoming.StartedAtUtc).TotalSeconds) <= 10;
        var overlaps = existing.StartedAtUtc <= incoming.EndedAtUtc.AddSeconds(5)
            && incoming.StartedAtUtc <= existing.EndedAtUtc.AddSeconds(5);

        return startsClose || overlaps;
    }

    private static string NormalizeName(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }

    private static bool IsExpired(CombatRecord record, int retentionSeconds)
    {
        var normalized = Math.Clamp(retentionSeconds, 60, 1800);
        return (DateTime.UtcNow - record.EndedAtUtc).TotalSeconds > normalized;
    }
}
