using Tomodachi.Core;
using Windows.Devices.Radios;
using Windows.Devices.WiFi;

namespace Tomodachi.Widgets;

/// <summary>Null = Hardware fehlt (z. B. Desktop-PC ohne WLAN), dann bleibt die Kachel unsichtbar.</summary>
internal sealed record RadioState(bool? WifiOn, bool? BluetoothOn);

internal sealed record WifiNetwork(string Ssid, int Bars, bool Secured, bool Connected);

/// <summary>
/// WLAN und Bluetooth an/aus (Windows.Devices.Radios) und die WLAN-Netze in der Nähe (Windows.Devices.WiFi).
/// <para>
/// <b>Ungetestet:</b> Der Entwicklungs-PC hat weder WLAN noch Bluetooth (27.10.2026). Seit Windows 11 24H2 liefert der
/// WLAN-Scan Netznamen nur, wenn Desktop-Apps auf den Standort zugreifen dürfen; sonst bleibt die Liste leer.
/// </para>
/// </summary>
internal sealed class RadioService
{
    private IReadOnlyList<Radio>? _radios;
    private WiFiAdapter? _wifi;
    private bool _wifiChecked;

    public async Task<RadioState> PollAsync()
    {
        try
        {
            if (_radios is null)
            {
                await Radio.RequestAccessAsync();
                _radios = await Radio.GetRadiosAsync();
            }
            return new RadioState(IsOn(RadioKind.WiFi), IsOn(RadioKind.Bluetooth));
        }
        catch (Exception ex)
        {
            Log.Debug($"Funkgeräte nicht lesbar: {ex.Message}");
            return new RadioState(null, null);
        }
    }

    public async Task SetAsync(bool wifi, bool on)
    {
        var radio = _radios?.FirstOrDefault(r => r.Kind == (wifi ? RadioKind.WiFi : RadioKind.Bluetooth));
        if (radio is null) return;
        try
        {
            var result = await radio.SetStateAsync(on ? Windows.Devices.Radios.RadioState.On : Windows.Devices.Radios.RadioState.Off);
            Log.Info($"{(wifi ? "WLAN" : "Bluetooth")} {(on ? "an" : "aus")}: {result}");
        }
        catch (Exception ex)
        {
            Log.Warn($"{(wifi ? "WLAN" : "Bluetooth")} nicht umschaltbar", ex);
        }
    }

    /// <summary>Netze in der Nähe, stärkstes Signal zuerst, jedes nur einmal (mehrere Access Points = ein Netz).</summary>
    public async Task<IReadOnlyList<WifiNetwork>> ScanAsync()
    {
        try
        {
            var adapter = await WifiAdapterAsync();
            if (adapter is null) return [];
            await adapter.ScanAsync();

            var connected = "";
            var profile = await adapter.NetworkAdapter.GetConnectedProfileAsync();
            if (profile?.IsWlanConnectionProfile == true) connected = profile.WlanConnectionProfileDetails.GetConnectedSsid() ?? "";

            return adapter.NetworkReport.AvailableNetworks
                .Where(n => !string.IsNullOrWhiteSpace(n.Ssid))
                .GroupBy(n => n.Ssid)
                .Select(g => g.OrderByDescending(n => n.SignalBars).First())
                .Select(n => new WifiNetwork(n.Ssid, n.SignalBars,
                    n.SecuritySettings.NetworkAuthenticationType is not (Windows.Networking.Connectivity.NetworkAuthenticationType.Open80211
                        or Windows.Networking.Connectivity.NetworkAuthenticationType.None),
                    n.Ssid == connected))
                .OrderByDescending(n => n.Connected).ThenByDescending(n => n.Bars).ThenBy(n => n.Ssid)
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Debug($"WLAN-Scan fehlgeschlagen: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// Mit gespeichertem Profil oder offenem Netz verbinden. Braucht das Netz ein Passwort, das Windows noch nicht kennt,
    /// klappt das nicht (der Widgets-Reiter hat keine Tastatur): false, dann öffnet der Aufrufer die WLAN-Einstellungen.
    /// </summary>
    public async Task<bool> ConnectAsync(string ssid)
    {
        try
        {
            var adapter = await WifiAdapterAsync();
            var network = adapter?.NetworkReport.AvailableNetworks.Where(n => n.Ssid == ssid).MaxBy(n => n.SignalBars);
            if (adapter is null || network is null) return false;
            var result = await adapter.ConnectAsync(network, WiFiReconnectionKind.Automatic);
            Log.Info($"WLAN verbinden: {result.ConnectionStatus}");
            return result.ConnectionStatus == WiFiConnectionStatus.Success;
        }
        catch (Exception ex)
        {
            Log.Warn("WLAN-Verbindung fehlgeschlagen", ex);
            return false;
        }
    }

    private bool? IsOn(RadioKind kind)
    {
        var radio = _radios?.FirstOrDefault(r => r.Kind == kind);
        return radio is null ? null : radio.State == Windows.Devices.Radios.RadioState.On;
    }

    private async Task<WiFiAdapter?> WifiAdapterAsync()
    {
        if (_wifiChecked) return _wifi;
        _wifiChecked = true;
        if (await WiFiAdapter.RequestAccessAsync() != WiFiAccessStatus.Allowed) return null;
        _wifi = (await WiFiAdapter.FindAllAdaptersAsync()).FirstOrDefault();
        return _wifi;
    }
}
