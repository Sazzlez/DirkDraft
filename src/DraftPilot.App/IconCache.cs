using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DraftPilot.Meta;

namespace DraftPilot.App;

/// <summary>
/// Champion portraits, decoded once and kept.
/// <para>
/// No eviction policy on purpose: decoded at 48 px a portrait is about 9 KB, so the whole roster
/// costs well under two megabytes. An LRU would add moving parts to save nothing measurable.
/// </para>
/// </summary>
public sealed class IconCache
{
    /// <summary>Decode size. Covers a 24 px slot at 200 % display scaling without resampling artefacts.</summary>
    private const int DecodeSize = 48;

    /// <summary>How long a failed lookup is remembered before the file is checked again.</summary>
    private static readonly TimeSpan MissRetryDelay = TimeSpan.FromSeconds(3);

    private readonly Dictionary<int, ImageSource> _loaded = [];
    private readonly Dictionary<int, DateTime> _misses = [];

    /// <summary>Portrait for a champion, or <see langword="null"/> if it is not cached on disk.</summary>
    public ImageSource? Get(int championId)
    {
        if (championId == 0)
            return null;

        if (_loaded.TryGetValue(championId, out var cached))
            return cached;

        // A miss must not be permanent. The portraits arrive with a data update, and a champion
        // looked up before that update finished would otherwise stay blank until the next restart.
        if (_misses.TryGetValue(championId, out var when) && DateTime.UtcNow - when < MissRetryDelay)
            return null;

        var image = Load(championId);

        if (image is null)
        {
            _misses[championId] = DateTime.UtcNow;
            return null;
        }

        _misses.Remove(championId);
        _loaded[championId] = image;
        return image;
    }

    /// <summary>Drops the cache so a data update's new portraits are picked up.</summary>
    public void Clear()
    {
        _loaded.Clear();
        _misses.Clear();
    }

    /// <summary>How many portraits are decoded and how many lookups came up empty.</summary>
    public (int Loaded, int Missing) Counts => (_loaded.Count, _misses.Count);

    private static ImageSource? Load(int championId)
    {
        var path = IconDownloader.PathFor(championId);

        if (!File.Exists(path))
            return null;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.DecodePixelWidth = DecodeSize;
            // OnLoad reads the file fully and releases the handle, so an update can overwrite it.
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or ArgumentException)
        {
            // A truncated or unreadable file shows as no icon rather than taking the panel down.
            return null;
        }
    }
}
