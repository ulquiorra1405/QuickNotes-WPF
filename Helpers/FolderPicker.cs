using System.Runtime.InteropServices;
using System.Text;

namespace QuickNotes.Helpers;

/// <summary>
/// Native folder browser via SHBrowseForFolder (shell32).
/// Works on all Windows versions, no COM VTable trickery needed.
/// </summary>
public static class FolderPicker
{
    public static string? Show(string? initialPath = null, string title = "Seleccionar carpeta")
    {
        var pidlRoot = IntPtr.Zero;
        var psf = Marshal.AllocHGlobal(Marshal.SizeOf<BROWSEINFO>());

        try
        {
            if (!string.IsNullOrWhiteSpace(initialPath))
            {
                pidlRoot = SHILCreateFromPath(initialPath);
            }

            var displayBuf = Marshal.AllocHGlobal(260 * 2); // MAX_PATH wide chars

            var bi = new BROWSEINFO
            {
                hwndOwner = IntPtr.Zero,
                pidlRoot = pidlRoot,
                pszDisplayName = displayBuf,
                lpszTitle = title,
                ulFlags = BIF_NEWDIALOGSTYLE | BIF_RETURNONLYFSDIRS | BIF_DONTGOBELOWDOMAIN | BIF_USENEWUI,
                lpfn = IntPtr.Zero,
                lParam = IntPtr.Zero,
                iImage = 0,
            };

            Marshal.StructureToPtr(bi, psf, false);

            var pidl = SHBrowseForFolder(psf);
            if (pidl == IntPtr.Zero)
                return null;

            var pathBuf = new StringBuilder(260);
            SHGetPathFromIDList(pidl, pathBuf);
            return pathBuf.ToString();
        }
        finally
        {
            if (pidlRoot != IntPtr.Zero)
                Marshal.FreeCoTaskMem(pidlRoot);
            Marshal.FreeHGlobal(psf);
        }
    }

    private static IntPtr SHILCreateFromPath(string path)
    {
        // Use SHParseDisplayName to get PIDL from a path
        var guid = new Guid("00000000-0000-0000-C000-000000000046"); // IID_IShellFolder
        SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _);
        return pidl;
    }

    // ── P/Invoke ──

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHBrowseForFolder(IntPtr lpbi);

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern bool SHGetPathFromIDList(IntPtr pidl, StringBuilder pszPath);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszName,
        IntPtr pbc,
        out IntPtr ppidl,
        uint sfgaoIn,
        out uint psfgaoOut);

    private const uint BIF_RETURNONLYFSDIRS = 0x0001;
    private const uint BIF_DONTGOBELOWDOMAIN = 0x0002;
    private const uint BIF_USENEWUI = 0x0040;
    private const uint BIF_NEWDIALOGSTYLE = 0x0040;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct BROWSEINFO
    {
        public IntPtr hwndOwner;
        public IntPtr pidlRoot;
        public IntPtr pszDisplayName;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string lpszTitle;
        public uint ulFlags;
        public IntPtr lpfn;
        public IntPtr lParam;
        public int iImage;
    }
}
