using System;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace QuickNotes.Helpers;

/// <summary>
/// RichTextBox that intercepts Ctrl+V in PreviewKeyDown for images.
/// Fires ImagePastedWithData instead of ImagePasted to pass the image directly.
/// </summary>
public class NoteRichTextBox : RichTextBox
{
    /// <summary>Fired with the pasted BitmapSource (Snipping Tool, screenshots, etc.).</summary>
    public event Action<BitmapSource>? ImagePastedWithData;

    /// <summary>Fired when an image file is pasted.</summary>
    public event Action<string>? ImageFilePasted;

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V)
        {
            try
            {
                var data = Clipboard.GetDataObject();
                if (data != null)
                {
                    // Check for BitmapSource (WPF native format)
                    if (data.GetDataPresent(typeof(BitmapSource)))
                    {
                        var img = data.GetData(typeof(BitmapSource)) as BitmapSource;
                        if (img != null)
                        {
                            e.Handled = true;
                            ImagePastedWithData?.Invoke(img);
                            return;
                        }
                    }

                    // Check for Bitmap / DIB (Snipping Tool, screenshots)
                    if (data.GetDataPresent(DataFormats.Bitmap))
                    {
                        // Try to convert to BitmapSource
                        try
                        {
                            var img = Clipboard.GetImage();
                            if (img != null)
                            {
                                e.Handled = true;
                                ImagePastedWithData?.Invoke(img);
                                return;
                            }
                        }
                        catch { }
                    }

                    // Check for image files
                    if (data.GetDataPresent(DataFormats.FileDrop))
                    {
                        var files = data.GetData(DataFormats.FileDrop) as string[];
                        if (files != null && files.Length > 0 && files[0] != null)
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
            }
            catch
            {
                // clipboard unavailable
            }
        }

        base.OnPreviewKeyDown(e);
    }
}
