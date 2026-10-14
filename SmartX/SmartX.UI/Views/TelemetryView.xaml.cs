using System;
using SmartX.Core;
using SmartX.UI;
using SmartX.UI.Models;
using SmartX.UI.Services;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SmartX.UI.Views
{
    public partial class TelemetryView : UserControl, INavigablePage
    {
        public event EventHandler<string>? NavigationRequested;

        private ObservableCollection<TelemetryPacket<object>> _completeHistory =
            new();

        private ICollectionView _telemetryView = null!;
        private string _currentSearchTerm = "";

        private DispatcherTimer _apiStatusTimer = null!;
        private DispatcherTimer _alertTestTimer = null!;

        private int _telemetryGenerated;
        private int _telemetryReceived;

        private readonly Stopwatch _insertionStopwatch = new();
        private readonly Stopwatch _searchStopwatch = new();

        private DeploymentNode _rootFacilityMap = null!;
        private readonly HttpClient _apiClient;
        private bool _apiConnected;

        private List<DisplayTelemetryModel> _registeredSensorsList = new();

        private int _testHighAlerts;
        private int _testMediumAlerts;

        private const int TestHighAlertCap = 80;
        private const int TestMediumAlertCap = 60;

        public TelemetryView()
        {
            InitializeComponent();

            _apiClient = ApiClientFactory.Client;

            NavBar.NavigationRequested +=
                (sender, key) => NavigationRequested?.Invoke(this, key);

            InitializeStorage();
            SeedValidNetworkLocationMap();
            SetupDataSeeder();

            LoadSharedTelemetry();

            LocalTelemetryStore.PacketGenerated += OnLocalPacketGenerated;

            _telemetryGenerated = LocalTelemetryStore.TotalGenerated;

            CheckApiStatus();
            StartAlertTestGenerator();
        }

        public void ActivateNav(string screenKey)
        {
            NavBar.SetActive(screenKey);
        }

        private void InitializeStorage()
        {
            _completeHistory =
                new ObservableCollection<TelemetryPacket<object>>();

            _telemetryView =
                CollectionViewSource.GetDefaultView(_completeHistory);

            _telemetryView.Filter = TelemetryFilter;
            TelemetryDataGrid.ItemsSource = _telemetryView;
        }

        private void SetupDataSeeder()
        {
            _apiStatusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };

            _apiStatusTimer.Tick += (sender, args) => CheckApiStatus();
            _apiStatusTimer.Start();
        }

        private void PauseBackgroundTimers()
        {
            _apiStatusTimer?.Stop();
        }

        private void ResumeBackgroundTimers()
        {
            _apiStatusTimer?.Start();
        }


        private async void CheckApiStatus()
        {
            try
            {
                using var response =
                    await _apiClient.GetAsync("api/telemetry/sensors");

                _apiConnected = response.IsSuccessStatusCode;
            }
            catch
            {
                _apiConnected = false;
            }

            ApiStatusBadge.Text =
                _apiConnected ? "API: ONLINE" : "API: OFFLINE";

            ApiStatusBadge.Foreground = _apiConnected
                ? Brushes.LimeGreen
                : Brushes.Crimson;

            // Registered sensor definitions remain separate from telemetry records.
            await LoadRegisteredSensorsFromApiAsync();
            await FetchAlertsFromAPI();

            // Refresh the grid using the latest connectivity result.
            RefreshApiDataGrid();

            TxtDashboardStatus.Text =
                $"API: {(_apiConnected ? "ONLINE" : "OFFLINE")}. " +
                (_apiConnected
                    ? "Displaying API telemetry."
                    : "Displaying local telemetry.");
        }


        private void SeedValidNetworkLocationMap()
        {
            _rootFacilityMap = new DeploymentNode("Facility A");

            var zone1 = new DeploymentNode("Zone 1");
            var zone2 = new DeploymentNode("Zone 2");
            var subZoneB = new DeploymentNode("Sub-Zone B");
            var subZoneC = new DeploymentNode("Sub-Zone C");

            zone1.SubZones.Add(subZoneB);
            zone2.SubZones.Add(subZoneC);

            _rootFacilityMap.SubZones.Add(zone1);
            _rootFacilityMap.SubZones.Add(zone2);
        }

        private void UpdateMetrics(double insertionTime)
        {
            TxtTotalSensors.Text = _completeHistory
                .Select(record => record.MacAddress)
                .Distinct()
                .Count()
                .ToString();

            TxtTelemetryGenerated.Text =
                _telemetryGenerated.ToString();

            TxtTelemetryReceived.Text =
                _telemetryReceived.ToString();

            TxtTotalRecordsStored.Text =
                _completeHistory.Count.ToString();

            TxtLastReading.Text =
                DateTime.Now.ToString("HH:mm:ss");

            TxtInsertionTime.Text =
                insertionTime.ToString("0.###");

            TxtApiGenerated.Text =
                LocalTelemetryStore.ApiHistory.Count.ToString();

            TxtRecentReadingsByZone.Text =
                LocalTelemetryStore.GetRecentReadingsSummary();

            UpdatePowerHealth();
            UpdateDisplayWithSourceTags();

            LastUpdateTime.Text =
                DateTime.Now.ToString("HH:mm:ss");

            TxtDashboardStatus.Text =
                $"Updated at {DateTime.Now:HH:mm:ss}. " +
                $"API: {(LocalTelemetryStore.ApiOnline ? "ONLINE" : "OFFLINE")}.";
        }

        private void UpdatePowerHealth()
        {
            var powerPackets = _completeHistory
                .Where(packet => packet.Category == "Power Consumption")
                .ToList();

            var recentPowerPackets = powerPackets
                .Where(packet =>
                    packet.Timestamp >= DateTime.Now.AddMinutes(-1))
                .ToList();

            var surgePackets =
                AnomalyDetector.DetectPowerSurges(recentPowerPackets);

            var latestPowerPackets = powerPackets
                .GroupBy(packet => packet.MacAddress)
                .Select(group => group
                    .OrderByDescending(packet => packet.Timestamp)
                    .First())
                .ToList();

            var loadMeters = latestPowerPackets
                .Select(packet =>
                {
                    int.TryParse(
                        Convert.ToString(packet.SensorValue),
                        out int wattage);

                    return new PowerGridMetric(wattage);
                })
                .ToList();

            PowerGridMetric aggregateLoad =
                AnomalyDetector.CalculateAggregateLoad(loadMeters);

            bool underStress = AnomalyDetector.IsSystemUnderStress(
                aggregateLoad,
                new PowerGridMetric(5000));

            TxtPowerHealth.Text =
                $"{aggregateLoad.Wattage:N0} W | " +
                $"{surgePackets.Count} recent surge(s) | " +
                (underStress ? "SYSTEM UNDER STRESS" : "NORMAL");

            TxtPowerHealth.Foreground = underStress
                ? Brushes.Crimson
                : Brushes.LimeGreen;

            PowerLoadProgressBar.Maximum =
                Math.Max(10000, latestPowerPackets.Count * 10000);

            PowerLoadProgressBar.Value =
                Math.Clamp(
                    aggregateLoad.Wattage,
                    0,
                    PowerLoadProgressBar.Maximum);
        }

        private async void BtnGenerateSingle_Click(
            object sender,
            RoutedEventArgs e)
        {
            await LocalTelemetryStore.GenerateOneAsync();
        }

        private void OnLocalPacketGenerated(
            object? sender,
            DisplayTelemetryModel packet)
        {
            Dispatcher.Invoke(() =>
            {
                _insertionStopwatch.Restart();

                var telemetryPacket = new TelemetryPacket<object>(
                    packet.MacAddress ?? "",
                    packet.LocationZone ?? "",
                    packet.Category ?? "",
                    packet.SensorValue);

                telemetryPacket.Timestamp = packet.Timestamp;

                _completeHistory.Add(telemetryPacket);

                _telemetryGenerated = LocalTelemetryStore.TotalGenerated;
                _telemetryReceived = _completeHistory.Count;

                _insertionStopwatch.Stop();
                _telemetryView.Refresh();

                UpdateMetrics(
                    _insertionStopwatch.Elapsed.TotalMilliseconds);
            });
        }

        private void LoadSharedTelemetry()
        {
            _completeHistory.Clear();

            foreach (var record in LocalTelemetryStore.History)
            {
                var packet = new TelemetryPacket<object>(
                    record.MacAddress ?? "",
                    record.LocationZone ?? "",
                    record.Category ?? "",
                    record.SensorValue);

                packet.Timestamp = record.Timestamp;
                _completeHistory.Add(packet);
            }

            _telemetryGenerated = LocalTelemetryStore.TotalGenerated;
            _telemetryReceived = _completeHistory.Count;

            _telemetryView.Refresh();
            UpdateMetrics(0);
            RefreshApiDataGrid();
        }

        private async void BtnGenerateBatch_Click(
            object sender,
            RoutedEventArgs e)
        {
            BtnGenerateBatch.IsEnabled = false;

            try
            {
                _insertionStopwatch.Restart();

                int generated =
                    await LocalTelemetryStore.GenerateBatchAsync(50);

                _insertionStopwatch.Stop();

                TxtInsertionTime.Text =
                    FormatElapsedMs(_insertionStopwatch);

                TxtDashboardStatus.Text =
                    $"Generated {generated} shared telemetry record(s).";
            }
            finally
            {
                BtnGenerateBatch.IsEnabled = true;
            }
        }

        private bool TelemetryFilter(object item)
        {
            if (item is not TelemetryPacket<object> packet)
                return false;

            if (string.IsNullOrWhiteSpace(_currentSearchTerm))
                return true;

            string term = _currentSearchTerm.Trim();

            return
                (packet.MacAddress ?? "").Contains(
                    term, StringComparison.OrdinalIgnoreCase) ||
                (packet.LocationZone ?? "").Contains(
                    term, StringComparison.OrdinalIgnoreCase) ||
                (packet.Category ?? "").Contains(
                    term, StringComparison.OrdinalIgnoreCase) ||
                (Convert.ToString(packet.SensorValue) ?? "").Contains(
                    term, StringComparison.OrdinalIgnoreCase);
        }

        private void BtnViewTelemetry_Click(
            object sender,
            RoutedEventArgs e)
        {
            _telemetryView.Refresh();

            TxtDashboardStatus.Text =
                $"Displaying {_completeHistory.Count:N0} telemetry records.";
        }

        private void BtnSearchHistory_Click(
            object sender,
            RoutedEventArgs e)
        {
            _currentSearchTerm = SearchInput.Text.Trim();

            if (string.IsNullOrWhiteSpace(_currentSearchTerm))
            {
                TxtDashboardStatus.Text =
                    "Enter a MAC address, zone, category, or sensor value to search.";
                return;
            }

            _searchStopwatch.Restart();
            _telemetryView.Refresh();
            _searchStopwatch.Stop();

            TxtSearchTime.Text = FormatElapsedMs(_searchStopwatch);

            int resultCount = _telemetryView.Cast<object>().Count();

            TxtDashboardStatus.Text =
                $"Found {resultCount:N0} matching telemetry record(s).";
        }

        private void BtnClearSearch_Click(
            object sender,
            RoutedEventArgs e)
        {
            SearchInput.Clear();
        }

        private void SearchInput_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            _currentSearchTerm = SearchInput.Text.Trim();

            _searchStopwatch.Restart();
            _telemetryView.Refresh();
            _searchStopwatch.Stop();

            TxtSearchTime.Text = FormatElapsedMs(_searchStopwatch);
        }

        private void TelemetryRecordGrid_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e)
        {
            if (sender is not DataGrid grid)
                return;

            List<KeyValuePair<string, string>>? details = null;
            string? attachmentMacAddress = null;

            if (grid.SelectedItem is TelemetryPacket<object> packet)
            {
                details = new List<KeyValuePair<string, string>>
                {
                    new("MAC Address", packet.MacAddress ?? "-"),
                    new("Location Zone", packet.LocationZone ?? "-"),
                    new("Category", packet.Category ?? "-"),
                    new("Sensor Value",
                        Convert.ToString(packet.SensorValue) ?? "-"),
                    new("Source", "Telemetry History"),
                    new("Timestamp",
                        packet.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"))
                };
            }
            else if (grid.SelectedItem is DisplayTelemetryModel record)
            {
                details = new List<KeyValuePair<string, string>>
                {
                    new("MAC Address", record.MacAddress ?? "-"),
                    new("Location Zone", record.LocationZone ?? "-"),
                    new("Category", record.Category ?? "-"),
                    new("Sensor Value",
                        Convert.ToString(record.SensorValue) ?? "-"),
                    new("Source", record.Source ?? "-"),
                    new("Timestamp",
                        record.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"))
                };

                if (_apiConnected &&
                    string.Equals(
                        record.Source,
                        "(API Registered)",
                        StringComparison.OrdinalIgnoreCase))
                {
                    attachmentMacAddress = record.MacAddress;
                }
            }

            if (details == null)
                return;

            var detailWindow = new RecordDetailWindow(
                "Telemetry Record Details",
                details,
                _apiClient,
                attachmentMacAddress)
            {
                Owner = Window.GetWindow(this)
            };

            PauseBackgroundTimers();

            try
            {
                detailWindow.ShowDialog();
            }
            finally
            {
                ResumeBackgroundTimers();
            }
        }

        private void AlertsListBox_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e)
        {
            if (AlertsListBox.SelectedItem is not AlertModel alert)
                return;

            if (alert.IsResolved)
                return;

            OpenAlertDetail(alert);
        }

        private void ResolvedAlertsListBox_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e)
        {
            if (ResolvedAlertsListBox.SelectedItem is not AlertModel alert)
                return;

            OpenAlertDetail(alert);
        }

        private void OpenAlertDetail(AlertModel alert)
        {
            var detailWindow = new AlertDetailWindow(alert, _apiClient)
            {
                Owner = Window.GetWindow(this)
            };

            PauseBackgroundTimers();

            try
            {
                detailWindow.ShowDialog();
            }
            finally
            {
                ResumeBackgroundTimers();
            }

            if (detailWindow.AlertWasUpdated)
                _ = FetchAlertsFromAPI();
        }

        private async Task FetchAlertsFromAPI()
        {
            if (!_apiConnected)
            {
                TxtActiveAlertCount.Text = "0 active";
                TxtResolvedAlertCount.Text = "0 resolved";
                return;
            }

            try
            {
                using var response =
                    await _apiClient.GetAsync("api/telemetry/alerts");

                if (!response.IsSuccessStatusCode)
                    return;

                string json = await response.Content.ReadAsStringAsync();

                var alerts =
                    JsonSerializer.Deserialize<List<AlertModel>>(
                        json,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        }) ?? new List<AlertModel>();

                var activeAlerts = alerts
                    .Where(alert => !alert.IsResolved)
                    .OrderByDescending(alert => alert.CreatedAt)
                    .ToList();

                var resolvedAlerts = alerts
                    .Where(alert => alert.IsResolved)
                    .OrderByDescending(alert => alert.CreatedAt)
                    .ToList();

                AlertsListBox.ItemsSource = activeAlerts;
                ResolvedAlertsListBox.ItemsSource = resolvedAlerts;

                TxtActiveAlertCount.Text =
                    $"{activeAlerts.Count} active";

                TxtResolvedAlertCount.Text =
                    $"{resolvedAlerts.Count} resolved";
            }
            catch
            {
                TxtDashboardStatus.Text =
                    "Could not refresh alerts from the API.";
            }
        }


        private void StartAlertTestGenerator()
        {
            _alertTestTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };

            _alertTestTimer.Tick += async (sender, args) =>
            {
                if (LocalTelemetryStore.TotalGenerated < 10)
                    return;

                _alertTestTimer.Stop();

                try
                {
                    await GenerateTestAlertAsync();

                    await Task.Delay(10000);

                    // This only resolves alerts if the API can be reached.
                    await ResolveAllUnresolvedAlertsAsync();
                }
                finally
                {
                    if (_testHighAlerts < TestHighAlertCap ||
                        _testMediumAlerts < TestMediumAlertCap)
                    {
                        _alertTestTimer.Start();
                    }
                }
            };

            _alertTestTimer.Start();
        }


        private async Task GenerateTestAlertAsync()
        {
            if (_testHighAlerts >= TestHighAlertCap &&
                _testMediumAlerts >= TestMediumAlertCap)
            {
                _alertTestTimer?.Stop();
                return;
            }

            var random = Random.Shared;

            bool highAvailable = _testHighAlerts < TestHighAlertCap;
            bool mediumAvailable = _testMediumAlerts < TestMediumAlertCap;

            bool generateHigh =
                highAvailable &&
                (!mediumAvailable || random.Next(0, 2) == 0);

            try
            {
                var result =
                    await LocalTelemetryStore.GeneratePowerTestReadingAsync(
                        generateHigh);

                // Count a test alert only when the API actually accepted the reading.
                if (result != null &&
                    result.Source?.StartsWith(
                        "(API",
                        StringComparison.Ordinal) == true)
                {
                    if (generateHigh)
                        _testHighAlerts++;
                    else
                        _testMediumAlerts++;

                    await FetchAlertsFromAPI();
                }
                else
                {
                    TxtDashboardStatus.Text =
                        "API offline or unavailable. Test telemetry was kept locally.";
                }
            }
            catch
            {
                TxtDashboardStatus.Text =
                    "Could not generate test telemetry. Local fallback was attempted.";
            }

            if (_testHighAlerts >= TestHighAlertCap &&
                _testMediumAlerts >= TestMediumAlertCap)
            {
                _alertTestTimer?.Stop();
            }
        }


        private async Task ResolveAllUnresolvedAlertsAsync()
        {
            try
            {
                using var response =
                    await _apiClient.GetAsync("api/telemetry/alerts");

                if (!response.IsSuccessStatusCode)
                    return;

                string json = await response.Content.ReadAsStringAsync();

                var alerts =
                    JsonSerializer.Deserialize<List<AlertModel>>(
                        json,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        }) ?? new List<AlertModel>();

                var unresolvedAlerts = alerts
                    .Where(alert => !alert.IsResolved)
                    .ToList();

                foreach (var alert in unresolvedAlerts)
                {
                    var updateDto = new
                    {
                        isResolved = true,
                        notes = "Solved automatically"
                    };

                    using var updateResponse =
                        await _apiClient.PutAsJsonAsync(
                            $"api/telemetry/alerts/{alert.Id}",
                            updateDto);
                }

                await FetchAlertsFromAPI();
            }
            catch
            {
                TxtDashboardStatus.Text =
                    "Could not resolve the test alerts.";
            }
        }

        private List<DisplayTelemetryModel> ConvertToDisplayModel(
            List<TelemetryPacket<object>> packets,
            string source)
        {
            return packets.Select(packet => new DisplayTelemetryModel
            {
                MacAddress = packet.MacAddress,
                LocationZone = packet.LocationZone,
                Category = packet.Category,
                SensorValue = packet.SensorValue,
                Timestamp = packet.Timestamp,
                Source = source
            }).ToList();
        }

        private async void BtnRegisterSensor_Click(
            object sender,
            RoutedEventArgs e)
        {
            var window = new RegisterSensorWindow(
                _apiClient,
                _apiConnected,
                _rootFacilityMap)
            {
                Owner = Window.GetWindow(this)
            };

            PauseBackgroundTimers();

            try
            {
                window.ShowDialog();
            }
            finally
            {
                ResumeBackgroundTimers();
            }

            await LoadRegisteredSensorsFromApiAsync();
        }

        private static string FormatElapsedMs(Stopwatch stopwatch)
        {
            return stopwatch.Elapsed.TotalMilliseconds.ToString("0.###");
        }

        private async void BtnGenerateWithApi_Click(
            object sender,
            RoutedEventArgs e)
        {
            BtnGenerateWithApi.IsEnabled = false;

            try
            {
                _insertionStopwatch.Restart();

                int generated =
                    await LocalTelemetryStore.GenerateBatchAsync(10);

                _insertionStopwatch.Stop();

                TxtInsertionTime.Text =
                    FormatElapsedMs(_insertionStopwatch);

                TxtDashboardStatus.Text =
                    $"Generated {generated} shared telemetry record(s). " +
                    $"API: {(LocalTelemetryStore.ApiOnline ? "ONLINE" : "OFFLINE")}.";
            }
            finally
            {
                BtnGenerateWithApi.IsEnabled = true;
            }
        }

        private async void BtnRefreshApiData_Click(
            object sender,
            RoutedEventArgs e)
        {
            await LoadRegisteredSensorsFromApiAsync();
        }

        private async Task LoadRegisteredSensorsFromApiAsync()
        {
            if (!_apiConnected)
            {
                _registeredSensorsList = LocalTelemetryStore
                    .GetRegisteredSensors()
                    .Select(sensor => new DisplayTelemetryModel
                    {
                        MacAddress = sensor.MacAddress,
                        LocationZone = sensor.LocationZone,
                        Category = sensor.Category,
                        SensorValue = string.IsNullOrWhiteSpace(sensor.Description)
                            ? "Registered"
                            : sensor.Description,
                        Timestamp = sensor.RegisteredDate,
                        Source = "(Local Registered)"
                    })
                    .ToList();

                RefreshApiDataGrid();
                return;
            }

            try
            {
                using var response =
                    await _apiClient.GetAsync("api/telemetry/sensors");

                if (!response.IsSuccessStatusCode)
                {
                    _apiConnected = false;
                    LoadLocalRegisteredSensors();
                    RefreshApiDataGrid();
                    return;
                }

                string json = await response.Content.ReadAsStringAsync();

                var sensors =
                    JsonSerializer.Deserialize<List<SensorModel>>(
                        json,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        }) ?? new List<SensorModel>();

                LocalTelemetryStore.SetRegisteredSensors(sensors);

                _registeredSensorsList = sensors
                    .Where(sensor => sensor.IsActive)
                    .Select(sensor => new DisplayTelemetryModel
                    {
                        MacAddress = sensor.MacAddress,
                        LocationZone = sensor.LocationZone,
                        Category = sensor.Category,
                        SensorValue = string.IsNullOrWhiteSpace(sensor.Description)
                            ? "Registered"
                            : sensor.Description,
                        Timestamp = sensor.RegisteredDate,
                        Source = "(API Registered)"
                    })
                    .ToList();
            }
            catch
            {
                _apiConnected = false;
                LoadLocalRegisteredSensors();
            }

            RefreshApiDataGrid();
        }

        private void LoadLocalRegisteredSensors()
        {
            _registeredSensorsList = LocalTelemetryStore
                .GetRegisteredSensors()
                .Select(sensor => new DisplayTelemetryModel
                {
                    MacAddress = sensor.MacAddress,
                    LocationZone = sensor.LocationZone,
                    Category = sensor.Category,
                    SensorValue = string.IsNullOrWhiteSpace(sensor.Description)
                        ? "Registered"
                        : sensor.Description,
                    Timestamp = sensor.RegisteredDate,
                    Source = "(Local Registered)"
                })
                .ToList();
        }



        private void RefreshApiDataGrid()
        {
            // Do not combine sensor registrations with telemetry readings.
            var displayedRecords = _apiConnected
                ? LocalTelemetryStore.GetApiHistory().ToList()
                : LocalTelemetryStore.GetLatest(500).ToList();

            ApiDataGrid.ItemsSource = null;
            ApiDataGrid.ItemsSource = displayedRecords
                .OrderByDescending(record => record.Timestamp)
                .ToList();

            TxtApiSensorCount.Text = _registeredSensorsList
                .Where(record =>
                    !string.Equals(
                        record.Source,
                        "(Local Registered)",
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.IsNullOrWhiteSpace(record.MacAddress))
                .Select(record => record.MacAddress)
                .Where(macAddress => !string.IsNullOrWhiteSpace(macAddress))
                .Distinct()
                .Count()
                .ToString();

            TxtApiGeneratedInline.Text = _apiConnected
                ? LocalTelemetryStore.ApiHistory.Count.ToString()
                : LocalTelemetryStore.TotalGenerated.ToString();
        }


        private void NavBar_NavigationRequested(
            object sender,
            string screenKey)
        {
            NavigationRequested?.Invoke(this, screenKey);
        }

        private void UpdateDisplayWithSourceTags()
        {
            _telemetryView.Refresh();
        }

        private void BtnRunPerformanceTest_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is Button button && button.ContextMenu != null)
            {
                button.ContextMenu.PlacementTarget = button;
                button.ContextMenu.IsOpen = true;
            }
        }

        private void PerformanceTest100_Click(
            object sender,
            RoutedEventArgs e)
        {
            RunPerformanceTest(100);
        }

        private void PerformanceTest1000_Click(
            object sender,
            RoutedEventArgs e)
        {
            RunPerformanceTest(1000);
        }

        private void PerformanceTest5000_Click(
            object sender,
            RoutedEventArgs e)
        {
            RunPerformanceTest(5000);
        }

        private void RunPerformanceTest(int testCount)
        {
            TxtDashboardStatus.Text =
                $"Running performance test with {testCount:N0} insertion cycles...";

            var random = Random.Shared;
            var testData = new List<TelemetryPacket<object>>(testCount);

            string[] zones =
            {
                "Zone 1",
                "Zone 2",
                "Sub-Zone B",
                "Sub-Zone C"
            };

            string[] categories =
            {
                "Environmental",
                "Power Consumption",
                "Actuator"
            };

            string[] macs =
            {
                "00:1A:2B:3C:4D:5E",
                "00:1A:2B:9F:8E:7D",
                "AA:BB:CC:DD:EE:FF",
                "11:22:33:44:55:66"
            };

            _insertionStopwatch.Restart();

            for (int i = 0; i < testCount; i++)
            {
                string category =
                    categories[random.Next(categories.Length)];

                object sensorValue = category switch
                {
                    "Environmental" =>
                        (float)Math.Round(random.NextDouble() * 100, 2),

                    "Power Consumption" =>
                        random.Next(100, 10000),

                    "Actuator" =>
                        random.Next(0, 2) == 0,

                    _ => 0
                };

                var packet = new TelemetryPacket<object>(
                    macs[random.Next(macs.Length)],
                    zones[random.Next(zones.Length)],
                    category,
                    sensorValue);

                testData.Add(packet);
            }

            _insertionStopwatch.Stop();

            double elapsedMs =
                _insertionStopwatch.Elapsed.TotalMilliseconds;

            TxtInsertionTime.Text =
                elapsedMs.ToString("0.###");

            TxtDashboardStatus.Text =
                $"Completed {testCount:N0} insertions in {elapsedMs:0.###} ms. " +
                $"Average: {(elapsedMs / testCount):0.######} ms per insertion. " +
                "Test data was not added to telemetry history.";
        }
    }
}
