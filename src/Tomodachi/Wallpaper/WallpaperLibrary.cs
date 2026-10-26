using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Tomodachi.Wallpaper;

public sealed class WallpaperItem(string path, DateTime lastWriteUtc) : INotifyPropertyChanged
{
    private BitmapSource? _thumbnail;
    private bool _isSelected;

    public string Path { get; } = path;
    public string Name { get; } = System.IO.Path.GetFileNameWithoutExtension(path);
    public DateTime LastWriteUtc { get; } = lastWriteUtc;

    public BitmapSource? Thumbnail
    {
        get => _thumbnail;
        set => SetField(ref _thumbnail, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value)) return;
        storage = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>Die Bilder aus dem Wallpaper-Ordner samt verkleinerter Vorschaubilder, die im Speicher bleiben.</summary>
public sealed class WallpaperLibrary(string folder, int thumbnailHeight)
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".webp",
    };

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private Dictionary<string, WallpaperItem> _known = new(StringComparer.OrdinalIgnoreCase);

    public string Folder { get; } = folder;

    /// <summary>
    /// Liest den Ordner neu ein, neueste Bilder zuerst. Unveränderte Dateien behalten ihr Vorschaubild,
    /// neue oder geänderte werden im Hintergrund geladen.
    /// </summary>
    public IReadOnlyList<WallpaperItem> Refresh()
    {
        var items = new List<WallpaperItem>();
        var toLoad = new List<WallpaperItem>();
        var known = new Dictionary<string, WallpaperItem>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(Folder))
        {
            var files = new DirectoryInfo(Folder).EnumerateFiles()
                .Where(f => ImageExtensions.Contains(f.Extension))
                .OrderByDescending(f => f.LastWriteTimeUtc);

            foreach (var file in files)
            {
                if (!_known.TryGetValue(file.FullName, out var item) || item.LastWriteUtc != file.LastWriteTimeUtc)
                {
                    item = new WallpaperItem(file.FullName, file.LastWriteTimeUtc);
                    toLoad.Add(item);
                }
                known[file.FullName] = item;
                items.Add(item);
            }
        }

        _known = known;
        if (toLoad.Count > 0)
            Task.Run(() => LoadThumbnails(toLoad));
        return items;
    }

    private void LoadThumbnails(List<WallpaperItem> items)
    {
        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) };
        Parallel.ForEach(items, options, item =>
        {
            BitmapSource? thumbnail = LoadThumbnail(item.Path, thumbnailHeight);
            if (thumbnail is not null)
                _dispatcher.InvokeAsync(() => item.Thumbnail = thumbnail);
        });
        MemoryRelief.CollectInBackground(); // Dekodier-Puffer der BitmapImage-Objekte freigeben
    }

    private static BitmapSource? LoadThumbnail(string path, int height)
    {
        try
        {
            // Über einen Stream statt einer URI, weil Zeichen wie # in Dateinamen eine URI zerlegen würden.
            var decoded = new BitmapImage();
            using (var stream = File.OpenRead(path))
            {
                decoded.BeginInit();
                decoded.CacheOption = BitmapCacheOption.OnLoad;
                decoded.DecodePixelHeight = height;
                decoded.StreamSource = stream;
                decoded.EndInit();
            }

            // Nur den mittleren Streifen behalten, den eine Karte überhaupt zeigt – spart gut die Hälfte Speicher.
            int width = Math.Min(decoded.PixelWidth, (int)(decoded.PixelHeight * CardLayout.ThumbnailAspect));
            var strip = new CroppedBitmap(decoded, new Int32Rect((decoded.PixelWidth - width) / 2, 0, width, decoded.PixelHeight));
            var thumbnail = new WriteableBitmap(strip);
            thumbnail.Freeze();
            return thumbnail;
        }
        catch (Exception)
        {
            return null; // kaputte oder nicht unterstützte Datei: die Karte zeigt dann nur den Namen
        }
    }
}
