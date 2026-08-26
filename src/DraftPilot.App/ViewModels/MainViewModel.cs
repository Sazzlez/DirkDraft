using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using DraftPilot.Core.Config;
using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using DraftPilot.Core.Lcu;
using DraftPilot.Meta;
using DraftPilot.Meta.OpGg;

namespace DraftPilot.App.ViewModels;

/// <summary>Drives the colour of the connection dot in the title bar.</summary>
public enum ConnectionTone
{
    /// <summary>Client not running, or League not found at all.</summary>
    Off,

    /// <summary>Client is there but the event socket is not subscribed.</summary>
    Warn,

    /// <summary>Connected and listening.</summary>
    Ok,
}

/// <summary>
/// The panel's single view model. Owns the live connection, the meta data and the recommendation
/// engine, and marshals every client push onto the UI thread.
/// </summary>
public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly Dispatcher _dispatcher = Application.Current.Dispatcher;
    private readonly AppSettings _settings;
    private readonly SnapshotStore _store = new();
    private readonly TraitTable _traits = TraitTable.Load();
    private readonly SeatPriors _seatPriors = SeatPriors.Load();
    private readonly IconCache _icons = new();

    /// <summary>Lanes the user fixed by hand, keyed by seat.</summary>
    private readonly Dictionary<long, Lane> _manualLanes = [];

    private readonly BuildCache _buildCache = new();

    /// <summary>Enemy champion ids whose live counter data was already fetched this draft.</summary>
    private readonly HashSet<int> _liveFetched = [];

    private BuildPlan? _build;
    private bool _isFetchingDraftData;
    private bool _buildCardClosed;

    /// <summary>The (champion, lane, opponent) the cache was last probed for, to probe only once.</summary>
    private (int Champion, Lane Lane, int Opponent) _buildProbe;

    /// <summary>Revealed enemies whose counters were not fetched yet; recomputed on every render.</summary>
    private int _pendingEnemyCount;

    /// <summary>My locked pick, its lane and its direct opponent — the build fetch's inputs.</summary>
    private (int Champion, Lane Lane, int Opponent)? _buildContext;

    /// <summary>Enemy composition from the last render, feeding the situational build hints.</summary>
    private CompProfile _enemyComp = CompProfile.Empty;

    private readonly CancellationTokenSource _lifetime = new();
    private LiveSessionSource? _source;
    private DraftTracker _tracker;

    private MetaLookup _meta;
    private LanePredictor _predictor;
    private Recommender _recommender;

    private CancellationTokenSource? _updateScope;
    private DraftState _state = DraftState.Inactive;
    private RecommendationTarget? _target;
    private HashSet<int>? _selectable;
    private bool _selectableFetched;
    private bool _selectableRequestRunning;

    private string _statusText = "Starte…";
    private string _phaseText = string.Empty;
    private string _secondsText = string.Empty;
    private double _phaseFraction;
    private ConnectionTone _statusTone = ConnectionTone.Off;
    private string _emptyHint = "Warte auf den League-Client.";
    private string _turnText = string.Empty;
    private string _listHeader = "Empfehlungen";
    private string _allyCompText = string.Empty;
    private string _enemyCompText = string.Empty;
    private string _snapshotText = string.Empty;
    private string _presetName = "Meta";
    private string _updateStatus = string.Empty;
    private bool _isUpdating;
    private bool _isDraftActive;
    private bool _isPinned;
    private bool _hasRecommendations;
    private bool _isMyTurn;
    private bool _isTimeCritical;
    private bool _hasMeta;
    private string _snapshotDetail = string.Empty;
    private double _updateFraction;
    private string _draftFetchText = string.Empty;
    private bool _showDraftFetchButton;
    private bool _hasBuild;
    private bool _showBuildSection;
    private bool _showBuildClose;
    private string _buildTitle = string.Empty;
    private string _buildSubtitle = string.Empty;
    private string _buildRunesPrimary = string.Empty;
    private string _buildRunesSecondary = string.Empty;
    private string _buildStarter = string.Empty;
    private string _buildSpells = string.Empty;
    private string _buildHint = string.Empty;
    private bool _isBuildExpanded;

    /// <summary>Why the snapshot could not be used, if it could not. Shown in the footer.</summary>
    private string? _snapshotProblem;

    /// <summary>The phase deadline, captured from the last client update and counted down locally.</summary>
    private PhaseClock _clock = PhaseClock.Stopped;
    private DispatcherTimer? _countdown;
    private FileSystemWatcher? _snapshotWatcher;
    private DispatcherTimer? _snapshotRetry;

    /// <summary>Collapses the watcher's event bursts — one save fires Created, Changed and Renamed.</summary>
    private bool _reloadQueued;

    /// <param name="source">
    /// Overrides the live client connection. Used by the development harness to drive the panel
    /// from a recording.
    /// </param>
    public MainViewModel(ISessionSource? source = null)
    {
        _settings = AppSettings.Load();
        _presetName = _settings.Preset;

        _meta = LoadMeta();
        _predictor = new LanePredictor(_meta, _seatPriors);
        _recommender = new Recommender(_meta, _traits);

        // The live source is kept separately because only it can answer which champions the local
        // player owns; a replayed recording has no client to ask.
        _source = source is null ? new LiveSessionSource(_settings.LockfilePath) : null;
        _tracker = new DraftTracker(source ?? _source!);
        _tracker.Changed += OnTrackerChanged;

        Allies.Resize(5, () => new SlotViewModel());
        Enemies.Resize(5, () => new SlotViewModel());

        // Seed the labels so the rows are never nameless, even before the first session arrives.
        for (var i = 0; i < 5; i++)
        {
            Allies[i].Label = $"Mitspieler {i + 1}";
            Enemies[i].Label = $"Gegner {i + 1}";
        }

        SelectSlotCommand = new RelayCommand(parameter =>
        {
            if (parameter is SlotViewModel slot && slot.CellId >= 0)
            {
                _tracker.Turns.SelectManually(slot.CellId, _state);
                _tracker.RefreshTarget();
            }
        });

        TogglePinCommand = new RelayCommand(_ =>
        {
            if (IsPinned)
            {
                _tracker.Turns.Unpin();
            }
            else if (_target is not null)
            {
                _tracker.Turns.Pin(_target.Slot.CellId);
            }

            IsPinned = _tracker.Turns.IsPinned;
            _tracker.RefreshTarget();
        });

        SetPresetCommand = new RelayCommand(parameter =>
        {
            if (parameter is string name)
            {
                PresetName = name;
                _settings.Preset = name;
                _settings.Save();
                Refresh();
            }
        });

        UpdateDataCommand = new RelayCommand(_ => _ = RunUpdateAsync(), _ => CanUpdate);
        CancelUpdateCommand = new RelayCommand(_ => _updateScope?.Cancel(), _ => IsUpdating);
        FetchDraftDataCommand = new RelayCommand(_ => _ = FetchDraftDataAsync(), _ => CanFetchDraftData);
        CloseBuildCommand = new RelayCommand(_ =>
        {
            _buildCardClosed = true;
            UpdateBuildSection();
        });

        UpdateSnapshotText();
        WatchSnapshotFile();

        // A snapshot that is absent or broken at start-up may well be fine a moment later.
        SetSnapshotRetryRunning(_meta.IsEmpty);
    }

    public ObservableCollection<SlotViewModel> Allies { get; } = [];

    public ObservableCollection<SlotViewModel> Enemies { get; } = [];

    public ObservableCollection<RecommendationViewModel> Recommendations { get; } = [];

    public ObservableCollection<string> Warnings { get; } = [];

    public RelayCommand SelectSlotCommand { get; }

    public RelayCommand TogglePinCommand { get; }

    public RelayCommand SetPresetCommand { get; }

    public RelayCommand UpdateDataCommand { get; }

    public RelayCommand CancelUpdateCommand { get; }

    public RelayCommand FetchDraftDataCommand { get; }

    public RelayCommand CloseBuildCommand { get; }

    public IReadOnlyList<string> PresetNames { get; } = [.. ScoreWeights.Presets.Select(preset => preset.Name)];

    /// <summary>Raised when champion select starts or ends, so the window can show or hide itself.</summary>
    public event Action<bool>? DraftActiveChanged;

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public string PhaseText
    {
        get => _phaseText;
        private set => Set(ref _phaseText, value);
    }

    /// <summary>Seconds left, on its own so it can be shown as a large figure.</summary>
    public string SecondsText
    {
        get => _secondsText;
        private set => Set(ref _secondsText, value);
    }

    /// <summary>Share of the phase still to run, 0 to 1, for the timer bar.</summary>
    public double PhaseFraction
    {
        get => _phaseFraction;
        private set => Set(ref _phaseFraction, value);
    }

    public ConnectionTone StatusTone
    {
        get => _statusTone;
        private set => Set(ref _statusTone, value);
    }

    /// <summary>What to show in place of the list when there is nothing to advise on.</summary>
    public string EmptyHint
    {
        get => _emptyHint;
        private set => Set(ref _emptyHint, value);
    }

    public string TurnText
    {
        get => _turnText;
        private set => Set(ref _turnText, value);
    }

    public string ListHeader
    {
        get => _listHeader;
        private set => Set(ref _listHeader, value);
    }

    public string AllyCompText
    {
        get => _allyCompText;
        private set => Set(ref _allyCompText, value);
    }

    public string EnemyCompText
    {
        get => _enemyCompText;
        private set => Set(ref _enemyCompText, value);
    }

    public string SnapshotText
    {
        get => _snapshotText;
        private set => Set(ref _snapshotText, value);
    }

    public string PresetName
    {
        get => _presetName;
        private set => Set(ref _presetName, value);
    }

    public string UpdateStatus
    {
        get => _updateStatus;
        private set => Set(ref _updateStatus, value);
    }

    public bool IsUpdating
    {
        get => _isUpdating;
        private set
        {
            if (!Set(ref _isUpdating, value))
                return;

            Raise(nameof(CanUpdate));
            UpdateDataCommand.RaiseCanExecuteChanged();
            CancelUpdateCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsDraftActive
    {
        get => _isDraftActive;
        private set
        {
            if (!Set(ref _isDraftActive, value))
                return;

            Raise(nameof(CanUpdate));
            UpdateDataCommand.RaiseCanExecuteChanged();
            DraftActiveChanged?.Invoke(value);
        }
    }

    /// <summary>
    /// Updating is blocked while a draft is running: no network, CPU or memory spike in the middle
    /// of champion select.
    /// </summary>
    public bool CanUpdate => !IsUpdating && !IsDraftActive;

    public bool IsPinned
    {
        get => _isPinned;
        private set => Set(ref _isPinned, value);
    }

    /// <summary>False while the empty-state hint should take the place of the list.</summary>
    public bool HasRecommendations
    {
        get => _hasRecommendations;
        private set => Set(ref _hasRecommendations, value);
    }

    /// <summary>The local player is on the clock; the turn card lights up for it.</summary>
    public bool IsMyTurn
    {
        get => _isMyTurn;
        private set => Set(ref _isMyTurn, value);
    }

    /// <summary>Ten seconds or less on the clock; number and bar switch to the warning colour.</summary>
    public bool IsTimeCritical
    {
        get => _isTimeCritical;
        private set => Set(ref _isTimeCritical, value);
    }

    /// <summary>Whether usable meta data is loaded; drives the tick in the idle checklist.</summary>
    public bool HasMeta
    {
        get => _hasMeta;
        private set => Set(ref _hasMeta, value);
    }

    /// <summary>Tooltip behind the snapshot line: the technical cause and what was found on disk.</summary>
    public string SnapshotDetail
    {
        get => _snapshotDetail;
        private set => Set(ref _snapshotDetail, value);
    }

    /// <summary>Progress of the running data update, 0 to 1.</summary>
    public double UpdateFraction
    {
        get => _updateFraction;
        private set => Set(ref _updateFraction, value);
    }

    /// <summary>
    /// Which build is on screen. Exists because an old instance is indistinguishable from a new one
    /// without it, and that ambiguity has already cost a debugging round.
    /// </summary>
    public string BuildInfoText { get; } = DescribeBuild();

    // ----- Draft live data (fetched only on the button) -------------------------------------

    public bool IsFetchingDraftData
    {
        get => _isFetchingDraftData;
        private set
        {
            if (Set(ref _isFetchingDraftData, value))
                FetchDraftDataCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Label of the draft button, e.g. "OP.GG: 3 Gegner + Build laden".</summary>
    public string DraftFetchText
    {
        get => _draftFetchText;
        private set => Set(ref _draftFetchText, value);
    }

    public bool ShowDraftFetchButton
    {
        get => _showDraftFetchButton;
        private set => Set(ref _showDraftFetchButton, value);
    }

    public bool CanFetchDraftData
        => !IsFetchingDraftData && !_meta.IsEmpty && _state.IsActive && PendingFetch().Total > 0;

    // ----- Build panel ----------------------------------------------------------------------

    public bool HasBuild
    {
        get => _hasBuild;
        private set => Set(ref _hasBuild, value);
    }

    /// <summary>The build block in the draft view and the stand-alone card after it.</summary>
    public bool ShowBuildSection
    {
        get => _showBuildSection;
        private set => Set(ref _showBuildSection, value);
    }

    /// <summary>The close glyph only makes sense on the post-draft card.</summary>
    public bool ShowBuildClose
    {
        get => _showBuildClose;
        private set => Set(ref _showBuildClose, value);
    }

    public string BuildTitle
    {
        get => _buildTitle;
        private set => Set(ref _buildTitle, value);
    }

    public string BuildSubtitle
    {
        get => _buildSubtitle;
        private set => Set(ref _buildSubtitle, value);
    }

    public string BuildRunesPrimary
    {
        get => _buildRunesPrimary;
        private set => Set(ref _buildRunesPrimary, value);
    }

    public string BuildRunesSecondary
    {
        get => _buildRunesSecondary;
        private set => Set(ref _buildRunesSecondary, value);
    }

    public string BuildStarter
    {
        get => _buildStarter;
        private set => Set(ref _buildStarter, value);
    }

    public string BuildSpells
    {
        get => _buildSpells;
        private set => Set(ref _buildSpells, value);
    }

    /// <summary>Hint shown in the build block while nothing is loaded yet.</summary>
    public string BuildHint
    {
        get => _buildHint;
        private set => Set(ref _buildHint, value);
    }

    /// <summary>
    /// Collapsed by default during the draft — the recommendation list needs the space more.
    /// Expands when a build arrives and on the post-draft card. Two-way: the chevron toggles it.
    /// </summary>
    public bool IsBuildExpanded
    {
        get => _isBuildExpanded;
        set => Set(ref _isBuildExpanded, value);
    }

    /// <summary>Core item combinations, best sample first.</summary>
    public ObservableCollection<string> BuildCoreLines { get; } = [];

    /// <summary>Situational pointers derived from the enemy composition, as toned chips.</summary>
    public ObservableCollection<Reason> BuildHints { get; } = [];

    public AppSettings Settings => _settings;

    /// <summary>What was found on disk at start-up; shown as the tooltip of the snapshot line.</summary>
    public string DataSummary => StartupReport.Summary;

    public Task StartAsync() => _tracker.RunAsync(_lifetime.Token);

    /// <summary>Applies a manual lane override from the dropdown and recalculates.</summary>
    public void SetManualLane(SlotViewModel slot, int laneIndex)
    {
        var lane = SlotViewModel.LaneAt(laneIndex);

        if (lane == Lane.Unknown)
            _manualLanes.Remove(slot.CellId);
        else
            _manualLanes[slot.CellId] = lane;

        Refresh();
    }

    /// <summary>
    /// Recomputes the visible clock from the captured deadline. Called on every client update and by
    /// the local tick in between.
    /// </summary>
    private void UpdateCountdown()
    {
        if (!_clock.IsRunning)
        {
            SecondsText = string.Empty;
            PhaseFraction = 0;
            IsTimeCritical = false;
            return;
        }

        var (seconds, fraction) = _clock.At(DateTimeOffset.UtcNow);
        SecondsText = seconds.ToString(CultureInfo.CurrentCulture);
        PhaseFraction = fraction;
        IsTimeCritical = seconds is > 0 and <= 10;
    }

    /// <summary>
    /// Runs the local clock only while a draft is on screen, so an idle tool still costs no ticks.
    /// </summary>
    private void SetCountdownRunning(bool running)
    {
        if (running)
        {
            _countdown ??= new DispatcherTimer(
                TimeSpan.FromMilliseconds(500),
                DispatcherPriority.Normal,
                (_, _) => UpdateCountdown(),
                _dispatcher);

            _countdown.Start();
            return;
        }

        _countdown?.Stop();
        _clock = PhaseClock.Stopped;
    }

    private MetaLookup LoadMeta()
    {
        var result = _store.LoadWithStatus();
        _snapshotProblem = result.IsOk ? null : result.Detail;

        return result.Snapshot is null ? MetaLookup.Empty : new MetaLookup(result.Snapshot);
    }

    /// <summary>
    /// Watches the snapshot file and picks up a new one without a restart.
    /// <para>
    /// Without this, data fetched by the command-line updater — or by another instance — stays
    /// invisible to a running window, and the only remedy is closing and reopening the tool. That is
    /// exactly the kind of state that is impossible to tell apart from a bug.
    /// </para>
    /// </summary>
    private void WatchSnapshotFile()
    {
        var directory = Path.GetDirectoryName(_store.Path);
        if (string.IsNullOrEmpty(directory))
            return;

        try
        {
            Directory.CreateDirectory(directory);

            _snapshotWatcher = new FileSystemWatcher(directory, Path.GetFileName(_store.Path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };

            _snapshotWatcher.Created += OnSnapshotFileChanged;
            _snapshotWatcher.Changed += OnSnapshotFileChanged;
            _snapshotWatcher.Renamed += OnSnapshotFileChanged;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Live reloading is a convenience; the app works without it.
            _snapshotWatcher = null;
        }
    }

    private void OnSnapshotFileChanged(object sender, FileSystemEventArgs e)
    {
        // One atomic save raises Created, Changed and Renamed in a burst; a single reload covers
        // them all. The flag is cleared on the UI thread once the reload actually runs.
        if (_reloadQueued)
            return;

        _reloadQueued = true;
        _ = ReloadSnapshotAsync();
    }

    /// <summary>
    /// Re-reads the snapshot on request — used when a second instance signals the window forward,
    /// so a double-click on the desktop icon always repairs a stale data display too.
    /// </summary>
    public void RequestSnapshotReload() => _ = ReloadSnapshotAsync();

    /// <summary>
    /// Retries a failed load on a timer.
    /// <para>
    /// The file watcher alone is not enough: it only fires on a change. If the app starts while the
    /// snapshot is absent and the file is put back without being rewritten, no event ever arrives and
    /// the panel stays stuck on an error that is no longer true. This polls until it loads, then
    /// stops.
    /// </para>
    /// </summary>
    private void SetSnapshotRetryRunning(bool running)
    {
        if (running)
        {
            _snapshotRetry ??= new DispatcherTimer(
                TimeSpan.FromSeconds(5),
                DispatcherPriority.Background,
                (_, _) => _ = ReloadSnapshotAsync(),
                _dispatcher);

            _snapshotRetry.Start();
            return;
        }

        _snapshotRetry?.Stop();
    }

    private async Task ReloadSnapshotAsync()
    {
        // The file is written as a temporary and then renamed, so the first event can arrive before
        // the rename lands. A short wait turns that race into a non-event.
        await Task.Delay(400).ConfigureAwait(true);

        if (!_dispatcher.CheckAccess())
        {
            await _dispatcher.InvokeAsync(() => _ = ReloadSnapshotAsync());
            return;
        }

        _reloadQueued = false;

        var meta = LoadMeta();
        if (meta.IsEmpty && !_meta.IsEmpty)
            return;

        _meta = meta;
        _predictor = new LanePredictor(_meta, _seatPriors);
        _recommender = new Recommender(_meta, _traits);

        // New portraits arrive with new data; a stale cache would keep showing blanks.
        _icons.Clear();

        SetSnapshotRetryRunning(_meta.IsEmpty);
        UpdateSnapshotText();
        Refresh();
    }

    private void OnTrackerChanged(DraftSnapshot snapshot)
    {
        // The client pushes from a background thread; everything below touches the UI.
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => OnTrackerChanged(snapshot));
            return;
        }

        var wasActive = _state.IsActive;
        _state = snapshot.State;
        _target = snapshot.Target;

        StatusText = DescribeStatus(snapshot.Status);
        StatusTone = ToneOf(snapshot.Status);

        if (!snapshot.State.IsActive)
        {
            _manualLanes.Clear();
            _selectable = null;
            _selectableFetched = false;

            // The live counter overlay belonged to the draft that just ended; the build stays,
            // as the post-draft card, so the shopping order is still readable during the game.
            _meta.ClearLiveMatchups();
            _liveFetched.Clear();
            _pendingEnemyCount = 0;
            _buildContext = null;
            ShowDraftFetchButton = false;

            IsDraftActive = false;
            SetCountdownRunning(false);
            Clear();
            UpdateBuildSection();
            return;
        }

        if (!wasActive)
        {
            _manualLanes.Clear();
            _selectable = null;
            _selectableFetched = false;

            // A fresh draft starts clean: no stale build card, no leftover overlay.
            _meta.ClearLiveMatchups();
            _liveFetched.Clear();
            _build = null;
            HasBuild = false;
            IsBuildExpanded = false;
            _buildCardClosed = false;
            _buildProbe = default;
        }

        // Retried on every event until it succeeds; see FetchSelectableAsync.
        if (!_selectableFetched)
            _ = FetchSelectableAsync();

        IsDraftActive = true;
        IsPinned = _tracker.Turns.IsPinned;
        SetCountdownRunning(true);
        Render();
    }

    /// <summary>
    /// Asks the client which champions the local player may take. Only meaningful for our own seat;
    /// the client does not report what team-mates own.
    /// <para>
    /// The flag is only latched on success: the client answers this endpoint unreliably in the first
    /// seconds of champion select, and a failure that latched would silently disable the ownership
    /// filter for the whole draft. Instead the next session event simply asks again.
    /// </para>
    /// </summary>
    private async Task FetchSelectableAsync()
    {
        if (_selectableFetched || _selectableRequestRunning || _source?.Client is not { } client)
            return;

        _selectableRequestRunning = true;

        try
        {
            var pickable = await client.GetPickableChampionIdsAsync(_lifetime.Token).ConfigureAwait(true);
            if (pickable.Count > 0)
            {
                _selectableFetched = true;
                _selectable = pickable;
                Refresh();
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            // A convenience filter must never bubble up as an unobserved task exception. Logged so
            // a persistent failure is still visible somewhere.
            CrashLog.Write("Pickable-Abfrage", ex);
        }
        finally
        {
            _selectableRequestRunning = false;
        }
    }

    private void Refresh()
    {
        if (_state.IsActive)
            Render();
    }

    private void Render()
    {
        var enemyPredictions = _predictor.Predict(_state.Enemies, _manualLanes);
        var allyPredictions = _predictor.Predict(_state.Allies, _manualLanes);

        PhaseText = DescribePhase(_state.Phase);

        // The client pushes only when something changes, and a ticking clock is not a change to it —
        // the remaining seconds simply ride along on the next update. Left alone the display would
        // freeze between events, so the deadline is captured here and counted down locally.
        _clock = PhaseClock.FromState(_state, DateTimeOffset.UtcNow);
        UpdateCountdown();

        TurnText = DescribeTurn();
        IsMyTurn = _state.Turn is { IsLocalPlayer: true };

        RenderTeam(Allies, _state.Allies, allyPredictions, isAlly: true);
        RenderTeam(Enemies, _state.Enemies, enemyPredictions, isAlly: false);

        RenderRecommendations(enemyPredictions);
        UpdateDraftLiveState(enemyPredictions, allyPredictions);
    }

    /// <summary>
    /// Recomputes what the draft button could fetch right now, probes the build cache once per
    /// matchup (a cache hit shows the build with no network at all), and refreshes the panels.
    /// </summary>
    private void UpdateDraftLiveState(LanePredictionResult enemyPredictions, LanePredictionResult allyPredictions)
    {
        _pendingEnemyCount = _state.Enemies.Count(slot =>
            slot.EffectiveChampionId != 0 && !_liveFetched.Contains(slot.EffectiveChampionId));

        _buildContext = null;

        if (_state.LocalSlot is { IsLocked: true } mine && mine.LockedChampionId != 0)
        {
            var myLane = mine.AssignedLane != Lane.Unknown
                ? mine.AssignedLane
                : allyPredictions.ForCell(mine.CellId)?.Lane ?? Lane.Unknown;

            var opponent = myLane == Lane.Unknown ? 0 : enemyPredictions.ChampionOnLane(myLane);

            if (myLane != Lane.Unknown && opponent != 0)
                _buildContext = (mine.LockedChampionId, myLane, opponent);
        }

        TryLoadCachedBuild();
        UpdateDraftFetchState();
        UpdateBuildSection();
    }

    private bool BuildMatchesContext
        => _build is not null && _buildContext is { } context
            && _build.ChampionId == context.Champion
            && _build.Lane == context.Lane
            && _build.OpponentId == context.Opponent;

    private (int Enemies, bool Build, int Total) PendingFetch()
    {
        var buildPending = _buildContext is not null && !BuildMatchesContext;
        return (_pendingEnemyCount, buildPending, _pendingEnemyCount + (buildPending ? 1 : 0));
    }

    private void UpdateDraftFetchState()
    {
        var (enemies, build, total) = PendingFetch();

        ShowDraftFetchButton = IsFetchingDraftData || (_state.IsActive && total > 0 && !_meta.IsEmpty);
        DraftFetchText = IsFetchingDraftData
            ? "OP.GG: lade…"
            : (enemies, build) switch
            {
                ( > 0, true) => $"OP.GG: {enemies} Gegner + Build laden",
                ( > 0, false) => $"OP.GG: {enemies} Gegner laden",
                (_, true) => "OP.GG: Build laden",
                _ => string.Empty,
            };

        FetchDraftDataCommand.RaiseCanExecuteChanged();
    }

    private void TryLoadCachedBuild()
    {
        if (_meta.IsEmpty || BuildMatchesContext || _buildContext is not { } context)
            return;

        if (_buildProbe == context)
            return;

        _buildProbe = context;

        var cached = _buildCache.Load(_meta.Patch, context.Champion, context.Lane, context.Opponent);
        if (cached is not null && !cached.IsEmpty)
            ApplyBuild(cached);
    }

    /// <summary>
    /// Runs the draft button: fresh counters for every newly revealed enemy, and the matchup guide
    /// for the locked pick. This is the only network access besides the big update, and like it,
    /// it happens exclusively on a click.
    /// </summary>
    private async Task FetchDraftDataAsync()
    {
        if (!CanFetchDraftData)
            return;

        IsFetchingDraftData = true;
        UpdateDraftFetchState();

        try
        {
            var resolver = new ChampionResolver(_meta.Champions);
            using var client = new OpGgMcpClient();
            var fetcher = new LiveDraftFetcher(client, _settings.GameMode);
            var predictions = _predictor.Predict(_state.Enemies, _manualLanes);

            foreach (var slot in _state.Enemies.ToList())
            {
                var id = slot.EffectiveChampionId;
                if (id == 0 || _liveFetched.Contains(id) || _meta.Champion(id) is not { } enemy)
                    continue;

                var lane = predictions.ForCell(slot.CellId)?.Lane ?? Lane.Unknown;
                var stats = await fetcher.FetchEnemyCountersAsync(enemy, lane, resolver, _lifetime.Token)
                    .ConfigureAwait(true);

                // Marked as fetched even when empty: asking again would not produce more data.
                _liveFetched.Add(id);

                if (stats.Count > 0)
                    _meta.ApplyLiveMatchups(stats);
            }

            if (_buildContext is { } context && !BuildMatchesContext
                && _meta.Champion(context.Champion) is { } me
                && _meta.Champion(context.Opponent) is { } opponent)
            {
                var plan = await fetcher.FetchBuildAsync(me, opponent, context.Lane, _meta.Patch, _lifetime.Token)
                    .ConfigureAwait(true);

                if (plan is not null && !plan.IsEmpty)
                {
                    _buildCache.Save(plan);
                    ApplyBuild(plan);

                    // The user asked for this build just now; show it opened.
                    IsBuildExpanded = true;
                }
            }

            Refresh();
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            UpdateStatus = $"OP.GG-Abruf fehlgeschlagen: {ex.Message}";
            CrashLog.Write("Draft-Abruf", ex);
        }
        finally
        {
            IsFetchingDraftData = false;
            UpdateDraftFetchState();
            UpdateBuildSection();
        }
    }

    private void ApplyBuild(BuildPlan plan)
    {
        // Deliberately NOT auto-expanding here: a silent cache hit mid-draft must not shove the
        // recommendation list aside. The explicit button click and the post-draft card do expand.
        _build = plan;
        _buildCardClosed = false;
        HasBuild = true;

        BuildTitle = $"{plan.ChampionName} vs {plan.OpponentName} · {plan.Lane.Display()}";

        var runes = plan.Runes;
        BuildSubtitle = runes is null
            ? $"Patch {plan.Patch}"
            : $"Runen: {runes.WinRate:P0} Winrate über {runes.Play} Spiele · Patch {plan.Patch}";

        BuildRunesPrimary = runes is null
            ? string.Empty
            : $"{runes.PrimaryPath}:  {string.Join(" · ", runes.PrimaryRunes)}";

        var shards = runes is null || runes.Shards.Count == 0
            ? string.Empty
            : $"    Shards: {string.Join(" / ", runes.Shards)}";

        BuildRunesSecondary = runes is null
            ? string.Empty
            : $"{runes.SecondaryPath}:  {string.Join(" · ", runes.SecondaryRunes)}{shards}";

        var starter = plan.Starters.Count > 0 ? string.Join(" + ", plan.Starters[0].Items) : "—";
        var boots = plan.Boots.Count > 0 ? plan.Boots[0].Items.FirstOrDefault() ?? "—" : "—";
        BuildStarter = $"Start: {starter}    Boots: {boots}";

        var spells = plan.SummonerSpells.Count > 0 ? string.Join(" + ", plan.SummonerSpells[0].Items) : "—";
        var skills = string.IsNullOrEmpty(plan.SkillPriority) ? string.Empty : $"    Skills: {plan.SkillPriority}";
        BuildSpells = $"Spells: {spells}{skills}";

        BuildCoreLines.Resize(plan.CoreItems.Count, () => string.Empty);
        for (var i = 0; i < plan.CoreItems.Count; i++)
        {
            var core = plan.CoreItems[i];
            BuildCoreLines[i] = $"{i + 1}.  {string.Join("  →  ", core.Items)}   ({core.WinRate:P0} · {core.Play} Spiele)";
        }

        RenderBuildHints();
        UpdateBuildSection();
    }

    /// <summary>
    /// Situational pointers from the enemy composition. They order the alternatives the data
    /// already offers — they never invent an item the statistics did not surface.
    /// </summary>
    private void RenderBuildHints()
    {
        var hints = new List<Reason>();

        if (_enemyComp.Count >= 3)
        {
            if (_enemyComp.PhysicalShare >= 0.65)
                hints.Add(Reason.Neutral($"Gegner {_enemyComp.PhysicalShare:P0} AD → Rüstung priorisieren"));

            if (_enemyComp.MagicShare >= 0.65)
                hints.Add(Reason.Neutral($"Gegner {_enemyComp.MagicShare:P0} AP → Magieresistenz priorisieren"));

            if (_enemyComp.TotalCrowdControl >= 6)
                hints.Add(Reason.Neutral("viel gegnerische CC → Zähigkeit einplanen"));
        }

        BuildHints.Resize(hints.Count, () => Reason.Neutral(string.Empty));
        for (var i = 0; i < hints.Count; i++)
            BuildHints[i] = hints[i];
    }

    private void UpdateBuildSection()
    {
        if (_state.IsActive)
        {
            ShowBuildClose = false;
            ShowBuildSection = HasBuild || _buildContext is not null;
            BuildHint = HasBuild
                ? string.Empty
                : "Noch kein Build geladen — oben den OP.GG-Knopf klicken (ein gezielter Abruf).";
            return;
        }

        // After the draft the build stays on screen as a card, so the shopping order is still
        // there when the user tabs out mid-game. Closed by hand or by the next draft.
        ShowBuildClose = true;
        ShowBuildSection = HasBuild && !_buildCardClosed;
        BuildHint = string.Empty;

        if (ShowBuildSection)
            IsBuildExpanded = true;
    }

    private void RenderTeam(
        ObservableCollection<SlotViewModel> rows,
        IReadOnlyList<DraftSlot> slots,
        LanePredictionResult predictions,
        bool isAlly)
    {
        rows.Resize(slots.Count, () => new SlotViewModel());

        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            var row = rows[i];
            var prediction = predictions.ForCell(slot.CellId);

            row.CellId = slot.CellId;
            row.IsLocalPlayer = isAlly && slot.CellId == _state.LocalCellId;
            row.Label = row.IsLocalPlayer ? "DU" : isAlly ? $"Mitspieler {i + 1}" : $"Gegner {i + 1}";

            row.IsLocked = slot.IsLocked;
            row.HasHover = !slot.IsLocked && slot.HoverChampionId != 0;
            row.Champion = slot.EffectiveChampionId == 0 ? "—" : _meta.ChampionName(slot.EffectiveChampionId);
            row.Icon = _icons.Get(slot.EffectiveChampionId);

            var lane = slot.AssignedLane != Lane.Unknown ? slot.AssignedLane : prediction?.Lane ?? Lane.Unknown;
            row.LaneText = lane == Lane.Unknown ? "?" : lane.Display();

            // The dropdown shows the lane that is actually in effect, predicted or overridden, so it
            // reads as information rather than as an empty control.
            row.LaneIndex = SlotViewModel.IndexOf(lane);
            row.IsManualLane = _manualLanes.ContainsKey(slot.CellId);
            row.HasChampion = slot.EffectiveChampionId != 0;

            row.IsOnClock = _state.Turn?.CellId == slot.CellId;
            row.IsTarget = _target?.Slot.CellId == slot.CellId;

            // Ally lanes come from the client, so a confidence figure there would be noise. And a
            // figure for a seat that has revealed nothing is guesswork dressed up as a measurement.
            var showConfidence = !isAlly
                && slot.AssignedLane == Lane.Unknown
                && slot.EffectiveChampionId != 0
                && prediction is not null;

            row.Confidence = showConfidence ? $"{prediction!.Confidence:P0}" : string.Empty;
            row.IsUncertain = showConfidence && prediction!.IsUncertain;

            row.Warning = isAlly ? HoverWarning(slot) : null;
        }
    }

    /// <summary>Flags an ally hover that cannot work out, which is worth saying before they lock it.</summary>
    private string? HoverWarning(DraftSlot slot)
    {
        if (slot.IsLocked || slot.HoverChampionId == 0)
            return null;

        if (_state.AllyBans.Contains(slot.HoverChampionId) || _state.EnemyBans.Contains(slot.HoverChampionId))
            return "gebannt";

        var duplicate = _state.Allies.Any(other =>
            other.CellId != slot.CellId && other.LockedChampionId == slot.HoverChampionId);

        return duplicate ? "schon gepickt" : null;
    }

    private void RenderRecommendations(LanePredictionResult enemyPredictions)
    {
        if (_target is null)
        {
            Recommendations.Clear();
            HasRecommendations = false;
            ListHeader = "Empfehlungen";
            EmptyHint = "Kein eigener Slot erkannt.";
            return;
        }

        // The ownership filter can only apply to our own seat.
        var selectable = !_settings.ShowUnowned && _target.Slot.CellId == _state.LocalCellId ? _selectable : null;

        var set = _recommender.Recommend(
            _state, _target, enemyPredictions, CurrentWeights(), selectable, _settings.RecommendationCount);

        var who = _target.Slot.CellId == _state.LocalCellId
            ? "dich"
            : $"Mitspieler {_state.Allies.ToList().FindIndex(slot => slot.CellId == _target.Slot.CellId) + 1}";

        var mode = set.Action == TurnAction.Ban ? "Bans" : "Picks";
        var suffix = _target.IsFollowingTurn ? string.Empty : " (Ausblick)";
        var lanePart = set.Lane == Lane.Unknown ? string.Empty : $" · {set.Lane.Display()}";
        ListHeader = $"{mode} für {who}{lanePart}{suffix}";

        HasRecommendations = set.Items.Count > 0;

        // The hint must describe the draft that is on screen now, not the idle state before it.
        if (!HasRecommendations)
        {
            EmptyHint = _meta.IsEmpty
                ? "Meta-Daten fehlen — Empfehlungen gibt es erst nach „Daten aktualisieren“."
                : "Keine Kandidaten verfügbar — alles auf dieser Lane ist gebannt oder vergeben.";
        }

        Recommendations.Resize(set.Items.Count, () => new RecommendationViewModel());
        for (var i = 0; i < set.Items.Count; i++)
            Recommendations[i].Apply(i + 1, set.Items[i], _icons.Get(set.Items[i].ChampionId));

        AllyCompText = DescribeComp(set.AllyComp);
        EnemyCompText = DescribeComp(set.EnemyComp);
        _enemyComp = set.EnemyComp;

        var findings = set.AllyComp.Findings.Select(finding => finding.Text).ToList();
        Warnings.Resize(findings.Count, () => string.Empty);
        for (var i = 0; i < findings.Count; i++)
            Warnings[i] = findings[i];
    }

    private ScoreWeights CurrentWeights()
    {
        foreach (var (name, weights) in ScoreWeights.Presets)
        {
            if (string.Equals(name, PresetName, StringComparison.OrdinalIgnoreCase))
                return weights;
        }

        return ScoreWeights.Meta;
    }

    private void Clear()
    {
        Recommendations.Clear();
        Warnings.Clear();
        HasRecommendations = false;
        PhaseText = string.Empty;
        SecondsText = string.Empty;
        PhaseFraction = 0;
        IsMyTurn = false;
        IsTimeCritical = false;
        TurnText = "Kein Champ Select";
        ListHeader = "Empfehlungen";
        AllyCompText = string.Empty;
        EnemyCompText = string.Empty;

        EmptyHint = _meta.IsEmpty
            ? "Noch keine Meta-Daten. Unten auf „Daten aktualisieren“ klicken — das dauert etwa vier Minuten und passiert nur auf deinen Klick."
            : StatusTone == ConnectionTone.Ok
                ? "Verbunden. Sobald ein Champ Select startet, erscheinen hier Empfehlungen."
                : "Starte den League-Client. Das Tool verbindet sich von selbst.";

        foreach (var row in Allies.Concat(Enemies))
        {
            row.Champion = "—";
            row.Icon = null;
            row.LaneText = "?";
            row.Confidence = string.Empty;
            row.IsOnClock = false;
            row.IsTarget = false;
            row.IsLocked = false;
            row.HasHover = false;
            row.IsUncertain = false;
            row.IsManualLane = false;
            row.HasChampion = false;
            row.Warning = null;
        }
    }

    private string DescribeTurn()
    {
        if (_state.Turn is not { } turn)
        {
            return _state.Phase switch
            {
                DraftPhase.Planning => "Planungsphase",
                DraftPhase.Finalization => "Warten auf Spielstart…",
                _ => "niemand am Zug",
            };
        }

        var action = turn.Action == TurnAction.Ban ? "bannt" : "pickt";

        if (turn.IsLocalPlayer)
            return $"Du {action}";

        if (!turn.IsAlly)
        {
            var enemyIndex = _state.Enemies.ToList().FindIndex(slot => slot.CellId == turn.CellId);
            return enemyIndex >= 0 ? $"Gegner {enemyIndex + 1} {action}" : $"Gegner {action}";
        }

        var index = _state.Allies.ToList().FindIndex(slot => slot.CellId == turn.CellId);
        return $"Mitspieler {index + 1} {action}";
    }

    private static string DescribePhase(DraftPhase phase) => phase switch
    {
        DraftPhase.Planning => "Planung",
        DraftPhase.BanPick => "Bans & Picks",
        DraftPhase.Finalization => "Abschluss",
        DraftPhase.None => "—",
        _ => "unbekannt",
    };

    private static ConnectionTone ToneOf(ClientStatus status)
    {
        if (!status.LeagueFound || !status.ClientRunning)
            return ConnectionTone.Off;

        return status.SocketConnected ? ConnectionTone.Ok : ConnectionTone.Warn;
    }

    private static string DescribeStatus(ClientStatus status)
    {
        if (!status.LeagueFound)
            return status.Detail ?? "League nicht gefunden";

        if (!status.ClientRunning)
            return "Client nicht gestartet";

        return status.SocketConnected ? "verbunden" : status.Detail ?? "Verbindung unterbrochen";
    }

    private static string DescribeComp(CompProfile profile)
    {
        if (profile.Count == 0)
            return "—";

        var text = $"AD {profile.PhysicalShare:P0} / AP {profile.MagicShare:P0} · Frontline {profile.FrontlineCount} · CC {profile.TotalCrowdControl}";

        // Say when part of the analysis could not run rather than implying a clean bill of health.
        if (profile.TraitCoverage < 0.999)
            text += $" · Traits {profile.TraitCoverage:P0}";

        return text;
    }

    private void UpdateSnapshotText()
    {
        if (_meta.IsEmpty)
        {
            HasMeta = false;

            // One short, actionable line everywhere; the technical cause lives in the tooltip.
            SnapshotText = "Meta-Daten fehlen — bitte aktualisieren";
            SnapshotDetail = _snapshotProblem is null
                ? DataSummary
                : $"Ursache: {_snapshotProblem}\n{DataSummary}";

            return;
        }

        var age = _store.Age();
        var ageText = age is null
            ? string.Empty
            : age.Value.TotalDays >= 1
                ? $" · {(int)age.Value.TotalDays} Tage alt"
                : $" · {(int)age.Value.TotalHours} h alt";

        HasMeta = true;
        SnapshotText = $"Daten: Patch {_meta.Patch}{ageText}";
        SnapshotDetail = DataSummary;
    }

    /// <summary>Version plus the executable's write time — the only reliable "which build is this".</summary>
    private static string DescribeBuild()
    {
        var version = typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "?";

        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path))
                return $"Version {version} · Build {File.GetLastWriteTime(path):dd.MM.yyyy HH:mm}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Purely informational; fall through to the version alone.
        }

        return $"Version {version}";
    }

    private async Task RunUpdateAsync()
    {
        if (!CanUpdate)
            return;

        IsUpdating = true;
        UpdateStatus = "Starte…";
        UpdateFraction = 0;

        using var scope = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _updateScope = scope;

        try
        {
            var progress = new Progress<BuildProgress>(report =>
            {
                UpdateStatus = report.ToString();

                // Per-stage progress; the bar restarting for each stage still reads as movement,
                // which is the point of a four-minute operation.
                UpdateFraction = report.Total > 0 ? (double)report.Done / report.Total : 0;
            });

            // Created here and disposed immediately after: outside an update the app holds no
            // outbound connection at all.
            using var client = new OpGgMcpClient();
            using var builder = new SnapshotBuilder(client);

            var snapshot = await Task.Run(
                () => builder.BuildAsync(new SnapshotBuildOptions { GameMode = _settings.GameMode }, progress, scope.Token),
                scope.Token).ConfigureAwait(true);

            // Written atomically; a failure above leaves the previous snapshot in place and in use.
            _store.Save(snapshot);

            _meta = new MetaLookup(snapshot);
            _predictor = new LanePredictor(_meta, _seatPriors);
            _recommender = new Recommender(_meta, _traits);

            // Newly downloaded portraits would otherwise stay invisible until the next start.
            _icons.Clear();

            UpdateSnapshotText();
            UpdateStatus = $"Aktualisiert: {snapshot.Champions.Count} Champions, {snapshot.Matchups.Count} Matchups";
            Refresh();
        }
        catch (OperationCanceledException)
        {
            UpdateStatus = "Abgebrochen — alte Daten bleiben aktiv";
        }
        catch (Exception ex) when (ex is OpGgApiException or OpGgParseException or HttpRequestException or IOException)
        {
            UpdateStatus = $"Fehlgeschlagen: {ex.Message}";
        }
        catch (Exception ex)
        {
            // The catch-all exists because an update failure the user cannot see looks exactly like
            // a hang. Whatever the type, the footer says it failed and the log says why.
            UpdateStatus = $"Fehlgeschlagen: {ex.Message}";
            CrashLog.Write("Daten-Update", ex);
        }
        finally
        {
            _updateScope = null;
            IsUpdating = false;
            UpdateFraction = 0;
        }
    }

    public async ValueTask DisposeAsync()
    {
        SetCountdownRunning(false);
        SetSnapshotRetryRunning(false);
        _snapshotWatcher?.Dispose();
        await _lifetime.CancelAsync().ConfigureAwait(false);
        _tracker.Changed -= OnTrackerChanged;
        await _tracker.DisposeAsync().ConfigureAwait(false);
        _lifetime.Dispose();
    }
}
