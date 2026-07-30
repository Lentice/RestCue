using Xunit;

namespace RestCue.App.Tests;

/// <summary>
/// The shortcut must use a combination Windows does not reserve, and a failed registration
/// must be discoverable. The operating-system registration call itself needs manual
/// verification on Windows 11 — see the ticket.
/// </summary>
public sealed class CancelBreakShortcutTests
{
    [Fact]
    public void Successful_registration_records_nothing()
    {
        var diagnostics = new List<string>();

        bool registered = CancelBreakShortcut.TryRegister(() => true, diagnostics.Add);

        Assert.True(registered);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Failed_registration_records_a_diagnostic_naming_the_combination()
    {
        var diagnostics = new List<string>();

        bool registered = CancelBreakShortcut.TryRegister(() => false, diagnostics.Add);

        Assert.False(registered);
        string diagnostic = Assert.Single(diagnostics);
        Assert.Contains(CancelBreakShortcut.Description, diagnostic);
    }

    [Fact]
    public void Combination_is_not_the_reserved_task_manager_shortcut()
    {
        const uint ctrlShift = CancelBreakShortcut.ModControl | CancelBreakShortcut.ModShift;
        const uint vkEscape = 0x1B;

        // Ctrl+Shift+Esc belongs to Task Manager; RegisterHotKey cannot claim it.
        Assert.False(
            CancelBreakShortcut.Modifiers == ctrlShift && CancelBreakShortcut.VirtualKey == vkEscape,
            "The break-cancel shortcut must not be the Task Manager combination.");
    }

    [Fact]
    public void Combination_requires_all_three_modifiers()
    {
        // Three modifiers keep it clear of both OS reservations and ordinary typing.
        Assert.True((CancelBreakShortcut.Modifiers & CancelBreakShortcut.ModControl) != 0);
        Assert.True((CancelBreakShortcut.Modifiers & CancelBreakShortcut.ModAlt) != 0);
        Assert.True((CancelBreakShortcut.Modifiers & CancelBreakShortcut.ModShift) != 0);
    }

    [Fact]
    public void Description_matches_the_registered_combination()
    {
        Assert.Equal("Ctrl+Alt+Shift+B", CancelBreakShortcut.Description);
        Assert.Equal((uint)'B', CancelBreakShortcut.VirtualKey);
    }
}
