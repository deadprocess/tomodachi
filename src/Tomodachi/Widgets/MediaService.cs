using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tomodachi.Core;
using Windows.Media.Control;

namespace Tomodachi.Widgets;

internal sealed record MediaSnapshot(
    string Title,
    string Artist,
    string Source,
    bool IsPlaying,
    TimeSpan Position,
    TimeSpan Duration,
    ImageSource? Cover);

/// <summary>
/// Paket 4.6: aktuelle Wiedergabe über die Windows-Mediensteuerung (dieselbe Quelle wie das Lautstärke-Overlay):
/// Spotify, Browser-Videos, Media Player … Das Cover wird nur bei einem neuen Titel neu geladen.
/// </summary>
internal sealed class MediaService(Func<string, string?> appName)
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private string? _coverKey;
    private ImageSource? _cover;

    public async Task<MediaSnapshot?> PollAsync()
    {
        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = _manager.GetCurrentSession();
            if (session is null) return null;

            var properties = await session.TryGetMediaPropertiesAsync();
            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            var playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            // Die Position meldet die App nur gelegentlich, beim Abspielen hochrechnen
            var position = timeline.Position;
            if (playing) position += DateTimeOffset.Now - timeline.LastUpdatedTime;
            var duration = timeline.EndTime - timeline.StartTime;
            if (duration > TimeSpan.Zero && position > duration) position = duration;

            var key = $"{session.SourceAppUserModelId}|{properties.Title}|{properties.Artist}";
            if (key != _coverKey)
            {
                _coverKey = key;
                _cover = await LoadCoverAsync(properties);
            }

            var appId = session.SourceAppUserModelId;
            return new MediaSnapshot(properties.Title, properties.Artist, appName(appId) ?? SourceName(appId),
                playing, position, duration, _cover);
        }
        catch (Exception ex)
        {
            Log.Debug($"Mediensteuerung nicht verfügbar: {ex.Message}");
            return null;
        }
    }

    public Task TogglePlayPauseAsync() => Do(s => s.TryTogglePlayPauseAsync().AsTask());
    public Task NextAsync() => Do(s => s.TrySkipNextAsync().AsTask());
    public Task PreviousAsync() => Do(s => s.TrySkipPreviousAsync().AsTask());

    private async Task Do(Func<GlobalSystemMediaTransportControlsSession, Task<bool>> action)
    {
        try
        {
            if (_manager?.GetCurrentSession() is { } session) await action(session);
        }
        catch (Exception ex)
        {
            Log.Debug($"Medienbefehl fehlgeschlagen: {ex.Message}");
        }
    }

    private static async Task<ImageSource?> LoadCoverAsync(GlobalSystemMediaTransportControlsSessionMediaProperties properties)
    {
        if (properties.Thumbnail is null) return null;
        try
        {
            using var stream = (await properties.Thumbnail.OpenReadAsync()).AsStreamForRead();
            var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            memory.Position = 0;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 128;
            image.StreamSource = memory;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            Log.Debug($"Cover nicht ladbar: {ex.Message}");
            return null;
        }
    }

    /// <summary>Fallback, wenn der App-Index die Kennung nicht kennt: „Spotify.exe“ → „Spotify“.</summary>
    private static string SourceName(string appId)
    {
        var name = appId.Contains('!') ? appId[(appId.IndexOf('!') + 1)..] : appId;
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }
}
