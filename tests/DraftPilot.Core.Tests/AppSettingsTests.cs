using System.Text.Json;
using DraftPilot.Core.Config;
using Xunit;

namespace DraftPilot.Core.Tests;

public class AppSettingsTests
{
    /// <summary>
    /// WindowLeft/Top default to NaN ("not placed yet"). System.Text.Json refuses NaN unless the
    /// context opts in — without that, the first Save before any window placement threw an
    /// ArgumentException past the too-narrow catch filter (the crash log had the proof).
    /// </summary>
    [Fact]
    public void UnplacedWindow_SerialisesAndComesBack()
    {
        var json = JsonSerializer.Serialize(new AppSettings(), SettingsJson.Default.AppSettings);
        var restored = JsonSerializer.Deserialize(json, SettingsJson.Default.AppSettings);

        Assert.NotNull(restored);
        Assert.True(double.IsNaN(restored!.WindowLeft));
        Assert.True(double.IsNaN(restored.WindowTop));
    }

    [Fact]
    public void PlacedWindow_RoundTripsItsPosition()
    {
        var settings = new AppSettings { WindowLeft = -1920.5, WindowTop = 42 };

        var json = JsonSerializer.Serialize(settings, SettingsJson.Default.AppSettings);
        var restored = JsonSerializer.Deserialize(json, SettingsJson.Default.AppSettings)!;

        Assert.Equal(-1920.5, restored.WindowLeft);
        Assert.Equal(42, restored.WindowTop);
    }
}
