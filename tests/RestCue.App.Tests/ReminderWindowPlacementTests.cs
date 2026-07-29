using RestCue.App;
using Xunit;

namespace RestCue.App.Tests;

public sealed class ReminderWindowPlacementTests
{
    [Fact]
    public void RightEdge_places_window_on_negative_coordinate_monitor()
    {
        var position = ReminderWindowPlacement.RightEdge(-1920, 0, 0, 2160, 480, 300, 4);

        Assert.Equal(-484, position.X);
        Assert.Equal(930, position.Y);
    }

    [Fact]
    public void RightEdge_clamps_oversized_window_to_work_area_origin()
    {
        var position = ReminderWindowPlacement.RightEdge(-1920, -100, 0, 980, 2400, 1200, 4);

        Assert.Equal(-1920, position.X);
        Assert.Equal(-100, position.Y);
    }

    [Fact]
    public void RightEdge_centers_window_in_work_area()
    {
        var position = ReminderWindowPlacement.RightEdge(1920, 100, 3840, 1180, 600, 400, 4);

        Assert.Equal(3236, position.X);
        Assert.Equal(440, position.Y);
    }
}
