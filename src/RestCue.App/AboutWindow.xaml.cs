using System.Diagnostics;
using System.Reflection;
using System.Windows;

namespace RestCue.App;

public sealed partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"版本 {version}";

        var process = Process.GetCurrentProcess();
        var memory = process.WorkingSet64 / (1024.0 * 1024.0);
        TechInfoText.Text = $".NET 10 (WPF) | 記憶體使用量: {memory:F1} MB | {Environment.OSVersion}";
    }
}
