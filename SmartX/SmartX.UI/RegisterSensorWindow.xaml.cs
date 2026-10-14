
using Microsoft.Win32;
using SmartX.Core;
using SmartX.UI.Models;
using SmartX.UI.Services;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SmartX.UI
{
    public partial class RegisterSensorWindow : Window
    {
        private const long MaxAttachmentBytes = 10 * 1024 * 1024;

        private static readonly string[] AllowedExtensions =
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp",
            ".mp4", ".avi", ".mov", ".mkv", ".wmv",
            ".mp3", ".wav", ".flac", ".aac", ".ogg",
            ".pdf", ".doc", ".docx", ".xls", ".xlsx",
            ".csv", ".txt", ".zip", ".rar", ".7z"
        };

        private readonly HttpClient _apiClient;
        private readonly bool _apiConnected;
        private readonly DeploymentNode _facilityMap;

        private string _selectedFilePath = "";

        public RegisterSensorWindow(
            HttpClient apiClient,
            bool apiConnected,
            DeploymentNode facilityMap)
        {
            InitializeComponent();

            _apiClient = apiClient;
            _apiConnected = apiConnected;
            _facilityMap = facilityMap
                ?? throw new ArgumentNullException(nameof(facilityMap));
        }

        private void TxtMacAddress_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            string input = TxtMacAddress.Text
                .Replace(":", "")
                .ToUpperInvariant();

            if (input.Length > 12)
                input = input.Substring(0, 12);

            string formatted = "";

            for (int i = 0; i < input.Length; i++)
            {
                if (i > 0 && i % 2 == 0)
                    formatted += ":";

                formatted += input[i];
            }

            if (TxtMacAddress.Text != formatted)
            {
                TxtMacAddress.Text = formatted;
                TxtMacAddress.CaretIndex = formatted.Length;
            }

            bool valid = Regex.IsMatch(
                formatted,
                "^([0-9A-F]{2}:){5}[0-9A-F]{2}$");

            TxtMacFormat.Foreground = valid
                ? Brushes.LimeGreen
                : Brushes.Gray;
        }

        private void BtnAttachFile_Click(
            object sender,
            RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Choose a sensor attachment",
                Filter =
                    "Supported files|" +
                    "*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.webp;" +
                    "*.mp4;*.avi;*.mov;*.mkv;*.wmv;" +
                    "*.mp3;*.wav;*.flac;*.aac;*.ogg;" +
                    "*.pdf;*.doc;*.docx;*.xls;*.xlsx;" +
                    "*.csv;*.txt;*.zip;*.rar;*.7z",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() != true)
                return;

            if (!ValidateAttachment(dialog.FileName, out string error))
            {
                _selectedFilePath = "";
                TxtAttachedFile.Text = "No file selected";
                ShowStatus(error, false);
                return;
            }

            _selectedFilePath = dialog.FileName;
            TxtAttachedFile.Text = Path.GetFileName(dialog.FileName);

            ShowStatus(
                "Attachment selected. It will be uploaded after registration.",
                true);
        }

        private static bool ValidateAttachment(
            string filePath,
            out string error)
        {
            error = "";

            string extension = Path.GetExtension(filePath);

            if (!AllowedExtensions.Contains(extension,
                    StringComparer.OrdinalIgnoreCase))
            {
                error = "This file type is not supported.";
                return false;
            }

            var file = new FileInfo(filePath);

            if (!file.Exists)
            {
                error = "The selected file no longer exists.";
                return false;
            }

            if (file.Length == 0)
            {
                error = "The selected file is empty.";
                return false;
            }

            if (file.Length > MaxAttachmentBytes)
            {
                error = "Attachments must be 10 MB or smaller.";
                return false;
            }

            return true;
        }

        private string GetRegistrationDescription()
        {
            string description = TxtDescription.Text.Trim();

            if (string.IsNullOrWhiteSpace(_selectedFilePath))
                return description;

            string fileName = Path.GetFileName(_selectedFilePath);
            string attachmentNote = $"Attachment: {fileName}";

            return string.IsNullOrWhiteSpace(description)
                ? attachmentNote
                : $"{description} | {attachmentNote}";
        }

        private static string GetClientContentType(string extension)
        {
            return extension.ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".webp" => "image/webp",
                ".pdf" => "application/pdf",
                ".txt" => "text/plain",
                ".csv" => "text/csv",
                ".mp4" => "video/mp4",
                ".avi" => "video/x-msvideo",
                ".mov" => "video/quicktime",
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                ".zip" => "application/zip",
                _ => "application/octet-stream"
            };
        }

        private void CacheRegisteredSensor(
            string mac,
            string zone,
            string category,
            string description)
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

        private async Task<bool> UploadAttachmentAsync(string macAddress)
        {
            if (string.IsNullOrWhiteSpace(_selectedFilePath))
                return true;

            if (!ValidateAttachment(_selectedFilePath, out string error))
            {
                ShowStatus(error, false);
                return false;
            }

            try
            {
                using var multipart = new MultipartFormDataContent();

                await using var fileStream =
                    File.OpenRead(_selectedFilePath);

                using var fileContent = new StreamContent(fileStream);

                fileContent.Headers.ContentType =
                    new MediaTypeHeaderValue(
                        GetClientContentType(
                            Path.GetExtension(_selectedFilePath)));

                multipart.Add(
                    fileContent,
                    "file",
                    Path.GetFileName(_selectedFilePath));

                using var response = await _apiClient.PostAsync(
                    $"api/telemetry/sensors/{Uri.EscapeDataString(macAddress)}/attachment",
                    multipart);

                return response.IsSuccessStatusCode;
            }
            catch (HttpRequestException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }

        private async void BtnRegister_Click(
            object sender,
            RoutedEventArgs e)
        {
            string mac = TxtMacAddress.Text.Trim();

            string zone =
                (CmbLocationZone.SelectedItem as ComboBoxItem)
                    ?.Content?.ToString() ?? "";

            string category =
                (CmbCategory.SelectedItem as ComboBoxItem)
                    ?.Content?.ToString() ?? "";

            if (!Regex.IsMatch(
                mac,
                "^([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}$"))
            {
                ShowStatus("Enter a valid MAC address.", false);
                return;
            }

            if (string.IsNullOrWhiteSpace(zone) ||
                string.IsNullOrWhiteSpace(category))
            {
                ShowStatus(
                    "Choose both a location zone and a category.",
                    false);
                return;
            }

            if (!NetworkHierarchyValidator.ValidateNodePlacement(
                _facilityMap,
                zone))
            {
                ShowStatus(
                    $"Zone '{zone}' is not in the facility hierarchy.",
                    false);
                return;
            }

            if (!string.IsNullOrWhiteSpace(_selectedFilePath) &&
                !ValidateAttachment(_selectedFilePath, out string error))
            {
                ShowStatus(error, false);
                return;
            }

            string description = GetRegistrationDescription();

            BtnRegister.IsEnabled = false;
            BtnCancel.IsEnabled = false;
            BtnAttachFile.IsEnabled = false;

            try
            {
                if (_apiConnected)
                {
                    var registerDto = new
                    {
                        macAddress = mac,
                        locationZone = zone,
                        category,
                        description
                    };

                    using var response = await _apiClient.PostAsJsonAsync(
                        "api/telemetry/register-sensor",
                        registerDto);

                    if (!response.IsSuccessStatusCode)
                    {
                        ShowStatus(
                            $"API registration failed ({(int)response.StatusCode}).",
                            false);
                        return;
                    }

                    CacheRegisteredSensor(
                        mac,
                        zone,
                        category,
                        description);

                    if (!string.IsNullOrWhiteSpace(_selectedFilePath))
                    {
                        bool uploaded = await UploadAttachmentAsync(mac);

                        if (!uploaded)
                        {
                            ShowStatus(
                                "Sensor registered, but attachment upload failed. The sensor is saved; retry the attachment when the API is available.",
                                false);
                            return;
                        }
                    }

                    ShowStatus(
                        $"Sensor {mac} registered via API.",
                        true);
                }
                else
                {
                    CacheRegisteredSensor(
                        mac,
                        zone,
                        category,
                        description);

                    if (!string.IsNullOrWhiteSpace(_selectedFilePath))
                    {
                        ShowStatus(
                            "Sensor registered locally. The attachment was not uploaded because the API is offline.",
                            false);
                        return;
                    }

                    ShowStatus(
                        $"Sensor {mac} registered locally.",
                        true);
                }

                await Task.Delay(900);
                Close();
            }
            catch (HttpRequestException)
            {
                ShowStatus(
                    "Could not contact the API. Check that the API is running.",
                    false);
            }
            catch (IOException)
            {
                ShowStatus(
                    "Could not read the selected attachment.",
                    false);
            }
            finally
            {
                BtnRegister.IsEnabled = true;
                BtnCancel.IsEnabled = true;
                BtnAttachFile.IsEnabled = true;
            }
        }

        private void ShowStatus(string message, bool success)
        {
            RegistrationStatus.Text = message;
            RegistrationStatus.Foreground = success
                ? Brushes.LimeGreen
                : Brushes.Crimson;
        }

        private void BtnCancel_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }
    }
}
