using SmartX.UI.Models;
using SmartX.UI.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows;

namespace SmartX.UI.Views
{
    public partial class LandingPageView : UserControl, INavigablePage
    {
        private const string API_BASE = "https://localhost:7001";
        private const int MaxFeedItems = 15;

        private readonly HttpClient _apiClient;

        private bool _apiConnected;

        private readonly ObservableCollection<DisplayTelemetryModel> _sharedTelemetryFeed = new();
        private readonly ObservableCollection<DisplayTelemetryModel> _apiTelemetryFeed = new();
        private readonly ObservableCollection<DisplayTelemetryModel> _apiSensorFeed = new();
        private readonly ObservableCollection<AlertModel> _latestAlerts = new();
        private readonly ObservableCollection<AlertModel> _latestResolvedAlerts = new();

        public event EventHandler<string>? NavigationRequested;

        public LandingPageView()
        {
            InitializeComponent();

            _apiClient = new HttpClient
            {
                BaseAddress = new Uri(API_BASE),
                Timeout = TimeSpan.FromSeconds(3)
            };

            LocalTelemetryGrid.ItemsSource = _sharedTelemetryFeed;
            ApiDataGrid.ItemsSource = _apiTelemetryFeed;

            LatestAlertsList.ItemsSource = _latestAlerts;
            LatestResolvedAlertsList.ItemsSource = _latestResolvedAlerts;

            LocalTelemetryStore.PacketGenerated += OnTelemetryGenerated;
            LocalTelemetryStore.ApiStatusChanged += OnApiStatusChanged;

            LoadExistingTelemetry();

            TxtLocalGenerated.Text =
                LocalTelemetryStore.TotalGenerated.ToString();

            _apiConnected = LocalTelemetryStore.ApiOnline;
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
                .OrderByDescending(x => x.Timestamp))
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

                TxtLocalGenerated.Text =
                    LocalTelemetryStore.TotalGenerated.ToString();

                TxtLastUpdate.Text =
                    packet.Timestamp.ToString("HH:mm:ss");
                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         
                if (LocalTelemetryStore.ApiOnline &&
                    packet.Source?.StartsWith("(API") == true)
                {
                    _apiTelemetryFeed.Insert(0, packet);
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
                ApiDataGrid.Visibility = Visibility.Collapsed;
                TxtApiOffline.Visibility = Visibility.Visible;
                TxtApiOffline.Text = "API Offline";
            }
        }

        private async Task RefreshApiInformationAsync()
        {
            try
            {
                var response =
                    await _apiClient.GetAsync(
                        "/api/telemetry/sensors");

                _apiConnected =
                    response.IsSuccessStatusCode;

                if (_apiConnected)
                {
                    var json =
                        await response.Content.ReadAsStringAsync();

                    var sensors =
                        JsonSerializer.Deserialize<
                            System.Collections.Generic.List<SensorModel>>(
                            json,
                            new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            })
                        ?? new();

                    TxtApiSensors.Text =
                        sensors
                            .Select(x => x.MacAddress)
                            .Distinct()
                            .Count()
                            .ToString();
                }
            }
            catch
            {
                _apiConnected = false;
            }

            UpdateApiDisplay();

            if (_apiConnected)
                await FetchAlertsAsync();
        }

        private async Task FetchAlertsAsync()
        {
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
                    JsonSerializer.Deserialize<
                        System.Collections.Generic.List<AlertModel>>(
                            json,
                            new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            })
                    ?? new();

                _latestAlerts.Clear();

                foreach (var alert in alerts
                    .Where(x => !x.IsResolved)
                    .Take(MaxFeedItems))
                {
                    _latestAlerts.Add(alert);
                }

                _latestResolvedAlerts.Clear();

                foreach (var alert in alerts
                    .Where(x => x.IsResolved)
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
            }
        }

        private void LoadApiTelemetry()
        {
            _apiTelemetryFeed.Clear();

            foreach (var record in LocalTelemetryStore
                .GetApiHistory()
                .OrderByDescending(x => x.Timestamp))
            {
                _apiTelemetryFeed.Add(record);
            }
        }

        private void NavBar_NavigationRequested(
            object sender,
            string screenKey)
        {
            NavigationRequested?.Invoke(
                this,
                screenKey);
        }
    }
}