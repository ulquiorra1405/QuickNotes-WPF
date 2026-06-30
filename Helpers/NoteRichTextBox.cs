using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace QuickNotes.Helpers;

/// <summary>
/// Extended RichTextBox with scroll helpers and image paste.
/// </summary>
public class NoteRichTextBox : RichTextBox
{
    private ScrollViewer? _scrollViewer;

    /// <summary>
    /// Scroll the viewport to bring the given TextPointer into view
    /// without requiring the control to have focus.
    /// </summary>
    public void BringPointerIntoView(TextPointer ptr)
    {
        if (ptr == null) return;

        var sv = GetScrollViewer();
        if (sv == null) return;

        try
        {
            var rect = ptr.GetCharacterRect(LogicalDirection.Forward);
            var viewHeight = sv.ViewportHeight;
            var viewWidth = sv.ViewportWidth;

            if (rect.IsEmpty) return;

            double newX = sv.HorizontalOffset;
            double newY = sv.VerticalOffset;

            if (rect.Top < sv.VerticalOffset)
                newY = rect.Top - 8;
            else if (rect.Bottom > sv.VerticalOffset + viewHeight)
                newY = rect.Bottom - viewHeight + 8;

            if (rect.Left < sv.HorizontalOffset)
                newX = rect.Left - 8;
            else if (rect.Right > sv.HorizontalOffset + viewWidth)
                newX = rect.Right - viewWidth + 8;

            sv.ScrollToHorizontalOffset(newX);
            sv.ScrollToVerticalOffset(newY);
        }
        catch { }
    }

    private ScrollViewer? GetScrollViewer()
    {
        if (_scrollViewer != null) return _scrollViewer;
        _scrollViewer = FindChild<ScrollViewer>(this);
        return _scrollViewer;
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var found = FindChild<T>(child);
            if (found != null) return found;
        }
        return null;
    }
}
