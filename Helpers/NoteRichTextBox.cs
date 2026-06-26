using System;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace QuickNotes.Helpers;

/// <summary>
/// RichTextBox with image paste support via CommandManager preview.
/// Uses multiple clipboard format checks for reliable image detection.
/// </summary>
public class NoteRichTextBox : RichTextBox
{
    public event Action? ImagePasted;
    public event Action<string>? ImageFilePasted;

    public NoteRichTextBox()
    {
        // PreviewExecuted runs BEFORE the control's own command bindings
        CommandManager.AddPreviewExecutedHandler(this, OnPreviewPaste);

        // Backup: AddPastingHandler catches paste at the data level
        DataObject.AddPastingHandler(this, OnPastingHandler);
    }

    private void OnPastingHandler(object sender, DataObjectPastingEventArgs e)
    {
        // Check all possible image formats on the data object
        var formats = e.DataObject.GetFormats();
        foreach (var fmt in formats)
        {
            if (fmt.Contains("Bitmap", StringComparison.OrdinalIgnoreCase) ||
                fmt.Contains("Image", StringComparison.OrdinalIgnoreCase) ||
                fmt.Contains("PNG", StringComparison.OrdinalIgnoreCase) ||
                fmt.Contains("JPEG", StringComparison.OrdinalIgnoreCase) ||
                fmt.Contains("GIF", StringComparison.OrdinalIgnoreCase) ||
                fmt.Contains("DIB", StringComparison.OrdinalIgnoreCase) ||
                fmt.Contains("Picture", StringComparison.OrdinalIgnoreCase) ||
                fmt == DataFormats.Bitmap ||
                fmt == DataFormats.Dib)
            {
                e.CancelCommand();
                Dispatcher.BeginInvoke(() => ImagePasted?.Invoke());
                return;
            }
        }

        // Check for image files
        if (e.DataObject.GetDataPresent(DataFormats.FileDrop))
        {
            var files = e.DataObject.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0)
            {
                var ext = Path.GetExtension(files[0]).ToLowerInvariant();
                var validExts = new HashSet<string>
                    { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };
                if (validExts.Contains(ext))
                {
                    e.CancelCommand();
                    var path = files[0];
                    Dispatcher.BeginInvoke(() => ImageFilePasted?.Invoke(path));
                    return;
                }
            }
        }
    }

    private void OnPreviewPaste(object? sender, ExecutedRoutedEventArgs e)
    {
        if (e.Command != ApplicationCommands.Paste) return;

        try
        {
            // Try every possible approach to detect image in clipboard
            var dataObject = Clipboard.GetDataObject();
            if (dataObject == null) return;

            // Check all known image formats
            if (dataObject.GetDataPresent(DataFormats.Bitmap) ||
                dataObject.GetDataPresent(DataFormats.Dib) ||
                dataObject.GetDataPresent("PNG") ||
                dataObject.GetDataPresent("System.Drawing.Bitmap") ||
                dataObject.GetDataPresent("System.Windows.Media.Imaging.BitmapSource"))
            {
                e.Handled = true;
                var img = Clipboard.GetImage();
                if (img != null)
                {
                    ImagePasted?.Invoke();
                    return;
                }
            }

            // Check for image files
            if (dataObject.GetDataPresent(DataFormats.FileDrop))
            {
                var files = dataObject.GetData(DataFormats.FileDrop) as string[];
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
        catch
        {
            // clipboard unavailable
        }
    }
}
