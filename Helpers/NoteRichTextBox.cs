using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Collections.Specialized;
using System.IO;

namespace QuickNotes.Helpers;

/// <summary>
/// RichTextBox with image paste support via CommandManager preview.
/// </summary>
public class NoteRichTextBox : RichTextBox
{
    /// <summary>Fired when an image is pasted from the clipboard.</summary>
    public event Action? ImagePasted;

    /// <summary>Fired when an image file is pasted (copied file).</summary>
    public event Action<string>? ImageFilePasted;

    public NoteRichTextBox()
    {
        // Register at CommandManager level — fires BEFORE the control's own bindings
        CommandManager.AddPreviewExecutedHandler(this, OnPreviewPaste);
    }

    private void OnPreviewPaste(object? sender, ExecutedRoutedEventArgs e)
    {
        if (e.Command != ApplicationCommands.Paste) return;

        try
        {
            // Check for bitmap image in clipboard
            if (Clipboard.ContainsImage())
            {
                var img = Clipboard.GetImage();
                if (img != null)
                {
                    e.Handled = true;
                    ImagePasted?.Invoke();
                    return;
                }
            }

            // Check for image files in clipboard
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
            // clipboard unavailable, fall through to base
        }
    }
}
