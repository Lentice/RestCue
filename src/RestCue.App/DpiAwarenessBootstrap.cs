using System.Runtime.CompilerServices;

namespace RestCue.App;

internal static class DpiAwarenessBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        System.Windows.Forms.Application.SetHighDpiMode(
            System.Windows.Forms.HighDpiMode.PerMonitorV2);
    }
}
