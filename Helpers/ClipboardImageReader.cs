using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace QuickNotes.Helpers;

/// <summary>
/// Reads image data from clipboard using raw Win32 P/Invoke,
/// bypassing WPF's limited clipboard format detection.
/// </summary>
internal static class ClipboardImageReader
{
    private const uint CF_DIB = 8;
    private const uint CF_DIBV5 = 17;
    private const uint CF_BITMAP = 2;

    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern uint GlobalSize(IntPtr hMem);

    public static BitmapSource? GetImageFromClipboard()
    {
        // 1) Try WPF built-in for simplest case
        try
        {
            var wpf = System.Windows.Clipboard.GetImage();
            if (wpf != null) return wpf;
        }
        catch { }

        // 2) Try Win32 DIB/DIBV5
        var dib = TryReadDib(CF_DIBV5) ?? TryReadDib(CF_DIB);
        if (dib != null) return dib;

        // 3) Try Win32 HBITMAP
        return TryReadHBitmap();
    }

    /// <summary>
    /// Reads CF_DIB or CF_DIBV5 and returns a BitmapSource.
    /// </summary>
    private static BitmapSource? TryReadDib(uint format)
    {
        if (!OpenClipboard(IntPtr.Zero)) return null;
        try
        {
            IntPtr hData = GetClipboardData(format);
            if (hData == IntPtr.Zero) return null;

            IntPtr locked = GlobalLock(hData);
            if (locked == IntPtr.Zero) return null;

            try
            {
                uint totalSize = GlobalSize(hData);
                if (totalSize < 40) return null;

                byte[] raw = new byte[totalSize];
                Marshal.Copy(locked, raw, 0, (int)totalSize);

                int biSize = BitConverter.ToInt32(raw, 0);
                if (biSize < 40) return null;

                int width = Math.Abs(BitConverter.ToInt32(raw, 4));
                int height = Math.Abs(BitConverter.ToInt32(raw, 8));
                short bitCount = BitConverter.ToInt16(raw, 14);
                int compression = BitConverter.ToInt32(raw, 16);
                uint sizeImage = (uint)BitConverter.ToInt32(raw, 20);

                if (width <= 0 && height <= 0) return null;

                // Calculate offset to pixel data within the DIB
                int pixelOffset = biSize;
                if (compression == 3) pixelOffset += 12; // BI_BITFIELDS masks

                if (bitCount <= 8 && compression != 3)
                {
                    uint clrUsed = (uint)BitConverter.ToInt32(raw, 32);
                    uint paletteCount = clrUsed > 0 ? clrUsed : (uint)(1 << bitCount);
                    pixelOffset += (int)paletteCount * 4;
                }

                if (pixelOffset >= totalSize) return null;

                int stride = ((width * bitCount + 31) / 32) * 4;
                int pixelDataSize = sizeImage > 0 ? (int)sizeImage : stride * Math.Abs(height);
                int available = (int)(totalSize - pixelOffset);
                if (available <= 0) return null;
                pixelDataSize = Math.Min(pixelDataSize, available);

                // Build BMP in memory: BITMAPFILEHEADER (14) + DIB header + pixels
                using var ms = new MemoryStream(14 + pixelOffset + pixelDataSize);

                // Write BITMAPFILEHEADER
                WriteLE16(ms, 0x4D42); // 'BM'
                WriteLE32(ms, (uint)(14 + pixelOffset + pixelDataSize)); // file size
                WriteLE16(ms, 0); // reserved1
                WriteLE16(ms, 0); // reserved2
                WriteLE32(ms, (uint)(14 + pixelOffset)); // offset to pixel data

                // DIB header + optional data
                ms.Write(raw, 0, pixelOffset);
                // Pixel data
                ms.Write(raw, pixelOffset, pixelDataSize);

                ms.Position = 0;

                var bi = new BitmapImage();
                bi.BeginInit();
                bi.StreamSource = ms;
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            finally { GlobalUnlock(locked); }
        }
        finally { CloseClipboard(); }
    }

    /// <summary>
    /// Reads CF_BITMAP (HBITMAP) and converts via WPF interop.
    /// </summary>
    private static BitmapSource? TryReadHBitmap()
    {
        if (!OpenClipboard(IntPtr.Zero)) return null;
        try
        {
            IntPtr hData = GetClipboardData(CF_BITMAP);
            if (hData == IntPtr.Zero) return null;

            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hData,
                IntPtr.Zero,
                System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            if (source != null) source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
        finally { CloseClipboard(); }
    }

    private static void WriteLE16(Stream s, ushort v)
    {
        s.WriteByte((byte)(v & 0xFF));
        s.WriteByte((byte)((v >> 8) & 0xFF));
    }

    private static void WriteLE32(Stream s, uint v)
    {
        s.WriteByte((byte)(v & 0xFF));
        s.WriteByte((byte)((v >> 8) & 0xFF));
        s.WriteByte((byte)((v >> 16) & 0xFF));
        s.WriteByte((byte)((v >> 24) & 0xFF));
    }
}
