using SmartX.UI.Models;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace SmartX.UI
{
    public partial class AlertDetailWindow : Window
    {
        private readonly HttpClient _apiClient;
        private AlertModel _alert;

        // True once a notes-save or resolve/reopen has gone through, so the
        // caller (MainWindow) knows to refresh its alerts list.
        public bool AlertWasUpdated { get; private set; }

        public AlertDetailWindow(AlertModel alert, HttpClient apiClient)
        {
            InitializeComponent();
            _alert = alert;
            _apiClient = apiClient;
            PopulateFromAlert();
        }

        private void PopulateFromAlert()
        {
            TxtAlertTitle.Text = $"Alert #{_alert.Id:D2}";
            TxtAlertCreatedAt.Text = _alert.CreatedAt.ToString("dd MMM yyyy");
            TxtAlertValue.Text = _alert.AnomalousValue?.ToString() ?? "-";
            TxtSeverityTag.Text = $"{_alert.SeverityLevel ?? "Unknown"} severity";
            TxtMacTag.Text = $"MAC: {_alert.SensorMacAddress ?? "-"}";
            TxtNotesInput.Text = _alert.Notes ?? string.Empty;

            UpdateStatusVisuals();
            HistoryItemsControl.ItemsSource = _alert.History ?? new List<AlertHistoryEntryModel>();
        }

        private void UpdateStatusVisuals()
        {
            if (_alert.IsResolved)
            {
                TxtStatusBadge.Text = "RESOLVED";
                StatusBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(0, 150, 80));
                BtnResolveToggle.Content = "Reopen Alert";
            }
            else
            {
                TxtStatusBadge.Text = "ACTIVE";
                StatusBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(255, 68, 68));
                BtnResolveToggle.Content = "Resolve Alert";
            }
        }

        private async void BtnSaveNotes_Click(object sender, RoutedEventArgs e)
        {
            await SendUpdateAsync(new { Notes = TxtNotesInput.Text });
        }

        private async void BtnResolveToggle_Click(object sender, RoutedEventArgs e)
        {
            await SendUpdateAsync(new { IsResolved = !_alert.IsResolved });
        }

        private async Task SendUpdateAsync(object payload)
        {
            try
            {
                var response = await _apiClient.PutAsJsonAsync($"/api/telemetry/alerts/{_alert.Id}", payload);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var updated = JsonSerializer.Deserialize<AlertModel>(
                        json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (updated is not null)
                    {
                        _alert = updated;
                        AlertWasUpdated = true;
                        PopulateFromAlert();
                    }
                }
                else
                {
                    MessageBox.Show("Could not update the alert. Please try again.", "Update Failed");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating alert: {ex.Message}", "Update Failed");
            }
        }

        private void BtnCloseAlertDetail_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}