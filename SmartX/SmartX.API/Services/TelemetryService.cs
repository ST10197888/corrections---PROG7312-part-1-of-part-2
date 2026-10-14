
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using SmartX.API.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SmartX.API.Services
{
    public class TelemetryService : ITelemetryService
    {
        private static readonly List<TelemetryResponseDto> _telemetryStorage = new();
        private static readonly List<SensorDto> _registeredSensors = new();
        private static readonly List<AlertDto> _alerts = new();

        private static int _telemetryIdCounter = 1;
        private static int _sensorIdCounter = 1;
        private static int _alertIdCounter = 1;

        private const int PowerSurgeThreshold = 9500;
        private const int PowerWarningThreshold = 7500;
        private const float MoistureAlertLow = 20f;
        private const float MoistureWarningLow = 35f;

        private const long MaxAttachmentBytes = 10 * 1024 * 1024;

        private static readonly string AttachmentDirectory =
            Path.Combine(
                AppContext.BaseDirectory,
                "App_Data",
                "sensor-attachments");

        private static readonly HashSet<string> AllowedAttachmentExtensions =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp",
                ".mp4", ".avi", ".mov", ".mkv", ".wmv",
                ".mp3", ".wav", ".flac", ".aac", ".ogg",
                ".pdf", ".doc", ".docx", ".xls", ".xlsx",
                ".csv", ".txt", ".zip", ".rar", ".7z"
            };

        private readonly TelemetryPipeline _pipeline;
        private readonly IDataProtector _attachmentProtector;

        public TelemetryService(
            TelemetryPipeline pipeline,
            IDataProtectionProvider dataProtectionProvider)
        {
            _pipeline = pipeline;

            _attachmentProtector =
                dataProtectionProvider.CreateProtector(
                    "SmartX.SensorAttachments.v1");
        }

        public async Task<TelemetryResponseDto> IngestTelemetryAsync(
            TelemetryIngestDto dto)
        {
            var severity = DetermineSeverity(dto);
            var isAnomalous = IsAnomalous(dto, severity);

            var telemetry = new TelemetryResponseDto
            {
                Id = _telemetryIdCounter++,
                MacAddress = dto.MacAddress,
                LocationZone = dto.LocationZone,
                Category = dto.Category,
                SensorValue = dto.SensorValue,
                Timestamp = dto.Timestamp,
                SeverityLevel = severity,
                IsAnomalous = isAnomalous
            };

            _telemetryStorage.Add(telemetry);

            // Preserve the Part 2 telemetry pipeline integration.
            _pipeline.Enqueue(telemetry);

            if (isAnomalous && _telemetryStorage.Count >= 10)
            {
                CreateAlert(
                    dto.MacAddress,
                    severity,
                    dto.SensorValue);
            }

            return await Task.FromResult(telemetry);
        }

        public async Task<TelemetryHistoryDto> GetTelemetryHistoryAsync(
            string macAddress)
        {
            var records = _telemetryStorage
                .Where(x => string.Equals(
                    x.MacAddress,
                    macAddress,
                    StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.Timestamp)
                .ToList();

            var history = new TelemetryHistoryDto
            {
                MacAddress = macAddress,
                TotalRecords = records.Count,
                Records = records
            };

            return await Task.FromResult(history);
        }

        public async Task<List<AlertDto>> GetAlertsAsync()
        {
            return await Task.FromResult(
                _alerts.OrderByDescending(x => x.CreatedAt).ToList());
        }

        public async Task<AlertDto?> UpdateAlertAsync(
            int id,
            UpdateAlertDto dto)
        {
            var alert = _alerts.FirstOrDefault(x => x.Id == id);

            if (alert is null)
                return null;

            if (dto.Notes is not null && dto.Notes != alert.Notes)
            {
                alert.Notes = dto.Notes;

                alert.History.Add(new AlertHistoryEntryDto
                {
                    Action = $"Notes updated: \"{dto.Notes}\"",
                    Timestamp = DateTime.Now
                });
            }

            if (dto.IsResolved.HasValue &&
                dto.IsResolved.Value != alert.IsResolved)
            {
                alert.IsResolved = dto.IsResolved.Value;

                alert.History.Add(new AlertHistoryEntryDto
                {
                    Action = alert.IsResolved
                        ? "Alert resolved"
                        : "Alert reopened",
                    Timestamp = DateTime.Now
                });
            }

            return await Task.FromResult<AlertDto?>(alert);
        }

        public async Task<List<TelemetryResponseDto>> GetLatestTelemetryAsync(
            int count)
        {
            var latest = _telemetryStorage
                .OrderByDescending(x => x.Timestamp)
                .Take(Math.Max(0, count))
                .ToList();

            return await Task.FromResult(latest);
        }

        public async Task<SensorDto> RegisterSensorAsync(
            RegisterSensorDto dto)
        {
            var existingSensor = _registeredSensors.FirstOrDefault(
                sensor => string.Equals(
                    sensor.MacAddress?.Trim(),
                    dto.MacAddress?.Trim(),
                    StringComparison.OrdinalIgnoreCase));

            if (existingSensor != null)
            {
                existingSensor.LocationZone = dto.LocationZone;
                existingSensor.Category = dto.Category;
                existingSensor.Description = dto.Description;
                existingSensor.IsActive = true;

                return await Task.FromResult(existingSensor);
            }

            var sensor = new SensorDto
            {
                Id = _sensorIdCounter++,
                MacAddress = dto.MacAddress,
                LocationZone = dto.LocationZone,
                Category = dto.Category,
                Description = dto.Description,
                RegisteredDate = DateTime.Now,
                IsActive = true
            };

            _registeredSensors.Add(sensor);

            return await Task.FromResult(sensor);
        }

        public async Task<List<SensorDto>> GetAllSensorsAsync()
        {
            return await Task.FromResult(
                _registeredSensors.ToList());
        }

        public async Task<SensorAttachmentDto?> SaveSensorAttachmentAsync(
            string macAddress,
            IFormFile file)
        {
            if (string.IsNullOrWhiteSpace(macAddress) ||
                file == null ||
                file.Length <= 0 ||
                file.Length > MaxAttachmentBytes)
            {
                return null;
            }

            var sensor = _registeredSensors.FirstOrDefault(
                item => item.IsActive &&
                    string.Equals(
                        item.MacAddress?.Trim(),
                        macAddress.Trim(),
                        StringComparison.OrdinalIgnoreCase));

            if (sensor == null)
                return null;

            string extension = Path.GetExtension(file.FileName);

            if (!AllowedAttachmentExtensions.Contains(extension))
                return null;

            string safeMac = Regex.Replace(
                macAddress.Trim(),
                "[^A-Za-z0-9]",
                "");

            if (string.IsNullOrWhiteSpace(safeMac))
                return null;

            Directory.CreateDirectory(AttachmentDirectory);

            string encryptedPath = Path.Combine(
                AttachmentDirectory,
                $"{safeMac}.encrypted");

            string metadataPath = Path.Combine(
                AttachmentDirectory,
                $"{safeMac}.json");

            byte[] originalBytes;

            await using (var input = new MemoryStream())
            {
                await file.CopyToAsync(input);
                originalBytes = input.ToArray();
            }

            byte[] encryptedBytes =
                _attachmentProtector.Protect(originalBytes);

            await File.WriteAllBytesAsync(
                encryptedPath,
                encryptedBytes);

            var metadata = new SensorAttachmentDto
            {
                MacAddress = macAddress.Trim(),
                FileName = Path.GetFileName(file.FileName),
                ContentType = GetAttachmentContentType(extension),
                UploadedAt = DateTime.UtcNow
            };

            await using (var metadataStream = File.Create(metadataPath))
            {
                await JsonSerializer.SerializeAsync(
                    metadataStream,
                    metadata);
            }

            return metadata;
        }

        public async Task<SensorAttachmentDownload?> GetSensorAttachmentAsync(
            string macAddress)
        {
            if (string.IsNullOrWhiteSpace(macAddress))
                return null;

            string safeMac = Regex.Replace(
                macAddress.Trim(),
                "[^A-Za-z0-9]",
                "");

            if (string.IsNullOrWhiteSpace(safeMac))
                return null;

            string encryptedPath = Path.Combine(
                AttachmentDirectory,
                $"{safeMac}.encrypted");

            string metadataPath = Path.Combine(
                AttachmentDirectory,
                $"{safeMac}.json");

            if (!File.Exists(encryptedPath) ||
                !File.Exists(metadataPath))
            {
                return null;
            }

            await using var metadataStream =
                File.OpenRead(metadataPath);

            var metadata =
                await JsonSerializer.DeserializeAsync<SensorAttachmentDto>(
                    metadataStream);

            if (metadata == null ||
                !string.Equals(
                    metadata.MacAddress,
                    macAddress.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            byte[] encryptedBytes =
                await File.ReadAllBytesAsync(encryptedPath);

            byte[] decryptedBytes =
                _attachmentProtector.Unprotect(encryptedBytes);

            return new SensorAttachmentDownload
            {
                Content = decryptedBytes,
                FileName = Path.GetFileName(metadata.FileName),
                ContentType = metadata.ContentType
            };
        }

        private static string GetAttachmentContentType(string extension)
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
                ".doc" => "application/msword",
                ".docx" =>
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".xls" => "application/vnd.ms-excel",
                ".xlsx" =>
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                _ => "application/octet-stream"
            };
        }

private string DetermineSeverity(TelemetryIngestDto dto)
        {
            int wattage = 0;
            float moisture = 0f;

            if (dto.Category == "Power Consumption")
            {
                if (!TryGetInt(dto.SensorValue, out wattage))
                    return "Low";

                if (wattage >= PowerSurgeThreshold)
                    return "High";

                if (wattage >= PowerWarningThreshold)
                    return "Medium";

                return "Low";
            }

            if (dto.Category == "Environmental")
            {
                if (!TryGetFloat(dto.SensorValue, out moisture))
                    return "Low";

                if (moisture <= MoistureAlertLow)
                    return "High";

                if (moisture <= MoistureWarningLow)
                    return "Medium";

                return "Low";
            }

            return "Low";
        }


        private static bool TryGetInt(object? value, out int result)
        {
            result = 0;

            if (value is int integer)
            {
                result = integer;
                return true;
            }

            if (value is JsonElement element &&
                element.ValueKind == JsonValueKind.Number &&
                element.TryGetInt32(out result))
            {
                return true;
            }

            return false;
        }

        private static bool TryGetFloat(object? value, out float result)
        {
            result = 0;

            if (value is float single)
            {
                result = single;
                return true;
            }

            if (value is double doubleValue)
            {
                result = (float)doubleValue;
                return true;
            }

            if (value is JsonElement element &&
                element.ValueKind == JsonValueKind.Number &&
                element.TryGetDouble(out double jsonValue))
            {
                result = (float)jsonValue;
                return true;
            }

            return false;
        }

        private static bool IsAnomalous(
            TelemetryIngestDto dto,
            string severity)
        {
            return severity == "High" || severity == "Medium";
        }

        private void CreateAlert(
            string? macAddress,
            string severity,
            object? value)
        {
            var alert = new AlertDto
            {
                Id = _alertIdCounter++,
                SensorMacAddress = macAddress,
                SeverityLevel = severity,
                Message = $"Anomalous reading detected: {value}",
                AnomalousValue = value,
                CreatedAt = DateTime.Now,
                IsResolved = false
            };

            alert.History.Add(new AlertHistoryEntryDto
            {
                Action = $"Alert created ({severity} severity): {alert.Message}",
                Timestamp = alert.CreatedAt
            });

            _alerts.Add(alert);
        }
    }
}
