using LibreHardwareMonitor.Hardware;
using Tomodachi.Core;

namespace Tomodachi.Widgets;

internal sealed record DiskSnapshot(string Name, double? Temperature, double? UsedPercent, double? ReadBytesPerSec, double? WriteBytesPerSec);

internal sealed record SystemSnapshot(
    string? CpuName,
    double? CpuLoad,
    double? CpuTemperature,
    string? GpuName,
    double? GpuLoad,
    double? GpuTemperature,
    double? VramUsedGb,
    double? VramTotalGb,
    double? RamUsedGb,
    double? RamTotalGb,
    IReadOnlyList<DiskSnapshot> Disks);

/// <summary>
/// Paket 4.2: Hardware-Sensoren über LibreHardwareMonitor. Öffnet einmal im Hintergrund und liest danach auf Abruf
/// (nur solange das Widget-Center offen ist). LibreHardwareMonitor ist nicht threadsicher, deshalb läuft alles seriell.
/// CPU-Temperaturen gibt es nur mit installiertem PawnIO-Treiber. Ohne bleiben sie leer, und es wird kein Treiber geladen.
/// </summary>
internal sealed class SensorService : IDisposable
{
    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMemoryEnabled = true,
        IsStorageEnabled = true,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Task _opening;
    private SystemSnapshot? _last;

    public SensorService()
    {
        _opening = Task.Run(() =>
        {
            try
            {
                _computer.Open();
                Log.Info($"Sensoren bereit: {string.Join(", ", _computer.Hardware.Select(h => $"{h.HardwareType} {h.Name}"))}");
            }
            catch (Exception ex)
            {
                Log.Error("Sensoren konnten nicht geöffnet werden", ex);
            }
        });
    }

    /// <summary>Liest alle Sensoren neu. Läuft schon ein Durchgang, kommt der letzte Stand zurück.</summary>
    public async Task<SystemSnapshot?> PollAsync()
    {
        if (!_opening.IsCompleted || !await _gate.WaitAsync(0)) return _last;
        try
        {
            _last = await Task.Run(Read);
            return _last;
        }
        catch (Exception ex)
        {
            Log.Warn("Sensoren nicht lesbar", ex);
            return _last;
        }
        finally
        {
            _gate.Release();
        }
    }

    private SystemSnapshot Read()
    {
        IHardware? cpu = null, gpu = null, memory = null;
        var disks = new List<DiskSnapshot>();
        var diskNames = new Dictionary<string, int>();

        foreach (var hardware in _computer.Hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware) sub.Update();

            switch (hardware.HardwareType)
            {
                case HardwareType.Cpu:
                    cpu ??= hardware;
                    break;
                case HardwareType.GpuAmd or HardwareType.GpuNvidia or HardwareType.GpuIntel:
                    // Dedizierte GPU vor integrierter
                    if (gpu is null || gpu.HardwareType == HardwareType.GpuIntel) gpu = hardware;
                    break;
                case HardwareType.Memory:
                    // Es gibt „Virtual Memory“ (inkl. Auslagerungsdatei) und „Total Memory“ (physisch): physisch gewinnt
                    if (memory is null || memory.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase)) memory = hardware;
                    break;
                case HardwareType.Storage:
                    disks.Add(ReadDisk(hardware, diskNames));
                    break;
            }
        }

        // Exakt vergleichen: „Memory Available“ steckt auch in „Virtual Memory Available“
        var ramUsed = Value(memory, SensorType.Data, "Memory Used", exact: true);
        var ramFree = Value(memory, SensorType.Data, "Memory Available", exact: true);
        var vramUsedMb = Value(gpu, SensorType.SmallData, "GPU Memory Used") ?? Value(gpu, SensorType.SmallData, "D3D Dedicated Memory Used");
        var vramTotalMb = Value(gpu, SensorType.SmallData, "GPU Memory Total") ?? Value(gpu, SensorType.SmallData, "D3D Dedicated Memory Total");

        return new SystemSnapshot(
            cpu?.Name,
            Value(cpu, SensorType.Load, "CPU Total"),
            Value(cpu, SensorType.Temperature, "Tctl/Tdie") ?? Value(cpu, SensorType.Temperature, "Core (Tctl/Tdie)") ?? Value(cpu, SensorType.Temperature, "Package"),
            gpu?.Name,
            Value(gpu, SensorType.Load, "GPU Core"),
            Value(gpu, SensorType.Temperature, "GPU Core") ?? Value(gpu, SensorType.Temperature, null),
            vramUsedMb / 1024,
            vramTotalMb / 1024,
            ramUsed,
            ramUsed + ramFree,
            disks);
    }

    /// <summary>Gleiche Modelle (zwei 970 EVO Plus) bekommen eine Nummer, damit man sie unterscheiden kann.</summary>
    private static DiskSnapshot ReadDisk(IHardware disk, Dictionary<string, int> names)
    {
        var count = names.GetValueOrDefault(disk.Name) + 1;
        names[disk.Name] = count;
        var name = count > 1 ? $"{disk.Name} #{count}" : disk.Name;

        return new DiskSnapshot(
            name,
            Value(disk, SensorType.Temperature, null),
            Value(disk, SensorType.Load, "Used Space"),
            Value(disk, SensorType.Throughput, "Read Rate"),
            Value(disk, SensorType.Throughput, "Write Rate"));
    }

    /// <summary>
    /// Erster Sensor dieses Typs, dessen Name <paramref name="name"/> enthält (null = egal welcher).
    /// Temperaturen ≤ 0 zählen als „nicht vorhanden“: Ohne PawnIO-Treiber meldet die CPU 0 °C statt nichts.
    /// </summary>
    private static double? Value(IHardware? hardware, SensorType type, string? name, bool exact = false)
    {
        if (hardware is null) return null;
        var sensor = hardware.Sensors.Concat(hardware.SubHardware.SelectMany(s => s.Sensors))
            .FirstOrDefault(s => s.SensorType == type && s.Value.HasValue
                && (type != SensorType.Temperature || s.Value > 0)
                && (name is null || (exact
                    ? s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                    : s.Name.Contains(name, StringComparison.OrdinalIgnoreCase))));
        return sensor?.Value;
    }

    public void Dispose()
    {
        try
        {
            _opening.Wait(TimeSpan.FromSeconds(2));
            _computer.Close();
        }
        catch (Exception ex)
        {
            Log.Debug($"Sensoren schließen: {ex.Message}");
        }
    }
}
