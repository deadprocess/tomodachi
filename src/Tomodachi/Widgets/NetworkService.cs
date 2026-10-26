using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Tomodachi.Widgets;

internal sealed record NetworkSnapshot(string Name, string Kind, long SpeedBitsPerSec, string? Address, double DownBytesPerSec, double UpBytesPerSec);

/// <summary>Paket 4.5: aktive Verbindung (die mit Standard-Gateway) und Up-/Down-Rate aus den Byte-Zählern.</summary>
internal sealed class NetworkService
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private string? _interfaceId;
    private long _lastReceived, _lastSent;
    private TimeSpan _lastTime;

    public NetworkSnapshot? Poll()
    {
        var nic = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
            .FirstOrDefault();
        if (nic is null) return null;

        var stats = nic.GetIPStatistics();
        var now = _clock.Elapsed;
        double down = 0, up = 0;
        if (nic.Id == _interfaceId)
        {
            var seconds = Math.Max(0.001, (now - _lastTime).TotalSeconds);
            down = Math.Max(0, (stats.BytesReceived - _lastReceived) / seconds);
            up = Math.Max(0, (stats.BytesSent - _lastSent) / seconds);
        }
        (_interfaceId, _lastReceived, _lastSent, _lastTime) = (nic.Id, stats.BytesReceived, stats.BytesSent, now);

        var address = nic.GetIPProperties().UnicastAddresses
            .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();
        var kind = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? Localization.Loc.Get("Net_WiFi") : "Ethernet";
        return new NetworkSnapshot(nic.Name, kind, nic.Speed, address, down, up);
    }
}
