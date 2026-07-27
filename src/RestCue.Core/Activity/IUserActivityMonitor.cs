namespace RestCue.Core.Activity;

public interface IUserActivityMonitor
{
    UserActivitySample GetCurrentActivity();
}
