
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;

namespace SmartX.UI
{
    public partial class RecordDetailWindow : Window
    {
        private readonly HttpClient? _apiClient;
        private readonly string? _attachmentMacAddress;

        public RecordDetailWindow(
            string title,
            List<KeyValuePair<string, string>> details,
            HttpClient? apiClient = null,
            string? attachmentMacAddress = null)
        {
            InitializeComponent();

            TxtDetailTitle.Text = title;
            DetailItemsControl.ItemsSource = details;

            _apiClient = apiClient;
            _attachmentMacAddress = attachmentMacAddress;

            BtnDownloadAttachment.Visibility =
                _apiClient != null &&
                !string.IsNullOrWhiteSpace(_attachmentMacAddress)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private async void BtnDownloadAttachment_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_apiClient == null ||
                string.IsNullOrWhiteSpace(_attachmentMacAddress))
            {
                TxtAttachmentStatus.Text =
                    "Attachment download is unavailable.";
                return;
            }

            BtnDownloadAttachment.IsEnabled = false;

            try
            {
                using var response = await _apiClient.GetAsync(
                    $"api/telemetry/sensors/{Uri.EscapeDataString(_attachmentMacAddress)}/attachment");

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    TxtAttachmentStatus.Text =
                        "No attachment is stored for this sensor.";
                    return;
                }

                if (!response.IsSuccessStatusCode)
                {
                    TxtAttachmentStatus.Text =
                        $"Download failed ({(int)response.StatusCode}).";
                    return;
                }

                string suggestedName =
                    response.Content.Headers.ContentDisposition?.FileNameStar
                    ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                    ?? $"sensor-{_attachmentMacAddress.Replace(':', '-')}.attachment";

                suggestedName = Path.GetFileName(suggestedName);

                var dialog = new SaveFileDialog
                {
                    Title = "Save sensor attachment",
                    FileName = suggestedName,
                    AddExtension = false,
                    OverwritePrompt = true
                };

                if (dialog.ShowDialog() != true)
                    return;

                byte[] content =
                    await response.Content.ReadAsByteArrayAsync();

                await File.WriteAllBytesAsync(dialog.FileName, content);

                TxtAttachmentStatus.Text =
                    "File downloaded. Opening it now...";

                Process.Start(new ProcessStartInfo(dialog.FileName)
                {
                    UseShellExecute = true
                });
            }
            catch (HttpRequestException)
            {
                TxtAttachmentStatus.Text =
                    "Could not contact the API.";
            }
            catch (IOException)
            {
                TxtAttachmentStatus.Text =
                    "Could not save the downloaded file.";
            }
            catch (System.ComponentModel.Win32Exception)
            {
                TxtAttachmentStatus.Text =
                    "The file was downloaded, but Windows could not open it.";
            }
            finally
            {
                BtnDownloadAttachment.IsEnabled = true;
            }
        }

        private void BtnCloseDetail_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }
    }
}
