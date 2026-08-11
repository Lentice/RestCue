using System.Threading;

namespace RestCue.App.Lifecycle;

/// <summary>
/// The session-local right to run RestCue, claimed atomically through a named
/// mutex so a race between two launches decides by the kernel rather than by a
/// check-then-act sequence. The OS releases the mutex when the owning process
/// dies, so a crash never leaves a stale lock behind.
/// </summary>
public sealed class SessionInstanceGuard : IDisposable
{
    public const string MutexName = @"Local\RestCue";

    private readonly Mutex mutex;
    private bool disposed;

    private SessionInstanceGuard(Mutex mutex, bool isPrimary)
    {
        this.mutex = mutex;
        IsPrimary = isPrimary;
    }

    /// <summary>
    /// True when this process won the session's single-instance claim.
    /// </summary>
    public bool IsPrimary { get; }

    /// <summary>
    /// Atomically claims the named mutex. The call creates the mutex when it does
    /// not exist and owns it, or observes the existing mutex without taking
    /// ownership. Only the caller that actually created the mutex is primary.
    /// Unexpected errors are thrown, never reported as a duplicate.
    /// </summary>
    public static SessionInstanceGuard Acquire(string mutexName = MutexName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);

        var mutex = new Mutex(initiallyOwned: true, mutexName, out bool createdNew);
        return new SessionInstanceGuard(mutex, createdNew);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        mutex.Dispose();
    }
}
