using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace QuickNotes.Helpers;

/// <summary>
/// Reads image data from clipboard using raw Win32 P/Invoke,
/// bypassing WPF's limited clipboard format detection.
/// Handles CF_DIBV5 and CF_DIB (the formats used by Snipping Tool,
/// PrintScreen, browser image copy, etc.).
/// </summary>
internal static class ClipboardImageReader
{
    // Win32 Clipboard Format Constants
    private const uint CF_DIB = 8;
    private const uint CF_DIBV5 = 17;
    private const uint CF_BITMAP = 2;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GlobalSize(IntPtr hMem);

    /// <summary>
    /// Attempts to read a BitmapSource from clipboard using Win32 APIs.
    /// Tries CF_DIBV5, CF_DIB, and CF_BITMAP formats.
    /// </summary>
    public static BitmapSource? GetImageFromClipboard()
    {
        if (!OpenClipboard(IntPtr.Zero))
            return null;

        try
        {
            // Try DIBV5 first (newer, common on Win10+)
            var result = ReadDibFormat(CF_DIBV5);
            if (result != null) return result;

            // Try standard DIB
            result = ReadDibFormat(CF_DIB);
            if (result != null) return result;

            // Try HBITMAP (less common)
            return ReadHBitmapFormat();
        }
        catch
        {
            return null;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>
    /// Reads CF_DIB or CF_DIBV5 from clipboard, wraps in BMP file header,
    /// and loads as BitmapImage.
    /// </summary>
    private static BitmapSource? ReadDibFormat(uint format)
    {
        IntPtr hData = GetClipboardData(format);
        if (hData == IntPtr.Zero)
            return null;

        IntPtr locked = GlobalLock(hData);
        if (locked == IntPtr.Zero)
            return null;

        try
        {
            uint totalSize = GlobalSize(hData);
            if (totalSize < 40) return null; // minimum BITMAPINFOHEADER

            byte[] dibData = new byte[totalSize];
            Marshal.Copy(locked, dibData, 0, (int)totalSize);

            // Parse BITMAPINFOHEADER fields
            int biSize = BitConverter.ToInt32(dibData, 0);
            if (biSize < 40) return null;

            int width = Math.Abs(BitConverter.ToInt32(dibData, 4));
            int height = Math.Abs(BitConverter.ToInt32(dibData, 8));
            short bitCount = BitConverter.ToInt16(dibData, 14);
            int compression = BitConverter.ToInt32(dibData, 16);
            uint sizeImage = (uint)BitConverter.ToInt32(dibData, 20);
            uint clrUsed = (uint)BitConverter.ToInt32(dibData, 32);

            // Calculate pixel data offset within DIB
            int pixelOffset = biSize;

            // BI_BITFIELDS adds 3 DWORD color masks
            if (compression == 3)
                pixelOffset += 12;

            // Color table for <= 8bpp (when not using BITFIELDS masks area)
            if (bitCount <= 8 && compression != 3)
            {
                uint paletteCount = clrUsed > 0 ? clrUsed : (uint)(1 << bitCount);
                pixelOffset += (int)paletteCount * 4;
            }

            int stride = ((width * bitCount + 31) / 32) * 4;
            int absHeight = Math.Abs(height);

            // Pixel data size (use biSizeImage if provided, else calculate)
            int pixelDataSize = sizeImage > 0 ? (int)sizeImage : stride * absHeight;

            // Sanity check
            if (pixelOffset + pixelDataSize > totalSize)
                pixelDataSize = (int)(totalSize - pixelOffset);

            if (pixelDataSize <= 0)
                return null;

            // Build BMP file: BITMAPFILEHEADER (14) + DIB header + pixel data
            int bmpSize = 14 + pixelOffset + pixelDataSize;
            byte[] bmp = new byte[bmpSize];

            // BITMAPFILEHEADER
            bmp[0] = (byte)'B';
            bmp[1] = (byte)'M';
            WriteLE32(bmp, 2, bmpSize);               // bfSize
            WriteLE32(bmp, 10, 14 + pixelOffset);     // bfOffBits

            // Copy DIB header (biSize bytes)
            Array.Copy(dibData, 0, bmp, 14, pixelOffset);

            // Copy pixel data
            Array.Copy(dibData, pixelOffset, bmp, 14 + pixelOffset, pixelDataSize);

            // Load BitmapImage from stream
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.StreamSource = new MemoryStream(bmp);
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch
        {
            return null;
        }
        finally
        {
            GlobalUnlock(locked);
        }
    }

    /// <summary>
    /// Reads CF_BITMAP (HBITMAP) format. Only used as last resort.
    /// </summary>
    private static BitmapSource? ReadHBitmapFormat()
    {
        IntPtr hData = GetClipboardData(CF_BITMAP);
        if (hData == IntPtr.Zero)
            return null;

        try
        {
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hData,
                IntPtr.Zero,
                System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }

    private static void WriteLE32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}
