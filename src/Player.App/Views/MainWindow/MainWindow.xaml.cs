using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Windows.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Player.App.Models;
using Player.App.Services;

namespace Player.App;

public partial class MainWindow : Window
{
    private const string AppDisplayName = "Relay Player";
    private const string DefaultViewTitle = "继续观看";
    private const int HomeListLimit = 80;

    private readonly SettingsStore _settingsStore = new();
    private readonly EmbyApiClient _embyClient = new();
    private readonly MpvNetLauncher _mpvLauncher = new();
    private readonly MpvIpcClient _mpvIpcClient = new();
    private readonly AppLogger _logger = new();
    private readonly PlaybackCoordinator _playbackCoordinator;
    private readonly ArtworkResolver _artworkResolver;
    private readonly ServerProfileManager _serverProfileManager;
    private readonly EpisodeSelectionCoordinator _episodeSelectionCoordinator = new();
    private readonly ObservableCollection<ServerProfile> _serverProfiles = [];
    private readonly ObservableCollection<EmbyItem> _items = [];
    private readonly ObservableCollection<EmbyItem> _seasons = [];
    private readonly ObservableCollection<EmbyItem> _episodes = [];
    private readonly Stack<BrowseState> _navigationStack = new();

    private AppSettings _settings = new();
    private EmbyItem? _selectedItem;
    private PlaybackInfoResponse? _selectedPlaybackInfo;
    private CancellationLease? _loadCancellation;
    private CancellationLease? _selectionCancellation;
    private CancellationLease? _episodeNavigationCancellation;
    private CancellationLease? _posterCancellation;
    private bool _isInitializing;
    private bool _isSwitchingServerProfile;
    private bool _suppressItemSelectionChanged;
    private bool _isApplyingEpisodeSelection;
    private bool _isApplyingMediaSourceSelection;
    private bool _isBusy;
    private string? _activeSeriesId;
    private BrowseState _currentState = new(BrowseViewKind.Resume, DefaultViewTitle);

    public MainWindow()
    {
        _playbackCoordinator = new PlaybackCoordinator(_embyClient, _mpvLauncher, _mpvIpcClient, _logger);
        _artworkResolver = new ArtworkResolver(_embyClient);
        _serverProfileManager = new ServerProfileManager();
        InitializeComponent();
        TryApplyWindowIcon();
        _embyClient.PlaybackReportFailed += EmbyClient_PlaybackReportFailed;
        _playbackCoordinator.StatusChanged += PlaybackCoordinator_StatusChanged;
        ItemsListView.ItemsSource = _items;
        ServerListBox.ItemsSource = _serverProfiles;
        SeasonComboBox.ItemsSource = _seasons;
        EpisodeComboBox.ItemsSource = _episodes;
        SetBusy(false);
    }
}


