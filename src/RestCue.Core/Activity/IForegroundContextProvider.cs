namespace RestCue.Core.Activity;

public interface IForegroundContextProvider
{
    ForegroundContext GetCurrentContext();
}