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
/// </summary>
public class NoteRichTextBox : RichTextBox
{
    public event Action? ImagePasted;
    public event Action<string>? ImageFilePasted;

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V)
        {
            // Try every possible image detection

            // 1. Check from Clipboard object directly
            try
            {
                var data = Clipboard.GetDataObject();
                if (data != null)
                {
                    // Check all known image formats
                    string[] imgFormats = [DataFormats.Bitmap, DataFormats.Dib, "PNG",
                        "System.Drawing.Bitmap", "System.Windows.Media.Imaging.BitmapSource",
                        "Bitmap", "DeviceIndependentBitmap"];

                    foreach (var fmt in imgFormats)
                    {
                        if (data.GetDataPresent(fmt))
                        {
                            e.Handled = true;
                            var img = Clipboard.GetImage();
                            if (img != null)
                            {
                                ImagePasted?.Invoke();
                            }
                            return;
                        }
                    }
                }

                // 2. Check for image files in clipboard
                if (Clipboard.ContainsFileDropList())
                {
                    var files = Clipboard.GetFileDropList();
                    if (files.Count > 0 && files[0] != null)
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
            catch
            {
                // clipboard unavailable
            }
        }

        base.OnPreviewKeyDown(e);
    }
}
