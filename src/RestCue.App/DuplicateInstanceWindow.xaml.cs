using System.ComponentModel;
using System.Windows;

namespace RestCue.App;

/// <summary>
/// The one-button, non-modal warning shown to a duplicate instance. Confirming —
/// either through the button, or by closing the window at all — raises
/// <see cref="Confirmed"/> so the host can end the duplicate process.
/// </summary>
public sealed partial class DuplicateInstanceWindow : Window
{
    private bool confirmed;

    public DuplicateInstanceWindow()
    {
        InitializeComponent();
    }

    public event EventHandler? Confirmed;

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        CompleteConfirmation();
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing the window is the only other way out, and it must end the
        // process too: a duplicate left running with no tray and no services
        // would be an invisible process with no way to leave.
        CompleteConfirmation();
        base.OnClosing(e);
    }

    private void CompleteConfirmation()
    {
        if (confirmed)
        {
            return;
        }

        confirmed = true;
        Confirmed?.Invoke(this, EventArgs.Empty);
    }
}
