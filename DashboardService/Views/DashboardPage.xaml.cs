using DashboardService.Helpers;
using DashboardService.Models;
using DashboardService.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace DashboardService.Views
{
    public partial class DashboardPage : Page
    {
        private readonly ListPager<Employee> _membersPager = new();

        public ObservableCollection<ChamberDashboard> Chambers { get; set; }

        public ObservableCollection<Employee> Employees { get; set; }

        public ObservableCollection<RfidReaderLiveStatus> ConnectedRfidReaders { get; set; }

        public ObservableCollection<RfidReaderLiveStatus> DisconnectedRfidReaders { get; set; }

        public ObservableCollection<SensorLiveStatus> ConnectedSensors { get; set; }

        public ObservableCollection<SensorLiveStatus> DisconnectedSensors { get; set; }

        private readonly DispatcherTimer _countdownTimer;
        private readonly DispatcherTimer _refreshTimer;
        
        private readonly User _currentUser;
        private readonly MonitoringService _monitoringService = new();
        private readonly SystemLogStatusService _systemLogStatusService = new();
        private readonly AlertMessageService _alertMessageService = new();
        private readonly VoiceAnnouncementService _voiceAnnouncementService;
        private readonly HashSet<long> _announcementInFlight = new();
        private readonly HashSet<long> _enqueuedAlertIds = new();
        private readonly SemaphoreSlim _announcementProcessLock = new(1, 1);
        private SensorReading? _latestSensorReading;

        //Payal
        private readonly DispatcherTimer _sensorViolationTimer;
        private readonly DispatcherTimer _sensorReadingTimer;
        private readonly ConfigurationService _configurationService = new();
        private readonly SensorConfigurationService _sensorConfigurationService = new();
        private int _configuredSensorCount;
        public ObservableCollection<SensorViolation> ActiveSensorViolations { get; set; }
        private readonly DispatcherTimer _sensorBlinkTimer;
        private bool _sensorBlinkState;
        private string _selectedVoiceCulture = AlertMessageService.CultureEnglishIndia;
        private bool _sensorVoiceEnabled = true;

        public DashboardPage()
            : this(new User
            {
                UserName = "admin",
                FullName = "Administrator",
                Role = "ADMIN"
            })
        {
        }

        public DashboardPage(User currentUser)
        {
            InitializeComponent();

            _currentUser = currentUser;
            ApplyCurrentUser();

            _voiceAnnouncementService = new VoiceAnnouncementService(alertId => _monitoringService.MarkAnnouncementPlayedAsync(alertId));
            _voiceAnnouncementService.VoicePlayingChanged += VoiceAnnouncementService_VoicePlayingChanged;

            Chambers = new ObservableCollection<ChamberDashboard>();
            Employees = new ObservableCollection<Employee>();
            ConnectedRfidReaders = new ObservableCollection<RfidReaderLiveStatus>();
            DisconnectedRfidReaders = new ObservableCollection<RfidReaderLiveStatus>();
            ConnectedSensors = new ObservableCollection<SensorLiveStatus>();
            DisconnectedSensors = new ObservableCollection<SensorLiveStatus>();
            ActiveSensorViolations = new ObservableCollection<SensorViolation>(); //Payal

            ChambersItemsControl.ItemsSource = Chambers;
            MembersPagerBar.Bind(_membersPager);
            MembersDataGrid.ItemsSource = _membersPager.PageItems;
            ConnectedReadersItemsControl.ItemsSource = ConnectedRfidReaders;
            DisconnectedReadersItemsControl.ItemsSource = DisconnectedRfidReaders;
            ConnectedSensorsItemsControl.ItemsSource = ConnectedSensors;
            DisconnectedSensorsItemsControl.ItemsSource = DisconnectedSensors;

            _countdownTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _countdownTimer.Tick += CountdownTimer_Tick;
            _countdownTimer.Start();

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10)
            };
            _refreshTimer.Tick += RefreshTimer_Tick;
            _refreshTimer.Start();

            var sensorAlertSettings = _configurationService.GetSensorAlertSettings(); // Payal

            _sensorViolationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(sensorAlertSettings.SensorViolationDbCheckIntervalSeconds)
            };

            _sensorViolationTimer.Tick += SensorViolationTimer_Tick;
            _sensorViolationTimer.Start();

            _sensorReadingTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(sensorAlertSettings.CheckLiveSensorReadingInterval)
            };
            _sensorReadingTimer.Tick += SensorReadingTimer_Tick;
            _sensorReadingTimer.Start();

            _sensorBlinkTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };

            _sensorBlinkTimer.Tick += SensorBlinkTimer_Tick;
            _sensorBlinkTimer.Start();

            Loaded += DashboardPage_Loaded;
            Unloaded += DashboardPage_Unloaded;

            SyncThemeToggleUi(ThemeService.IsDarkMode);
            ApplySciFiChrome(ThemeService.IsDarkMode);
            ThemeService.ThemeChanged += ThemeService_ThemeChanged;
            UpdateSensorVoiceButtons();
        }

        private void ThemeService_ThemeChanged(bool isDark)
        {
            Dispatcher.Invoke(() =>
            {
                SyncThemeToggleUi(isDark);
                ApplySciFiChrome(isDark);
            });
        }

        private async void ThemeToggle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            bool enabling = !ThemeService.IsDarkMode;

            if (enabling)
            {
                await PlaySciFiBootSequenceAsync();
                ThemeService.SetDarkMode(true);
            }
            else
            {
                ThemeService.SetDarkMode(false);
                DashboardTitleText.Text = "Live Dashboard";
            }
        }

        private async Task PlaySciFiBootSequenceAsync()
        {
            SciFiBootOverlay.Visibility = Visibility.Visible;
            SciFiBootTitle.Text = "DARK MODE ENGAGED";
            SciFiBootSubtitle.Text = "Switching to dark interface...";
            SciFiBootProgress.Width = 0;
            SciFiScanLine.Margin = new Thickness(0, 0, 0, 0);

            var progressAnim = new DoubleAnimation(0, 280, TimeSpan.FromMilliseconds(1400))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            SciFiBootProgress.BeginAnimation(FrameworkElement.WidthProperty, progressAnim);

            var scanAnim = new ThicknessAnimation(
                new Thickness(0, 0, 0, 0),
                new Thickness(0, Math.Max(ActualHeight, 700), 0, 0),
                TimeSpan.FromMilliseconds(1400))
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            SciFiScanLine.BeginAnimation(FrameworkElement.MarginProperty, scanAnim);

            await Task.Delay(1550);

            SciFiBootSubtitle.Text = "DARK INTERFACE ONLINE";
            await Task.Delay(450);

            SciFiBootOverlay.Visibility = Visibility.Collapsed;
            SciFiBootProgress.BeginAnimation(FrameworkElement.WidthProperty, null);
            SciFiScanLine.BeginAnimation(FrameworkElement.MarginProperty, null);
            DashboardTitleText.Text = "Live Dashboard";
        }

        private void SyncThemeToggleUi(bool isDark)
        {
            ThemeToggleStateText.Text = isDark ? "ON" : "OFF";
            ThemeToggleKnob.HorizontalAlignment = isDark
                ? HorizontalAlignment.Right
                : HorizontalAlignment.Left;
            ThemeToggleKnob.Margin = isDark
                ? new Thickness(0, 0, 3, 0)
                : new Thickness(3, 0, 0, 0);

            DashboardTitleText.Text = "Live Dashboard";
        }

        private void ApplySciFiChrome(bool isDark)
        {
            double overlayOpacity = isDark ? 1 : 0;
            SidebarSciFiOverlay.Opacity = overlayOpacity;
            ContentSciFiOverlay.Opacity = overlayOpacity;

            var cardBg = (Brush)FindResource("CardBgBrush");
            var textPrimary = (Brush)FindResource("TextPrimaryBrush");
            var gridLine = (Brush)FindResource("GridLineBrush");
            var altRow = (Brush)FindResource("AltRowBgBrush");

            MembersDataGrid.Background = cardBg;
            MembersDataGrid.Foreground = textPrimary;
            MembersDataGrid.RowBackground = Brushes.Transparent;
            MembersDataGrid.AlternatingRowBackground = Brushes.Transparent;
            MembersDataGrid.HorizontalGridLinesBrush = Brushes.Transparent;
        }

        private async void DashboardPage_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshLiveDataAsync();
            await RefreshLiveSensorReadingAsync();
        }

        private void DashboardPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _countdownTimer.Stop();
            _refreshTimer.Stop();
            _sensorReadingTimer.Stop();
            _sensorViolationTimer.Stop();
            _sensorBlinkTimer.Stop();
            ThemeService.ThemeChanged -= ThemeService_ThemeChanged;
            _voiceAnnouncementService.VoicePlayingChanged -= VoiceAnnouncementService_VoicePlayingChanged;
            _voiceAnnouncementService.Dispose();
        }

        private void VoiceAnnouncementService_VoicePlayingChanged(long transactionId, bool isPlaying)
        {
            Dispatcher.Invoke(() =>
            {
                foreach (var employee in Employees.Where(x => x.TransactionId == transactionId))
                {
                    employee.IsVoicePlaying = isPlaying;
                }

                UpdateStopAllButtonVisibility();
            });
        }

        private void SyncVoicePlayingFlags()
        {
            foreach (var employee in Employees)
            {
                employee.IsVoicePlaying =
                    _voiceAnnouncementService.IsPlaying(employee.TransactionId);
            }

            UpdateStopAllButtonVisibility();
        }

        private void UpdateStopAllButtonVisibility()
        {
            bool anyPlaying = Employees.Any(x => x.IsVoicePlaying) ||
                              _voiceAnnouncementService.HasAnyPlaying;

            StopAllVoiceButton.Visibility =
                anyPlaying ? Visibility.Visible : Visibility.Collapsed;
        }

        private void StartVoiceLoop(AnnouncementRequest announcement)
        {
            if (announcement.TransactionId <= 0 ||
                string.IsNullOrWhiteSpace(announcement.Message))
            {
                return;
            }

            var settings = new ConfigurationService().GetAlertSettings();
            if (!settings.VoiceEnabled)
            {
                // Still mark as handled so DB alerts don't pile up as "unplayed".
                if (announcement.AlertId > 0)
                {
                    _ = _monitoringService.MarkAnnouncementPlayedAsync(announcement.AlertId);
                }

                return;
            }

            _enqueuedAlertIds.Add(announcement.AlertId);
            _voiceAnnouncementService.StartLooping(
                announcement.TransactionId,
                announcement.GetVoiceLines(_selectedVoiceCulture),
                announcement.AlertId);
        }

        private void VoiceLanguageRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (VoiceLangBengaliRadio?.IsChecked == true)
            {
                _selectedVoiceCulture = AlertMessageService.CultureBengaliIndia;
            }
            else
            {
                _selectedVoiceCulture = AlertMessageService.CultureEnglishIndia;
            }
        }

        private void StopSensorVoice_Click(object sender, RoutedEventArgs e)
        {
            _sensorVoiceEnabled = false;
            _voiceAnnouncementService.StopOneTimeAnnouncements();
            UpdateSensorVoiceButtons();
        }

        private async void StartSensorVoice_Click(object sender, RoutedEventArgs e)
        {
            _sensorVoiceEnabled = true;
            UpdateSensorVoiceButtons();

            try
            {
                await _monitoringService.ClearSensorAnnouncementMarksAsync(1);
                await CheckSensorViolationsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Start sensor voice failed: {ex.Message}");
            }
        }

        private void UpdateSensorVoiceButtons()
        {
            if (StopSensorVoiceButton == null || StartSensorVoiceButton == null)
            {
                return;
            }

            StopSensorVoiceButton.IsEnabled = _sensorVoiceEnabled;
            StartSensorVoiceButton.IsEnabled = !_sensorVoiceEnabled;
        }

        private void StopMemberVoice_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: Employee employee })
            {
                return;
            }

            _voiceAnnouncementService.Stop(employee.TransactionId);
            employee.IsVoicePlaying = false;
            UpdateStopAllButtonVisibility();
        }

        private void StopAllVoice_Click(object sender, RoutedEventArgs e)
        {
            _voiceAnnouncementService.StopAll();

            foreach (var employee in Employees)
            {
                employee.IsVoicePlaying = false;
            }

            UpdateStopAllButtonVisibility();
        }

        private async void RefreshTimer_Tick(object? sender, EventArgs e)
        {
            await RefreshLiveDataAsync();
        }

        private async void SensorReadingTimer_Tick(object? sender, EventArgs e)
        {
            await RefreshLiveSensorReadingAsync();
        }

        private async Task RefreshLiveSensorReadingAsync()
        {
            try
            {
                _latestSensorReading = await _monitoringService.GetCurrentSensorReadingAsync(1);
                UpdateSensorDisplay();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Live sensor reading refresh failed: {ex.Message}");
            }
        }

        private async Task RefreshLiveDataAsync()
        {
            try
            {
                var chambers = await _monitoringService.GetChamberOccupancyAsync();
                var members = await _monitoringService.GetMembersInsideAsync();

                Chambers.Clear();
                foreach (var chamber in chambers)
                {
                    Chambers.Add(chamber);
                }

                Employees.Clear();
                foreach (var member in members)
                {
                    member.IsVoicePlaying =
                        _voiceAnnouncementService.IsPlaying(member.TransactionId);
                    Employees.Add(member);
                }

                RefreshPreviewMembers();
                UpdateCountdowns();
                UpdateDashboardSummary();
                SyncVoicePlayingFlags();
                await RefreshRfidReaderStatusAsync();
                await RefreshSensorConnectionStatusAsync();
                await EnqueueUnplayedAnnouncementsAsync();
                await ProcessDueAnnouncementsAsync();
            }
            catch (Exception ex)
            {
                _refreshTimer.Stop();
            _sensorReadingTimer.Stop();
                MessageBox.Show(
                    ex.Message,
                    "Dashboard",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async Task RefreshRfidReaderStatusAsync()
        {
            try
            {
            var statuses = await _systemLogStatusService.GetLatestRfidStatusesAsync();

            ConnectedRfidReaders.Clear();
            DisconnectedRfidReaders.Clear();

            var connected = statuses.Where(x => x.IsConnected).ToList();
            var disconnected = statuses.Where(x => !x.IsConnected).ToList();

            foreach (var row in connected)
            {
                ConnectedRfidReaders.Add(MapRfidLog(row));
            }

            foreach (var row in disconnected)
            {
                DisconnectedRfidReaders.Add(MapRfidLog(row));
            }

            DateTime? lastAt = statuses.Count > 0 ? statuses.Max(x => x.CreatedAt) : null;
            RfidLastCheckedText.Text = lastAt.HasValue
                ? $"Last log: {lastAt:dd-MM-yyyy HH:mm:ss}"
                : "Last log: --";

            RfidServiceStatusText.Text =
                statuses.Count == 0
                    ? "No RFID connection logs in system_logs."
                    : $"{connected.Count} connected · {disconnected.Count} disconnected";

            ConnectedReadersCountText.Text = $"({connected.Count})";
            DisconnectedReadersCountText.Text = $"({disconnected.Count})";

            NoConnectedReadersText.Text = statuses.Count == 0
                ? "No RFID connection events in system_logs."
                : "No RFID readers connected.";
            NoDisconnectedReadersText.Text = statuses.Count == 0
                ? "No RFID disconnect events in system_logs."
                : "All RFID readers are connected.";

            NoConnectedReadersText.Visibility =
                connected.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            NoDisconnectedReadersText.Visibility =
                disconnected.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                NoConnectedReadersText.Text = $"Could not load system_logs: {ex.Message}";
                NoDisconnectedReadersText.Text = "RFID connection history unavailable.";
                NoConnectedReadersText.Visibility = Visibility.Visible;
                NoDisconnectedReadersText.Visibility = Visibility.Visible;
            }
        }

        private async Task RefreshSensorConnectionStatusAsync()
        {
            try
            {
            var statuses = await _systemLogStatusService.GetLatestSensorStatusesAsync();

            ConnectedSensors.Clear();
            DisconnectedSensors.Clear();

            var connected = statuses.Where(x => x.IsConnected).ToList();
            var disconnected = statuses.Where(x => !x.IsConnected).ToList();

            foreach (var row in connected)
            {
                ConnectedSensors.Add(MapSensorLog(row));
            }

            foreach (var row in disconnected)
            {
                DisconnectedSensors.Add(MapSensorLog(row));
            }

            int connectedCount = ConnectedSensors.Count;
            int disconnectedCount = DisconnectedSensors.Count;
            _configuredSensorCount = Math.Max(1, connectedCount + disconnectedCount);

            DateTime? lastAt = statuses.Count > 0 ? statuses.Max(x => x.CreatedAt) : null;
            if (SensorLastCheckedText != null)
            {
                SensorLastCheckedText.Text = lastAt.HasValue
                    ? $"Last log: {lastAt:dd-MM-yyyy HH:mm:ss}"
                    : "Last log: --";
            }

            if (SensorServiceStatusText != null)
            {
                SensorServiceStatusText.Text =
                    statuses.Count == 0
                        ? "No sensor connection logs in system_logs."
                        : $"{connectedCount} connected · {disconnectedCount} disconnected";
            }

            if (ConnectedSensorsCountText != null)
            {
                ConnectedSensorsCountText.Text = $"({connectedCount})";
            }

            if (DisconnectedSensorsCountText != null)
            {
                DisconnectedSensorsCountText.Text = $"({disconnectedCount})";
            }

            if (NoConnectedSensorsText != null)
            {
                NoConnectedSensorsText.Visibility =
                    connectedCount == 0 ? Visibility.Visible : Visibility.Collapsed;
                NoConnectedSensorsText.Text = statuses.Count == 0
                    ? "No sensor connection events in system_logs."
                    : "No sensors connected.";
            }

            if (NoDisconnectedSensorsText != null)
            {
                NoDisconnectedSensorsText.Visibility =
                    disconnectedCount == 0 ? Visibility.Visible : Visibility.Collapsed;
                NoDisconnectedSensorsText.Text = statuses.Count == 0
                    ? "No sensor disconnect events in system_logs."
                    : "All sensors are connected.";
            }
            }
            catch (Exception ex)
            {
                if (NoConnectedSensorsText != null)
                {
                    NoConnectedSensorsText.Text = $"Could not load system_logs: {ex.Message}";
                    NoConnectedSensorsText.Visibility = Visibility.Visible;
                }

                if (NoDisconnectedSensorsText != null)
                {
                    NoDisconnectedSensorsText.Text = "Sensor connection history unavailable.";
                    NoDisconnectedSensorsText.Visibility = Visibility.Visible;
                }
            }
        }

        private static RfidReaderLiveStatus MapRfidLog(SystemLogConnectionStatus row)
        {
            string ip = row.LocationDisplay;
            int port = 0;
            int colon = row.LocationDisplay.LastIndexOf(':');
            if (colon > 0 && int.TryParse(row.LocationDisplay[(colon + 1)..], out int parsedPort))
            {
                ip = row.LocationDisplay[..colon];
                port = parsedPort;
            }

            return new RfidReaderLiveStatus
            {
                ReaderName = row.DeviceName,
                IpAddress = ip,
                Port = port,
                ReaderPurpose = row.DetailDisplay
            };
        }

        private static SensorLiveStatus MapSensorLog(SystemLogConnectionStatus row) =>
            new()
            {
                SensorName = row.DeviceName,
                LocationDisplay = row.LocationDisplay,
                DetailDisplay = $"{row.LocationDisplay} · {row.DetailDisplay}"
            };

        private void RefreshPreviewMembers()
        {
            // Mix statuses so the first page is not only violations.
            var ordered = Employees
                .OrderBy(x => x.Status switch
                {
                    "Violation" => 0,
                    "Warning" => 1,
                    "Attention" => 2,
                    _ => 3
                })
                .ThenBy(x => x.RemainingTime)
                .ToList();

            _membersPager.SetItems(ordered);

            ViewMoreMembersButton.Visibility =
                Employees.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void CountdownTimer_Tick(object? sender, EventArgs e)
        {
            UpdateCountdowns();
            UpdateDashboardSummary();
            await ProcessDueAnnouncementsAsync();
        }

        private void UpdateCountdowns()
        {
            foreach (var employee in Employees)
            {
                DateTime allowedExitTime =
                    employee.EntryTime.AddMinutes(employee.TimeThresholdMinutes);

                employee.RemainingTime = allowedExitTime - DateTime.Now;
            }
        }

        private async Task EnqueueUnplayedAnnouncementsAsync()
        {
            var pending = await _monitoringService.GetUnplayedAnnouncementsAsync();

            foreach (var announcement in pending)
            {
                StartVoiceLoop(announcement);
            }
        }

        private async Task ProcessDueAnnouncementsAsync()
        {
            if (!await _announcementProcessLock.WaitAsync(0))
            {
                return;
            }

            try
            {
                foreach (var employee in Employees.ToList())
                {
                    if (employee.TransactionId <= 0)
                    {
                        continue;
                    }

                    // Keep continuous loop; don't stack new alerts while voice is active.
                    if (_voiceAnnouncementService.IsPlaying(employee.TransactionId))
                    {
                        continue;
                    }

                    if (!_announcementInFlight.Add(employee.TransactionId))
                    {
                        continue;
                    }

                    try
                    {
                        AnnouncementRequest? announcement =
                            await _monitoringService.TryCreateDueAnnouncementAsync(employee);

                        if (announcement != null &&
                            !string.IsNullOrWhiteSpace(announcement.Message))
                        {
                            StartVoiceLoop(announcement);
                        }
                    }
                    catch
                    {
                    }
                    finally
                    {
                        _announcementInFlight.Remove(employee.TransactionId);
                    }
                }
            }
            finally
            {
                _announcementProcessLock.Release();
            }
        }

        private void UpdateDashboardSummary()
        {
            MembersInsideText.Text = Employees.Count.ToString();
            ActiveChambersText.Text = Chambers.Count.ToString();

            AlertsText.Text = Employees.Count(x =>
                x.Status == "Attention" || x.Status == "Warning").ToString();

            ViolationsText.Text = Employees.Count(x =>
                x.Status == "Violation").ToString();
        }

        private void ViewMoreMembers_Click(object sender, RoutedEventArgs e)
        {
            var window = new CurrentMembersWindow(
                Employees,
                stopMemberVoice: transactionId =>
                {
                    _voiceAnnouncementService.Stop(transactionId);
                    foreach (var employee in Employees.Where(x => x.TransactionId == transactionId))
                    {
                        employee.IsVoicePlaying = false;
                    }

                    UpdateStopAllButtonVisibility();
                },
                stopAllVoice: () =>
                {
                    _voiceAnnouncementService.StopAll();
                    foreach (var employee in Employees)
                    {
                        employee.IsVoicePlaying = false;
                    }

                    UpdateStopAllButtonVisibility();
                },
                isVoicePlaying: transactionId =>
                    _voiceAnnouncementService.IsPlaying(transactionId),
                hasAnyVoicePlaying: () =>
                    _voiceAnnouncementService.HasAnyPlaying,
                subscribeVoicePlayingChanged: handler =>
                    _voiceAnnouncementService.VoicePlayingChanged += handler,
                unsubscribeVoicePlayingChanged: handler =>
                    _voiceAnnouncementService.VoicePlayingChanged -= handler)
            {
                Owner = Window.GetWindow(this)
            };

            window.ShowDialog();
            SyncVoicePlayingFlags();
        }

        private void ApplyCurrentUser()
        {
            string displayName = string.IsNullOrWhiteSpace(_currentUser.FullName)
                ? _currentUser.UserName
                : _currentUser.FullName;

            AdminNameText.Text = displayName;
            AdminRoleText.Text = string.IsNullOrWhiteSpace(_currentUser.Role)
                ? "USER"
                : _currentUser.Role;

            AdminInitialText.Text = displayName.Length > 0
                ? displayName[..1].ToUpperInvariant()
                : "A";
        }

        private void AdminMenuButton_MouseLeftButtonUp( object sender,MouseButtonEventArgs e)
        {
            if (AdminMenuPopup.IsOpen)
            {
                CloseAdminMenu();
                return;
            }

            OpenAdminMenu();
        }

        private void OpenAdminMenu()
        {
            // Align dropdown's right edge with the profile button (CSS right: 0).
            AdminMenuButton.UpdateLayout();
            double buttonWidth = AdminMenuButton.ActualWidth;
            const double menuWidth = 230;
            double horizontalOffset = buttonWidth - menuWidth;

            // Keep menu inside the viewport on smaller screens.
            try
            {
                Point buttonScreen = AdminMenuButton.PointToScreen(new Point(0, 0));
                double screenWidth = SystemParameters.WorkArea.Width;
                double menuRight = buttonScreen.X + buttonWidth;
                double menuLeft = menuRight - menuWidth;

                if (menuLeft < 0)
                {
                    horizontalOffset += -menuLeft;
                }
                else if (menuRight > screenWidth)
                {
                    horizontalOffset -= menuRight - screenWidth;
                }
            }
            catch
            {
            }

            AdminMenuPopup.HorizontalOffset = horizontalOffset;
            AdminMenuPopup.VerticalOffset = 8;
            AdminMenuPopup.IsOpen = true;
            AdminMenuArrow.Text = "▲";
        }

        private void CloseAdminMenu()
        {
            AdminMenuPopup.IsOpen = false;
            AdminMenuArrow.Text = "▼";
        }

        private void AdminMenuPopup_Closed(object? sender, EventArgs e)
        {
            AdminMenuArrow.Text = "▼";
        }

        private void Logout_Click(object sender, RoutedEventArgs e)
        {
            CloseAdminMenu();
            _countdownTimer.Stop();
            _refreshTimer.Stop();
            _sensorReadingTimer.Stop();
            _voiceAnnouncementService.StopAll();

            var navigation = NavigationService;
            navigation?.Navigate(new LoginPage());

            while (navigation?.CanGoBack == true)
            {
                navigation.RemoveBackEntry();
            }
        }

        private void DashboardMenu_Click(object sender, RoutedEventArgs e)
        {
            AppNavigation.Go(NavigationService, "Dashboard", _currentUser);
        }

        private void ChambersMenu_Click(object sender, RoutedEventArgs e)
        {
            _countdownTimer.Stop();
            _refreshTimer.Stop();
            _sensorReadingTimer.Stop();
            AppNavigation.Go(NavigationService, "Chambers", _currentUser);
        }

        private void EmployeesMenu_Click(object sender, RoutedEventArgs e)
        {
            _countdownTimer.Stop();
            _refreshTimer.Stop();
            _sensorReadingTimer.Stop();
            AppNavigation.Go(NavigationService, "Employees", _currentUser);
        }

        private void ReadersMenu_Click(object sender, RoutedEventArgs e)
        {
            _countdownTimer.Stop();
            _refreshTimer.Stop();
            _sensorReadingTimer.Stop();
            AppNavigation.Go(NavigationService, "Readers", _currentUser);
        }

        private void ReportsToggle_Click(object sender, RoutedEventArgs e)
        {
            bool open = ReportsSubMenuPanel.Visibility != Visibility.Visible;
            ReportsSubMenuPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            ReportsArrowText.Text = open ? "▲" : "▼";
        }

        private void EntryExitReportMenu_Click(object sender, RoutedEventArgs e)
        {
            _countdownTimer.Stop();
            _refreshTimer.Stop();
            _sensorReadingTimer.Stop();
            AppNavigation.Go(NavigationService, "Reports", _currentUser);
        }

        private void CameraAccessReportMenu_Click(object sender, RoutedEventArgs e)
        {
            _countdownTimer.Stop();
            _refreshTimer.Stop();
            _sensorReadingTimer.Stop();
            AppNavigation.Go(NavigationService, "CameraAccessReport", _currentUser);
        }

        private void ChamberEmployeesReportMenu_Click(object sender, RoutedEventArgs e)
        {
            _countdownTimer.Stop();
            _refreshTimer.Stop();
            _sensorReadingTimer.Stop();
            AppNavigation.Go(NavigationService, "ChamberEmployeesReport", _currentUser);
        }

        private void ChamberCriticalReportMenu_Click(object sender, RoutedEventArgs e)
        {
            _countdownTimer.Stop();
            _refreshTimer.Stop();
            _sensorReadingTimer.Stop();
            AppNavigation.Go(NavigationService, "ChamberCriticalReport", _currentUser);
        }

        private void ProductionLossReportMenu_Click(object sender, RoutedEventArgs e)
        {
            _countdownTimer.Stop();
            _refreshTimer.Stop();
            _sensorReadingTimer.Stop();
            AppNavigation.Go(NavigationService, "ProductionLossReport", _currentUser);
        }
        private void SensorReadingsReportMenu_Click(object sender, RoutedEventArgs e)
        {
            _countdownTimer.Stop();
            _refreshTimer.Stop();
            _sensorReadingTimer.Stop();
            AppNavigation.Go(NavigationService, "SensorReadingsReport", _currentUser);
        }

        private void ConfigurationToggle_Click(object sender, RoutedEventArgs e) =>
            SidebarMenuHelper.ToggleSubMenu(ConfigurationSubMenuPanel, ConfigurationArrowText);

        private void SensorConfigurationMenu_Click(object sender, RoutedEventArgs e)
        {
            _countdownTimer.Stop();
            _refreshTimer.Stop();
            _sensorReadingTimer.Stop();
            AppNavigation.Go(NavigationService, "SensorConfiguration", _currentUser);
        }

        private void LiveCameraMenu_Click(object sender, RoutedEventArgs e)
        {
            _countdownTimer.Stop();
            _refreshTimer.Stop();
            _sensorReadingTimer.Stop();
            AppNavigation.Go(NavigationService, "LiveCamera", _currentUser);
        }

        private void CameraConfigurationMenu_Click(object sender, RoutedEventArgs e)
        {
            _countdownTimer.Stop();
            _refreshTimer.Stop();
            _sensorReadingTimer.Stop();
            AppNavigation.Go(NavigationService, "CameraConfiguration", _currentUser);
        }

        private void ManualRfidMenu_Click(object sender, RoutedEventArgs e)
        {
            _countdownTimer.Stop();
            _refreshTimer.Stop();
            _sensorReadingTimer.Stop();
            AppNavigation.Go(NavigationService, "ManualRfidTransactions", _currentUser);
        }

        private void CodeInqLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            ExternalLinks.OpenCodeInq();
            e.Handled = true;
        }

        //Payal

        private void UpdateSensorDisplay()
        {
            if (_latestSensorReading == null)
            {
                TemperatureValueText.Text = "--";
                HumidityValueText.Text = "--";
                CoValueText.Text = "--";
                Co2ValueText.Text = "--";
                OxygenValueText.Text = "--";

                return;
            }

            TemperatureValueText.Text =
                _latestSensorReading.Temperature.HasValue
                    ? $"{_latestSensorReading.Temperature:0.0} °C"
                    : "--";

            HumidityValueText.Text =
                _latestSensorReading.Humidity.HasValue
                    ? $"{_latestSensorReading.Humidity:0.0} %"
                    : "--";

            CoValueText.Text =
                _latestSensorReading.CO.HasValue
                    ? $"{_latestSensorReading.CO:0.0} PPM"
                    : "--";

            Co2ValueText.Text =
                _latestSensorReading.CO2.HasValue
                    ? $"{_latestSensorReading.CO2:0.0} PPM"
                    : "--";

            OxygenValueText.Text =
                _latestSensorReading.O2.HasValue
                    ? $"{_latestSensorReading.O2:0.0} %V/V"
                    : "--";
        }

        private async void SensorViolationTimer_Tick(object? sender,EventArgs e)
        {
            await CheckSensorViolationsAsync();
        }

        private async Task CheckSensorViolationsAsync()
        {
            try
            {
                var violations = await _monitoringService.GetActiveSensorViolationsAsync(1);

                // Update the five sensor cards
                UpdateSensorStatuses(violations);

                // Process voice announcements
                await ProcessSensorAnnouncementsAsync(violations); // Do announcements for

                foreach (var violation in violations)
                {
                    ActiveSensorViolations.Add(violation);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Sensor violation check failed: {ex.Message}");
            }
        }

        private void UpdateSensorStatuses(List<SensorViolation> violations)
        {
            SetSensorStatus(
                "Temperature",
                TemperatureCardBorder,
                TemperatureStatusBorder,
                TemperatureStatusText,
                violations);

            SetSensorStatus(
                "Humidity",
                HumidityCardBorder,
                HumidityStatusBorder,
                HumidityStatusText,
                violations);

            SetSensorStatus(
                "CO",
                CoCardBorder,
                CoStatusBorder,
                CoStatusText,
                violations);

            SetSensorStatus(
                "CO2",
                Co2CardBorder,
                Co2StatusBorder,
                Co2StatusText,
                violations);

            SetSensorStatus(
                "O2",
                OxygenCardBorder,
                OxygenStatusBorder,
                OxygenStatusText,
                violations);
        }

        private void SetSensorStatus(
            string parameter,
            Border cardBorder,
            Border statusBorder,
            TextBlock statusText,
            List<SensorViolation> violations)
        {
            var parameterViolations = violations
                .Where(v =>
                    string.Equals(
                        v.Parameter,
                        parameter,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

            // =========================
            // NORMAL
            // =========================

            if (!parameterViolations.Any())
            {
                statusText.Text = "NORMAL";

                statusBorder.Background =
                    new SolidColorBrush(Color.FromRgb(220, 252, 231));

                statusBorder.BorderBrush =
                    new SolidColorBrush(Color.FromRgb(74, 222, 128));

                statusBorder.BorderThickness = new Thickness(1);

                statusText.Foreground =
                    new SolidColorBrush(Color.FromRgb(22, 101, 52));

                cardBorder.Opacity = 1.0;
                statusBorder.Opacity = 1.0;

                return;
            }

            // =========================
            // CRITICAL
            // =========================

            if (parameterViolations.Any(v =>
                string.Equals(
                    v.Status,
                    "CRITICAL",
                    StringComparison.OrdinalIgnoreCase)))
            {
                statusText.Text = "CRITICAL";

                statusBorder.Background =
                    new SolidColorBrush(Color.FromRgb(254, 226, 226));

                statusBorder.BorderBrush =
                    new SolidColorBrush(Color.FromRgb(239, 68, 68));

                statusBorder.BorderThickness = new Thickness(1);

                statusText.Foreground =
                    new SolidColorBrush(Color.FromRgb(185, 28, 28));

                return;
            }

            // =========================
            // WARNING
            // =========================

            if (parameterViolations.Any(v =>
                string.Equals(
                    v.Status,
                    "WARNING",
                    StringComparison.OrdinalIgnoreCase)))
            {
                statusText.Text = "WARNING";

                statusBorder.Background =
                    new SolidColorBrush(Color.FromRgb(254, 243, 199));

                statusBorder.BorderBrush =
                    new SolidColorBrush(Color.FromRgb(250, 204, 21));

                statusBorder.BorderThickness = new Thickness(1);

                statusText.Foreground =
                    new SolidColorBrush(Color.FromRgb(161, 98, 7));

                return;
            }

            // =========================
            // FALLBACK
            // =========================

            statusText.Text = "NORMAL";

            statusBorder.Background =
                new SolidColorBrush(Color.FromRgb(220, 252, 231));

            statusBorder.BorderBrush =
                new SolidColorBrush(Color.FromRgb(74, 222, 128));

            statusBorder.BorderThickness = new Thickness(1);

            statusText.Foreground =
                new SolidColorBrush(Color.FromRgb(22, 101, 52));

            cardBorder.Opacity = 1.0;
            statusBorder.Opacity = 1.0;
        }

        private void SensorBlinkTimer_Tick(object? sender,EventArgs e)
        {
            _sensorBlinkState = !_sensorBlinkState;

            UpdateSensorCardBlink(
                TemperatureCardBorder,
                TemperatureStatusText);

            UpdateSensorCardBlink(
                HumidityCardBorder,
                HumidityStatusText);

            UpdateSensorCardBlink(
                CoCardBorder,
                CoStatusText);

            UpdateSensorCardBlink(
                Co2CardBorder,
                Co2StatusText);

            UpdateSensorCardBlink(
                OxygenCardBorder,
                OxygenStatusText);
        }

        private void UpdateSensorCardBlink(Border cardBorder, TextBlock statusText)
        {
            if (statusText.Text == "NORMAL")
            {
                cardBorder.Opacity = 1.0;
                return;
            }

            cardBorder.Opacity = _sensorBlinkState
                ? 1.0
                : 0.55;
        }

        private async Task ProcessSensorAnnouncementsAsync(List<SensorViolation> violations)
        {
            var settings = _configurationService.GetSensorAlertSettings();

            if (!settings.VoiceEnabled || !_sensorVoiceEnabled)
            {
                return;
            }

            foreach (var violation in violations)
            {
                string currentSeverity =
                    violation.Status.ToUpperInvariant();

                bool severityChanged =
                    !string.Equals(
                        violation.LastAnnouncedSeverity,
                        currentSeverity,
                        StringComparison.OrdinalIgnoreCase);

                bool repeatDue =
                    violation.LastAnnouncedAt.HasValue &&
                    (DateTime.Now - ToLocalTime(violation.LastAnnouncedAt.Value))
                        >= TimeSpan.FromMinutes(
                            settings.RepeatAfterMinutes);

                bool neverAnnounced =
                    !violation.LastAnnouncedAt.HasValue;

                if (!neverAnnounced &&
                    !severityChanged &&
                    !repeatDue)
                {
                    continue;
                }

                var templates = await _alertMessageService.GetTemplatesAsync(AlertMessageService.CategorySensor,currentSeverity);

                string chamberName = $"Chamber {violation.ChamberId}";
                var voiceLines = new List<VoiceAnnouncementLine>();

                string preferredCulture = _selectedVoiceCulture;
                string fallbackCulture =
                    preferredCulture.Equals(settings.BengaliVoiceCulture, StringComparison.OrdinalIgnoreCase)
                        ? settings.EnglishVoiceCulture
                        : settings.BengaliVoiceCulture;

                if (templates.TryGetValue(preferredCulture, out string? preferredTemplate) &&
                    !string.IsNullOrWhiteSpace(preferredTemplate))
                {
                    voiceLines.Add(new VoiceAnnouncementLine(
                        MonitoringService.FormatSensorMessage(
                            preferredTemplate,
                            violation.Parameter,
                            chamberName),
                        preferredCulture));
                }
                else if (templates.TryGetValue(fallbackCulture, out string? fallbackTemplate) &&  !string.IsNullOrWhiteSpace(fallbackTemplate))
                {
                    voiceLines.Add(new VoiceAnnouncementLine(MonitoringService.FormatSensorMessage(
                            fallbackTemplate,
                            violation.Parameter,
                            chamberName),
                        fallbackCulture));
                }

                if (voiceLines.Count == 0)
                {
                    continue;
                }

                _voiceAnnouncementService.AnnounceOnce(voiceLines);

                await _monitoringService.MarkSensorViolationAnnouncedAsync(violation.SensorViolationsId,currentSeverity);
            }
        }

        private static DateTime ToLocalTime(DateTime value) =>
            value.Kind switch
            {
                DateTimeKind.Utc => value.ToLocalTime(),
                DateTimeKind.Local => value,
                _ => DateTime.SpecifyKind(value, DateTimeKind.Local)
            };
    }
}
