
using SmartX.UI.Models;
using SmartX.UI.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SmartX.UI.Views
{
    public partial class LandingPageView : UserControl, INavigablePage
    {
        private const int MaxFeedItems = 15;

        private readonly HttpClient _apiClient = ApiClientFactory.Client;
        private bool _apiConnected;

        private readonly ObservableCollection<DisplayTelemetryModel>
            _sharedTelemetryFeed = new();

        private readonly ObservableCollection<DisplayTelemetryModel>
            _apiTelemetryFeed = new();

        private readonly ObservableCollection<AlertModel>
            _latestAlerts = new();

        private readonly ObservableCollection<AlertModel>
            _latestResolvedAlerts = new();

        public event EventHandler<string>? NavigationRequested;

        public LandingPageView()
        {
            InitializeComponent();

            LocalTelemetryGrid.ItemsSource = _sharedTelemetryFeed;
            ApiDataGrid.ItemsSource = _apiTelemetryFeed;

            LatestAlertsList.ItemsSource = _latestAlerts;
            LatestResolvedAlertsList.ItemsSource = _latestResolvedAlerts;

            LocalTelemetryStore.PacketGenerated += OnTelemetryGenerated;
            LocalTelemetryStore.ApiStatusChanged += OnApiStatusChanged;

            _apiConnected = LocalTelemetryStore.ApiOnline;

            LoadExistingTelemetry();
            LoadApiTelemetry();

            UpdateApiDisplay();
        }

        private void LandingPageView_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            LoadExistingTelemetry();
            LoadApiTelemetry();

            _ = RefreshApiInformationAsync();
        }

        public void ActivateNav(string screenKey)
        {
            NavBar.SetActive(screenKey);
        }

        private void LoadExistingTelemetry()
        {
            _sharedTelemetryFeed.Clear();

            foreach (var record in LocalTelemetryStore
                .GetLatest(MaxFeedItems)
                .OrderByDescending(record => record.Timestamp))
            {
                _sharedTelemetryFeed.Add(record);
            }

            TxtLocalGenerated.Text =
                LocalTelemetryStore.TotalGenerated.ToString();

            if (_sharedTelemetryFeed.Count > 0)
            {
                TxtLastUpdate.Text =
                    _sharedTelemetryFeed[0].Timestamp.ToString("HH:mm:ss");
            }
        }

        private void OnTelemetryGenerated(
            object? sender,
            DisplayTelemetryModel packet)
        {
            Dispatcher.Invoke(() =>
            {
                _sharedTelemetryFeed.Insert(0, packet);

                while (_sharedTelemetryFeed.Count > MaxFeedItems)
                {
                    _sharedTelemetryFeed.RemoveAt(
                        _sharedTelemetryFeed.Count - 1);
                }

                TxtLocalGenerated.Text =
                    LocalTelemetryStore.TotalGenerated.ToString();

                TxtLastUpdate.Text =
                    packet.Timestamp.ToString("HH:mm:ss");

                // While offline, show local readings in the API data grid.
                // While online, only show readings confirmed by the API.
                bool shouldDisplay =
                    !_apiConnected ||
                    packet.Source?.StartsWith(
                        "(API",
                        StringComparison.Ordinal) == true;

                if (shouldDisplay)
                {
                    _apiTelemetryFeed.Insert(0, packet);

                    while (_apiTelemetryFeed.Count > MaxFeedItems)
                    {
                        _apiTelemetryFeed.RemoveAt(
                            _apiTelemetryFeed.Count - 1);
                    }
                }
            });
        }

        private void OnApiStatusChanged(
            object? sender,
            bool online)
        {
            Dispatcher.Invoke(() =>
            {
                _apiConnected = online;

                UpdateApiDisplay();
                LoadApiTelemetry();

                if (online)
                    _ = RefreshApiInformationAsync();
            });
        }

        private void UpdateApiDisplay()
        {
            if (_apiConnected)
            {
                ApiDataGrid.Visibility = Visibility.Visible;
                TxtApiOffline.Visibility = Visibility.Collapsed;
                TxtApiOffline.Text = "";
            }
            else
            {
                ApiDataGrid.Visibility = Visibility.Visible;
                TxtApiOffline.Visibility = Visibility.Visible;
                TxtApiOffline.Text =
                    "API Offline — displaying local telemetry";
            }
        }

        private async Task RefreshApiInformationAsync()
        {
            try
            {
                using var response =
                    await _apiClient.GetAsync("api/telemetry/sensors");

                _apiConnected = response.IsSuccessStatusCode;

                if (_apiConnected)
                {
                    string json =
                        await response.Content.ReadAsStringAsync();

                    var sensors =
                        JsonSerializer.Deserialize<
                            System.Collections.Generic.List<SensorModel>>(
                            json,
                            new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            }) ?? new();

                    LocalTelemetryStore.SetRegisteredSensors(sensors);

                    TxtApiSensors.Text = sensors
                        .Where(sensor => sensor.IsActive)
                        .Select(sensor => sensor.MacAddress)
                        .Distinct()
                        .Count()
                        .ToString();

                    await FetchAlertsAsync();
                }
            }
            catch
            {
                _apiConnected = false;
            }

            UpdateApiDisplay();
            LoadApiTelemetry();
        }

        private async Task FetchAlertsAsync()
        {
            try
            {
                using var response =
                    await _apiClient.GetAsync("api/telemetry/alerts");

                if (!response.IsSuccessStatusCode)
                    return;

                string json =
                    await response.Content.ReadAsStringAsync();

                var alerts =
                    JsonSerializer.Deserialize<
                        System.Collections.Generic.List<AlertModel>>(
                        json,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        }) ?? new();

                _latestAlerts.Clear();

                foreach (var alert in alerts
                    .Where(alert => !alert.IsResolved)
                    .OrderByDescending(alert => alert.CreatedAt)
                    .Take(MaxFeedItems))
                {
                    _latestAlerts.Add(alert);
                }

                _latestResolvedAlerts.Clear();

                foreach (var alert in alerts
                    .Where(alert => alert.IsResolved)
                    .OrderByDescending(alert => alert.CreatedAt)
                    .Take(MaxFeedItems))
                {
                    _latestResolvedAlerts.Add(alert);
                }

                TxtActiveAlertCount.Text =
                    _latestAlerts.Count.ToString();

                TxtResolvedAlertCount.Text =
                    _latestResolvedAlerts.Count.ToString();
            }
            catch
            {
                // Keep the last displayed alert data if refresh fails.
            }
        }

        private void LoadApiTelemetry()
        {
            _apiTelemetryFeed.Clear();

            // API records online; shared local history offline.
            var records = _apiConnected
                ? LocalTelemetryStore.GetApiHistory()
                : LocalTelemetryStore.GetLatest(MaxFeedItems);

            foreach (var record in records
                .OrderByDescending(record => record.Timestamp)
                .Take(MaxFeedItems))
            {
                _apiTelemetryFeed.Add(record);
            }
        }

        private void NavBar_NavigationRequested(
            object sender,
            string screenKey)
        {
            NavigationRequested?.Invoke(this, screenKey);
        }
    }
}
