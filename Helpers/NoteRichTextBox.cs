using System;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace QuickNotes.Helpers;

/// <summary>
/// RichTextBox that intercepts Ctrl+V for image paste.
/// Handles both WPF BitmapSource and System.Drawing.Bitmap (Snipping Tool).
/// </summary>
public class NoteRichTextBox : RichTextBox
{
    public event Action<BitmapSource>? ImagePastedWithData;
    public event Action<string>? ImageFilePasted;

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V)
        {
            try
            {
                var data = Clipboard.GetDataObject();
                if (data == null) { base.OnPreviewKeyDown(e); return; }

                // 1. Try WPF native BitmapSource
                if (data.GetDataPresent(typeof(BitmapSource)))
                {
                    var bs = data.GetData(typeof(BitmapSource)) as BitmapSource;
                    if (bs != null)
                    {
                        e.Handled = true;
                        ImagePastedWithData?.Invoke(bs);
                        return;
                    }
                }

                // 2. Try System.Drawing.Bitmap (Snipping Tool, PrintScreen, etc.)
                if (data.GetDataPresent(DataFormats.Bitmap))
                {
                    var raw = data.GetData(DataFormats.Bitmap);
                    if (raw is System.Drawing.Bitmap drawingBmp)
                    {
                        e.Handled = true;
                        ImagePastedWithData?.Invoke(ConvertDrawingToBitmapSource(drawingBmp));
                        drawingBmp.Dispose();
                        return;
                    }
                    else if (raw is System.Drawing.Image drawingImg)
                    {
                        e.Handled = true;
                        ImagePastedWithData?.Invoke(ConvertDrawingToBitmapSource(
                            new System.Drawing.Bitmap(drawingImg)));
                        drawingImg.Dispose();
                        return;
                    }
                }

                // 3. Try GetImage() as fallback
                var img = Clipboard.GetImage();
                if (img != null)
                {
                    e.Handled = true;
                    ImagePastedWithData?.Invoke(img);
                    return;
                }

                // 4. Try file drop
                if (data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = data.GetData(DataFormats.FileDrop) as string[];
                    if (files?.Length > 0 && files[0] != null)
                    {
                        var ext = Path.GetExtension(files[0]).ToLowerInvariant();
                        var validExts = new HashSet<string>
                            { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };
                        if (validExts.Contains(ext))
                        {
                            e.Handled = true;
                            ImageFilePasted?.Invoke(files[0]);
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Clipboard error: {ex.Message}");
                // Models.ErrorLog.Write(ex, "NoteRichTextBox.Paste");
            }
        }

        base.OnPreviewKeyDown(e);
    }

    private static BitmapSource ConvertDrawingToBitmapSource(System.Drawing.Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        ms.Position = 0;
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.StreamSource = ms;
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.EndInit();
        bi.Freeze();
        return bi;
    }
}
