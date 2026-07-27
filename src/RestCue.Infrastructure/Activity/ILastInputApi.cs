namespace RestCue.Infrastructure.Activity;

internal interface ILastInputApi
{
    bool TryGetLastInputTickCount(out uint tickCount);

    uint GetTickCount();
}
