using Tomodachi.Launcher;

namespace Tomodachi.Tests;

public class PinnedAppsTests
{
    private static AppEntry App(string id) => AppEntry.Create(id, id);

    [Fact]
    public void Pin_KeepsOrder_AndIgnoresDuplicates()
    {
        var pinned = PinnedApps.InMemory();
        Assert.True(pinned.Pin("b"));
        Assert.True(pinned.Pin("a"));
        Assert.False(pinned.Pin("b"));
        Assert.Equal(["b", "a"], pinned.Ids);
        Assert.True(pinned.IsPinned("a"));
        Assert.False(pinned.IsPinned("c"));
    }

    [Fact]
    public void Pin_StopsAtMax()
    {
        var pinned = PinnedApps.InMemory();
        for (var i = 0; i < PinnedApps.Max; i++) Assert.True(pinned.Pin($"app{i}"));
        Assert.True(pinned.IsFull);
        Assert.False(pinned.Pin("one-too-many"));
        Assert.Equal(PinnedApps.Max, pinned.Ids.Count);
    }

    [Fact]
    public void Unpin_RemovesAndRaisesChanged()
    {
        var pinned = PinnedApps.InMemory("a", "b", "c");
        var changed = 0;
        pinned.Changed += () => changed++;

        Assert.True(pinned.Unpin("b"));
        Assert.False(pinned.Unpin("b"));
        Assert.Equal(["a", "c"], pinned.Ids);
        Assert.Equal(1, changed);
        Assert.False(pinned.IsFull);
    }

    [Fact]
    public void Resolve_FollowsPinOrder_AndSkipsMissingApps()
    {
        var pinned = PinnedApps.InMemory("steam:1", "missing", "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App");
        var apps = new[] { App("Microsoft.WindowsNotepad_8wekyb3d8bbwe!App"), App("other"), App("steam:1") };

        var resolved = pinned.Resolve(apps);

        Assert.Equal(["steam:1", "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App"], resolved.Select(a => a.Id));
        Assert.Equal(3, pinned.Ids.Count); // the missing app stays pinned
    }

    [Fact]
    public void InMemory_DedupesAndCaps()
    {
        var pinned = PinnedApps.InMemory("a", "a", "b", "c", "d", "e", "f");
        Assert.Equal(["a", "b", "c", "d", "e"], pinned.Ids);
    }
}
