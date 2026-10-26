using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tomodachi.Core;

namespace Tomodachi.Notch;

/// <summary>
/// Profilbild der Notch: ein eigenes Bild aus <c>%APPDATA%\tomodachi\avatar.png</c>, sonst das neutrale Standardbild (友)
/// aus der Exe. Das eigene Bild steckt bewusst nicht in der Exe, damit sie sich weitergeben lässt.
/// </summary>
internal static class AvatarStore
{
    private const int Size = 256;
    private static readonly Uri DefaultImage = new("pack://application:,,,/Assets/avatar.png");

    public static string CustomFile => Path.Combine(Paths.Root, "avatar.png");

    public static bool HasCustom => File.Exists(CustomFile);

    public static ImageSource Load()
    {
        try
        {
            if (HasCustom) return Decode(CustomFile);
        }
        catch (Exception ex)
        {
            Log.Warn("Eigenes Profilbild unlesbar, nehme das Standardbild", ex);
        }
        var image = new BitmapImage(DefaultImage);
        image.Freeze();
        return image;
    }

    /// <summary>Mittig quadratisch zuschneiden, auf 256 px bringen und als PNG ablegen. Wirft bei unlesbaren Dateien.</summary>
    public static void SetCustom(string sourceFile)
    {
        var source = Decode(sourceFile);
        var side = Math.Min(source.PixelWidth, source.PixelHeight);
        var square = new CroppedBitmap(source, new Int32Rect((source.PixelWidth - side) / 2, (source.PixelHeight - side) / 2, side, side));
        var scaled = new TransformedBitmap(square, new ScaleTransform((double)Size / side, (double)Size / side));

        Directory.CreateDirectory(Paths.Root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(scaled));
        using var stream = File.Create(CustomFile);
        encoder.Save(stream);
        Log.Info($"Eigenes Profilbild gesetzt ({side}×{side} px Ausschnitt)");
    }

    public static void ResetToDefault()
    {
        if (!HasCustom) return;
        File.Delete(CustomFile);
        Log.Info("Profilbild zurück auf Standard");
    }

    /// <summary>Über einen Stream: hält die Datei nicht offen und verträgt Zeichen wie # im Pfad.</summary>
    private static BitmapSource Decode(string file)
    {
        var image = new BitmapImage();
        using (var stream = File.OpenRead(file))
        {
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
        }
        image.Freeze();
        return image;
    }
}
