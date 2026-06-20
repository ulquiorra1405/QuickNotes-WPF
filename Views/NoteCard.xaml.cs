using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using QuickNotes.Models;

namespace QuickNotes.Views;

public partial class NoteCard : UserControl
{
    public NoteCard()
    {
        InitializeComponent();
    }

    // === Routed events for MainWindow to handle ===

    public static readonly RoutedEvent PinToggleEvent =
        EventManager.RegisterRoutedEvent("PinToggle", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(NoteCard));
    public event RoutedEventHandler PinToggle
    { add => AddHandler(PinToggleEvent, value); remove => RemoveHandler(PinToggleEvent, value); }

    public static readonly RoutedEvent ColorChangedEvent =
        EventManager.RegisterRoutedEvent("ColorChanged", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(NoteCard));
    public event RoutedEventHandler ColorChanged
    { add => AddHandler(ColorChangedEvent, value); remove => RemoveHandler(ColorChangedEvent, value); }

    public static readonly RoutedEvent TitleChangedEvent =
        EventManager.RegisterRoutedEvent("TitleChanged", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(NoteCard));
    public event RoutedEventHandler TitleChanged
    { add => AddHandler(TitleChangedEvent, value); remove => RemoveHandler(TitleChangedEvent, value); }

    // === Initialization ===

    private void Card_Loaded(object sender, RoutedEventArgs e)
    {
        var fade = AnimationHelper.MakeAnimation(0, 1, 220);
        fade.EasingFunction = new QuadraticEase();
        BeginAnimation(OpacityProperty, fade);

        if (DataContext is Note note)
        {
            var pinBtn = FindVisualChild<Button>(this);
            if (pinBtn != null)
                UpdatePinVisual(pinBtn, note, false);
        }
    }

    // === Pin button ===

    private void PinIndicator_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is Note note)
        {
            note.IsPinned = !note.IsPinned;
            UpdatePinVisual((Button)sender, note, true);
            RaiseEvent(new RoutedEventArgs(PinToggleEvent));
        }
    }

    private void UpdatePinVisual(Button btn, Note note, bool cardHovered)
    {
        var dash = btn.Template.FindName("dashText", btn) as TextBlock;
        var hover = btn.Template.FindName("pinHoverText", btn) as TextBlock;
        var pin = btn.Template.FindName("pinText", btn) as TextBlock;
        if (dash == null || hover == null || pin == null) return;

        double toDash = 0, toHover = 0, toPin = 0;
        if (note.IsPinned) { toPin = 1; }
        else if (cardHovered) { toHover = 1; }
        else { toDash = 1; }

        dash.BeginAnimation(OpacityProperty, AnimationHelper.MakeAnimation(toDash, 150));
        hover.BeginAnimation(OpacityProperty, AnimationHelper.MakeAnimation(toHover, 150));
        pin.BeginAnimation(OpacityProperty, AnimationHelper.MakeAnimation(toPin, 150));
    }

    // === Title ===

    private void Title_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox { IsLoaded: false }) return;
        if (DataContext is Note note)
        {
            note.IsDirty = true;
            RaiseEvent(new RoutedEventArgs(TitleChangedEvent));
        }
    }

    // === Color picker ===

    private void CardCurrentColor_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            var popup = fe.FindName("cardCurrentColor") is Border b
                ? (b.Parent as FrameworkElement)?.FindName("") as Popup
                : null;
            // Use the UserControl's popup directly
            var cardPopup = FindVisualChild<Popup>(this);            if (cardPopup != null)
                cardPopup.IsOpen = !cardPopup.IsOpen;
        }
    }

    private void ColorDot_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border dot && dot.Tag is string color && DataContext is Note note)
        {
            note.Color = color;
            note.IsDirty = true;
            RaiseEvent(new RoutedEventArgs(ColorChangedEvent));

            // Close popup
            var popup = FindVisualChild<Popup>(this);            if (popup != null)
                popup.IsOpen = false;
        }
    }

    // === Helpers ===

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var found = FindVisualChild<T>(child);
            if (found != null) return found;
        }
        return null;
    }
}
