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
        Assert.True(double.IsNaN(restored.WindowWidth));
        Assert.True(double.IsNaN(restored.WindowHeight));
    }

    [Fact]
    public void PlacedWindow_RoundTripsItsBounds()
    {
        var settings = new AppSettings { WindowLeft = -1920.5, WindowTop = 42, WindowWidth = 980, WindowHeight = 712.5 };

        var json = JsonSerializer.Serialize(settings, SettingsJson.Default.AppSettings);
        var restored = JsonSerializer.Deserialize(json, SettingsJson.Default.AppSettings)!;

        Assert.Equal(-1920.5, restored.WindowLeft);
        Assert.Equal(42, restored.WindowTop);
        Assert.Equal(980, restored.WindowWidth);
        Assert.Equal(712.5, restored.WindowHeight);
    }

    /// <summary>
    /// A settings file from before the pin carries <c>"alwaysOnTop": true</c> — the old default,
    /// written back on every save, never chosen by anybody. It must not keep the window pinned.
    /// </summary>
    [Fact]
    public void TheOldAlwaysOnTopDefault_NoLongerPinsTheWindow()
    {
        const string written = """{ "windowLeft": 100, "windowTop": 80, "alwaysOnTop": true }""";

        var restored = JsonSerializer.Deserialize(written, SettingsJson.Default.AppSettings)!;

        Assert.False(restored.KeepOnTop);
        Assert.Equal(100, restored.WindowLeft);
    }

    [Fact]
    public void ThePin_RoundTrips()
    {
        var json = JsonSerializer.Serialize(new AppSettings { KeepOnTop = true }, SettingsJson.Default.AppSettings);

        Assert.True(JsonSerializer.Deserialize(json, SettingsJson.Default.AppSettings)!.KeepOnTop);
    }
}
