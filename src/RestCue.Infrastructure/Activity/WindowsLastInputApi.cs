using System.Runtime.InteropServices;

namespace RestCue.Infrastructure.Activity;

internal sealed class WindowsLastInputApi : ILastInputApi
{
    public bool TryGetLastInputTickCount(out uint tickCount)
    {
        var info = new LastInputInfo
        {
            Size = (uint)Marshal.SizeOf<LastInputInfo>()
        };

        bool succeeded = GetLastInputInfo(ref info);
        tickCount = info.TickCount;
        return succeeded;
    }

    public uint GetTickCount() => NativeGetTickCount();

    [DllImport("user32.dll", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo lastInputInfo);

    [DllImport("kernel32.dll", EntryPoint = "GetTickCount", SetLastError = false)]
    private static extern uint NativeGetTickCount();

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint TickCount;
    }
}
