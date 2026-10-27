using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;

namespace Tomodachi.Core;

/// <summary>
/// Paket 1.4: eine Aufgabe in der Aufgabenplanung, die tomodachi bei der Anmeldung mit höchsten Rechten startet.
/// Über <c>schtasks /Run</c> kann auch ein normaler Start ohne UAC-Abfrage auf die elevated Variante umsteigen.
/// Elevated sieht der Tastatur-Hook auch Tasten, während ein Admin-Fenster den Fokus hat (UIPI).
/// Die Aufgabe startet die Exe über cmd.exe, das zuvor die <see cref="RuntimeInjectionVars"/> leert (siehe <see cref="StarterArguments"/>).
/// </summary>
internal static class Autostart
{
    private const string TaskName = "tomodachi";
    public const string FromTaskArgument = "--from-task";
    private const int ErrorCancelled = 1223;

    /// <summary>
    /// Umgebungsvariablen, über die die .NET-Runtime beim Start fremden Code (Profiler, zusätzliche Assemblies) in den
    /// Prozess laden würde, noch bevor tomodachis eigener Code läuft. Die Aufgabe erbt <c>HKCU\Environment</c>, das jedes
    /// Programm des Users setzen darf; sie würden also sonst mit Adminrechten geladen. Deshalb leert der cmd-Starter sie,
    /// bevor er die (elevated) Exe startet. StartupHookSupport ist schon per csproj aus, hier zur Sicherheit noch einmal.
    /// </summary>
    private static readonly string[] RuntimeInjectionVars =
    [
        "CORECLR_ENABLE_PROFILING", "CORECLR_PROFILER", "CORECLR_PROFILER_PATH",
        "CORECLR_PROFILER_PATH_32", "CORECLR_PROFILER_PATH_64",
        "COR_ENABLE_PROFILING", "COR_PROFILER", "COR_PROFILER_PATH",
        "DOTNET_STARTUP_HOOKS", "DOTNET_ADDITIONAL_DEPS", "DOTNET_SHARED_STORE",
    ];

    static Autostart() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Für <c>--install</c> / <c>--uninstall</c>. Holt sich bei Bedarf selbst Adminrechte.</summary>
    public static int RunCli(bool install)
    {
        if (!Elevation.IsElevated) return Elevation.RelaunchElevated(install ? "--install" : "--uninstall");
        if (install && !ConfirmFolder()) return ErrorCancelled;

        var (exitCode, output) = install ? Install() : Schtasks("/Delete", "/TN", TaskName, "/F");
        var action = install ? "Autostart mit Adminrechten eingerichtet" : "Autostart entfernt";
        if (exitCode == 0)
        {
            Log.Info($"{action}: {output}");
            System.Windows.MessageBox.Show(Localization.Loc.Get(install ? "Autostart_Installed" : "Autostart_Removed"), "tomodachi");
        }
        else
        {
            Log.Error($"schtasks fehlgeschlagen ({exitCode}): {output}");
            System.Windows.MessageBox.Show($"{Localization.Loc.Format("Autostart_Failed", exitCode)}\n{output}", "tomodachi");
        }
        return exitCode;
    }

    /// <summary>Liegt die Exe in einem Ordner, den auch Programme ohne Adminrechte ändern dürfen, erst nachfragen.</summary>
    private static bool ConfirmFolder()
    {
        var problems = WritableByNonAdmins();
        if (problems.Count == 0) return true;

        // Under Program Files the parent folders are admin-only and the files carry stale ACLs (typically from a move).
        // We are already elevated here, so offer to reset them instead of making the user type icacls commands.
        var folder = Path.GetDirectoryName(Environment.ProcessPath!)!;
        var underProgramFiles = InstallFolder.IsUnderProgramFiles(folder);
        if (underProgramFiles)
        {
            var reset = System.Windows.MessageBox.Show(
                Localization.Loc.Format("Autostart_UnsafeFolder_ResetAsk", DescribeProblems(problems, MoreProblems)), "tomodachi",
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.Yes);
            if (reset != System.Windows.MessageBoxResult.Yes)
            {
                Log.Warn("Autostart in beschreibbarem Ordner abgebrochen");
                return false;
            }

            // icacls /C reports exit code 0 even when every file failed, so only the re-check decides
            var (exitCode, output) = ResetPermissions(folder);
            problems = WritableByNonAdmins();
            if (problems.Count == 0)
            {
                Log.Info($"Rechte von {folder} zurückgesetzt");
                return true;
            }
            Log.Error($"Rechte zurücksetzen fehlgeschlagen ({exitCode}): {output}");
            System.Windows.MessageBox.Show(Localization.Loc.Format("Autostart_ResetFailed", FirstLines(output, 10)), "tomodachi",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }

        // Fallback: explain and let the user decide (copy to Program Files, or the manual icacls commands)
        var advice = underProgramFiles
            ? Localization.Loc.Format("Autostart_UnsafeFolder_Reset", folder)
            : Localization.Loc.Get("Autostart_UnsafeFolder_Move");
        var answer = System.Windows.MessageBox.Show(
            Localization.Loc.Format("Autostart_UnsafeFolder", DescribeProblems(problems, MoreProblems), advice), "tomodachi",
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.No);
        var confirmed = answer == System.Windows.MessageBoxResult.Yes;
        Log.Warn($"Autostart in beschreibbarem Ordner {(confirmed ? "trotzdem eingerichtet" : "abgebrochen")}");
        return confirmed;
    }

    /// <summary>
    /// Für <see cref="ConfirmFolder"/> und den Start über die Aufgabe (Log-Warnung). Ist die Prüfung selbst nicht möglich,
    /// wird nur geloggt: Daran soll der Autostart nicht scheitern.
    /// </summary>
    public static IReadOnlyList<string> WritableByNonAdmins()
    {
        try
        {
            var problems = InstallFolder.FindWritable(Environment.ProcessPath!);
            foreach (var problem in problems) Log.Warn($"Für Nicht-Admins änderbar: {problem}");
            return problems;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Warn("Rechte des Installationsordners nicht prüfbar", ex);
            return [];
        }
    }

    /// <summary>True, wenn die Aufgabe existiert und auf genau diese Exe zeigt.</summary>
    public static bool IsInstalledForThisExe()
    {
        var (exitCode, output) = Schtasks("/Query", "/TN", TaskName, "/XML");
        if (exitCode != 0) return false;

        try
        {
            var exec = XDocument.Parse(output).Descendants().FirstOrDefault(e => e.Name.LocalName == "Exec");
            var command = exec?.Elements().FirstOrDefault(e => e.Name.LocalName == "Command")?.Value ?? "";
            var arguments = exec?.Elements().FirstOrDefault(e => e.Name.LocalName == "Arguments")?.Value ?? "";
            var exe = Environment.ProcessPath;
            // Alt: Command ist direkt die Exe. Neu: Command ist cmd.exe, die Exe steht in Quotes in den Argumenten.
            return string.Equals(command.Trim('"'), exe, StringComparison.OrdinalIgnoreCase)
                || arguments.Contains($"\"{exe}\"", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Log.Warn("Aufgaben-XML unlesbar", ex);
            return false;
        }
    }

    public static bool RunTask()
    {
        var (exitCode, output) = Schtasks("/Run", "/TN", TaskName);
        if (exitCode != 0) Log.Warn($"schtasks /Run fehlgeschlagen ({exitCode}): {output}");
        return exitCode == 0;
    }

    private static (int ExitCode, string Output) Install()
    {
        var exe = Environment.ProcessPath!;
        var user = WindowsIdentity.GetCurrent().Name;

        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var arguments = StarterArguments(exe);
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>tomodachi Shell: Start bei Anmeldung mit höchsten Rechten</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{SecurityElement.Escape(user)}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{SecurityElement.Escape(user)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>4</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{SecurityElement.Escape(cmd)}</Command>
                  <Arguments>{SecurityElement.Escape(arguments)}</Arguments>
                  <WorkingDirectory>{SecurityElement.Escape(Path.GetDirectoryName(exe))}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
        // Ohne Priority 4 läuft die Aufgabe mit „below normal“ (7), und die UI würde ruckeln

        return Register(xml);
    }

    /// <summary>
    /// Argumente für cmd.exe: erst die <see cref="RuntimeInjectionVars"/> leeren, dann die Exe starten. Die Aufgabe ruft
    /// nicht direkt die Exe, sondern cmd auf, damit diese Variablen nicht mit Adminrechten in den .NET-Prozess kommen.
    /// „start“ löst die Exe von cmd, sodass cmd sofort endet (kurzes Konsolenblitzen beim Anmelden). „/d“ unterdrückt eine
    /// AutoRun-Zeile aus HKCU, „/s“ nimmt den Rest wörtlich samt inneren Anführungszeichen.
    /// </summary>
    internal static string StarterArguments(string exe)
    {
        var scrub = string.Concat(RuntimeInjectionVars.Select(v => $"set \"{v}=\" & "));
        return $"/d /s /c \"{scrub}start \"\" \"{exe}\" {FromTaskArgument}\"";
    }

    /// <summary>
    /// Über die COM-Schnittstelle der Aufgabenplanung, die XML als String. Nicht über eine Datei in %TEMP% und
    /// <c>schtasks /Create /XML</c>: Zwischen Schreiben und Einlesen könnte jedes Programm des Users die Datei austauschen
    /// und so eine eigene Aufgabe mit höchsten Rechten anlegen lassen.
    /// </summary>
    private static (int ExitCode, string Output) Register(string xml)
    {
        const int TASK_CREATE_OR_UPDATE = 6;
        const int TASK_LOGON_INTERACTIVE_TOKEN = 3;
        try
        {
            dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!)!;
            service.Connect();
            dynamic folder = service.GetFolder(@"\");
            dynamic task = folder.RegisterTask(TaskName, xml, TASK_CREATE_OR_UPDATE, null, null, TASK_LOGON_INTERACTIVE_TOKEN, null);
            return (0, $"Aufgabe {(string)task.Path} angelegt");
        }
        catch (Exception ex)
        {
            return (ex.HResult, $"0x{ex.HResult:X8}: {ex.Message}");
        }
    }

    private static string MoreProblems(int count) => Localization.Loc.Format("Autostart_MoreProblems", count);

    /// <summary>
    /// For the dialog: each affected path once (the log keeps the account and rights per entry), grouped by folder so
    /// the long folder path appears once and the narrow message box doesn't wrap inside file names. At most
    /// <paramref name="max"/> names, plus an "and N more" line. Entries look like <c>path (account: rights)</c>;
    /// the last " (" splits them, since a path may itself contain " (" (Program Files (x86)).
    /// </summary>
    internal static string DescribeProblems(IReadOnlyList<string> problems, Func<int, string> more, int max = 5)
    {
        var paths = problems
            .Select(p => p.LastIndexOf(" (", StringComparison.Ordinal) is var i and > 0 ? p[..i] : p)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var lines = new List<string>();
        string? folder = null;
        foreach (var path in paths.Take(max))
        {
            var parent = Path.GetDirectoryName(path);
            if (parent is null)
            {
                lines.Add(path); // a drive root has no parent
                folder = null;
                continue;
            }
            if (!string.Equals(parent, folder, StringComparison.OrdinalIgnoreCase)) lines.Add(parent);
            folder = parent;
            lines.Add("    " + Path.GetFileName(path));
        }
        if (paths.Count > max) lines.Add(more(paths.Count - max));
        return string.Join("\n", lines);
    }

    /// <summary>icacls lists every file it touched; the dialog only needs the start.</summary>
    internal static string FirstLines(string text, int count)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join("\n", lines.Take(count)) + (lines.Length > count ? "\n…" : "");
    }

    /// <summary>
    /// Makes Administrators the owner of the folder and everything in it, then drops all explicit ACEs so the tree
    /// inherits the admin-only permissions of Program Files again. Only called for folders under Program Files.
    /// The SID instead of a group name, because the name is localized ("Administratoren").
    /// </summary>
    private static (int ExitCode, string Output) ResetPermissions(string folder)
    {
        var owner = RunSystemTool("icacls.exe", folder, "/setowner", "*S-1-5-32-544", "/T", "/C", "/Q");
        var reset = RunSystemTool("icacls.exe", folder, "/reset", "/T", "/C", "/Q");
        return (owner.ExitCode != 0 ? owner.ExitCode : reset.ExitCode, $"{owner.Output}\n{reset.Output}".Trim());
    }

    private static (int ExitCode, string Output) Schtasks(params string[] args) => RunSystemTool("schtasks.exe", args);

    private static (int ExitCode, string Output) RunSystemTool(string tool, params string[] args)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, tool))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
            StandardErrorEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);

        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, (stdout.Result + stderr).Trim());
    }
}
