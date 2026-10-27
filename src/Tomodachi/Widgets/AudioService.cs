using System.Runtime.InteropServices;
using Tomodachi.Core;
using Tomodachi.Interop;

namespace Tomodachi.Widgets;

internal sealed record AudioDevice(string Id, string Name);

internal sealed record AudioState(string DeviceId, string DeviceName, double Volume, bool Muted);

/// <summary>
/// Lautstärke, Stumm und Ausgabegerät des Standard-Wiedergabegeräts über Core Audio. Fehler (Gerät gerade abgesteckt,
/// Audiodienst neu gestartet) werden geloggt und als „kein Gerät“ gemeldet, nie weitergeworfen.
/// </summary>
internal sealed class AudioService
{
    private static readonly Guid NoContext = Guid.Empty;
    private IMMDeviceEnumerator? _enumerator;

    public AudioState? Poll()
    {
        try
        {
            var device = DefaultDevice();
            if (device is null) return null;
            var volume = EndpointVolume(device);
            volume.GetMasterVolumeLevelScalar(out var level);
            volume.GetMute(out var muted);
            device.GetId(out var id);
            return new AudioState(id, Name(device), Math.Round(level * 100), muted);
        }
        catch (Exception ex)
        {
            Log.Debug($"Audio nicht lesbar: {ex.Message}");
            _enumerator = null; // beim nächsten Mal neu verbinden
            return null;
        }
    }

    /// <summary>Alle aktiven Wiedergabegeräte (Lautsprecher, Headsets, Monitore mit Ton).</summary>
    public IReadOnlyList<AudioDevice> Devices()
    {
        try
        {
            if (Enumerator().EnumAudioEndpoints(EDataFlow.Render, CoreAudio.DEVICE_STATE_ACTIVE, out var collection) < 0) return [];
            collection.GetCount(out var count);
            var devices = new List<AudioDevice>();
            for (uint i = 0; i < count; i++)
            {
                if (collection.Item(i, out var device) < 0) continue;
                device.GetId(out var id);
                devices.Add(new AudioDevice(id, Name(device)));
            }
            return devices.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        catch (Exception ex)
        {
            Log.Debug($"Audiogeräte nicht lesbar: {ex.Message}");
            return [];
        }
    }

    /// <param name="percent">0 bis 100.</param>
    public void SetVolume(double percent) => WithVolume(v => v.SetMasterVolumeLevelScalar((float)Math.Clamp(percent / 100, 0, 1), NoContext));

    public void SetMuted(bool muted) => WithVolume(v => v.SetMute(muted, NoContext));

    /// <summary>Wie „Als Standardgerät festlegen“ in den Soundeinstellungen: für alle drei Rollen.</summary>
    public void SetDefault(string deviceId)
    {
        try
        {
            var policy = (IPolicyConfig)new PolicyConfigObject();
            foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
                Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(deviceId, role));
            Log.Info("Ausgabegerät gewechselt");
        }
        catch (Exception ex)
        {
            Log.Warn("Ausgabegerät nicht umschaltbar", ex);
        }
    }

    private void WithVolume(Func<IAudioEndpointVolume, int> action)
    {
        try
        {
            if (DefaultDevice() is { } device) Marshal.ThrowExceptionForHR(action(EndpointVolume(device)));
        }
        catch (Exception ex)
        {
            Log.Warn("Lautstärke nicht setzbar", ex);
        }
    }

    private IMMDeviceEnumerator Enumerator() => _enumerator ??= (IMMDeviceEnumerator)new MMDeviceEnumeratorObject();

    private IMMDevice? DefaultDevice() =>
        Enumerator().GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var device) >= 0 ? device : null;

    private static IAudioEndpointVolume EndpointVolume(IMMDevice device)
    {
        Marshal.ThrowExceptionForHR(device.Activate(CoreAudio.IID_IAudioEndpointVolume, CoreAudio.CLSCTX_ALL, 0, out var endpoint));
        return (IAudioEndpointVolume)endpoint;
    }

    private static string Name(IMMDevice device)
    {
        if (device.OpenPropertyStore(CoreAudio.STGM_READ, out var store) < 0) return "";
        if (store.GetValue(CoreAudio.FriendlyName, out var value) < 0) return "";
        try
        {
            return value.VarType == CoreAudio.VT_LPWSTR ? Marshal.PtrToStringUni(value.Pointer) ?? "" : "";
        }
        finally
        {
            CoreAudio.PropVariantClear(ref value);
        }
    }
}
