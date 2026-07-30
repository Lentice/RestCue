using System.Windows;

namespace RestCue.App;

/// <summary>
/// Attached behaviours shared by RestCue's secondary windows.
/// </summary>
public static class WindowInteraction
{
    /// <summary>
    /// When true, pressing Escape closes the window. Applied in XAML so that
    /// every dialog-style window gets the same dismissal affordance without
    /// duplicating a key handler in each code-behind.
    /// </summary>
    public static readonly DependencyProperty CloseOnEscapeProperty =
        DependencyProperty.RegisterAttached(
            "CloseOnEscape",
            typeof(bool),
            typeof(WindowInteraction),
            new PropertyMetadata(false, OnCloseOnEscapeChanged));

    public static void SetCloseOnEscape(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(CloseOnEscapeProperty, value);
    }

    public static bool GetCloseOnEscape(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(CloseOnEscapeProperty);
    }

    private static void OnCloseOnEscapeChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window)
            return;

        window.PreviewKeyDown -= OnPreviewKeyDown;

        if (e.NewValue is true)
        {
            window.PreviewKeyDown += OnPreviewKeyDown;
        }
    }

    // Fully qualified: the project enables both WPF and WinForms, which makes
    // the unqualified KeyEventArgs / Key names ambiguous.
    private static void OnPreviewKeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape || sender is not Window window)
            return;

        e.Handled = true;
        window.Close();
    }
}
