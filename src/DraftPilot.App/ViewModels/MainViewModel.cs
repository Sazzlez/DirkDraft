using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DraftPilot.Core.Config;
using DraftPilot.Core.Data;
using DraftPilot.Core.Draft;
using DraftPilot.Core.Lcu;
using DraftPilot.Meta;
using DraftPilot.Meta.OpGg;

namespace DraftPilot.App.ViewModels;

/// <summary>Colours the own team's draft win rate: green ahead, red behind, plain when even.</summary>
public enum BalanceTone
{
    /// <summary>Within a point of even — inside the noise, so it stays neutral.</summary>
    Even,

    Ahead,

    Behind,
}

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
    private readonly TraitTable _traits;
    private readonly SeatPriors _seatPriors = SeatPriors.Load();
    private readonly IconCache _icons = new();

    /// <summary>Lanes the user fixed by hand, keyed by seat.</summary>
    private readonly Dictionary<long, Lane> _manualLanes = [];

    private readonly BuildCache _buildCache = new();

    /// <summary>
    /// Enemy counters already fetched this draft, keyed by the arguments the call was actually made
    /// with. Keying by champion alone looked right and was not: live matchups are stored per lane, so
    /// a champion fetched under a guessed lane that later turns out different keeps its edges filed
    /// where nothing ever looks them up, and the champion counts as done for the rest of the draft.
    /// </summary>
    private readonly HashSet<(int Champion, Lane Lane)> _liveFetched = [];

    /// <summary>
    /// Counter calls made this draft. A prediction that keeps flipping would otherwise buy a new
    /// request on every flip; twelve is well above what an honest draft needs.
    /// </summary>
    private int _liveCallsThisDraft;

    private const int MaxLiveCallsPerDraft = 12;

    /// <summary>Consecutive rounds that ended with at least one failed call; drives the backoff.</summary>
    private int _fetchFailures;

    /// <summary>
    /// The build on screen was fetched against a stand-in opponent, because the queue never reveals
    /// the real one. Everything the card says about it has to carry that caveat.
    /// </summary>
    private bool _buildIsStandIn;

    /// <summary>The matchup whose build could not be fetched, so the card can say so.</summary>
    private (int Champion, Lane Lane, int Opponent)? _buildFailedFor;

    /// <summary>How often the build failed for that same matchup, so it cannot retry forever.</summary>
    private int _buildFailCount;

    /// <summary>
    /// The enemy lane prediction the current render worked with. The fetch must select and count
    /// enemies from the same prediction it was counted with, or the pending count never reaches zero.
    /// </summary>
    private LanePredictionResult? _enemyPredictions;

    /// <summary>The ally prediction of the current render, for the matchup panel's own lane.</summary>
    private LanePredictionResult? _allyPredictions;

    /// <summary>
    /// Matchups the build was already requested for this draft. Lane predictions can flip while
    /// seats fill up, and without this a wobbling prediction would fetch the same guide repeatedly.
    /// </summary>
    private readonly HashSet<(int Champion, Lane Lane, int Opponent)> _buildFetched = [];

    private BuildPlan? _build;
    private bool _isFetchingDraftData;
    private bool _buildCardClosed;

    /// <summary>
    /// Set when a fetch failed, to stop a dead network from being retried on every client event.
    /// </summary>
    private DateTimeOffset _fetchCooldownUntil = DateTimeOffset.MinValue;

    /// <summary>Something became fetchable while a fetch was already running.</summary>
    private bool _fetchAgainWhenDone;

    /// <summary>The (champion, lane, opponent) the cache was last probed for, to probe only once.</summary>
    private (int Champion, Lane Lane, int Opponent) _buildProbe;

    /// <summary>Revealed enemies whose counters were not fetched yet; recomputed on every render.</summary>
    private int _pendingEnemyCount;

    /// <summary>My locked pick, its lane and its direct opponent — the build fetch's inputs.</summary>
    private (int Champion, Lane Lane, int Opponent)? _buildContext;

    /// <summary>Cancels in-flight fetches when their draft ends; linked to the app lifetime.</summary>
    private CancellationTokenSource? _draftScope;

    /// <summary>
    /// One shared OP.GG client for all draft fetches. A fresh HttpClient plus MCP handshake per
    /// fetch — and a fetch runs after every revealed pick — cost two extra round trips each and
    /// piled up half-closed sockets.
    /// </summary>
    private OpGgMcpClient? _opGg;

    /// <summary>
    /// Re-runs the fetch once after a failure's cooldown. Without it "versuche es weiter" was a
    /// lie: no retry ever happened unless the client pushed another event, and finalization
    /// produces none.
    /// </summary>
    private DispatcherTimer? _fetchRetry;

    /// <summary>True while the fetch loop's own Refresh() runs, so it does not queue itself.</summary>
    private bool _refreshingFromFetch;

    /// <summary>A snapshot reload that arrived mid-draft; honoured once the draft ends.</summary>
    private bool _reloadDeferred;

    /// <summary>Enemy composition from the last render, feeding the situational build hints.</summary>
    private CompProfile _enemyComp = CompProfile.Empty;

    /// <summary>Localised item/rune/spell names; refreshed after a data update.</summary>
    private AssetNames _names;

    private bool _isGameRunning;
    private bool _showGameView;
    private bool _showIdleCard = true;
    private bool _isImportingRunes;
    private string _runeImportText = string.Empty;

    private readonly CancellationTokenSource _lifetime = new();

    private readonly AppUpdater _updater = new();
    private string _updateNotice = string.Empty;
    private bool _hasUpdate;
    private bool _isInstallingUpdate;
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
    private ConnectionTone _statusTone = ConnectionTone.Off;
    private string _emptyHint = "Warte auf den League-Client.";
    private string _turnText = string.Empty;
    private string _allyWinRateText = "—";
    private string _enemyWinRateText = "—";
    private BalanceTone _balanceTone;
    private string _balanceHint = string.Empty;
    private string _listHeader = "Empfehlungen";
    private string _snapshotText = string.Empty;
    private string _updateStatus = string.Empty;
    private bool _isUpdating;
    private bool _isDraftActive;
    private bool _hasRecommendations;
    private bool _showMatchupPanel;
    private bool _showNarrowBuildCard;
    private bool _showIdleHero;
    private bool _showEmptyHint;
    private bool _hasMatchupFigure;
    private string _matchupHeadline = string.Empty;
    private string _matchupSubline = string.Empty;
    private string _matchupFigure = string.Empty;
    private string _matchupNote = string.Empty;
    private ScoreTone _matchupTone;
    private ImageSource? _matchupOwnIcon;
    private ImageSource? _matchupOpponentIcon;
    private bool _isMyTurn;
    private bool _hasMeta;
    private string _snapshotDetail = string.Empty;
    private double _updateFraction;
    private string _draftFetchText = string.Empty;
    private bool _showDraftFetchStatus;
    private bool _draftFetchFailed;
    private bool _hasBuild;
    private bool _showBuildSection;
    private bool _showBuildClose;
    private string _buildTitle = string.Empty;
    private string _buildSubtitle = string.Empty;
    private string _buildHint = string.Empty;

    /// <summary>Why the snapshot could not be used, if it could not. Shown in the footer.</summary>
    private string? _snapshotProblem;

    private FileSystemWatcher? _snapshotWatcher;
    private DispatcherTimer? _snapshotRetry;

    /// <summary>Collapses the watcher's event bursts — one save fires Created, Changed and Renamed.
    /// Volatile: set on the watcher's thread, cleared on the UI thread.</summary>
    private volatile bool _reloadQueued;

    /// <param name="source">
    /// Overrides the live client connection. Used by the development harness to drive the panel
    /// from a recording.
    /// </param>
    public MainViewModel(ISessionSource? source = null)
    {
        _settings = AppSettings.Load();

        // The start-up report already read both files a moment ago; taking them over saves parsing
        // 441 KB of JSON twice on the UI thread before the first frame is drawn.
        var preloaded = StartupReport.TakePreloaded();

        _traits = preloaded?.Traits ?? TraitTable.Load();
        _meta = preloaded is { } ready ? UseSnapshot(ready.Load) : LoadMeta();
        _predictor = new LanePredictor(_meta, _seatPriors);
        _recommender = new Recommender(_meta, _traits);

        _names = AssetNames.Load(_settings.DataLanguage);

        // The live source is kept separately because only it can answer which champions the local
        // player owns; a replayed recording has no client to ask.
        _source = source is null ? new LiveSessionSource(_settings.LockfilePath) : null;
        var sessionSource = source ?? _source!;
        sessionSource.GameflowPhase += phase => DispatchFromClientThread(() => OnGameflowPhase(phase));
        _tracker = new DraftTracker(sessionSource);
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

        UpdateDataCommand = new RelayCommand(_ => _ = RunUpdateAsync(), _ => CanUpdate);
        CancelUpdateCommand = new RelayCommand(_ => _updateScope?.Cancel(), _ => IsUpdating);
        CloseBuildCommand = new RelayCommand(_ =>
        {
            _buildCardClosed = true;
            UpdateBuildSection();
        });

        ImportRunesCommand = new RelayCommand(_ => _ = ImportRunesAsync(), _ => CanImportRunes);
        InstallUpdateCommand = new RelayCommand(_ => _ = InstallUpdateAsync(), _ => HasUpdate && !IsInstallingUpdate);

        // CanImportRunes depends on Client, and Client changes on every reconnect — without this
        // the button froze in whatever state the last session event left it.
        if (_source is not null)
            _source.ClientChanged += () => DispatchFromClientThread(() => ImportRunesCommand?.RaiseCanExecuteChanged());

        UpdateSnapshotText();
        WatchSnapshotFile();

        // A snapshot that is absent or broken at start-up may well be fine a moment later.
        SetSnapshotRetryRunning(_meta.IsEmpty);

        // Housekeeping, off the UI thread: the data directory must not silt up over months. The
        // patch travels as a parameter because the worker must not read _meta while the UI thread
        // may be replacing it.
        var maintenancePatch = _meta.IsEmpty || string.IsNullOrWhiteSpace(_meta.Patch) ? null : _meta.Patch;
        _ = Task.Run(() => RunMaintenance(maintenancePatch));
    }

    /// <summary>
    /// Prunes what accumulates on its own: build plans from old patches or older than two weeks,
    /// leftover .tmp files, and the two append-forever logs. Icons stay — they never go stale and
    /// deleting them would only mean downloading them again.
    /// </summary>
    private void RunMaintenance(string? currentPatch)
    {
        try
        {
            // Under the crash log's own lock: a crash being written mid-trim waits instead of
            // colliding with the exclusive stream and being dropped.
            CrashLog.WithLogLock(() => Maintenance.TrimLog(CrashLog.Path));
            Maintenance.TrimLog(StartupReport.Path);

            Maintenance.SweepTempFiles(
                AppPaths.DataDirectory,
                AppPaths.IconDirectory,
                AppPaths.ItemIconDirectory,
                AppPaths.RuneIconDirectory,
                AppPaths.SpellIconDirectory);

            if (currentPatch is not null)
                _buildCache.CleanUp(currentPatch, TimeSpan.FromDays(14));
        }
        catch (Exception ex)
        {
            // Cleaning up must never be the thing that breaks; log it and move on.
            CrashLog.Write("Aufräumen", ex);
        }
    }

    public ObservableCollection<SlotViewModel> Allies { get; } = [];

    public ObservableCollection<SlotViewModel> Enemies { get; } = [];

    public ObservableCollection<RecommendationViewModel> Recommendations { get; } = [];

    public ObservableCollection<string> Warnings { get; } = [];

    public RelayCommand SelectSlotCommand { get; }

    public RelayCommand UpdateDataCommand { get; }

    public RelayCommand CancelUpdateCommand { get; }

    public RelayCommand CloseBuildCommand { get; }

    public RelayCommand ImportRunesCommand { get; }

    public RelayCommand InstallUpdateCommand { get; }

    /// <summary>Raised when champion select starts or ends, so the window can show or hide itself.</summary>
    public event Action<bool>? DraftActiveChanged;

    /// <summary>Raised when a game starts or ends, so the window can surface the build view.</summary>
    public event Action<bool>? GameActiveChanged;

    /// <summary>The in-game build view's content: runes, purchase order, skill table.</summary>
    public GameBuildViewModel GameBuild { get; } = new();

    public bool IsGameRunning
    {
        get => _isGameRunning;
        private set => Set(ref _isGameRunning, value);
    }

    /// <summary>The in-game build view, shown instead of the idle card while a game runs.</summary>
    public bool ShowGameView
    {
        get => _showGameView;
        private set => Set(ref _showGameView, value);
    }

    /// <summary>The compact status card: outside a draft AND outside a game with a build.</summary>
    public bool ShowIdleCard
    {
        get => _showIdleCard;
        private set => Set(ref _showIdleCard, value);
    }

    /// <summary>
    /// Tracks the client's gameflow phase. The build view appears when the game actually starts,
    /// not when champion select ends — loading screens count as "started".
    /// </summary>
    public void OnGameflowPhase(string phase)
    {
        // Reconnect counts as running: closing the build view in the exact moment somebody is
        // clicking "Wieder verbinden" would be the least helpful time to do it.
        var running = phase.Equals("GameStart", StringComparison.OrdinalIgnoreCase)
            || phase.Equals("InProgress", StringComparison.OrdinalIgnoreCase)
            || phase.Equals("Reconnect", StringComparison.OrdinalIgnoreCase);

        // Every DISTINCT running phase re-announces. GameStart is only the loading screen;
        // exclusive fullscreen grabs the display (and minimises other windows) at InProgress,
        // which can trail by minutes — the anti-minimise grace window has to re-anchor there,
        // and the once-per-game latch alone left it anchored on the loading screen.
        if (running && !phase.Equals(_lastGamePhase, StringComparison.OrdinalIgnoreCase))
        {
            _lastGamePhase = phase;
            _gameActiveAnnounced = false;
        }

        if (running == IsGameRunning)
        {
            if (running)
                AnnounceGameActive();

            return;
        }

        IsGameRunning = running;
        UpdateBuildSection();

        if (running)
        {
            AnnounceGameActive();
        }
        else
        {
            _lastGamePhase = string.Empty;
            _gameActiveAnnounced = false;
            GameActiveChanged?.Invoke(false);
        }
    }

    /// <summary>The last running gameflow phase, to detect GameStart → InProgress transitions.</summary>
    private string _lastGamePhase = string.Empty;

    /// <summary>Set once <see cref="GameActiveChanged"/> announced the running game.</summary>
    private bool _gameActiveAnnounced;

    /// <summary>The boots preference the build tiles were last rendered with.</summary>
    private BootsPreference _appliedBootsPreference;

    /// <summary>Renders the build tiles with the current situational boots preference.</summary>
    private void ApplyGameBuild(BuildPlan plan)
    {
        _appliedBootsPreference = SituationalBuild.PreferredBoots(_enemyComp);
        GameBuild.Apply(plan, _names, _icons, _appliedBootsPreference, _buildIsStandIn);
    }

    /// <summary>
    /// Fires the auto-show event when a game runs AND a build exists. Called from the phase
    /// change and again when a build lands — a slow fetch used to mean the phase event found no
    /// build, never fired, and the window stayed in the tray for the whole game.
    /// </summary>
    private void AnnounceGameActive()
    {
        if (!IsGameRunning || !HasBuild || _gameActiveAnnounced)
            return;

        _gameActiveAnnounced = true;
        GameActiveChanged?.Invoke(true);
    }

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

    /// <summary>Estimated win rate of the own team, over its column.</summary>
    public string AllyWinRateText
    {
        get => _allyWinRateText;
        private set => Set(ref _allyWinRateText, value);
    }

    /// <summary>The counterpart over the enemy column; the two always add up to 100 %.</summary>
    public string EnemyWinRateText
    {
        get => _enemyWinRateText;
        private set => Set(ref _enemyWinRateText, value);
    }

    /// <summary>Colours the own number: green ahead, red behind, plain inside the noise.</summary>
    public BalanceTone BalanceTone
    {
        get => _balanceTone;
        private set => Set(ref _balanceTone, value);
    }

    /// <summary>What the two numbers mean and what they leave out; shown on hover.</summary>
    public string BalanceHint
    {
        get => _balanceHint;
        private set => Set(ref _balanceHint, value);
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

    public string SnapshotText
    {
        get => _snapshotText;
        private set => Set(ref _snapshotText, value);
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

    /// <summary>False while the empty-state hint should take the place of the list.</summary>
    public bool HasRecommendations
    {
        get => _hasRecommendations;
        private set => Set(ref _hasRecommendations, value);
    }

    /// <summary>
    /// The widest column shows the matchup instead of a pick list. Reached once the advised seat has
    /// locked and nobody on our team is on the clock — in that state a list of alternatives is
    /// advice about a decision already made, and it occupied the one column with room to breathe
    /// while the build sat in the 280 px column next to it.
    /// </summary>
    public bool ShowMatchupPanel
    {
        get => _showMatchupPanel;
        private set => Set(ref _showMatchupPanel, value);
    }

    /// <summary>
    /// The narrow build card in the left column. Hidden while the matchup panel shows the same
    /// build in the wide column — two copies on one screen are noise. Expressed as one property
    /// rather than a style trigger, because the local Visibility attribute on that Border would
    /// win against any trigger.
    /// </summary>
    public bool ShowNarrowBuildCard
    {
        get => _showNarrowBuildCard;
        private set => Set(ref _showNarrowBuildCard, value);
    }

    /// <summary>
    /// The centred brand-and-status hero that fills the otherwise empty window between drafts. It
    /// yields to any build card: the two would overlap, and a shopping order the user kept open
    /// beats a logo.
    /// </summary>
    public bool ShowIdleHero
    {
        get => _showIdleHero;
        private set => Set(ref _showIdleHero, value);
    }

    /// <summary>The empty-list hint. Suppressed while the matchup panel has the column.</summary>
    public bool ShowEmptyHint
    {
        get => _showEmptyHint;
        private set => Set(ref _showEmptyHint, value);
    }

    /// <summary>Both champions of the duel, e.g. <c>Darius vs Jax</c>.</summary>
    public string MatchupHeadline
    {
        get => _matchupHeadline;
        private set => Set(ref _matchupHeadline, value);
    }

    /// <summary>Where the number comes from: lane, sample size, patch.</summary>
    public string MatchupSubline
    {
        get => _matchupSubline;
        private set => Set(ref _matchupSubline, value);
    }

    /// <summary>The duel win rate as a figure, or empty when the duel is unknown.</summary>
    public string MatchupFigure
    {
        get => _matchupFigure;
        private set => Set(ref _matchupFigure, value);
    }

    public ScoreTone MatchupTone
    {
        get => _matchupTone;
        private set => Set(ref _matchupTone, value);
    }

    /// <summary>Said in words when there is no number: no opponent yet, or no data for the pairing.</summary>
    public string MatchupNote
    {
        get => _matchupNote;
        private set => Set(ref _matchupNote, value);
    }

    public bool HasMatchupFigure
    {
        get => _hasMatchupFigure;
        private set => Set(ref _hasMatchupFigure, value);
    }

    public ImageSource? MatchupOwnIcon
    {
        get => _matchupOwnIcon;
        private set => Set(ref _matchupOwnIcon, value);
    }

    public ImageSource? MatchupOpponentIcon
    {
        get => _matchupOpponentIcon;
        private set => Set(ref _matchupOpponentIcon, value);
    }

    /// <summary>The local player is on the clock; the turn card lights up for it.</summary>
    public bool IsMyTurn
    {
        get => _isMyTurn;
        private set => Set(ref _isMyTurn, value);
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

    private string _buildInfoText = DescribeBuild();

    /// <summary>
    /// Which build is on screen. Exists because an old instance is indistinguishable from a new one
    /// without it, and that ambiguity has already cost a debugging round. After a successful update
    /// check the line also says whether this is the newest release.
    /// </summary>
    public string BuildInfoText { get => _buildInfoText; private set => Set(ref _buildInfoText, value); }

    // ----- Draft live data (fetched automatically after every pick) -------------------------

    public bool IsFetchingDraftData
    {
        get => _isFetchingDraftData;
        private set => Set(ref _isFetchingDraftData, value);
    }

    /// <summary>
    /// What the automatic draft fetch is doing, e.g. "lädt Daten zu Syndra…". Empty when there is
    /// nothing to say, which is most of the time.
    /// </summary>
    public string DraftFetchText
    {
        get => _draftFetchText;
        private set => Set(ref _draftFetchText, value);
    }

    public bool ShowDraftFetchStatus
    {
        get => _showDraftFetchStatus;
        private set => Set(ref _showDraftFetchStatus, value);
    }

    /// <summary>True while the status line is reporting a failure rather than progress.</summary>
    public bool DraftFetchFailed
    {
        get => _draftFetchFailed;
        private set => Set(ref _draftFetchFailed, value);
    }

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

    /// <summary>Hint shown in the build block while nothing is loaded yet.</summary>
    public string BuildHint
    {
        get => _buildHint;
        private set => Set(ref _buildHint, value);
    }

    /// <summary>Situational pointers derived from the enemy composition, as toned chips.</summary>
    public ObservableCollection<Reason> BuildHints { get; } = [];

    public AppSettings Settings => _settings;

    /// <summary>What was found on disk at start-up; shown as the tooltip of the snapshot line.</summary>
    public string DataSummary => StartupReport.Summary;

    /// <summary>What the start-up check found, e.g. <c>Version 1.1.0 ist da</c>; empty when current.</summary>
    public string UpdateNotice
    {
        get => _updateNotice;
        private set => Set(ref _updateNotice, value);
    }

    /// <summary>A newer release exists and can be installed with one click.</summary>
    public bool HasUpdate
    {
        get => _hasUpdate;
        private set
        {
            if (Set(ref _hasUpdate, value))
                InstallUpdateCommand?.RaiseCanExecuteChanged();
        }
    }

    public bool IsInstallingUpdate
    {
        get => _isInstallingUpdate;
        private set
        {
            if (Set(ref _isInstallingUpdate, value))
                InstallUpdateCommand?.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// One question to GitHub per start: is there a newer release? Only the answer is acted on —
    /// nothing is downloaded until the user clicks. A copy that was not installed through the
    /// installer skips this entirely (see <see cref="AppUpdater.CanUpdate"/>). When the check comes
    /// back with "nothing newer", the version line says so: otherwise nobody could tell a current
    /// installation from one whose check never ran.
    /// </summary>
    private async Task CheckForUpdateAsync()
    {
        var check = await _updater.CheckAsync(_lifetime.Token).ConfigureAwait(true);
        switch (check.Outcome)
        {
            case UpdateCheck.Newer:
                UpdateNotice = $"Version {check.NewerVersion} ist da";
                HasUpdate = true;
                break;
            case UpdateCheck.Current:
                BuildInfoText = $"{BuildInfoText} · auf dem neuesten Stand";
                break;
            // NotInstalled and Failed say nothing: a copy from the build folder cannot update, and a
            // failed check is not news anyone needs at start-up.
        }
    }

    /// <summary>Downloads and restarts into the new version. On success this method never returns.</summary>
    private async Task InstallUpdateAsync()
    {
        if (!HasUpdate || IsInstallingUpdate)
            return;

        IsInstallingUpdate = true;
        UpdateNotice = "Lädt die neue Version …";

        var failure = await _updater.InstallAsync(
            percent => _dispatcher.Invoke(() => UpdateNotice = $"Lädt die neue Version … {percent} %"),
            _lifetime.Token).ConfigureAwait(true);

        // Only reached when the update did not go through; success has already restarted the app.
        IsInstallingUpdate = false;
        UpdateNotice = failure ?? "Aktualisierung fehlgeschlagen.";
    }

    public Task StartAsync()
    {
        // Fire-and-forget on purpose: the tracker must not wait for GitHub, and the check
        // reports through the view model whenever it comes back.
        _ = CheckForUpdateAsync();
        return _tracker.RunAsync(_lifetime.Token);
    }

    /// <summary>Applies a manual lane override from the dropdown and recalculates.</summary>
    public void SetManualLane(SlotViewModel slot, int laneIndex)
    {
        var lane = SlotViewModel.LaneAt(laneIndex);

        if (lane == Lane.Unknown)
        {
            _manualLanes.Remove(slot.CellId);
        }
        else
        {
            // One lane, one seat: leaving the same lane pinned on another seat would make the
            // constraints contradictory and collapse every prediction.
            foreach (var taken in _manualLanes.Where(pair => pair.Value == lane && pair.Key != slot.CellId).ToList())
                _manualLanes.Remove(taken.Key);

            _manualLanes[slot.CellId] = lane;
        }

        Refresh();
    }

    private MetaLookup LoadMeta() => UseSnapshot(_store.LoadWithStatus());

    /// <summary>Adopts a load result, whether we read it ourselves or inherited it.</summary>
    private MetaLookup UseSnapshot(SnapshotLoadResult result)
    {
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
        // Fire-and-forget from a watcher thread and a timer: anything escaping here would vanish
        // as an unobserved task exception and the panel would just silently stop updating.
        try
        {
            // The file is written as a temporary and then renamed, so the first event can arrive
            // before the rename lands. A short wait turns that race into a non-event.
            await Task.Delay(400).ConfigureAwait(true);

            if (!_dispatcher.CheckAccess())
            {
                await _dispatcher.InvokeAsync(() => _ = ReloadSnapshotAsync());
                return;
            }

            _reloadQueued = false;

            // Mid-draft, replacing _meta would discard the live matchups this very draft fetched —
            // while _liveFetched keeps claiming they are there, so they would never come back.
            if (_state.IsActive)
            {
                _reloadDeferred = true;
                return;
            }

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
        catch (Exception ex)
        {
            CrashLog.Write("Snapshot-Reload", ex);
        }
    }

    private void OnTrackerChanged(DraftSnapshot snapshot)
    {
        // The client pushes from a background thread; everything below touches the UI.
        if (!_dispatcher.CheckAccess())
        {
            DispatchFromClientThread(() => OnTrackerChanged(snapshot));
            return;
        }

        var wasActive = _state.IsActive;
        _state = snapshot.State;
        _target = snapshot.Target;

        StatusText = DescribeStatus(snapshot.Status);
        StatusTone = ToneOf(snapshot.Status);

        // League gone while the in-game view was open: no gameflow event will ever arrive to
        // close it, so the view (and the rune button's lock) stayed frozen until one did.
        if (!snapshot.Status.ClientRunning && IsGameRunning)
            OnGameflowPhase("None");

        if (!snapshot.State.IsActive)
        {
            _manualLanes.Clear();
            _selectable = null;
            _selectableFetched = false;

            // In-flight fetches belong to the draft that just ended; let them stop instead of
            // writing counters into the freshly cleared lookup.
            ResetDraftScope();
            _fetchRetry?.Stop();

            // The live counter overlay belonged to the draft that just ended; the build stays,
            // as the post-draft card, so the shopping order is still readable during the game.
            _meta.ClearLiveMatchups();
            _liveFetched.Clear();
            _buildFetched.Clear();
            _liveCallsThisDraft = 0;
            _fetchFailures = 0;
            _buildFailedFor = null;
            _buildFailCount = 0;
            _enemyPredictions = null;
            _pendingEnemyCount = 0;
            _buildContext = null;
            _fetchAgainWhenDone = false;
            _fetchCooldownUntil = DateTimeOffset.MinValue;
            DraftFetchText = string.Empty;
            DraftFetchFailed = false;
            ShowDraftFetchStatus = false;

            IsDraftActive = false;
            Clear();
            UpdateBuildSection();

            // A snapshot reload that arrived mid-draft was parked to protect the live matchups.
            if (_reloadDeferred)
            {
                _reloadDeferred = false;
                _ = ReloadSnapshotAsync();
            }

            return;
        }

        // The tracker's IsNewDraft also covers the client jumping straight from one draft into
        // the next — our own wasActive bookkeeping never sees an inactive frame then, and the
        // previous draft's manual lanes and build card leaked into the new one.
        if (!wasActive || snapshot.IsNewDraft)
        {
            _manualLanes.Clear();
            _selectable = null;
            _selectableFetched = false;

            // A fresh draft starts clean: no stale build card, no leftover overlay.
            _meta.ClearLiveMatchups();
            _liveFetched.Clear();
            _buildFetched.Clear();
            _liveCallsThisDraft = 0;
            _fetchFailures = 0;
            _buildFailedFor = null;
            _buildFailCount = 0;
            _fetchCooldownUntil = DateTimeOffset.MinValue;
            RuneImportText = string.Empty;
            _build = null;
            HasBuild = false;
            _buildCardClosed = false;
            _buildProbe = default;

            // Cancel, not just dispose: on a draft→draft jump the previous fetch is still running
            // and must stop before it applies the old draft's build to the new one.
            ResetDraftScope();
            _draftScope = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        }

        // Retried on every event until it succeeds; see FetchSelectableAsync.
        if (!_selectableFetched)
            _ = FetchSelectableAsync();

        IsDraftActive = true;
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

        TurnText = DescribeTurn();
        IsMyTurn = _state.Turn is { IsLocalPlayer: true };

        RenderTeam(Allies, _state.Allies, allyPredictions, isAlly: true);
        RenderTeam(Enemies, _state.Enemies, enemyPredictions, isAlly: false);
        RenderBalance(allyPredictions, enemyPredictions);

        RenderRecommendations(enemyPredictions, allyPredictions);

        // Re-derived on every render, not only when a build lands: the enemy composition keeps
        // growing after my own pick, and a hint frozen at "78 % physisch" from three revealed
        // enemies is a wrong shopping advice once the last two turn out to be mages.
        RenderBuildHints();

        UpdateDraftLiveState(enemyPredictions, allyPredictions);
    }

    /// <summary>
    /// Works out what data this draft is still missing, probes the build cache once per matchup (a
    /// cache hit needs no network at all), and starts the fetch for whatever is left.
    /// </summary>
    private void UpdateDraftLiveState(LanePredictionResult enemyPredictions, LanePredictionResult allyPredictions)
    {
        _enemyPredictions = enemyPredictions;
        _allyPredictions = allyPredictions;
        _pendingEnemyCount = PendingEnemies().Count;

        _buildContext = null;

        _buildIsStandIn = false;

        if (_state.LocalSlot is { IsLocked: true } mine && mine.LockedChampionId != 0)
        {
            var myLane = mine.AssignedLane != Lane.Unknown
                ? mine.AssignedLane
                : allyPredictions.ForCell(mine.CellId)?.Lane ?? Lane.Unknown;

            var opponent = myLane == Lane.Unknown ? 0 : enemyPredictions.ChampionOnLane(myLane);

            // Blind pick never reveals the enemy team, and OP.GG has no build that works without an
            // opponent — so without a stand-in these queues get no build and no rune import for the
            // whole game. Only when NOTHING is revealed; a draft that will show the real opponent in
            // a few seconds is worth waiting for.
            if (opponent == 0 && myLane != Lane.Unknown && StandInOpponent.EnemiesAreHidden(_state))
            {
                opponent = StandInOpponent.For(_meta, myLane, _state.Unavailable, mine.LockedChampionId);
                _buildIsStandIn = opponent != 0;
            }

            if (myLane != Lane.Unknown && opponent != 0)
                _buildContext = (mine.LockedChampionId, myLane, opponent);
        }

        TryLoadCachedBuild();
        UpdateBuildSection();

        // After _buildContext, because the panel answers the same question the build does.
        RenderMatchupPanel();

        TryFetchDraftData();
    }

    private bool BuildMatchesContext
        => _build is not null && _buildContext is { } context
            && _build.ChampionId == context.Champion
            && _build.Lane == context.Lane
            && _build.OpponentId == context.Opponent;

    /// <summary>
    /// Queues work from a client thread onto the UI thread. BeginInvoke throws once the
    /// dispatcher shuts down, and the LCU source can still fire during the two-second dispose
    /// window — on a worker thread that exception would take the whole process down mid-exit.
    /// </summary>
    private void DispatchFromClientThread(Action action)
    {
        if (_dispatcher.HasShutdownStarted)
            return;

        try
        {
            _dispatcher.BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // Shutdown raced the check above; the work is moot anyway.
        }
    }

    /// <summary>Ends the current draft's fetch scope: cancelled first, so in-flight work stops.</summary>
    private void ResetDraftScope()
    {
        if (_draftScope is null)
            return;

        try
        {
            _draftScope.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already gone; nothing running on it either then.
        }

        _draftScope.Dispose();
        _draftScope = null;
    }

    private (int Enemies, bool Build, int Total) PendingFetch()
    {
        var buildPending = _buildContext is { } context
            && !BuildMatchesContext
            && !_buildFetched.Contains(context);

        return (_pendingEnemyCount, buildPending, _pendingEnemyCount + (buildPending ? 1 : 0));
    }

    /// <summary>
    /// The enemies still worth a counter call, keyed exactly as the call will be made. Counting and
    /// selecting run through this same method: two copies of the predicate drifted apart the moment
    /// the lane became part of the key, and a pending count that never reaches zero turns every
    /// client event into an empty round.
    /// </summary>
    private List<(int Champion, Lane Lane, string Name)> PendingEnemies()
    {
        var pending = new List<(int Champion, Lane Lane, string Name)>();

        if (_enemyPredictions is not { } predictions || _liveCallsThisDraft >= MaxLiveCallsPerDraft)
            return pending;

        var seen = new HashSet<(int, Lane)>();

        foreach (var slot in _state.Enemies)
        {
            var id = slot.EffectiveChampionId;
            if (id == 0 || _meta.Champion(id) is not { } enemy)
                continue;

            var prediction = predictions.ForCell(slot.CellId);
            var lane = LiveDraftFetcher.RequestedLane(prediction?.Lane ?? Lane.Unknown);
            var key = (id, lane);

            // Two seats can hover the same champion; that is still one call.
            if (_liveFetched.Contains(key) || !seen.Add(key))
                continue;

            // The first call for a champion always goes out, however unsure the lane — some data
            // beats none. A SECOND lane for the same champion only once the prediction has settled:
            // an uncertain lane wobbles while seats fill up, and each wobble would cost a request.
            var alreadyHaveOne = _liveFetched.Any(entry => entry.Champion == id);
            if (alreadyHaveOne && prediction is not { IsUncertain: false })
                continue;

            pending.Add((id, lane, enemy.Name));
        }

        return pending;
    }

    /// <summary>
    /// Starts the draft fetch if there is anything new to get. Called after every client event, so
    /// each newly revealed pick pulls its own data in without anybody having to ask.
    /// </summary>
    private void TryFetchDraftData()
    {
        if (!_state.IsActive || _meta.IsEmpty || PendingFetch().Total == 0)
            return;

        // One request at a time; whatever appeared meanwhile is picked up when this one finishes.
        // The fetch loop's own Refresh() lands here too — that is progress being painted, not new
        // work, and queueing it produced a pointless extra round after every fetch.
        if (IsFetchingDraftData)
        {
            if (!_refreshingFromFetch)
                _fetchAgainWhenDone = true;

            return;
        }

        if (DateTimeOffset.UtcNow < _fetchCooldownUntil)
            return;

        _ = FetchDraftDataAsync();
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
    /// Fetches what this draft is missing: the matchup guide for the locked pick first, then fresh
    /// counters for every newly revealed enemy, three at a time.
    /// <para>
    /// Runs by itself after every pick, because the alternative — a button — meant the advice was
    /// quietly worse until somebody remembered to press it. It stays cheap: one call per enemy and
    /// lane, one per matchup, a hard ceiling of <see cref="MaxLiveCallsPerDraft"/> for the whole
    /// draft, everything cached on disk, and a failure that costs only its own call.
    /// </para>
    /// </summary>
    private async Task FetchDraftDataAsync()
    {
        IsFetchingDraftData = true;
        _fetchAgainWhenDone = false;
        DraftFetchFailed = false;

        // Bound to the draft, not the app: a fetch must stop when its draft ends instead of
        // writing that draft's counters into the freshly cleared lookup.
        var token = _draftScope?.Token ?? _lifetime.Token;

        var attempted = 0;
        var failed = 0;

        try
        {
            var resolver = new ChampionResolver(_meta.Champions);
            _opGg ??= new OpGgMcpClient();
            var fetcher = new LiveDraftFetcher(
                _opGg, _settings.GameMode, message => CrashLog.Note("Draft-Abruf", message));

            // The build goes first. It is the only result of this whole round the user can act on —
            // the rune button hangs off it — and behind up to five serial enemy calls it regularly
            // arrived after the draft had already ended. Nothing the enemy calls fetch can change
            // which build is wanted: _buildContext comes from lane predictions and the locked pick,
            // and the predictor does not read matchups.
            if (_buildContext is { } context && !BuildMatchesContext
                && !_buildFetched.Contains(context)
                && _meta.Champion(context.Champion) is { } me
                && _meta.Champion(context.Opponent) is { } opponent)
            {
                attempted++;
                ReportFetch($"lädt Build für {me.Name}…");

                if (!await FetchTheBuildAsync(fetcher, context, me, opponent, token).ConfigureAwait(true))
                    failed++;
            }

            if (token.IsCancellationRequested)
                return;

            var pending = PendingEnemies();
            if (pending.Count > 0)
            {
                attempted += pending.Count;
                ReportFetch(pending.Count == 1
                    ? $"lädt Daten zu {pending[0].Name}…"
                    : $"lädt Daten zu {pending.Count} Gegnern…");

                // Three at a time. The update run drives the same endpoint at ten without ever
                // seeing a 429, and a pick phase does not have thirty seconds to spend serially.
                using var gate = new SemaphoreSlim(3);
                var remaining = pending.Count;

                var results = await Task.WhenAll(pending.Select(async entry =>
                {
                    await gate.WaitAsync(token).ConfigureAwait(true);
                    try
                    {
                        // ConfigureAwait(true) throughout is what makes this safe without a lock:
                        // every continuation returns to the dispatcher, so the shared sets, the
                        // lookup and the UI are still only ever touched from one thread.
                        var ok = await FetchOneEnemyAsync(fetcher, entry, resolver, token).ConfigureAwait(true);

                        if (!token.IsCancellationRequested && --remaining > 0)
                            ReportFetch($"lädt Daten zu {remaining} weiteren Gegnern…");

                        return ok;
                    }
                    finally
                    {
                        gate.Release();
                    }
                })).ConfigureAwait(true);

                failed += results.Count(ok => !ok);
            }

            if (token.IsCancellationRequested)
                return;

            _refreshingFromFetch = true;
            try
            {
                Refresh();
            }
            finally
            {
                _refreshingFromFetch = false;
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down, or the draft this fetch belonged to has ended.
            return;
        }
        catch (Exception ex)
        {
            // Anything that got past the per-call handlers is a real defect, not a bad connection.
            CrashLog.Write("Draft-Abruf", ex);
            failed = Math.Max(failed, 1);
            attempted = Math.Max(attempted, 1);
        }
        finally
        {
            IsFetchingDraftData = false;
            UpdateBuildSection();
        }

        if (failed > 0)
        {
            ScheduleFetchRetry(failed, attempted);
            return;
        }

        _fetchFailures = 0;
        _fetchRetry?.Stop();
        DraftFetchFailed = false;
        DraftFetchText = string.Empty;
        ShowDraftFetchStatus = false;

        // A pick that landed while this ran gets its own round.
        if (_fetchAgainWhenDone)
            TryFetchDraftData();
    }

    /// <summary>
    /// One enemy's counters. Returns <see langword="false"/> when the call failed in a way the round
    /// can survive; the enemy simply stays on the pending list for the next attempt.
    /// </summary>
    private async Task<bool> FetchOneEnemyAsync(
        LiveDraftFetcher fetcher,
        (int Champion, Lane Lane, string Name) entry,
        ChampionResolver resolver,
        CancellationToken token)
    {
        if (_meta.Champion(entry.Champion) is not { } enemy)
            return true;

        try
        {
            _liveCallsThisDraft++;
            var stats = await fetcher.FetchEnemyCountersAsync(enemy, entry.Lane, resolver, token)
                .ConfigureAwait(true);

            // The draft may have ended while this was in flight: the sets and the lookup have been
            // cleared by now, and writing into them would leave the NEXT draft with stale edges.
            if (token.IsCancellationRequested)
                return true;

            // Marked as fetched even when empty: asking again would not produce more data.
            _liveFetched.Add((entry.Champion, entry.Lane));

            if (stats.Count > 0)
                _meta.ApplyLiveMatchups(stats);

            // Show each enemy's data as it lands rather than after the last one.
            _refreshingFromFetch = true;
            try
            {
                Refresh();
            }
            finally
            {
                _refreshingFromFetch = false;
            }

            return true;
        }
        catch (Exception ex) when (IsRecoverable(ex, token))
        {
            CrashLog.Note("Draft-Abruf", $"{entry.Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>The matchup guide for the locked pick, with the same per-call error handling.</summary>
    private async Task<bool> FetchTheBuildAsync(
        LiveDraftFetcher fetcher,
        (int Champion, Lane Lane, int Opponent) context,
        ChampionEntry me,
        ChampionEntry opponent,
        CancellationToken token)
    {
        try
        {
            var plan = await fetcher.FetchBuildAsync(me, opponent, context.Lane, _meta.Patch, token)
                .ConfigureAwait(true);

            if (token.IsCancellationRequested)
                return true;

            // Marked AFTER the await, mirroring _liveFetched: marked before it, one network hiccup
            // meant no build and a locked rune button for the rest of the draft, because nothing
            // would ever ask again.
            _buildFetched.Add(context);
            _buildFailedFor = null;
            _buildFailCount = 0;

            if (plan is not null && !plan.IsEmpty)
            {
                _buildCache.Save(plan);
                ApplyBuild(plan);
            }

            return true;
        }
        catch (Exception ex) when (IsRecoverable(ex, token))
        {
            CrashLog.Note("Draft-Abruf", $"Build {me.Name} vs {opponent.Name}: {ex.Message}");

            // Three attempts, then stop asking: a matchup OP.GG cannot answer would otherwise keep
            // the retry timer alive for the whole draft.
            _buildFailCount = _buildFailedFor == context ? _buildFailCount + 1 : 1;
            _buildFailedFor = context;

            if (_buildFailCount >= 3)
                _buildFetched.Add(context);

            return false;
        }
    }

    /// <summary>
    /// Whether a failed call should cost only its own item. A cancelled token means the draft ended
    /// and the whole round is void; the same exception type WITHOUT a cancelled token is the HTTP
    /// timeout, which used to be swallowed as if the draft had ended.
    /// </summary>
    private static bool IsRecoverable(Exception ex, CancellationToken token) => ex switch
    {
        OperationCanceledException => !token.IsCancellationRequested,
        OpGgApiException or OpGgParseException => true,
        System.Text.Json.JsonException or HttpRequestException or IOException => true,
        _ => false,
    };

    /// <summary>
    /// Backs off after a failed round: 3, 6, 12, 24, then 30 seconds. The old flat 30 s cost a
    /// quarter of the whole draft for one hiccup, and it applied to every pending call at once.
    /// </summary>
    private void ScheduleFetchRetry(int failed, int attempted)
    {
        _fetchFailures++;

        var seconds = Math.Min(30, 3 * (1 << Math.Min(_fetchFailures - 1, 4)));
        var backoff = TimeSpan.FromSeconds(seconds);

        _fetchCooldownUntil = DateTimeOffset.UtcNow + backoff;
        DraftFetchFailed = true;
        DraftFetchText = failed >= attempted
            ? "OP.GG nicht erreichbar — versuche es weiter"
            : $"OP.GG: {failed} von {attempted} Abrufen fehlgeschlagen — versuche es weiter";
        ShowDraftFetchStatus = true;

        // The status line just promised to keep trying, so something actually has to: the last picks
        // of a draft produce no further client events to piggyback on.
        _fetchRetry ??= new DispatcherTimer(
            backoff,
            DispatcherPriority.Background,
            (_, _) =>
            {
                _fetchRetry!.Stop();
                TryFetchDraftData();
            },
            _dispatcher);

        _fetchRetry.Stop();

        // Assigned every time, not just on creation: the timer is built with ??= , so the interval
        // of the very first failure would otherwise be the interval forever.
        // One second past the cooldown, or the tick bounces off the cooldown check and does nothing.
        _fetchRetry.Interval = backoff + TimeSpan.FromSeconds(1);
        _fetchRetry.Start();
    }

    private void ReportFetch(string text)
    {
        DraftFetchText = $"OP.GG: {text}";
        ShowDraftFetchStatus = true;
    }

    private void ApplyBuild(BuildPlan plan)
    {
        _build = plan;
        _buildCardClosed = false;
        HasBuild = true;

        // A result line from an earlier import would now sit next to DIFFERENT runes and read as
        // if they had been transferred.
        RuneImportText = string.Empty;

        // No opponent in the title for a stand-in: the 280 px column truncates it and the caveat is
        // exactly the half that gets cut off. It becomes a chip below instead, where it wraps.
        BuildTitle = _buildIsStandIn
            ? $"{plan.ChampionName} · {plan.Lane.Display()}"
            : $"{plan.ChampionName} vs {plan.OpponentName} · {plan.Lane.Display()}";

        var runes = plan.Runes;
        BuildSubtitle = runes is null
            ? $"Patch {plan.Patch}"
            : $"Runen: {runes.WinRate:P0} Winrate über {runes.Play} Spiele · Patch {plan.Patch}";

        // The tiles themselves — runes, purchase order, spells, skills — all live in GameBuild;
        // the draft column and the in-game view render the same content at different sizes.
        ApplyGameBuild(plan);

        // The hints are re-derived on every render pass so they follow the enemy composition, but
        // two of them read _build itself — the stand-in caveat and the "boots adjusted" wording.
        // A cached build lands in the MIDDLE of a render, after the hints for that pass were
        // already built, and if nothing else triggers another pass those two never appear at all.
        RenderBuildHints();
        UpdateBuildSection();
        AnnounceGameActive();

        // The plan's item icons are the one asset that cannot come with the big update — which
        // items matter depends on the matchup. Fetched here, missing files only, ~10 small PNGs.
        _ = FetchItemIconsAsync(plan);
    }

    /// <summary>Downloads missing item icons for the plan and refreshes the tiles once they land.</summary>
    private async Task FetchItemIconsAsync(BuildPlan plan)
    {
        try
        {
            var ids = plan.Starters.Concat(plan.Boots).Concat(plan.CoreItems).Concat(plan.LateItems)
                .SelectMany(set => set.ItemIds)
                .ToList();

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var downloader = new AssetDownloader(http);

            var added = await downloader
                .DownloadItemIconsAsync(ids, plan.Patch, _lifetime.Token)
                .ConfigureAwait(true);

            // Rune and spell icons normally arrive with the big update; fetched here too so a
            // user who never ran one is not stuck with text chips forever.
            IEnumerable<int> runeIds = plan.Runes is { } runes
                ? runes.PrimaryRuneIds.Concat(runes.SecondaryRuneIds)
                : [];
            var spellIds = plan.SummonerSpells.SelectMany(set => set.ItemIds);

            added += await downloader
                .DownloadRuneAndSpellIconsAsync(runeIds, spellIds, plan.Patch, _lifetime.Token)
                .ConfigureAwait(true);

            // Still the same build on screen? Then swap the placeholder labels for the icons.
            if (added > 0 && ReferenceEquals(_build, plan))
                ApplyGameBuild(plan);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            // Icons are decoration on top of a working text view; never let them take it down.
            CrashLog.Write("Item-Icons", ex);
        }
    }

    /// <summary>
    /// Situational pointers from the enemy composition. They order the alternatives the data
    /// already offers — they never invent an item the statistics did not surface.
    /// </summary>
    /// <summary>
    /// The two numbers over the team columns. Deliberately a dash until champions are actually
    /// revealed: a confident "50,0 %" before anyone has picked would be a claim about nothing.
    /// </summary>
    private void RenderBalance(LanePredictionResult allyPredictions, LanePredictionResult enemyPredictions)
    {
        var balance = DraftBalance.Estimate(_meta, _state, allyPredictions, enemyPredictions);

        if (!balance.HasData)
        {
            AllyWinRateText = "—";
            EnemyWinRateText = "—";
            BalanceTone = BalanceTone.Even;
            BalanceHint = "Sobald Champions aufgedeckt sind, steht hier die geschätzte Siegquote "
                + "des Drafts.";
            return;
        }

        // Marked as a win rate, like every other win rate in the window: the team columns also
        // carry the lane-confidence percentages, and unlabelled the two read the same.
        AllyWinRateText = $"{balance.AllyWinRate.ToString("P1", CultureInfo.CurrentCulture)} WR";
        EnemyWinRateText = $"{balance.EnemyWinRate.ToString("P1", CultureInfo.CurrentCulture)} WR";

        // A point either way is inside the noise of the underlying samples; only beyond that does
        // the colour claim anything.
        BalanceTone = balance.AllyWinRate switch
        {
            >= 0.51 => BalanceTone.Ahead,
            <= 0.49 => BalanceTone.Behind,
            _ => BalanceTone.Even,
        };

        var duels = balance.ContestedLanes switch
        {
            0 => "noch kein direktes Lane-Duell in den Daten",
            1 => "1 direktes Lane-Duell",
            _ => $"{balance.ContestedLanes} direkte Lane-Duelle",
        };

        BalanceHint = $"Geschätzte Siegquote dieses Drafts aus {balance.RatedChampions} aufgedeckten "
            + $"Champions und {duels}: gerechnet werden die Lane-Siegquoten beider Teams und die "
            + "Matchups dort, wo sich zwei Picks direkt gegenüberstehen.\n\n"
            + "50 % ist ausgeglichen, Unterschiede unter einem Punkt sind Rauschen. Noch verdeckte "
            + "Picks zählen nicht mit — die Zahl bewegt sich also mit jedem weiteren Pick.";
    }

    /// <summary>
    /// Decides whether the widest column shows a pick list or the matchup, and fills the latter.
    /// <para>
    /// The list is meaningless exactly when the advised seat has already locked AND nobody on our
    /// team is on the clock: it then offers alternatives to a decision that is made. That is the
    /// last minute of every draft — finalisation, and the stretches where the enemy is picking —
    /// and it is the only phase where the 280 px column held the build while the wide one held
    /// nothing anyone could act on. A team-mate on the clock keeps the list: advising them is real.
    /// </para>
    /// </summary>
    private void RenderMatchupPanel()
    {
        var show = _target?.IsSettled == true;

        ShowMatchupPanel = show;
        ShowEmptyHint = !show && !HasRecommendations;
        ShowNarrowBuildCard = ShowBuildSection && !show;

        if (!show)
            return;

        // Not _buildContext: that one additionally requires an opponent, and the panel has
        // something to say without one.
        var mine = _target!.Slot;
        var lane = mine.AssignedLane != Lane.Unknown
            ? mine.AssignedLane
            : _allyPredictions?.ForCell(mine.CellId)?.Lane ?? Lane.Unknown;

        var opponent = lane == Lane.Unknown ? 0 : _enemyPredictions?.ChampionOnLane(lane) ?? 0;
        var ownName = _meta.ChampionName(mine.LockedChampionId);

        MatchupOwnIcon = _icons.Get(mine.LockedChampionId);
        MatchupOpponentIcon = opponent == 0 ? null : _icons.Get(opponent);

        var lanePart = lane == Lane.Unknown ? string.Empty : lane.Display();

        if (opponent == 0)
        {
            MatchupHeadline = ownName;
            MatchupSubline = lanePart;
            MatchupFigure = string.Empty;
            HasMatchupFigure = false;
            MatchupTone = ScoreTone.Weak;
            MatchupNote = "Der Gegner auf deiner Lane ist noch nicht aufgedeckt.";
            return;
        }

        var opponentName = _meta.ChampionName(opponent);
        MatchupHeadline = $"{ownName} vs {opponentName}";

        if (_meta.Matchup(mine.LockedChampionId, opponent, lane) is not { } duel)
        {
            MatchupSubline = lanePart;
            MatchupFigure = string.Empty;
            HasMatchupFigure = false;
            MatchupTone = ScoreTone.Weak;
            MatchupNote = $"OP.GG kennt dieses Duell nicht — von den möglichen Paarungen auf "
                + $"{(lanePart.Length > 0 ? lanePart : "einer Lane")} ist nur etwa ein Fünftel erfasst.";
            return;
        }

        var culture = CultureInfo.CurrentCulture;

        // The absolute rate, not the centred term: "wie steht mein Duell" is answered by the rate
        // as it would be read out loud, and the breakdown next door carries the shift.
        MatchupFigure = $"{duel.WinRate.ToString($"P{ScoreError.Decimals(ScoreError.LogitVariance(duel.WinRate, duel.Play, Shrinkage.MatchupPrior))}", culture)} WR";
        HasMatchupFigure = true;

        MatchupTone = duel.WinRateDelta switch
        {
            > 0.02 => ScoreTone.Strong,
            > -0.02 => ScoreTone.Fair,
            _ => ScoreTone.Weak,
        };

        var sample = duel.Play > 0 ? $" · {duel.Play.ToString("N0", culture)} Spiele" : string.Empty;
        MatchupSubline = $"{lanePart}{sample}";

        MatchupNote = duel.WinRateDelta switch
        {
            > 0.02 => "Das Duell läuft für dich. 50 % wäre ausgeglichen.",
            > -0.02 => "Ein ausgeglichenes Duell — es entscheidet sich im Spiel, nicht im Draft.",
            _ => "Das Duell läuft gegen dich. Vorsichtig spielen und auf Hilfe des Junglers setzen.",
        };
    }

    private void RenderBuildHints()
    {
        var hints = new List<Reason>();

        // The boots slot ADAPTS to these hints (the one slot that does): when the fetched
        // alternatives contain the matching boot, the build shows it and the chip says so —
        // otherwise the chip stays plain advice. Applied lazily below so a late enemy reveal
        // (composition shifting after my pick) updates the shown boots too.
        var preference = SituationalBuild.PreferredBoots(_enemyComp);
        var adjusted = _build is { } plan && SituationalBuild.PickBoots(plan.Boots, preference) is not null;

        // Named first, because everything below it is advice about an opponent we do not have.
        if (_buildIsStandIn && _build is { } standIn)
        {
            hints.Add(Reason.Neutral(
                $"Gegner unbekannt — Build gegen {standIn.OpponentName}",
                $"Diese Warteschlange deckt die gegnerischen Picks nie auf, und OP.GG liefert keinen "
                + $"Build ohne Gegner. Gezeigt wird deshalb das Matchup gegen {standIn.OpponentName} — "
                + $"den am häufigsten gespielten Champion auf {standIn.Lane.Display()}. Runen, Spells und "
                + "Skill-Reihenfolge hängen kaum am Gegenspieler und passen so; die Kern-Items sind "
                + "nur eine Richtung."));
        }

        if (_enemyComp.Count >= 3)
        {
            if (_enemyComp.PhysicalShare >= 0.65)
            {
                var acted = adjusted && preference == BootsPreference.Armor;
                hints.Add(Reason.Neutral(
                    $"Gegner macht {_enemyComp.PhysicalShare:P0} physischen Schaden → "
                        + (acted ? "Stahlkappen im Build vorgezogen" : "Rüstung zuerst"),
                    "Von dem Schaden, den das gegnerische Team austeilt, ist der größte Teil "
                    + "physisch. Rüstung wirkt hier stärker als Magieresistenz."
                    + (acted ? " Die Schuhwahl im Build ist entsprechend angepasst." : string.Empty)));
            }

            if (_enemyComp.MagicShare >= 0.65)
            {
                var acted = adjusted && preference == BootsPreference.MagicResist;
                hints.Add(Reason.Neutral(
                    $"Gegner macht {_enemyComp.MagicShare:P0} magischen Schaden → "
                        + (acted ? "Merkurstiefel im Build vorgezogen" : "Magieresistenz zuerst"),
                    "Von dem Schaden, den das gegnerische Team austeilt, ist der größte Teil "
                    + "magisch. Magieresistenz wirkt hier stärker als Rüstung."
                    + (acted ? " Die Schuhwahl im Build ist entsprechend angepasst." : string.Empty)));
            }

            if (_enemyComp.TotalCrowdControl >= 6)
            {
                var acted = adjusted && preference == BootsPreference.Tenacity;
                hints.Add(Reason.Neutral(
                    "viele Betäubungen im Gegnerteam → "
                        + (acted ? "Merkurstiefel im Build vorgezogen" : "Zähigkeit einplanen"),
                    "Das gegnerische Team hat auffällig viele Effekte, die dich bewegungsunfähig "
                    + "machen. Zähigkeit (z. B. Merkurstiefel) verkürzt deren Dauer."
                    + (acted ? " Die Schuhwahl im Build ist entsprechend angepasst." : string.Empty)));
            }
        }

        // A revealed enemy can shift the preference after the build already landed.
        if (_build is { } current && preference != _appliedBootsPreference)
            ApplyGameBuild(current);

        BuildHints.ReplaceAll(hints);
    }

    /// <summary>Progress or result of the rune import, next to its button.</summary>
    public string RuneImportText
    {
        get => _runeImportText;
        private set => Set(ref _runeImportText, value);
    }

    /// <summary>
    /// Import works while the page can still change: during champion select and up to the load
    /// screen. Once in game the client ignores page changes, so the button locks.
    /// </summary>
    public bool CanImportRunes
        => !_isImportingRunes
            && !IsGameRunning
            // The importer's full precondition (4+2+3), not just the primaries — a weaker check
            // left the button clickable only to produce an error message.
            && _build?.Runes is { } runes
            && runes.PrimaryRuneIds.Count >= 4
            && runes.SecondaryRuneIds.Count >= 2
            && runes.ShardIds.Count >= 3
            && _source?.Client is not null;

    /// <summary>
    /// The tool's one write to the client: creates (or replaces) the DraftPilot rune page for the
    /// current build and selects it. A foreign page is only ever deleted after the user confirmed
    /// it by name.
    /// </summary>
    private async Task ImportRunesAsync()
    {
        if (!CanImportRunes || _build is not { Runes: { } runes } build)
        {
            // A stale button can still fire after the client reconnected or the build changed;
            // saying nothing here reads as "the button is broken".
            RuneImportText = "Gerade nicht möglich — Client verbunden und Build geladen?";
            return;
        }

        _isImportingRunes = true;
        ImportRunesCommand.RaiseCanExecuteChanged();
        RuneImportText = "Übertrage…";

        try
        {
            var result = await RuneImporter.ImportAsync(
                _source!.Client!,
                runes,
                $"{build.ChampionName} vs {build.OpponentName}",
                confirmReplace: name => _dispatcher.Invoke(() =>
                {
                    // Owned by the main window, or a Topmost panel covers the dialog and the
                    // import hangs invisibly on this very callback.
                    var owner = Application.Current?.MainWindow;
                    var text = $"Alle Runenseiten sind belegt. Die Seite „{name}“ löschen und ersetzen?";
                    const string caption = "DirkDraft — Runen übertragen";

                    var choice = owner is { IsVisible: true }
                        ? MessageBox.Show(owner, text, caption, MessageBoxButton.YesNo, MessageBoxImage.Question)
                        : MessageBox.Show(text, caption, MessageBoxButton.YesNo, MessageBoxImage.Question);

                    return choice == MessageBoxResult.Yes;
                }),
                _lifetime.Token).ConfigureAwait(true);

            RuneImportText = result.Message;
        }
        catch (OperationCanceledException)
        {
            // Shutting down; don't leave "Übertrage…" standing.
            RuneImportText = string.Empty;
        }
        catch (Exception ex)
        {
            RuneImportText = $"Fehlgeschlagen: {ex.Message}";
            CrashLog.Write("Runen-Import", ex);
        }
        finally
        {
            _isImportingRunes = false;
            ImportRunesCommand.RaiseCanExecuteChanged();
        }
    }

    private void UpdateBuildSection()
    {
        ImportRunesCommand?.RaiseCanExecuteChanged();

        if (_state.IsActive)
        {
            ShowGameView = false;
            ShowIdleCard = false;
            ShowBuildClose = false;
            ShowBuildSection = HasBuild || _buildContext is not null;
            ShowNarrowBuildCard = ShowBuildSection && !ShowMatchupPanel;
            ShowIdleHero = false;

            // The old text claimed to be waiting for the pick and the lane opponent — in the only
            // situation where the card is visible at all, since the line above needs _buildContext,
            // which needs both. Say what is actually going on instead.
            BuildHint = (HasBuild, _buildContext) switch
            {
                (true, _) => string.Empty,
                (false, { } ctx) when _buildFailedFor == ctx => "Build konnte nicht geladen werden — versuche es weiter.",
                (false, { } ctx) when _buildFetched.Contains(ctx) => "OP.GG hat zu diesem Duell keinen Build.",
                (false, { } ctx) => $"Build für {_meta.ChampionName(ctx.Champion)} gegen {_meta.ChampionName(ctx.Opponent)} wird geladen…",
                _ => string.Empty,
            };

            return;
        }

        // While the game runs, the full-width build view takes over from both the idle card and
        // the compact post-draft card — it is the same content with room to breathe.
        ShowGameView = IsGameRunning && HasBuild && !_buildCardClosed;
        ShowIdleCard = !ShowGameView;

        // After the draft the build stays on screen as a card, so the shopping order is still
        // there when the user tabs out mid-game. Closed by hand or by the next draft.
        ShowBuildClose = true;
        ShowBuildSection = HasBuild && !_buildCardClosed && !ShowGameView;
        ShowNarrowBuildCard = ShowBuildSection;
        ShowIdleHero = ShowIdleCard && !ShowBuildSection;
        BuildHint = string.Empty;
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

            row.IsTarget = _target?.Slot.CellId == slot.CellId;

            // Only for an enemy seat that has revealed a champion and whose lane the client did
            // not hand us: everywhere else "uncertain" would be either noise or guesswork dressed
            // up as a reading.
            row.IsUncertain = !isAlly
                && slot.AssignedLane == Lane.Unknown
                && slot.EffectiveChampionId != 0
                && prediction is { IsUncertain: true };

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

    private void RenderRecommendations(LanePredictionResult enemyPredictions, LanePredictionResult allyPredictions)
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
            _state, _target, enemyPredictions, allyPredictions, selectable, _settings.RecommendationCount);

        var allyIndex = _state.Allies.ToList().FindIndex(slot => slot.CellId == _target.Slot.CellId);
        var who = _target.Slot.CellId == _state.LocalCellId || allyIndex < 0
            ? "dich"
            : $"Mitspieler {allyIndex + 1}";

        var isBan = set.Action == TurnAction.Ban;
        var mode = isBan ? "Bans" : "Picks";
        var suffix = _target.IsFollowingTurn ? string.Empty : " (Ausblick)";

        // The matchup panel takes the column in this phase, so the header names that instead of a
        // list nobody can act on.
        if (_target.IsSettled)
        {
            ListHeader = set.Lane == Lane.Unknown ? "Dein Matchup" : $"Dein Matchup · {set.Lane.Display()}";
            HasRecommendations = false;
            return;
        }
        var lanePart = set.Lane == Lane.Unknown ? string.Empty : $" · {set.Lane.Display()}";

        // When the top picks sit inside the sampling error of the numbers behind them, their order
        // is noise and the header says so. The bar is the measured error, not a fixed fraction of a
        // point: a thin duo statistic makes four champions equivalent, while 40.000-game lane data
        // separates them at a tenth of that distance.
        var tied = isBan ? 0 : ScoreError.CountLeadingTies(set.Items);
        var tiePart = tied >= 2 ? $" — Top {tied} nahezu gleich" : string.Empty;

        ListHeader = $"{mode} für {who}{lanePart}{suffix}{tiePart}";

        HasRecommendations = set.Items.Count > 0;

        // The hint must describe the draft that is on screen now, not the idle state before it.
        if (!HasRecommendations)
        {
            EmptyHint = _meta.IsEmpty
                ? "Meta-Daten fehlen — Empfehlungen gibt es erst nach „Daten aktualisieren“."
                : "Keine Kandidaten verfügbar — alles auf dieser Lane ist gebannt oder vergeben.";
        }

        // One precision for the whole column, decided by the list, not by each row.
        var precision = isBan ? 1 : ScoreError.Decimals(set.Items);

        Recommendations.Resize(set.Items.Count, () => new RecommendationViewModel());
        for (var i = 0; i < set.Items.Count; i++)
            Recommendations[i].Apply(i + 1, set.Items[i], _icons.Get(set.Items[i].ChampionId), isBan, precision);

        _enemyComp = set.EnemyComp;

        // The queue caveat goes first: everything after it is lane advice, and in a mode without
        // lanes that is the one thing the reader has to know before reading the rest.
        var findings = new List<string>();

        if (_state.Queue.LaneCaveat() is { Length: > 0 } caveat)
            findings.Add(caveat);

        findings.AddRange(set.AllyComp.Findings.Select(finding => finding.Text));

        Warnings.ReplaceAll(findings);
    }

    private void Clear()
    {
        Recommendations.Clear();
        Warnings.Clear();
        HasRecommendations = false;
        ShowMatchupPanel = false;
        ShowEmptyHint = false;
        PhaseText = string.Empty;
        IsMyTurn = false;
        TurnText = "Kein Champ Select";
        AllyWinRateText = "—";
        EnemyWinRateText = "—";
        BalanceTone = BalanceTone.Even;
        BalanceHint = string.Empty;
        ListHeader = "Empfehlungen";

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
            row.LaneIndex = 5;
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
                _ => "Niemand am Zug",
            };
        }

        var action = turn.Action == TurnAction.Ban ? "bannt" : "pickt";

        // Second person needs its own conjugation — "Du pickt" was wrong all along and only
        // became obvious once the status grew into the card's headline.
        if (turn.IsLocalPlayer)
            return turn.Action == TurnAction.Ban ? "Du bannst" : "Du pickst";

        if (!turn.IsAlly)
        {
            var enemyIndex = _state.Enemies.ToList().FindIndex(slot => slot.CellId == turn.CellId);
            return enemyIndex >= 0 ? $"Gegner {enemyIndex + 1} {action}" : $"Gegner {action}";
        }

        var index = _state.Allies.ToList().FindIndex(slot => slot.CellId == turn.CellId);
        return index >= 0 ? $"Mitspieler {index + 1} {action}" : $"Mitspieler {action}";
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

        // Three clocks that must not be mixed up: the file time says when the button was pressed,
        // Patch is the client version at that moment, and DataPatch is the patch OP.GG's numbers
        // actually describe. Only the last one answers "do these numbers still fit the game", so it
        // gets the headline whenever the snapshot carries it.
        var age = _store.Age();
        var ageText = age is null
            ? string.Empty
            : age.Value.TotalDays >= 1
                ? $" · vor {(int)age.Value.TotalDays} Tagen geholt"
                : $" · vor {(int)age.Value.TotalHours} h geholt";

        // No scolding below a week: a two-day-old snapshot in the same patch is perfectly current.
        var nudge = age is { TotalDays: >= 7 } ? " · ein Update lohnt sich" : string.Empty;

        var patchText = _meta.DataPatch.Length > 0
            ? $"OP.GG-Patch {_meta.DataPatch}"
            : $"Patch {_meta.Patch}";

        HasMeta = true;
        SnapshotText = $"Daten: {patchText}{ageText}{nudge}";
        SnapshotDetail = DescribeDataProvenance();
    }

    /// <summary>
    /// The long form behind the footer: which numbers, from when, and whether OP.GG was still on the
    /// previous patch when they were fetched.
    /// </summary>
    private string DescribeDataProvenance()
    {
        var lines = new List<string>(5);

        if (_meta.DataAsOfUtc is { } asOf)
            lines.Add($"Zahlenstand laut OP.GG: {asOf.ToLocalTime():dd.MM.yyyy HH:mm}");

        if (_meta.Patch.Length > 0)
            lines.Add($"Client-Patch beim Abruf: {_meta.Patch}");

        if (_meta.DataPatch.Length > 0 && !_meta.Patch.StartsWith(_meta.DataPatch, StringComparison.Ordinal))
            lines.Add("OP.GG lag beim Abruf einen Patch zurück.");

        // Snapshot warnings had no reader in the app at all — the update wrote them and only the
        // console tool ever showed them.
        lines.AddRange(_meta.Warnings);

        lines.Add(DataSummary);

        return string.Join("\n", lines);
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

        // Shown with the result: makes the update's speed visible, and a future regression obvious.
        var updateClock = System.Diagnostics.Stopwatch.StartNew();

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
                () => builder.BuildAsync(
                    new SnapshotBuildOptions { GameMode = _settings.GameMode, Language = _settings.DataLanguage },
                    progress,
                    scope.Token),
                scope.Token).ConfigureAwait(true);

            // Written atomically; a failure above leaves the previous snapshot in place and in use.
            _store.Save(snapshot);

            _meta = new MetaLookup(snapshot);
            _predictor = new LanePredictor(_meta, _seatPriors);
            _recommender = new Recommender(_meta, _traits);

            // Newly downloaded portraits and names would otherwise stay invisible until restart.
            _icons.Clear();
            _names = AssetNames.Load(_settings.DataLanguage);

            // The patch may just have changed; sweep the previous one's build plans right away.
            var freshPatch = _meta.IsEmpty || string.IsNullOrWhiteSpace(_meta.Patch) ? null : _meta.Patch;
            _ = Task.Run(() => RunMaintenance(freshPatch));

            // The update just proved the snapshot loads; without this the 5-second retry poll ran
            // forever whenever the file watcher could not be created.
            SetSnapshotRetryRunning(false);

            UpdateSnapshotText();
            UpdateStatus = $"Aktualisiert in {updateClock.Elapsed:m\\:ss}: "
                + $"{snapshot.Champions.Count} Champions, {snapshot.Matchups.Count} Matchups";
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
        SetSnapshotRetryRunning(false);
        _fetchRetry?.Stop();
        _snapshotWatcher?.Dispose();
        await _lifetime.CancelAsync().ConfigureAwait(false);
        _tracker.Changed -= OnTrackerChanged;
        await _tracker.DisposeAsync().ConfigureAwait(false);
        _draftScope?.Dispose();
        _opGg?.Dispose();
        _lifetime.Dispose();
    }
}
