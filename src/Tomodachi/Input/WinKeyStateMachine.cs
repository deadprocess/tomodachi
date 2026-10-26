using Tomodachi.Interop;

namespace Tomodachi.Input;

[Flags]
internal enum Modifiers
{
    None = 0,
    Ctrl = 1,
    Shift = 2,
    Alt = 4,
}

internal enum KeyVerdict
{
    /// <summary>Event unverändert weiterreichen.</summary>
    PassThrough,

    /// <summary>Event schlucken.</summary>
    Swallow,

    /// <summary>Dieses Event schlucken und stattdessen das zurückgehaltene Win-Down plus dieses Event injizieren.</summary>
    ReplayWinThenKey,

    /// <summary>Win+L: Event schlucken und den PC sperren (simuliertes Win+L ignoriert Windows).</summary>
    LockWorkstation,
}

/// <param name="Action">Shell-Aktion, die ausgelöst werden soll (<see cref="ShellActions"/>), 0 = keine.</param>
internal readonly record struct KeyDecision(KeyVerdict Verdict, bool Tap, int Action = 0)
{
    public static readonly KeyDecision Pass = new(KeyVerdict.PassThrough, false);
    public static readonly KeyDecision Swallow = new(KeyVerdict.Swallow, false);
}

internal static class ShellActions
{
    public const int ToggleWidgets = 1;
    public const int ClosePanel = 2;
}

/// <summary>
/// Reine Entscheidungslogik des Win-Key-Hooks, ohne Win32. Dadurch lässt sie sich testen,
/// ohne echte Tasten zu senden, die das Startmenü öffnen oder den Fokus verschieben.
/// <para>
/// Prinzip: Win-Down wird zurückgehalten, Windows sieht einen einzelnen Win-Tipp also nie (kein Startmenü).
/// Kommt vor dem Loslassen eine andere Taste, wird das Win-Down vor ihr nachgespielt, und Windows sieht
/// eine normale Kombi. Der frühere Ansatz (Win-Down durchlassen, Win-Up mit Mask-Key ersetzen) öffnete
/// das Startmenü sporadisch trotzdem (beobachtet am 26.10.2026).
/// </para>
/// </summary>
internal sealed class WinKeyStateMachine
{
    private const uint VK_L = 0x4C;

    private readonly Dictionary<uint, int> _shellCombos = [];
    private readonly HashSet<uint> _swallowedUps = [];
    private uint _heldWinKey;
    private bool _comboUsed;
    private bool _releasedToSystem;
    private bool _ctrlEscSwallowed;

    /// <summary>Gerade keine Taste in Bearbeitung. Nur dann darf der Hook neu eingehängt werden.</summary>
    public bool IsIdle => _heldWinKey == 0 && !_ctrlEscSwallowed && _swallowedUps.Count == 0;

    /// <summary>Ein Panel ohne eigenen Fokus ist offen (Widget-Center): Esc schließt es, statt an die App zu gehen.</summary>
    public bool PanelOpen { get; set; }

    /// <summary>Win + <paramref name="vk"/> löst eine Shell-Aktion aus, statt an Windows zu gehen (z. B. Win+W → Widgets).</summary>
    public void SetShellCombo(uint vk, int action) => _shellCombos[vk] = action;

    public bool IsWinHeld => _heldWinKey != 0;

    /// <summary>Welche Win-Taste gerade gehalten wird (für das Nachspielen), 0 wenn keine.</summary>
    public uint HeldWinKey => _heldWinKey;

    /// <summary>Für die Diagnose: Kam seit dem Win-Down eine andere Taste?</summary>
    public bool ComboUsed => _comboUsed;

    /// <param name="modifiers">Wird nur für Esc und Win-Kombis ausgewertet. Der Hook liest den Zustand nur dann aus.</param>
    public KeyDecision Process(uint vk, bool up, Modifiers modifiers)
    {
        if (vk is Native.VK_LWIN or Native.VK_RWIN) return ProcessWin(vk, up);

        // Zu einem geschluckten Down gehört auch das Up, sonst sieht die App eine verwaiste Taste
        if (up && _swallowedUps.Remove(vk)) return KeyDecision.Swallow;
        if (!up && _swallowedUps.Contains(vk)) return KeyDecision.Swallow; // Auto-Repeat

        if (_heldWinKey != 0)
        {
            if (up || _releasedToSystem) return KeyDecision.Pass;

            _comboUsed = true;
            // Nur die reine Kombi: Strg+Win+W gehört dem Wallpaper-Picker (RegisterHotKey), nicht Win+W
            if (modifiers == Modifiers.None && _shellCombos.TryGetValue(vk, out var action))
            {
                _swallowedUps.Add(vk);
                return new KeyDecision(KeyVerdict.Swallow, false, action);
            }
            if (vk == VK_L) return new KeyDecision(KeyVerdict.LockWorkstation, false);

            _releasedToSystem = true;
            return new KeyDecision(KeyVerdict.ReplayWinThenKey, false);
        }

        // Esc schließt ein offenes Panel. Es hat keinen Fokus und bekommt die Taste sonst nicht.
        if (vk == Native.VK_ESCAPE && !up && PanelOpen && modifiers == Modifiers.None)
        {
            _swallowedUps.Add(vk);
            return new KeyDecision(KeyVerdict.Swallow, false, ShellActions.ClosePanel);
        }

        // Ctrl+Esc öffnet sonst ebenfalls das Startmenü. Ctrl+Shift+Esc (Task-Manager) bleibt unberührt.
        if (vk == Native.VK_ESCAPE)
        {
            if (up && _ctrlEscSwallowed)
            {
                _ctrlEscSwallowed = false;
                return KeyDecision.Swallow;
            }
            if (!up && modifiers == Modifiers.Ctrl)
            {
                var tap = !_ctrlEscSwallowed; // Auto-Repeat nicht mehrfach melden
                _ctrlEscSwallowed = true;
                return new KeyDecision(KeyVerdict.Swallow, tap);
            }
        }

        return KeyDecision.Pass;
    }

    private KeyDecision ProcessWin(uint vk, bool up)
    {
        if (!up)
        {
            if (_heldWinKey == 0)
            {
                _heldWinKey = vk;
                _comboUsed = false;
                _releasedToSystem = false;
                return KeyDecision.Swallow;
            }
            if (_heldWinKey != vk) return KeyDecision.Pass; // zweite Win-Taste: Windows regelt das selbst

            // Auto-Repeat: zurückhalten, bis Windows das Win-Down kennt
            return _releasedToSystem ? KeyDecision.Pass : KeyDecision.Swallow;
        }

        if (_heldWinKey != vk) return KeyDecision.Pass;
        _heldWinKey = 0;

        // Hat Windows das Win-Down gesehen, muss es auch das Win-Up sehen, sonst klemmt die Taste
        if (_releasedToSystem) return KeyDecision.Pass;
        return new KeyDecision(KeyVerdict.Swallow, Tap: !_comboUsed);
    }
}
