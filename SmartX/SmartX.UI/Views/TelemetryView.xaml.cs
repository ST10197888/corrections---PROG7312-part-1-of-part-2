using SmartX.Core;
using SmartX.UI;
using SmartX.UI.Models;
using SmartX.UI.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SmartX.UI.Views
{
    public partial class TelemetryView : UserControl, INavigablePage
    {
        public event EventHandler<string> NavigationRequested;

        private ObservableCollection<TelemetryPacket<object>> _completeHistory;

        private ICollectionView _telemetryView;
        private string _currentSearchTerm = "";


        private DispatcherTimer _apiStatusTimer;
        
        private int _telemetryGenerated = 0;
        private int _telemetryReceived = 0;
        private Stopwatch _insertionStopwatch;
        private Stopwatch _searchStopwatch;

        private DeploymentNode _rootFacilityMap;
        private HttpClient _apiClient;
        private bool _apiConnected = false;
        private const string API_BASE = "https://localhost:7001";

        private List<DisplayTelemetryModel> _registeredSensorsList;

        private DispatcherTimer _alertTestTimer;
        private int _testHighAlerts = 0;
        private int _testMediumAlerts = 0;
        private const int TestHighAlertCap = 80;
        private const int TestMediumAlertCap = 60;

        public TelemetryView()
        {
            InitializeComponent();

            NavBar.NavigationRequested +=
                (s, key) => NavigationRequested?.Invoke(this, key);

            InitializeStorage();
            SeedValidNetworkLocationMap();
            SetupDataSeeder();

            LoadSharedTelemetry();

            LocalTelemetryStore.PacketGenerated += OnLocalPacketGenerated;

            _telemetryGenerated =
                LocalTelemetryStore.TotalGenerated;

            SetupApiClient();
            CheckApiStatus();
            StartAlertTestGenerator();
        }

        public void ActivateNav(string screenKey) => NavBar.SetActive(screenKey);

        private void InitializeStorage()
        {
            _completeHistory = new ObservableCollection<TelemetryPacket<object>>();

            _insertionStopwatch = new Stopwatch();
            _searchStopwatch = new Stopwatch();

            _registeredSensorsList = new List<DisplayTelemetryModel>();

            _telemetryView = CollectionViewSource.GetDefaultView(_completeHistory);

            _telemetryView.Filter = TelemetryFilter;

            TelemetryDataGrid.ItemsSource = _telemetryView;
        }

        private void SetupApiClient()
        {
            _apiClient = new HttpClient();
            _apiClient.BaseAddress = new Uri(API_BASE);
            _apiClient.Timeout = TimeSpan.FromSeconds(5);
        }

        private void SetupDataSeeder()
        {
            _apiStatusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };

            _apiStatusTimer.Tick += (s, e) => CheckApiStatus();
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
                var response = await _apiClient.GetAsync("/api/telemetry/sensors");
                _apiConnected = response.IsSuccessStatusCode;

            }
            catch
            {
                _apiConnected = false;
            }

            ApiStatusBadge.Text = _apiConnected ? "API: ONLINE" : "API: OFFLINE";
            ApiStatusBadge.Foreground = System.Windows.Media.Brushes.LimeGreen;
            if (!_apiConnected)
                ApiStatusBadge.Foreground = System.Windows.Media.Brushes.Crimson;

            await LoadRegisteredSensorsFromApiAsync();
            FetchAlertsFromAPI();
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
            TxtTotalSensors.Text =
                _completeHistory
                    .Select(x => x.MacAddress)
                    .Distinct()
                    .Count()
                    .ToString();

            TxtTelemetryGenerated.Text = _telemetryGenerated.ToString();

            TxtTelemetryReceived.Text = _telemetryReceived.ToString();

            TxtTotalRecordsStored.Text = _completeHistory.Count.ToString();

            TxtLastReading.Text = DateTime.Now.ToString("HH:mm:ss");

            TxtInsertionTime.Text = insertionTime.ToString("0.###");

            UpdateDisplayWithSourceTags();

            LastUpdateTime.Text = DateTime.Now.ToString("HH:mm:ss");
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

                var telemetryPacket =
                    new TelemetryPacket<object>(
                        packet.MacAddress ?? "",
                        packet.LocationZone ?? "",
                        packet.Category ?? "",
                        packet.SensorValue);

                telemetryPacket.Timestamp = packet.Timestamp;

                _completeHistory.Add(telemetryPacket);

                _telemetryGenerated =
                    LocalTelemetryStore.TotalGenerated;

                _telemetryReceived =
                    _completeHistory.Count;

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

            _telemetryGenerated =
                LocalTelemetryStore.TotalGenerated;

            _telemetryReceived =
                _completeHistory.Count;

            _telemetryView.Refresh();

            UpdateMetrics(0);
        }

        private async void BtnGenerateBatch_Click(
    object sender,
    RoutedEventArgs e)
        {
            _insertionStopwatch.Restart();

            int generated =
                await LocalTelemetryStore.GenerateBatchAsync(50);

            _insertionStopwatch.Stop();

            TxtInsertionTime.Text =
                FormatElapsedMs(_insertionStopwatch);

            MessageBox.Show(
                $"Generated {generated} shared telemetry record(s).",
                "Batch Generation Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private bool TelemetryFilter(object item)
        {
            if (item is not TelemetryPacket<object> packet)
                return false;

            if (string.IsNullOrWhiteSpace(_currentSearchTerm))
                return true;

            string searchTerm = _currentSearchTerm.Trim();

            return
                (packet.MacAddress ?? "")
                    .Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
                ||
                (packet.LocationZone ?? "")
                    .Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
                ||
                (packet.Category ?? "")
                    .Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
                ||
                (packet.SensorValue?.ToString() ?? "")
                    .Contains(searchTerm, StringComparison.OrdinalIgnoreCase);
        }

        private void BtnViewTelemetry_Click(
        object sender,
        RoutedEventArgs e)
        {
            _telemetryView.Refresh();
            MessageBox.Show( $"Displaying {_completeHistory.Count} telemetry records.", "View Telemetry", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnSearchHistory_Click( object sender, RoutedEventArgs e)
        {
            _currentSearchTerm = SearchInput.Text.Trim();

            if (string.IsNullOrWhiteSpace(_currentSearchTerm))
            {
                MessageBox.Show(
                    "Enter a search term such as a MAC address, zone, category, or sensor value.", "Search Input", MessageBoxButton.OK, MessageBoxImage.Warning);

                return;
            }

            _searchStopwatch.Restart();

            _telemetryView.Refresh();

            _searchStopwatch.Stop();

            TxtSearchTime.Text = FormatElapsedMs(_searchStopwatch);

            int resultCount = _telemetryView.Cast<object>().Count();

            MessageBox.Show( $"Found {resultCount} matching telemetry record(s).", "Search Complete", MessageBoxButton.OK, MessageBoxImage.Information);
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

        private void TelemetryRecordGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not DataGrid grid)
                return;

            // TelemetryDataGrid is bound to TelemetryPacket<object>, while
            // ApiDataGrid is bound to DisplayTelemetryModel. The old handler
            // only accepted DisplayTelemetryModel, so double-clicking a normal
            // telemetry record silently did nothing.
            List<KeyValuePair<string, string>>? details = null;

            if (grid.SelectedItem is TelemetryPacket<object> packet)
            {
                details = new List<KeyValuePair<string, string>>
                {
                    new("MAC Address", packet.MacAddress ?? "-"),
                    new("Location Zone", packet.LocationZone ?? "-"),
                    new("Category", packet.Category ?? "-"),
                    new("Sensor Value", packet.SensorValue?.ToString() ?? "-"),
                    new("Source", "Telemetry History"),
                    new("Timestamp", packet.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"))
                };
            }
            else if (grid.SelectedItem is DisplayTelemetryModel record)
            {
                details = new List<KeyValuePair<string, string>>
                {
                    new("MAC Address", record.MacAddress ?? "-"),
                    new("Location Zone", record.LocationZone ?? "-"),
                    new("Category", record.Category ?? "-"),
                    new("Sensor Value", record.SensorValue?.ToString() ?? "-"),
                    new("Source", record.Source ?? "-"),
                    new("Timestamp", record.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"))
                };
            }

            if (details == null)
                return;

            var detailWindow = new RecordDetailWindow(
                "Telemetry Record Details",
                details)
            {
                Owner = Window.GetWindow(this)
            };

            PauseBackgroundTimers();
            detailWindow.ShowDialog();
            ResumeBackgroundTimers();
        }

        private void AlertsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (AlertsListBox.SelectedItem is not AlertModel alert)
                return;

            if (alert.IsResolved)
                return;

            OpenAlertDetail(alert);
        }

        private void ResolvedAlertsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
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
            detailWindow.ShowDialog();
            ResumeBackgroundTimers();

            if (detailWindow.AlertWasUpdated)
            {
                FetchAlertsFromAPI();
            }
        }

        private async void FetchAlertsFromAPI()
        {
            if (!_apiConnected)
                return;

            try
            {
                var response =
                    await _apiClient.GetAsync(
                        "/api/telemetry/alerts");

                if (!response.IsSuccessStatusCode)
                    return;

                var json =
                    await response.Content.ReadAsStringAsync();

                var alerts =
                    JsonSerializer.Deserialize<List<AlertModel>>(
                        json,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        })
                    ?? new List<AlertModel>();

                AlertsListBox.ItemsSource = null;

                AlertsListBox.ItemsSource =
                    alerts
                        .Where(x => !x.IsResolved)
                        .OrderByDescending(x => x.CreatedAt)
                        .ToList();

                ResolvedAlertsListBox.ItemsSource = null;

                ResolvedAlertsListBox.ItemsSource =
                    alerts
                        .Where(x => x.IsResolved)
                        .OrderByDescending(x => x.CreatedAt)
                        .ToList();
            }
            catch
            {
            }
        }

        private void StartAlertTestGenerator()
        {
            _alertTestTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };

            _alertTestTimer.Tick += async (s, e) =>
            {
                if (!_apiConnected)
                    return;

                if (LocalTelemetryStore.TotalGenerated < 10)
                    return;

                // Prevent overlapping timer executions.
                _alertTestTimer.Stop();

                try
                {
                    // Generate a new test alert.
                    await GenerateTestAlertAsync();

                    // Give the API time to create and store the alert (in memory.)
                    await Task.Delay(6000);

                    // Automatically resolve all currently unresolved alerts.
                    await ResolveAllUnresolvedAlertsAsync();
                }
                finally
                {
                    // Start the same timer again so the cycle repeats.
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

            if (!_apiConnected)
                return;

            var random = Random.Shared;

            bool highAvailable = _testHighAlerts < TestHighAlertCap;

            bool mediumAvailable = _testMediumAlerts < TestMediumAlertCap;

            bool generateHigh = highAvailable && (!mediumAvailable || random.Next(0, 2) == 0);

            string[] macs = { "00:1A:2B:3C:4D:5E", "00:1A:2B:9F:8E:7D", "AA:BB:CC:DD:EE:FF", "11:22:33:44:55:66" };
            string[] zones = { "Zone 1", "Zone 2", "Sub-Zone B", "Sub-Zone C" };

            int wattage = generateHigh
                ? random.Next(9500, 10000)
                : random.Next(7500, 9500);

            var ingestDto = new
            {
                macAddress = macs[random.Next(0, macs.Length)],
                locationZone = zones[random.Next(0, zones.Length)],
                category = "Power Consumption",
                sensorValue = wattage,
                timestamp = DateTime.Now
            };

            try
            {
                var response = await _apiClient.PostAsJsonAsync("/api/telemetry/ingest", ingestDto);
                if (response.IsSuccessStatusCode)
                {
                    if (generateHigh) _testHighAlerts++;
                    else _testMediumAlerts++;

                    FetchAlertsFromAPI();
                }
            }
            catch
            {
            }

            if (_testHighAlerts >= TestHighAlertCap && _testMediumAlerts >= TestMediumAlertCap)
            {
                _alertTestTimer?.Stop();
            }
        }

        private async Task ResolveAllUnresolvedAlertsAsync()
        {
            try
            {
                var response =
                    await _apiClient.GetAsync("/api/telemetry/alerts");

                if (!response.IsSuccessStatusCode)
                    return;

                var json =
                    await response.Content.ReadAsStringAsync();

                var alerts =
                    JsonSerializer.Deserialize<List<AlertModel>>(
                        json,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        })
                    ?? new List<AlertModel>();

                var unresolvedAlerts = alerts
                    .Where(x => !x.IsResolved)
                    .ToList();

                if (unresolvedAlerts.Count == 0)
                    return;

                int resolvedCount = 0;

                foreach (var alert in unresolvedAlerts)
                {
                    var updateDto = new
                    {
                        isResolved = true,
                        notes = "Solved automatically"
                    };

                    var updateResponse =
                        await _apiClient.PutAsJsonAsync(
                            $"/api/telemetry/alerts/{alert.Id}",
                            updateDto);

                    if (updateResponse.IsSuccessStatusCode)
                    {
                        resolvedCount++;
                    }
                }

                if (resolvedCount > 0)
                {
                    await Task.Delay(100);

                    FetchAlertsFromAPI();
                }
            }
            catch
            {
            }
        }

        private List<DisplayTelemetryModel> ConvertToDisplayModel(List<TelemetryPacket<object>> packets, string source)
        {
            return packets.Select(p => new DisplayTelemetryModel
            {
                MacAddress = p.MacAddress,
                LocationZone = p.LocationZone,
                Category = p.Category,
                SensorValue = p.SensorValue,
                Timestamp = p.Timestamp,
                Source = source
            }).ToList();
        }

        private async void BtnRegisterSensor_Click(object sender, RoutedEventArgs e)
        {
            var window = new RegisterSensorWindow(_apiClient, _apiConnected);

            PauseBackgroundTimers();
            window.ShowDialog();
            ResumeBackgroundTimers();

            await LoadRegisteredSensorsFromApiAsync();
        }

        private static string FormatElapsedMs(Stopwatch sw)
        {
            double ms = sw.Elapsed.TotalMilliseconds;
            return ms.ToString("0.###");
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

                MessageBox.Show(
                    $"Generated {generated} shared telemetry record(s).\n\n" +
                    $"API status: {(LocalTelemetryStore.ApiOnline ? "ONLINE" : "OFFLINE")}\n\n" +
                    "The same records are now available on the Landing Page and Telemetry Page.",
                    "Telemetry Generation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            finally
            {
                BtnGenerateWithApi.IsEnabled = true;
            }
        }

        private async void BtnRefreshApiData_Click(object sender, RoutedEventArgs e)
        {
            await LoadRegisteredSensorsFromApiAsync();
        }

        private async Task LoadRegisteredSensorsFromApiAsync()
        {
            if (!_apiConnected)
            {
                // Keep locally registered sensors visible and usable when the API
                // is offline.
                _registeredSensorsList = LocalTelemetryStore
                    .GetRegisteredSensors()
                    .Select(s => new DisplayTelemetryModel
                    {
                        MacAddress = s.MacAddress,
                        LocationZone = s.LocationZone,
                        Category = s.Category,
                        SensorValue = string.IsNullOrWhiteSpace(s.Description) ? "Registered" : s.Description,
                        Timestamp = s.RegisteredDate,
                        Source = "(Local Registered)"
                    })
                    .ToList();

                RefreshApiDataGrid();
                return;
            }

            try
            {
                var response = await _apiClient.GetAsync("/api/telemetry/sensors");

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();

                    var sensors =
                        JsonSerializer.Deserialize<List<SensorModel>>(
                            json,
                            new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            })
                        ?? new List<SensorModel>();

                    // Keep the shared telemetry generator in sync with the API.
                    LocalTelemetryStore.SetRegisteredSensors(sensors);

                    _registeredSensorsList = sensors
                        .Where(s => s.IsActive)
                        .Select(s => new DisplayTelemetryModel
                        {
                            MacAddress = s.MacAddress,
                            LocationZone = s.LocationZone,
                            Category = s.Category,
                            SensorValue = string.IsNullOrWhiteSpace(s.Description)
                                ? "Registered"
                                : s.Description,
                            Timestamp = s.RegisteredDate,
                            Source = "(API Registered)"
                        })
                        .ToList();
                }
                else
                {
                    _apiConnected = false;
                    _registeredSensorsList = LocalTelemetryStore
                        .GetRegisteredSensors()
                        .Select(s => new DisplayTelemetryModel
                        {
                            MacAddress = s.MacAddress,
                            LocationZone = s.LocationZone,
                            Category = s.Category,
                            SensorValue = string.IsNullOrWhiteSpace(s.Description) ? "Registered" : s.Description,
                            Timestamp = s.RegisteredDate,
                            Source = "(Local Registered)"
                        })
                        .ToList();
                }
            }
            catch
            {
                _apiConnected = false;
                _registeredSensorsList = LocalTelemetryStore
                    .GetRegisteredSensors()
                    .Select(s => new DisplayTelemetryModel
                    {
                        MacAddress = s.MacAddress,
                        LocationZone = s.LocationZone,
                        Category = s.Category,
                        SensorValue = string.IsNullOrWhiteSpace(s.Description) ? "Registered" : s.Description,
                        Timestamp = s.RegisteredDate,
                        Source = "(Local Registered)"
                    })
                    .ToList();
            }

            RefreshApiDataGrid();
        }

        private void RefreshApiDataGrid()
        {
            var apiRecords =
                LocalTelemetryStore
                    .GetApiHistory()
                    .ToList();

            var apiSensors =
                _registeredSensorsList
                    .ToList();

            var combined = apiSensors
                .Concat(apiRecords)
                .OrderByDescending(x => x.Timestamp)
                .ToList();

            ApiDataGrid.ItemsSource = null;
            ApiDataGrid.ItemsSource = combined;

            TxtApiSensorCount.Text =
                _registeredSensorsList
                    .Select(x => x.MacAddress)
                    .Distinct()
                    .Count()
                    .ToString();

            TxtApiGeneratedInline.Text =
                LocalTelemetryStore.ApiHistory.Count.ToString();
        }

        private void NavBar_NavigationRequested(object sender, string screenKey) =>
            NavigationRequested?.Invoke(this, screenKey);

        private void UpdateDisplayWithSourceTags()
        {
            _telemetryView.Refresh();
        }
        private void BtnRunPerformanceTest_Click(object sender, RoutedEventArgs e)
        {
            // Open the performance-test dropdown.
            if (sender is Button button)
            {
                button.ContextMenu.PlacementTarget = button;
                button.ContextMenu.IsOpen = true;
            }
        }
        private void PerformanceTest100_Click(object sender, RoutedEventArgs e)
        {
            RunPerformanceTest(100);
        }

        private void PerformanceTest1000_Click(object sender, RoutedEventArgs e)
        {
            RunPerformanceTest(1000);
        }

        private void PerformanceTest5000_Click(object sender, RoutedEventArgs e)
        {
            RunPerformanceTest(5000);
        }


        private void RunPerformanceTest(int testCount)
        {
            MessageBox.Show(
                $"Running performance test with {testCount:N0} insertion cycles...",
                "Performance Test",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            var random = Random.Shared;

            // Temporary collection used for the performance test,nothing is added or displayed in the DataGrid.
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

            // Start measuring only the actual test.
            _insertionStopwatch.Restart();

            for (int i = 0; i < testCount; i++)
            {
                float mockSoilMoisture =
                    (float)Math.Round(random.NextDouble() * 100, 2);

                int mockPowerWattage =
                    random.Next(100, 10000);

                string category =
                    categories[random.Next(0, categories.Length)];

                object sensorValue;

                switch (category)
                {
                    case "Environmental":
                        sensorValue = mockSoilMoisture;
                        break;

                    case "Power Consumption":
                        sensorValue = mockPowerWattage;
                        break;

                    case "Actuator":
                        sensorValue = random.Next(0, 2) == 0;
                        break;

                    default:
                        sensorValue = 0;
                        break;
                }

                var packet = new TelemetryPacket<object>(
                    macs[random.Next(0, macs.Length)],
                    zones[random.Next(0, zones.Length)],
                    category,
                    sensorValue
                );

                
                //only add to the temporary performance test collection.
                testData.Add(packet);
            }

            _insertionStopwatch.Stop();

            double elapsedMs =
                _insertionStopwatch.Elapsed.TotalMilliseconds;

            TxtInsertionTime.Text =
                elapsedMs.ToString("0.###");

            MessageBox.Show(
                $"Performance test completed.\n\n" +
                $"Test size: {testCount:N0} insertions\n" +
                $"Time: {elapsedMs:0.###} ms\n" +
                $"Average: {(elapsedMs / testCount):0.######} ms per insertion\n\n" +
                "Test data was NOT added to telemetry history.",
                "Performance Test Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            //testData does not affect the telemetry display.
        }
    }
}