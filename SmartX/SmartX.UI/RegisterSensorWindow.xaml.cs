using Microsoft.Win32;
using SmartX.UI.Models;
using SmartX.UI.Services;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;

namespace SmartX.UI
{
    public partial class RegisterSensorWindow : Window
    {
        private HttpClient _apiClient;
        private bool _apiConnected;
        private static System.Collections.Generic.List<SensorRegistration> _localSensors = new();
        private string _selectedFilePath = "";

        public RegisterSensorWindow(HttpClient apiClient, bool apiConnected)
        {
            InitializeComponent();
            _apiClient = apiClient;
            _apiConnected = apiConnected;
        }

        private void TxtMacAddress_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            string input = TxtMacAddress.Text.Replace(":", "").ToUpper();

            if (input.Length > 12)
                input = input.Substring(0, 12);

            string formatted = "";
            for (int i = 0; i < input.Length; i++)
            {
                if (i > 0 && i % 2 == 0)
                    formatted += ":";
                formatted += input[i];
            }

            TxtMacAddress.Text = formatted;
            TxtMacAddress.CaretIndex = TxtMacAddress.Text.Length;

            if (formatted.Length == 17)
                TxtMacFormat.Foreground = System.Windows.Media.Brushes.LimeGreen;
            else
                TxtMacFormat.Foreground = System.Windows.Media.Brushes.Gray;
        }
        private void BtnAttachFile_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.Filter = "All Files (*.*)|*.*";

            if (dialog.ShowDialog() == true)
            {
                _selectedFilePath = dialog.FileName;
                TxtAttachedFile.Text = System.IO.Path.GetFileName(dialog.FileName);
            }
        }
        private string GetAttachmentDescription()
        {
            if (string.IsNullOrWhiteSpace(_selectedFilePath))
                return "";

            string fileName = Path.GetFileName(_selectedFilePath);
            string extension = Path.GetExtension(_selectedFilePath).ToLowerInvariant();

            string attachmentType = extension switch
            {
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".tiff"
                    => "Image",

                ".mp4" or ".avi" or ".mov" or ".mkv" or ".wmv"
                    => "Video",

                ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg"
                    => "Audio",

                ".pdf"
                    => "PDF",

                ".doc" or ".docx"
                    => "Word Document",

                ".xls" or ".xlsx"
                    => "Excel Spreadsheet",

                ".csv"
                    => "CSV",

                ".txt"
                    => "Text File",

                ".zip" or ".rar" or ".7z"
                    => "Archive",

                _ => "File"
            };

            return $"File attached - \"{fileName}\" ({attachmentType})";
        }
        private void CacheRegisteredSensor(string mac, string zone, string category, string description)
        {
            LocalTelemetryStore.RegisterLocalSensor(new SensorModel
            {
                MacAddress = mac,
                LocationZone = zone,
                Category = category,
                Description = description,
                RegisteredDate = DateTime.Now,
                IsActive = true
            });
        }

        private async void BtnRegister_Click(object sender, RoutedEventArgs e)
        {
            string mac = TxtMacAddress.Text.Trim();
            string zone = (CmbLocationZone.SelectedItem as System.Windows.Controls.ComboBoxItem) ?.Content?.ToString() ?? "";

            string category = (CmbCategory.SelectedItem as System.Windows.Controls.ComboBoxItem) ?.Content?.ToString() ?? "";
            string description = TxtDescription.Text.Trim();

            if (!string.IsNullOrWhiteSpace(_selectedFilePath))
            {
                description = GetAttachmentDescription();
            }

            if (mac.Length != 17 || string.IsNullOrEmpty(zone) || string.IsNullOrEmpty(category))
            {
                RegistrationStatus.Text = " Invalid MAC or missing fields";
                RegistrationStatus.Foreground = System.Windows.Media.Brushes.Crimson;
                return;
            }

            if (_apiConnected)
            {
                try
                {
                    var registerDto = new
                    {
                        macAddress = mac,
                        locationZone = zone,
                        category = category,
                        description = description
                    };

                    var response = await _apiClient.PostAsJsonAsync(
                        "https://localhost:7001/api/telemetry/register-sensor",
                        registerDto
                    );

                    if (response.IsSuccessStatusCode)
                    {
                        // Make the sensor immediately available to the local
                        // telemetry generator instead of waiting for another
                        // page refresh/API poll.
                        CacheRegisteredSensor(mac, zone!, category!, description);

                        RegistrationStatus.Text = $" Sensor {mac} registered via API";
                        RegistrationStatus.Foreground = System.Windows.Media.Brushes.LimeGreen;
                        await System.Threading.Tasks.Task.Delay(1500);
                        this.Close();
                    }
                }
                catch
                {
                    RegistrationStatus.Text = " Registration failed";
                    RegistrationStatus.Foreground = System.Windows.Media.Brushes.Crimson;
                }
            }
            else
            {
                CacheRegisteredSensor(mac, zone!, category!, description);

                RegistrationStatus.Text = $" Sensor {mac} registered locally";
                RegistrationStatus.Foreground = System.Windows.Media.Brushes.LimeGreen;
                await System.Threading.Tasks.Task.Delay(1500);
                this.Close();
            }
        }

        private void StoreLocally(string mac, string zone, string category, string description)
        {
            _localSensors.Add(new SensorRegistration
            {
                MacAddress = mac,
                LocationZone = zone,
                Category = category,
                Description = description,
                RegisteredDate = DateTime.Now,
                IsActive = true
            });

            MessageBox.Show($"Sensor {mac} registered locally.", "Local Registration", MessageBoxButton.OK, MessageBoxImage.Information);
            this.Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }

    public class SensorRegistration
    {
        public string MacAddress { get; set; }
        public string LocationZone { get; set; }
        public string Category { get; set; }
        public string Description { get; set; }
        public DateTime RegisteredDate { get; set; }
        public bool IsActive { get; set; }
    }
}