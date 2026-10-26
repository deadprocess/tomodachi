using Tomodachi.Input;
using Tomodachi.Interop;

namespace Tomodachi.Tests;

public class WinKeyStateMachineTests
{
    private const uint LWin = Native.VK_LWIN;
    private const uint RWin = Native.VK_RWIN;
    private const uint Esc = Native.VK_ESCAPE;
    private const uint KeyE = 0x45;
    private const uint KeyL = 0x4C;
    private const uint KeyS = 0x53;
    private const uint Shift = 0x10;
    private const uint Ctrl = 0x11;

    private static readonly KeyDecision Replay = new(KeyVerdict.ReplayWinThenKey, false);
    private static readonly KeyDecision TapSwallow = new(KeyVerdict.Swallow, true);

    private readonly WinKeyStateMachine _machine = new();

    private KeyDecision Down(uint vk, Modifiers mods = Modifiers.None) => _machine.Process(vk, up: false, mods);
    private KeyDecision Up(uint vk, Modifiers mods = Modifiers.None) => _machine.Process(vk, up: true, mods);

    [Theory]
    [InlineData(LWin)]
    [InlineData(RWin)]
    public void LoneWinTap_IsSwallowedCompletelyAndTaps(uint win)
    {
        Assert.Equal(KeyDecision.Swallow, Down(win));
        Assert.Equal(TapSwallow, Up(win));
        Assert.True(_machine.IsIdle);
    }

    [Fact]
    public void WinAutoRepeat_IsSwallowedAndStillTaps()
    {
        Down(LWin);
        Assert.Equal(KeyDecision.Swallow, Down(LWin)); // Auto-Repeat
        Assert.Equal(KeyDecision.Swallow, Down(LWin));
        Assert.Equal(TapSwallow, Up(LWin));
    }

    [Fact]
    public void WinCombo_ReplaysWinBeforeFirstKey_ThenPassesEverything()
    {
        Assert.Equal(KeyDecision.Swallow, Down(LWin));
        Assert.Equal(Replay, Down(KeyE));
        Assert.Equal(LWin, _machine.HeldWinKey);
        Assert.Equal(KeyDecision.Pass, Up(KeyE));
        Assert.Equal(KeyDecision.Pass, Up(LWin)); // Windows kennt das Down, also auch das Up durchlassen
        Assert.True(_machine.IsIdle);
    }

    [Fact]
    public void WinShiftS_ReplaysOnceAtShift()
    {
        Down(LWin);
        Assert.Equal(Replay, Down(Shift));
        Assert.Equal(KeyDecision.Pass, Down(KeyS));
        Assert.Equal(KeyDecision.Pass, Up(KeyS));
        Assert.Equal(KeyDecision.Pass, Up(Shift));
        Assert.Equal(KeyDecision.Pass, Up(LWin));
    }

    [Fact]
    public void AutoRepeatAfterReplay_PassesThrough()
    {
        Down(LWin);
        Down(KeyE);
        Assert.Equal(KeyDecision.Pass, Down(LWin));
    }

    [Fact]
    public void CtrlHeldBeforeWin_StillCombo()
    {
        // Ctrl+Win+→ (Desktop wechseln): Ctrl kommt vor Win und läuft normal durch
        Assert.Equal(KeyDecision.Pass, Down(Ctrl));
        Down(LWin);
        Assert.Equal(Replay, Down(0x27));
    }

    [Fact]
    public void WinL_LocksInsteadOfReplay_AndWinUpStaysSwallowed()
    {
        Down(LWin);
        Assert.Equal(new KeyDecision(KeyVerdict.LockWorkstation, false), Down(KeyL));
        Assert.Equal(KeyDecision.Pass, Up(KeyL));
        Assert.Equal(KeyDecision.Swallow, Up(LWin)); // Windows hat kein Win-Down gesehen, kein Tap wegen Kombi
    }

    [Fact]
    public void ComboKeyReleasedAfterWin_DoesNotBreakNextTap()
    {
        Down(LWin);
        Down(KeyE);
        Up(LWin);
        Up(KeyE);

        Down(LWin);
        Assert.Equal(TapSwallow, Up(LWin));
    }

    [Fact]
    public void KeyUpOnlyWhileWinHeld_IsNoCombo()
    {
        // Taste war schon vor Win gedrückt und wird nur losgelassen. Das ist keine Kombi.
        Down(LWin);
        Assert.Equal(KeyDecision.Pass, Up(KeyE));
        Assert.Equal(TapSwallow, Up(LWin));
    }

    [Fact]
    public void OtherWinKeyUp_IsIgnored()
    {
        Down(LWin);
        Assert.Equal(KeyDecision.Pass, Up(RWin));
        Assert.True(Up(LWin).Tap);
    }

    [Fact]
    public void CtrlEsc_SwallowsDownAndUpAndTapsOnce()
    {
        Assert.Equal(TapSwallow, Down(Esc, Modifiers.Ctrl));
        Assert.Equal(KeyDecision.Swallow, Down(Esc, Modifiers.Ctrl)); // Auto-Repeat
        Assert.False(_machine.IsIdle);
        Assert.Equal(KeyDecision.Swallow, Up(Esc, Modifiers.Ctrl));
        Assert.True(_machine.IsIdle);
    }

    [Fact]
    public void CtrlEsc_UpAfterCtrlReleased_IsStillSwallowed()
    {
        Down(Esc, Modifiers.Ctrl);
        Assert.Equal(KeyVerdict.Swallow, Up(Esc, Modifiers.None).Verdict);
    }

    [Theory]
    [InlineData((int)(Modifiers.Ctrl | Modifiers.Shift))] // Task-Manager
    [InlineData((int)(Modifiers.Ctrl | Modifiers.Alt))]
    [InlineData((int)Modifiers.None)]
    public void EscWithoutExactlyCtrl_PassesThrough(int modifiers) // int, weil Modifiers internal ist
    {
        var mods = (Modifiers)modifiers;
        Assert.Equal(KeyDecision.Pass, Down(Esc, mods));
        Assert.Equal(KeyDecision.Pass, Up(Esc, mods));
    }

    [Fact]
    public void WinEsc_IsComboNotCtrlEsc()
    {
        Down(LWin);
        Assert.Equal(Replay, Down(Esc, Modifiers.Ctrl));
        Up(Esc, Modifiers.Ctrl);
        Assert.Equal(KeyDecision.Pass, Up(LWin));
    }

    [Fact]
    public void ShellCombo_SwallowsKeyAndWin_TriggersAction()
    {
        _machine.SetShellCombo(0x57, ShellActions.ToggleWidgets); // Win+W
        Down(LWin);
        Assert.Equal(new KeyDecision(KeyVerdict.Swallow, false, ShellActions.ToggleWidgets), Down(0x57));
        Assert.Equal(KeyDecision.Swallow, Down(0x57)); // Auto-Repeat ohne erneute Aktion
        Assert.Equal(KeyDecision.Swallow, Up(0x57));
        Assert.Equal(KeyDecision.Swallow, Up(LWin)); // Windows hat nie ein Win-Down gesehen, kein Tipp
        Assert.True(_machine.IsIdle);
    }

    [Fact]
    public void ShellCombo_KeyUpAfterWinUp_StillSwallowed()
    {
        _machine.SetShellCombo(0x57, ShellActions.ToggleWidgets);
        Down(LWin);
        Down(0x57);
        Up(LWin);
        Assert.Equal(KeyDecision.Swallow, Up(0x57));
        Assert.True(_machine.IsIdle);
    }

    [Fact]
    public void ShellCombo_AfterOtherCombo_GoesToWindows()
    {
        // Win+E schon nachgespielt: Windows kennt das Win-Down, ein folgendes W gehört dann Windows
        _machine.SetShellCombo(0x57, ShellActions.ToggleWidgets);
        Down(LWin);
        Down(KeyE);
        Assert.Equal(KeyDecision.Pass, Down(0x57));
    }

    [Fact]
    public void ShellCombo_WithCtrlHeldBeforeWin_GoesToWindows()
    {
        // Strg zuerst, dann Win, dann W: Strg+Win+W ist der Wallpaper-Picker (RegisterHotKey), nicht Win+W
        _machine.SetShellCombo(0x57, ShellActions.ToggleWidgets);
        Down(LWin);
        Assert.Equal(Replay, Down(0x57, Modifiers.Ctrl));
        Assert.Equal(KeyDecision.Pass, Up(0x57, Modifiers.Ctrl));
        Assert.Equal(KeyDecision.Pass, Up(LWin));
        Assert.True(_machine.IsIdle);
    }

    [Fact]
    public void PanelOpen_EscClosesPanel_AndUpIsSwallowed()
    {
        _machine.PanelOpen = true;
        Assert.Equal(new KeyDecision(KeyVerdict.Swallow, false, ShellActions.ClosePanel), Down(Esc));
        Assert.Equal(KeyDecision.Swallow, Up(Esc));
        Assert.True(_machine.IsIdle);
    }

    [Fact]
    public void PanelClosed_EscPassesThrough()
    {
        Assert.Equal(KeyDecision.Pass, Down(Esc));
        Assert.Equal(KeyDecision.Pass, Up(Esc));
    }

    [Fact]
    public void PanelOpen_CtrlEscStillOpensShell()
    {
        _machine.PanelOpen = true;
        Assert.Equal(TapSwallow, Down(Esc, Modifiers.Ctrl));
    }

    [Fact]
    public void OrdinaryKeys_PassThrough()
    {
        Assert.Equal(KeyDecision.Pass, Down(KeyE));
        Assert.Equal(KeyDecision.Pass, Up(KeyE));
        Assert.True(_machine.IsIdle);
    }
}
