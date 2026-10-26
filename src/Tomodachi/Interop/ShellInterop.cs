using System.Runtime.InteropServices;
using System.Text;

namespace Tomodachi.Interop;

// Klassisches COM-Interop ([ComImport]) für die Shell-Item-API. Die Source-Generatoren (LibraryImport)
// unterstützen keine COM-Interfaces als Parameter, deshalb hier DllImport.

internal enum SIGDN : uint
{
    NORMALDISPLAY = 0x00000000,
    PARENTRELATIVEPARSING = 0x80018001,
}

[Flags]
internal enum SIIGBF : uint
{
    BIGGERSIZEOK = 0x01,
    ICONONLY = 0x04,
}

[StructLayout(LayoutKind.Sequential)]
internal struct SIZE
{
    public int cx, cy;
    public SIZE(int cx, int cy) { this.cx = cx; this.cy = cy; }
}

[ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    [PreserveSig] int BindToHandler(nint pbc, in Guid bhid, in Guid riid, out nint ppv);
    [PreserveSig] int GetParent(out IShellItem ppsi);
    [PreserveSig] int GetDisplayName(SIGDN sigdnName, out nint ppszName);
    [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
    [PreserveSig] int Compare(IShellItem psi, uint hint, out int piOrder);
}

[ComImport, Guid("70629033-e363-4a28-a567-0db78006e6d7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumShellItems
{
    [PreserveSig] int Next(uint celt, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IShellItem[] rgelt, out uint pceltFetched);
    [PreserveSig] int Skip(uint celt);
    [PreserveSig] int Reset();
    [PreserveSig] int Clone(out IEnumShellItems ppenum);
}

[ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemImageFactory
{
    [PreserveSig] int GetImage(SIZE size, SIIGBF flags, out nint phbm);
}

/// <summary>Nur der Anfang der Vtable, mehr brauchen wir nicht.</summary>
[ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLinkW
{
    [PreserveSig] int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, nint findData, uint flags);
}

[ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPersistFile
{
    [PreserveSig] int GetClassID(out Guid clsid);
    [PreserveSig] int IsDirty();
    [PreserveSig] int Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
}

[ComImport, Guid("00021401-0000-0000-C000-000000000046")]
internal class ShellLinkObject;

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFOHEADER
{
    public uint biSize;
    public int biWidth;
    public int biHeight;
    public ushort biPlanes;
    public ushort biBitCount;
    public uint biCompression;
    public uint biSizeImage;
    public int biXPelsPerMeter;
    public int biYPelsPerMeter;
    public uint biClrUsed;
    public uint biClrImportant;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAP
{
    public int bmType;
    public int bmWidth;
    public int bmHeight;
    public int bmWidthBytes;
    public ushort bmPlanes;
    public ushort bmBitsPixel;
    public nint bmBits;
}

internal static class ShellNative
{
    public static readonly Guid IID_IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
    public static readonly Guid IID_IEnumShellItems = new("70629033-e363-4a28-a567-0db78006e6d7");
    public static readonly Guid BHID_EnumItems = new("94f60519-2850-4924-aa5a-d15e84868039");

    public const uint SFGAO_FOLDER = 0x20000000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    public static extern void SHCreateItemFromParsingName(string path, nint pbc, in Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    [DllImport("gdi32.dll")]
    public static extern int GetObject(nint hObject, int size, out BITMAP bitmap);

    [DllImport("gdi32.dll")]
    public static extern unsafe int GetDIBits(nint hdc, nint hbmp, uint start, uint lines, void* bits,
        ref BITMAPINFOHEADER bmi, uint usage);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(nint hObject);

    [DllImport("user32.dll")]
    public static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(nint hWnd, nint hdc);

    /// <summary>Ziel einer .lnk-Datei, oder null (kaputt, oder MSI-„advertised“ ohne Pfad).</summary>
    public static string? ResolveShortcut(string lnkPath)
    {
        var link = (IShellLinkW)new ShellLinkObject();
        try
        {
            if (((IPersistFile)link).Load(lnkPath, 0) < 0) return null;
            var target = new StringBuilder(1024);
            return link.GetPath(target, target.Capacity, 0, 0) == 0 && target.Length > 0 ? target.ToString() : null;
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }

    /// <summary>Liest einen Shell-String (CoTaskMem) und gibt ihn frei.</summary>
    public static string? TakeString(int hr, nint ptr)
    {
        if (hr < 0 || ptr == 0) return null;
        try
        {
            return Marshal.PtrToStringUni(ptr);
        }
        finally
        {
            Marshal.FreeCoTaskMem(ptr);
        }
    }
}
