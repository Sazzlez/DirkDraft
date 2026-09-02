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

    /// <summary>
    /// Guards both dictionaries. All current callers live on the UI thread, but the cache is
    /// handed around freely (view models, fetch continuations) and a corrupted Dictionary fails
    /// as an endless loop, not an exception — not a failure mode worth risking to save a lock.
    /// </summary>
    private readonly Lock _lock = new();

    private readonly Dictionary<(char Kind, int Id), ImageSource> _loaded = [];
    private readonly Dictionary<(char Kind, int Id), DateTime> _misses = [];

    /// <summary>Portrait for a champion, or <see langword="null"/> if it is not cached on disk.</summary>
    public ImageSource? Get(int championId)
        => Get('c', championId, IconDownloader.PathFor(championId));

    /// <summary>Item icon by Riot item id; fetched alongside the build plan that references it.</summary>
    public ImageSource? GetItem(int itemId)
        => Get('i', itemId, Path.Combine(Core.Config.AppPaths.ItemIconDirectory, $"{itemId}.png"));

    /// <summary>Rune icon by perk id; arrives with the big update.</summary>
    public ImageSource? GetRune(int runeId)
        => Get('r', runeId, Path.Combine(Core.Config.AppPaths.RuneIconDirectory, $"{runeId}.png"));

    /// <summary>Summoner-spell icon by spell id; arrives with the big update.</summary>
    public ImageSource? GetSpell(int spellId)
        => Get('s', spellId, Path.Combine(Core.Config.AppPaths.SpellIconDirectory, $"{spellId}.png"));

    private ImageSource? Get(char kind, int id, string path)
    {
        if (id == 0)
            return null;

        var key = (kind, id);

        lock (_lock)
        {
            if (_loaded.TryGetValue(key, out var cached))
                return cached;

            // A miss must not be permanent. The files arrive with a data update or a build fetch,
            // and an id looked up before that finished would otherwise stay blank until restart.
            if (_misses.TryGetValue(key, out var when) && DateTime.UtcNow - when < MissRetryDelay)
                return null;
        }

        var image = Load(path);

        lock (_lock)
        {
            if (image is null)
            {
                _misses[key] = DateTime.UtcNow;
                return null;
            }

            _misses.Remove(key);
            _loaded[key] = image;
            return image;
        }
    }

    /// <summary>
    /// Forgets which lookups came up empty, without dropping what is already decoded.
    /// <para>
    /// Must be called whenever files arrive after a lookup missed them. A miss is remembered for
    /// <see cref="MissRetryDelay"/> to keep every render from stat-ing the disk for an icon that is
    /// not there — but an on-demand download of a handful of small PNGs finishes well inside that
    /// window, so the re-render that follows it would ask the cache and be told "still missing".
    /// The tiles then stayed blank until some later event happened to re-render them.
    /// </para>
    /// </summary>
    public void ForgetMisses()
    {
        lock (_lock)
        {
            _misses.Clear();
        }
    }

    /// <summary>Drops the cache so a data update's new files are picked up.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _loaded.Clear();
            _misses.Clear();
        }
    }

    /// <summary>How many portraits are decoded and how many lookups came up empty.</summary>
    public (int Loaded, int Missing) Counts
    {
        get
        {
            lock (_lock)
            {
                return (_loaded.Count, _misses.Count);
            }
        }
    }

    private static ImageSource? Load(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.DecodePixelWidth = DecodeSize;
            // OnLoad reads the file fully and releases the handle, so an update can overwrite it.
            // IgnoreImageCache matters just as much: WPF keeps its own per-URI decoder cache, and
            // without the flag a re-decode after Clear() silently returned the OLD image for an
            // overwritten file until the app restarted.
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.IgnoreImageCache;
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
