using System.Runtime.InteropServices;
using Tomodachi.Core;
using Tomodachi.Interop;

namespace Tomodachi.Input;

/// <summary>
/// Low-Level-Tastatur-Hook auf eigenem Thread. Ein einzelner Win-Tipp (und Ctrl+Esc) öffnet die Shell statt des Startmenüs.
/// Win+Kombis (Win+E, Win+Shift+S …) funktionieren weiter: Das zurückgehaltene Win-Down wird vor der zweiten Taste
/// nachgespielt (Logik in <see cref="WinKeyStateMachine"/>). Win+L sperrt direkt per LockWorkStation.
/// <para>
/// <b>Zweiter Weg über Raw Input:</b> Solange ein Fenster von tomodachi selbst im Vordergrund ist, ruft Windows den
/// eigenen LL-Hook nicht auf (beobachtet und reproduziert am 26.10.2026, unabhängig vom Tastaturfokus). Raw Input kommt
/// trotzdem an. Win-Events, die der Hook gesehen hat, sind geschluckt und erzeugen kein Raw Input. Ein Win im Raw Input
/// heißt also immer: Der Hook hat es nicht gesehen. Dann übernimmt <see cref="HandleRawInput"/>.
/// </para>
/// Braucht keinen Restore-Eintrag: Windows entfernt den Hook mit dem Prozess.
/// </summary>
internal sealed unsafe class WinKeyHook : IDisposable
{
    private const nuint InjectedSignature = Native.InjectedSignature;

    /// <summary>
    /// Windows entfernt LL-Hooks still, wenn sie zu langsam antworten (LowLevelHooksTimeout). Beobachtet beim Start unter
    /// Last (Virenscan der frischen Exe): Der erste Aufruf musste JIT-kompilieren und kam zu spät. Gegenmittel:
    /// Hook-Pfad vorab aufwärmen, Raw Input als Gegenprobe (siehe <see cref="CheckHookAlive"/>) und zusätzlich
    /// regelmäßig neu einhängen.
    /// </summary>
    private const uint RehookIntervalMs = 30_000;
    private const string RawInputClass = "tomodachi.rawinput";

    // Hook → eigene Message-Loop: Alles außer der Entscheidung selbst läuft außerhalb des zeitkritischen Hook-Aufrufs
    private const uint WM_APP_TAP = 0x8001;
    private const uint WM_APP_DIAG = 0x8002;
    private const uint WM_APP_LOCK = 0x8003;
    private const uint WM_APP_FOREGROUND = 0x8004;
    private const uint WM_APP_ACTION = 0x8005;
    private const uint VK_W = 0x57;

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint LLKHF_EXTENDED = 0x01;
    private const ushort VK_DUMMY = 0xFF;

    private static WinKeyHook? s_instance;
    private static int s_eventsSinceRehook; // Diagnose: lebt der Hook?
    private static volatile bool s_panelOpen;

    /// <summary>Ein Panel ohne Fokus ist offen: Esc wird abgefangen und schließt es.</summary>
    public static bool PanelOpen
    {
        set => s_panelOpen = value;
    }

    private readonly Action _onTap;
    private readonly Action<int> _onAction;
    private readonly Thread _thread;
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;
    private uint _threadId;
    private nint _hook;

    // Nur auf dem Hook-Thread angefasst
    private readonly WinKeyStateMachine _machine = new();
    private nint _rawInputWindow;
    private long _hookEvents;
    private long _rawEvents;
    private bool _ownForeground;
    private ushort _rawHeldWin;
    private bool _rawCombo;

    /// <param name="onTap">Läuft auf dem Hook-Thread (außerhalb des Hook-Callbacks), sollte aber trotzdem kurz sein.</param>
    /// <param name="onAction">Shell-Aktionen (<see cref="ShellActions"/>), ebenfalls auf dem Hook-Thread.</param>
    public WinKeyHook(Action onTap, Action<int> onAction)
    {
        if (s_instance is not null) throw new InvalidOperationException("Es darf nur einen WinKeyHook geben");
        _onTap = onTap;
        _onAction = onAction;
        _machine.SetShellCombo(VK_W, ShellActions.ToggleWidgets);

        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Run(ready))
        {
            IsBackground = true,
            Name = "tomodachi.keyboard-hook",
            Priority = ThreadPriority.Highest,
        };
        _thread.Start();
        ready.Wait();
    }

    private void Run(ManualResetEventSlim ready)
    {
        _threadId = Native.GetCurrentThreadId();
        Native.PeekMessage(out _, 0, 0, 0, Native.PM_NOREMOVE); // Message-Queue anlegen, bevor jemand WM_QUIT postet
        s_instance = this;
        WarmUp();
        _hook = Install();
        CreateRawInputSink();
        var timer = Native.SetTimer(0, 0, RehookIntervalMs, 0);
        delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> onForeground = &OnForegroundChanged;
        var foregroundHook = Native.SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, 0,
            (nint)onForeground, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
        ready.Set();

        while (Native.GetMessage(out var msg, 0, 0, 0) > 0)
        {
            if (msg.message == WM_APP_TAP)
            {
                _onTap();
            }
            else if (msg.message == WM_APP_ACTION)
            {
                _onAction((int)msg.wParam);
            }
            else if (msg.message == WM_APP_LOCK)
            {
                Log.Info("Win+L: sperre den PC");
                if (!Native.LockWorkStation()) Log.Warn($"LockWorkStation fehlgeschlagen (Win32 {Marshal.GetLastPInvokeError()})");
            }
            else if (msg.message == WM_APP_DIAG)
            {
                var info = (uint)msg.lParam;
                var what = msg.wParam == 0 ? "andere Taste" : $"Win 0x{msg.wParam:X2}";
                Log.Debug($"Hook: {what} {((info & Native.LLKHF_UP) != 0 ? "up" : "down")}" +
                          $"{((info & 0x10) != 0 ? " injiziert" : "")} → {(KeyVerdict)((info >> 16) & 0xF)}" +
                          $" (Win gehalten: {(info & (1u << 21)) != 0}, Kombi: {(info & (1u << 20)) != 0})");
            }
            else if (msg.message == Native.WM_INPUT)
            {
                HandleRawInput(msg.lParam);
                CheckHookAlive();
                Native.DispatchMessage(msg); // DefWindowProc räumt den Raw-Input-Puffer auf
            }
            else if (msg.message == WM_APP_FOREGROUND)
            {
                Native.GetWindowThreadProcessId(msg.lParam, out var pid);
                _ownForeground = pid == _ownProcessId;
            }
            else if (msg.message == Native.WM_TIMER && _machine.IsIdle)
            {
                Rehook();
                Log.Debug($"Tastatur-Hook neu eingehängt ({Interlocked.Exchange(ref s_eventsSinceRehook, 0)} Events seit dem letzten Mal, " +
                          $"gesamt Hook {_hookEvents} / Raw Input {_rawEvents})");
            }
        }

        if (foregroundHook != 0) Native.UnhookWinEvent(foregroundHook);
        Native.KillTimer(0, timer);
        if (_rawInputWindow != 0) Native.DestroyWindow(_rawInputWindow);
        Uninstall();
        s_instance = null;
    }

    [UnmanagedCallersOnly]
    private static void OnForegroundChanged(nint hook, uint evt, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (s_instance is { } self) Native.PostThreadMessage(self._threadId, WM_APP_FOREGROUND, 0, hwnd);
    }

    /// <summary>Erst neu einhängen, dann alt aushängen: keine Lücke, in der Tasten am Hook vorbeigehen.</summary>
    private void Rehook()
    {
        var fresh = Install();
        if (fresh == 0) return;
        Uninstall();
        _hook = fresh;
    }

    /// <summary>
    /// Raw Input kommt unabhängig von Hooks und immer <b>nach</b> dem Hook-Aufruf für dasselbe Event auf diesem Thread an.
    /// Gibt es mehr Raw-Input-Events als Hook-Aufrufe, hat Windows den Hook entfernt, und er wird sofort neu eingehängt.
    /// Vom Hook geschluckte Events erzeugen kein Raw Input, das zählt also nur in die richtige Richtung.
    /// Ausnahme: tomodachi selbst im Vordergrund, dann ist der Hook absichtlich „blind“ (siehe Klassenkommentar).
    /// </summary>
    private void CheckHookAlive()
    {
        if (++_rawEvents <= _hookEvents) return;
        _rawEvents = _hookEvents;
        if (_ownForeground) return;

        Rehook();
        Log.Warn("Tastatur-Hook war weg (von Windows entfernt), sofort neu eingehängt");
    }

    /// <summary>
    /// Win-Taste, die am Hook vorbeigekommen ist (eigenes Fenster im Vordergrund oder Hook kurz weg):
    /// Bei Win-Down sofort die Dummy-Taste, damit Windows beim Win-Up kein Startmenü öffnet. Win-Up ohne andere Taste
    /// dazwischen zählt als Tipp. Andere Tasten werden nur als „Kombi“ gezählt und nie geloggt.
    /// </summary>
    private void HandleRawInput(nint rawInput)
    {
        const int bufferSize = 64;
        var buffer = stackalloc byte[bufferSize];
        uint size = bufferSize;
        if (Native.GetRawInputData(rawInput, Native.RID_INPUT, buffer, ref size, (uint)sizeof(RAWINPUTHEADER)) == unchecked((uint)-1))
            return;

        var header = (RAWINPUTHEADER*)buffer;
        if (header->dwType != 1) return; // RIM_TYPEKEYBOARD
        var keyboard = (RAWKEYBOARD*)(buffer + sizeof(RAWINPUTHEADER));
        if (keyboard->ExtraInformation == (uint)InjectedSignature) return; // eigene Injektionen

        var vk = keyboard->VKey;
        var up = (keyboard->Flags & Native.RI_KEY_BREAK) != 0;

        if (vk is not (Native.VK_LWIN or Native.VK_RWIN))
        {
            if (!up && _rawHeldWin != 0) _rawCombo = true;
            return;
        }

        if (!up)
        {
            if (_rawHeldWin != 0) return; // Auto-Repeat
            _rawHeldWin = vk;
            _rawCombo = false;
            SendDummyKey();
            Log.Debug($"Raw Input: Win 0x{vk:X2} down am Hook vorbei → Dummy-Taste gegen das Startmenü");
            return;
        }

        if (_rawHeldWin != vk) return;
        _rawHeldWin = 0;
        Log.Debug($"Raw Input: Win 0x{vk:X2} up am Hook vorbei → {(_rawCombo ? "Kombi, kein Tipp" : "Tipp")}");
        if (!_rawCombo) _onTap();
    }

    /// <summary>Unsichtbares Message-Only-Fenster, das Tastatur-Raw-Input auch im Hintergrund bekommt (RIDEV_INPUTSINK).</summary>
    private void CreateRawInputSink()
    {
        var instance = Native.GetModuleHandle(null);
        fixed (char* className = RawInputClass)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = Native.GetProcAddress(Native.GetModuleHandle("user32.dll"), "DefWindowProcW"),
                hInstance = instance,
                lpszClassName = (nint)className,
            };
            Native.RegisterClassEx(wc); // Fehler „existiert schon“ ist egal
        }

        _rawInputWindow = Native.CreateWindowEx(0, RawInputClass, "", 0, 0, 0, 0, 0, Native.HWND_MESSAGE, 0, instance, 0);
        var device = new RAWINPUTDEVICE
        {
            usUsagePage = Native.HID_USAGE_PAGE_GENERIC,
            usUsage = Native.HID_USAGE_GENERIC_KEYBOARD,
            dwFlags = Native.RIDEV_INPUTSINK,
            hwndTarget = _rawInputWindow,
        };
        if (_rawInputWindow == 0 || !Native.RegisterRawInputDevices(&device, 1, (uint)sizeof(RAWINPUTDEVICE)))
            Log.Warn($"Raw Input nicht verfügbar (Win32 {Marshal.GetLastPInvokeError()}), Hook-Überwachung nur per Timer");
    }

    /// <summary>Kompiliert alles, was im Hook-Aufruf läuft, bevor der erste echte Tastendruck kommt.</summary>
    private void WarmUp()
    {
        try
        {
            var dummy = new KBDLLHOOKSTRUCT { vkCode = 0 };
            ShouldSwallow(&dummy);
            ReadModifiers();
            // HookProc zuletzt: PrepareMethod auf [UnmanagedCallersOnly] wirft je nach Runtime
            foreach (var name in new[] { nameof(ReplayWinThenKey), nameof(Key), nameof(HookProc) })
            {
                var method = typeof(WinKeyHook).GetMethod(name,
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (method is not null) System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(method.MethodHandle);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Aufwärmen des Tastatur-Hooks fehlgeschlagen", ex);
        }
    }

    private static nint Install()
    {
        delegate* unmanaged<int, nint, nint, nint> proc = &HookProc;
        var hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, (nint)proc, Native.GetModuleHandle(null), 0);
        if (hook == 0) Log.Error($"Tastatur-Hook fehlgeschlagen (Win32 {Marshal.GetLastPInvokeError()})");
        return hook;
    }

    private void Uninstall()
    {
        if (_hook == 0) return;
        Native.UnhookWindowsHookEx(_hook);
        _hook = 0;
    }

    [UnmanagedCallersOnly]
    private static nint HookProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && s_instance is { } self)
        {
            try
            {
                if (self.ShouldSwallow((KBDLLHOOKSTRUCT*)lParam)) return 1;
            }
            catch (Exception ex)
            {
                // Exceptions dürfen nicht in nativen Code durchschlagen. Im Zweifel Taste durchlassen.
                Log.Error("Fehler im Tastatur-Hook", ex);
            }
        }
        return Native.CallNextHookEx(0, nCode, wParam, lParam);
    }

    /// <summary>Muss schnell sein: kein Logging, keine Allokationen im Normalfall.</summary>
    private bool ShouldSwallow(KBDLLHOOKSTRUCT* key)
    {
        Interlocked.Increment(ref s_eventsSinceRehook);
        _hookEvents++;
        if (key->dwExtraInfo == InjectedSignature) return false;

        var vk = key->vkCode;
        // Modifier nur für Esc und Win-Kombis auslesen (Win+W, nicht aber Strg+Win+W)
        var modifiers = vk == Native.VK_ESCAPE || _machine.IsWinHeld ? ReadModifiers() : Modifiers.None;
        var wasWinHeld = _machine.IsWinHeld;
        var heldWin = _machine.HeldWinKey;
        _machine.PanelOpen = s_panelOpen;
        var decision = _machine.Process(vk, (key->flags & Native.LLKHF_UP) != 0, modifiers);

        // Diagnose rund um die Win-Taste. Andere Tasten nur als „andere Taste“ ohne Code, damit nie Getipptes im Log landet.
        // Geloggt wird erst in der Message-Loop, nie im Hook-Aufruf selbst.
        var isWin = vk is Native.VK_LWIN or Native.VK_RWIN;
        if (isWin || wasWinHeld)
        {
            var info = (key->flags & 0xFF) | ((uint)decision.Verdict << 16)
                | (_machine.ComboUsed ? 1u << 20 : 0) | (_machine.IsWinHeld ? 1u << 21 : 0);
            Native.PostThreadMessage(_threadId, WM_APP_DIAG, isWin ? vk : 0, (nint)info);
        }

        switch (decision.Verdict)
        {
            case KeyVerdict.ReplayWinThenKey:
                ReplayWinThenKey((ushort)heldWin, key);
                break;
            case KeyVerdict.LockWorkstation:
                Native.PostThreadMessage(_threadId, WM_APP_LOCK, 0, 0);
                break;
        }
        if (decision.Tap) Native.PostThreadMessage(_threadId, WM_APP_TAP, 0, 0);
        if (decision.Action != 0) Native.PostThreadMessage(_threadId, WM_APP_ACTION, (nuint)decision.Action, 0);
        return decision.Verdict != KeyVerdict.PassThrough;
    }

    /// <summary>
    /// Das gerade verarbeitete Event wird geschluckt. Ein SendInput hier landet erst <b>hinter</b> ihm in der Queue,
    /// deshalb werden Win-Down und das Event zusammen in richtiger Reihenfolge neu eingespielt.
    /// </summary>
    private static void ReplayWinThenKey(ushort winVk, KBDLLHOOKSTRUCT* key)
    {
        var inputs = stackalloc INPUT[2];
        inputs[0] = Key(winVk, 0, Native.KEYEVENTF_EXTENDEDKEY);
        inputs[1] = Key((ushort)key->vkCode, (ushort)key->scanCode,
            (key->flags & LLKHF_EXTENDED) != 0 ? Native.KEYEVENTF_EXTENDEDKEY : 0);
        Native.SendInput(2, inputs, sizeof(INPUT));
    }

    private static Modifiers ReadModifiers()
    {
        var modifiers = Modifiers.None;
        if (IsDown(Native.VK_CONTROL)) modifiers |= Modifiers.Ctrl;
        if (IsDown(Native.VK_SHIFT)) modifiers |= Modifiers.Shift;
        if (IsDown(Native.VK_MENU)) modifiers |= Modifiers.Alt;
        return modifiers;
    }

    private static bool IsDown(int vk) => Native.GetAsyncKeyState(vk) < 0;

    /// <summary>
    /// VK 0xFF ist nicht belegt und wird von Apps ignoriert. Für Windows ist sie aber eine „andere Taste“ zwischen
    /// Win-Down und Win-Up, und dann öffnet sich kein Startmenü.
    /// </summary>
    private static void SendDummyKey()
    {
        var inputs = stackalloc INPUT[2];
        inputs[0] = Key(VK_DUMMY, 0, 0);
        inputs[1] = Key(VK_DUMMY, 0, Native.KEYEVENTF_KEYUP);
        Native.SendInput(2, inputs, sizeof(INPUT));
    }

    private static INPUT Key(ushort vk, ushort scan, uint flags) => new()
    {
        type = Native.INPUT_KEYBOARD,
        ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags, dwExtraInfo = InjectedSignature },
    };

    public void Dispose()
    {
        Native.PostThreadMessage(_threadId, Native.WM_QUIT, 0, 0);
        if (!_thread.Join(TimeSpan.FromSeconds(2))) Log.Warn("Hook-Thread hat sich nicht rechtzeitig beendet");
    }
}
