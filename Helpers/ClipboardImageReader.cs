using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuickNotes.Helpers;

/// <summary>
/// Reads image data from clipboard using raw Win32 P/Invoke,
/// bypassing WPF's limited clipboard format detection.
/// </summary>
internal static class ClipboardImageReader
{
    // Win32 Clipboard Format Constants
    private const uint CF_BITMAP = 2;
    private const uint CF_DIB = 8;
    private const uint CF_DIBV5 = 17;
    private const uint CF_ENHMETAFILE = 14;

    // BITMAPFILEHEADER for constructing a BMP from DIB data
    private struct BITMAPFILEHEADER
    {
        public ushort bfType;      // 'BM'
        public uint bfSize;
        public ushort bfReserved1;
        public ushort bfReserved2;
        public uint bfOffBits;
    }

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
    /// Returns null if no image is available.
    /// </summary>
    public static BitmapSource? GetImageFromClipboard()
    {
        if (!OpenClipboard(IntPtr.Zero))
            return null;

        try
        {
            // Try formats in order of preference
            BitmapSource? result;

            result = ReadDibFormat(CF_DIBV5);
            if (result != null) return result;

            result = ReadDibFormat(CF_DIB);
            if (result != null) return result;

            result = ReadBitmapFormat();
            if (result != null) return result;

            return null;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>
    /// Reads CF_DIB or CF_DIBV5 format data and converts to BitmapSource.
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
            uint size = GlobalSize(hData);
            if (size == 0) return null;

            // Read DIB data
            byte[] dibData = new byte[size];
            Marshal.Copy(locked, dibData, 0, (int)size);

            // DIB data is: BITMAPINFOHEADER (or BITMAPV5HEADER) + pixel data
            // We need to wrap it in a BMP file structure for BitmapImage or BitmapSource.Create

            // Get header info
            int biSize = BitConverter.ToInt32(dibData, 0);
            int width = Math.Abs(BitConverter.ToInt32(dibData, 4));
            int height = Math.Abs(BitConverter.ToInt32(dibData, 8));
            short planes = BitConverter.ToInt16(dibData, 12);
            short bitCount = BitConverter.ToInt16(dibData, 14);
            int compression = (biSize >= 40) ? BitConverter.ToInt32(dibData, 16) : 0;

            // Calculate stride
            int stride = ((width * bitCount + 31) / 32) * 4;

            // Calculate offset to pixel data
            // If biSize > 40 (e.g., BITMAPV5HEADER = 124), header is larger
            uint headerSize = (uint)biSize;

            // For BI_BITFIELDS compression, there are 3 DWORD masks after header
            if (compression == 3) // BI_BITFIELDS
            {
                headerSize += 12; // 3 color masks
            }

            // Check if we have valid pixel data
            if (headerSize >= size) return null;

            // Create BMP file in memory
            int pixelDataOffset = (int)(14 + headerSize); // 14 = BITMAPFILEHEADER size
            int bmpFileSize = pixelDataOffset + (stride * height);

            byte[] bmpData = new byte[bmpFileSize];

            // BITMAPFILEHEADER
            bmpData[0] = (byte)'B';
            bmpData[1] = (byte)'M';
            BitConverter.GetBytes((uint)bmpFileSize).CopyTo(bmpData, 2);
            BitConverter.GetBytes((uint)pixelDataOffset).CopyTo(bmpData, 10);

            // DIB header + pixel data
            Array.Copy(dibData, 0, bmpData, 14, (int)Math.Min(headerSize, size));

            // Copy pixel data (handling top-down vs bottom-up)
            // DIB is typically bottom-up (negative height in header = top-down)
            int dibHeight = BitConverter.ToInt32(dibData, 8);
            int dibPixelOffset = (int)headerSize;
            int dibPixelSize = (int)(size - headerSize);

            // The pixel data from clipboard might be complete or partial
            int copySize = Math.Min(dibPixelSize, stride * height);
            Array.Copy(dibData, dibPixelOffset, bmpData, pixelDataOffset, copySize);

            // Load as BitmapImage
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.StreamSource = new MemoryStream(bmpData);
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
    /// Reads CF_BITMAP (HBITMAP) format by converting to DIB first.
    /// This is less commonly used but serves as a last resort.
    /// </summary>
    private static BitmapSource? ReadBitmapFormat()
    {
        IntPtr hData = GetClipboardData(CF_BITMAP);
        if (hData == IntPtr.Zero)
            return null;

        try
        {
            // Use WPF's built-in conversion from HBITMAP
            // GetImageSourceFromHbitmap handles the conversion
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
}
