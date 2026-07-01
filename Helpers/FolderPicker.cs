using System.Runtime.InteropServices;

namespace QuickNotes.Helpers;

/// <summary>
/// Native folder picker using the Vista/Windows 7+ IFileOpenDialog.
/// No dependency on System.Windows.Forms required.
/// </summary>
public static class FolderPicker
{
    public static string? Show(string? initialPath = null, string title = "Seleccionar carpeta")
    {
        var dialog = (IFileOpenDialog)new FileOpenDialog();
        try
        {
            dialog.SetOptions(FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_DONTADDTORECENT);
            dialog.SetTitle(title);

            if (!string.IsNullOrWhiteSpace(initialPath))
            {
                if (NativeMethods.SHCreateItemFromParsingName(initialPath, IntPtr.Zero,
                        typeof(IShellItem).GUID, out var item) == 0)
                {
                    dialog.SetFolder(item);
                    Marshal.ReleaseComObject(item);
                }
            }

            int hr = dialog.Show(IntPtr.Zero);
            if (hr < 0)
                return null; // user cancelled or error

            dialog.GetResult(out var result);
            result.GetDisplayName(SIGDN_FILESYSPATH, out var path);
            Marshal.ReleaseComObject(result);

            return path;
        }
        finally
        {
            Marshal.ReleaseComObject(dialog);
        }
    }

    private const uint FOS_PICKFOLDERS = 0x20;
    private const uint FOS_FORCEFILESYSTEM = 0x40;
    private const uint FOS_DONTADDTORECENT = 0x8000000;
    private const uint SIGDN_FILESYSPATH = 0x80058000;

    // ── COM interfaces (minimal VTable-correct declarations) ──

    [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    private class FileOpenDialog { }

    [ComImport, Guid("42f85136-db7e-49cf-bb39-0fb5a5a1c731"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        // IFileDialog methods (in VTable order)
        void _SetFileTypes();                // 3
        void _SetFileTypeIndex();            // 4
        void _GetFileTypeIndex();            // 5
        void _Advise();                      // 6
        void _Unadvise();                    // 7
        void SetOptions(uint options);       // 8
        void _GetOptions();                  // 9
        void _SetDefaultFolder(IShellItem psi); // 10
        void SetFolder(IShellItem psi);      // 11
        void _GetFolder();                   // 12
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title); // 13
        void _SetOkButtonLabel();            // 14
        void _SetFileName();                 // 15
        void _GetFileName();                 // 16
        void _SetFileTypeIndex2();           // 17
        void _GetFileTypeIndex2();           // 18
        void GetResult(out IShellItem ppsi); // 19
        int Show(IntPtr hwndOwner);          // 20 — returns HRESULT
        void _SetClientGuid();               // 21
        void _ClearClientData();             // 22
        void _SetFilter();                   // 23
        void _GetResult2();                  // 24
        void _SetCancelButtonLabel();        // 25
        void _SetFileNameLabel();            // 26
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void _BindToHandler();
        void _GetParent();
        void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        void _GetAttributes();
        void _Compare();
    }

    private static class NativeMethods
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        public static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            out IShellItem ppv);
    }
}
