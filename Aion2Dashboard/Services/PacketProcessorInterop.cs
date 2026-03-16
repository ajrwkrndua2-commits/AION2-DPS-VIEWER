using System.Buffers.Binary;
using System.Runtime.InteropServices;
using WindivertDotnet;

namespace Aion2Dashboard.Services;

internal interface IPacketCaptureLogger
{
    void LogPacket(int srcPort, int dstPort, uint seqNum, byte[] payload);
}

internal static class PacketProcessorNative
{
    private const string DllName = "PacketProcessor.dll";

    [StructLayout(LayoutKind.Sequential)]
    public struct Config
    {
        public int serverPort;
        public int tcpReorder;
        public int workerCount;
        public int maxBufferSize;
        public int maxReorderBytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Callbacks
    {
        public OnDamageDelegate onDamage;
        public OnMobSpawnDelegate onMobSpawn;
        public OnSummonDelegate onSummon;
        public OnUserInfoDelegate onUserInfo;
        public OnEntityRemovedDelegate onEntityRemoved;
        public OnLogDelegate onLog;
        public IntPtr getSkillName;
        public IntPtr containsSkillCode;
        public IntPtr isMobBoss;
        public IntPtr userdata;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnDamageDelegate(int actorId, int targetId, int skillCode, byte damageType, int damage, uint specialFlags, int multiHitCount, int multiHitDamage, int healAmount, int isDot, IntPtr userdata);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnMobSpawnDelegate(int mobId, int mobCode, int hp, int isBoss, IntPtr userdata);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnSummonDelegate(int actorId, int petId, IntPtr userdata);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnUserInfoDelegate(int entityId, IntPtr nickname, int serverId, int jobCode, IntPtr userdata);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnEntityRemovedDelegate(int entityId, IntPtr userdata);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnLogDelegate(int level, IntPtr message, IntPtr userdata);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr PacketProcessor_Create(ref Config cfg, ref Callbacks cbs);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void PacketProcessor_Destroy(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void PacketProcessor_Start(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void PacketProcessor_Stop(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void PacketProcessor_Reset(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int PacketProcessor_GetCombatPort(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void PacketProcessor_Enqueue(IntPtr handle, int srcPort, int dstPort, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)] byte[] data, int dataLen, [MarshalAs(UnmanagedType.LPStr)] string? deviceName, uint seqNum);
}

internal sealed class PacketProcessorBridge : IDisposable
{
    private readonly IntPtr _handle;
    private readonly PacketProcessorNative.OnDamageDelegate _onDamage;
    private readonly PacketProcessorNative.OnMobSpawnDelegate _onMobSpawn;
    private readonly PacketProcessorNative.OnSummonDelegate _onSummon;
    private readonly PacketProcessorNative.OnUserInfoDelegate _onUserInfo;
    private readonly PacketProcessorNative.OnEntityRemovedDelegate _onEntityRemoved;
    private readonly PacketProcessorNative.OnLogDelegate _onLog;

    public PacketProcessorBridge(int serverPort)
    {
        _onDamage = DamageCallback;
        _onMobSpawn = MobSpawnCallback;
        _onSummon = SummonCallback;
        _onUserInfo = UserInfoCallback;
        _onEntityRemoved = (_, _) => { };
        _onLog = LogCallback;

        var config = new PacketProcessorNative.Config
        {
            serverPort = serverPort,
            tcpReorder = 1,
            workerCount = 0,
            maxBufferSize = 0,
            maxReorderBytes = 0
        };

        var callbacks = new PacketProcessorNative.Callbacks
        {
            onDamage = _onDamage,
            onMobSpawn = _onMobSpawn,
            onSummon = _onSummon,
            onUserInfo = _onUserInfo,
            onEntityRemoved = _onEntityRemoved,
            onLog = _onLog,
            getSkillName = IntPtr.Zero,
            containsSkillCode = IntPtr.Zero,
            isMobBoss = IntPtr.Zero,
            userdata = IntPtr.Zero
        };

        _handle = PacketProcessorNative.PacketProcessor_Create(ref config, ref callbacks);
        if (_handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("PacketProcessor.dll 초기화에 실패했습니다.");
        }
    }

    public event Action<int, int, int, byte, int, uint, int, int, int, int>? DamageReceived;
    public event Action<int, int, int, bool>? MobSpawnReceived;
    public event Action<int, int>? SummonReceived;
    public event Action<int, string, int, int>? UserInfoReceived;
    public event Action<int, string>? LogReceived;

    public void Start() => PacketProcessorNative.PacketProcessor_Start(_handle);
    public void Stop() => PacketProcessorNative.PacketProcessor_Stop(_handle);
    public void Reset() => PacketProcessorNative.PacketProcessor_Reset(_handle);
    public int GetCombatPort() => PacketProcessorNative.PacketProcessor_GetCombatPort(_handle);

    public void Enqueue(int srcPort, int dstPort, byte[] payload, uint seqNum)
    {
        if (payload.Length == 0)
        {
            return;
        }

        PacketProcessorNative.PacketProcessor_Enqueue(_handle, srcPort, dstPort, payload, payload.Length, "WinDivert", seqNum);
    }

    private void DamageCallback(int actorId, int targetId, int skillCode, byte damageType, int damage, uint flags, int multiHitCount, int multiHitDamage, int healAmount, int isDot, IntPtr userdata)
        => DamageReceived?.Invoke(actorId, targetId, skillCode, damageType, damage, flags, multiHitCount, multiHitDamage, healAmount, isDot);

    private void MobSpawnCallback(int mobId, int mobCode, int hp, int isBoss, IntPtr userdata)
        => MobSpawnReceived?.Invoke(mobId, mobCode, hp, isBoss != 0);

    private void SummonCallback(int actorId, int petId, IntPtr userdata)
        => SummonReceived?.Invoke(actorId, petId);

    private void UserInfoCallback(int entityId, IntPtr nickname, int serverId, int jobCode, IntPtr userdata)
        => UserInfoReceived?.Invoke(entityId, Marshal.PtrToStringUTF8(nickname) ?? $"#{entityId}", serverId, jobCode);

    private void LogCallback(int level, IntPtr message, IntPtr userdata)
        => LogReceived?.Invoke(level, Marshal.PtrToStringUTF8(message) ?? string.Empty);

    public void Dispose()
    {
        Stop();
        PacketProcessorNative.PacketProcessor_Destroy(_handle);
    }
}

internal sealed class WfpCapturer : IDisposable
{
    private readonly PacketProcessorBridge _bridge;
    private readonly IPacketCaptureLogger? _packetLogger;
    private readonly CancellationTokenSource _cts = new();
    private WinDivert? _divert;
    private Task[]? _tasks;

    public WfpCapturer(PacketProcessorBridge bridge, IPacketCaptureLogger? packetLogger = null)
    {
        _bridge = bridge;
        _packetLogger = packetLogger;
    }

    public void Start()
    {
        _divert = new WinDivert("tcp", WinDivertLayer.Network, 0, WinDivertFlag.Sniff);
        _bridge.Start();
        _tasks = Enumerable.Range(0, Math.Max(2, Environment.ProcessorCount / 2))
            .Select(_ => Task.Run(() => CaptureLoopAsync(_cts.Token)))
            .ToArray();
    }

    private async Task CaptureLoopAsync(CancellationToken cancellationToken)
    {
        using var packet = new WinDivertPacket();
        using var address = new WinDivertAddress();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var length = await _divert!.RecvAsync(packet, address, cancellationToken);
                if (length <= 0)
                {
                    continue;
                }

                var parsed = ParseIpTcpPacket(packet.Span[..length]);
                if (parsed is not null)
                {
                    _packetLogger?.LogPacket(parsed.Value.SrcPort, parsed.Value.DstPort, parsed.Value.SeqNum, parsed.Value.Payload);
                    _bridge.Enqueue(parsed.Value.SrcPort, parsed.Value.DstPort, parsed.Value.Payload, parsed.Value.SeqNum);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch
            {
            }
        }
    }

    private static (int SrcPort, int DstPort, byte[] Payload, uint SeqNum)? ParseIpTcpPacket(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < 40 || (raw[0] >> 4) != 4)
        {
            return null;
        }

        var ipHeaderLength = (raw[0] & 0x0F) * 4;
        if (ipHeaderLength < 20 || ipHeaderLength > raw.Length || raw[9] != 6)
        {
            return null;
        }

        var ipTotalLength = BinaryPrimitives.ReadUInt16BigEndian(raw[2..4]);
        if (ipTotalLength > raw.Length)
        {
            ipTotalLength = (ushort)raw.Length;
        }

        var tcp = raw[ipHeaderLength..];
        if (tcp.Length < 20)
        {
            return null;
        }

        var srcPort = BinaryPrimitives.ReadUInt16BigEndian(tcp[0..2]);
        var dstPort = BinaryPrimitives.ReadUInt16BigEndian(tcp[2..4]);
        var seqNum = BinaryPrimitives.ReadUInt32BigEndian(tcp[4..8]);
        var tcpHeaderLength = ((tcp[12] >> 4) & 0x0F) * 4;
        if (tcpHeaderLength < 20 || tcpHeaderLength > tcp.Length)
        {
            return null;
        }

        var payloadLength = ipTotalLength - ipHeaderLength - tcpHeaderLength;
        if (payloadLength <= 0)
        {
            return null;
        }

        var payloadOffset = ipHeaderLength + tcpHeaderLength;
        if (payloadOffset + payloadLength > raw.Length)
        {
            return null;
        }

        return (srcPort, dstPort, raw[payloadOffset..(payloadOffset + payloadLength)].ToArray(), seqNum);
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (_tasks is not null)
        {
            try
            {
                Task.WaitAll(_tasks, TimeSpan.FromSeconds(2));
            }
            catch
            {
            }
        }

        _divert?.Dispose();
        _cts.Dispose();
    }
}
