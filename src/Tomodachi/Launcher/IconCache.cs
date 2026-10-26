using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tomodachi.Core;
using Tomodachi.Interop;

namespace Tomodachi.Launcher;

/// <summary>
/// App-Icons: erst Speicher, dann PNG auf Platte (<c>%LOCALAPPDATA%\tomodachi\icons</c>), sonst über die Shell.
/// Alle Bilder sind gefroren und dürfen deshalb zwischen Threads geteilt werden.
/// </summary>
internal sealed class IconCache(StaWorker worker)
{
    private const int Size = 64;
    private readonly ConcurrentDictionary<string, ImageSource?> _memory = new();

    public Task<ImageSource?> GetAsync(AppEntry app)
    {
        if (_memory.TryGetValue(app.Id, out var cached)) return Task.FromResult(cached);
        return worker.Run(() => _memory.GetOrAdd(app.Id, _ => Load(app)));
    }

    private static ImageSource? Load(AppEntry app)
    {
        var file = Path.Combine(Paths.IconCacheDir, $"{Hash(app.Id)}-{Size}.png");
        try
        {
            if (File.Exists(file)) return Decode(file);

            var image = app.IconFile is not null ? Decode(app.IconFile)
                : app.LaunchTarget.Contains("://") ? null // URL ohne eigenes Bild: kennt die Shell nicht
                : Extract(app.LaunchTarget);
            if (image is not null) Save(image, file);
            return image;
        }
        catch (Exception ex)
        {
            Log.Warn($"Icon für '{app.Name}' nicht ladbar", ex);
            return null;
        }
    }

    /// <param name="parsingName"><c>shell:AppsFolder\…</c> oder Pfad einer Verknüpfung.</param>
    private static unsafe BitmapSource? Extract(string parsingName)
    {
        ShellNative.SHCreateItemFromParsingName(parsingName, 0, ShellNative.IID_IShellItem, out var item);
        nint hbitmap = 0;
        try
        {
            var factory = (IShellItemImageFactory)item;
            if (factory.GetImage(new SIZE(Size, Size), SIIGBF.BIGGERSIZEOK | SIIGBF.ICONONLY, out hbitmap) < 0) return null;

            ShellNative.GetObject(hbitmap, Marshal.SizeOf<BITMAP>(), out var bitmap);
            int width = bitmap.bmWidth, height = bitmap.bmHeight, stride = width * 4;
            var pixels = new byte[stride * height];

            // Negative Höhe = top-down. So stimmt die Zeilenreihenfolge unabhängig davon, wie die Shell das Bitmap angelegt hat.
            var header = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
            };
            var hdc = ShellNative.GetDC(0);
            try
            {
                fixed (byte* p = pixels) ShellNative.GetDIBits(hdc, hbitmap, 0, (uint)height, p, ref header, 0);
            }
            finally
            {
                ShellNative.ReleaseDC(0, hdc);
            }

            // Alte Icons ohne Alphakanal hätten sonst Alpha 0 überall, wären also unsichtbar
            var hasAlpha = false;
            for (var i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] != 0) { hasAlpha = true; break; }
            }
            var format = hasAlpha ? PixelFormats.Pbgra32 : PixelFormats.Bgr32;

            var source = BitmapSource.Create(width, height, 96, 96, format, null, pixels, stride);
            source.Freeze();
            return source;
        }
        finally
        {
            if (hbitmap != 0) ShellNative.DeleteObject(hbitmap);
            Marshal.ReleaseComObject(item);
        }
    }

    private static BitmapSource Decode(string file)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad; // Datei nicht offen halten
        image.UriSource = new Uri(file);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static void Save(BitmapSource image, string file)
    {
        Directory.CreateDirectory(Paths.IconCacheDir);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(file);
        encoder.Save(stream);
    }

    private static string Hash(string id) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(id)))[..16];
}
