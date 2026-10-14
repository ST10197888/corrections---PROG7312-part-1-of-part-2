using SmartX.API.Models;
using System.Text.Json;

using SmartX.API.Models;
using System.Linq;
using System.Text.Json;

namespace SmartX.API.Services
{
    public class TelemetryService : ITelemetryService
    {
        private static List<TelemetryResponseDto> _telemetryStorage = new();
        private static List<SensorDto> _registeredSensors = new();
        private static List<AlertDto> _alerts = new();
        private static int _telemetryIdCounter = 1;
        private static int _sensorIdCounter = 1;
        private static int _alertIdCounter = 1;

        private const int PowerSurgeThreshold = 9500;
        private const int PowerWarningThreshold = 7500;
        private const float MoistureAlertLow = 20f;
        private const float MoistureWarningLow = 35f;


        public async Task<TelemetryResponseDto> IngestTelemetryAsync(TelemetryIngestDto dto)
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
            _pipeline.Enqueue(telemetry);

            //create a new anmoalous alert after the first 10 seconds the API starts
            if (isAnomalous && _telemetryStorage.Count >= 10)
            {
                CreateAlert(
                    dto.MacAddress,
                    severity,
                    dto.SensorValue);
            }

            return await Task.FromResult(telemetry);
        }

        // This method retrieves the telemetry history for a specific sensor identified by its MAC address.
        public async Task<TelemetryHistoryDto> GetTelemetryHistoryAsync(string macAddress)
        {
            var records = _telemetryStorage
                .Where(x => x.MacAddress == macAddress)
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
            return await Task.FromResult(_alerts.OrderByDescending(x => x.CreatedAt).ToList());
        }

        public async Task<AlertDto?> UpdateAlertAsync(int id, UpdateAlertDto dto)
        {
            var alert = _alerts.FirstOrDefault(x => x.Id == id);
            if (alert is null)
                return await Task.FromResult<AlertDto?>(null);

            if (dto.Notes is not null && dto.Notes != alert.Notes)
            {
                alert.Notes = dto.Notes;
                alert.History.Add(new AlertHistoryEntryDto
                {
                    Action = $"Notes updated: \"{dto.Notes}\"",
                    Timestamp = DateTime.Now
                });
            }

            if (dto.IsResolved.HasValue && dto.IsResolved.Value != alert.IsResolved)
            {
                alert.IsResolved = dto.IsResolved.Value;
                alert.History.Add(new AlertHistoryEntryDto
                {
                    Action = alert.IsResolved ? "Alert resolved" : "Alert reopened",
                    Timestamp = DateTime.Now
                });
            }

            return await Task.FromResult<AlertDto?>(alert);
        }

        public async Task<List<TelemetryResponseDto>> GetLatestTelemetryAsync(int count)
        {
            var latest = _telemetryStorage
                .OrderByDescending(x => x.Timestamp)
                .Take(count)
                .ToList();

            return await Task.FromResult(latest);
        }
        public async Task<SensorDto> RegisterSensorAsync(RegisterSensorDto dto)
        {
            // A MAC address uniquely identifies a sensor. Dont create another sensor if the MAC is already registered.
            var existingSensor = _registeredSensors.FirstOrDefault(
                s => string.Equals(
                    s.MacAddress?.Trim(),
                    dto.MacAddress?.Trim(),
                    StringComparison.OrdinalIgnoreCase));

            if (existingSensor != null)
            {
                // Update the existing sensor instead of creating a duplicate.
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
            return await Task.FromResult(_registeredSensors);
        }

        private string DetermineSeverity(TelemetryIngestDto dto)
        {
            int wattage = 0;
            if (dto.Category == "Power Consumption" && TryGetInt(dto.SensorValue, out wattage))
            {
                if (wattage >= PowerSurgeThreshold)
                    return "High";
                if (wattage >= PowerWarningThreshold)
                    return "Medium";
                return "Low";
            }

            float moisture = 0f;
            if (dto.Category == "Environmental" && TryGetFloat(dto.SensorValue, out moisture))
            {
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
            if (value is int i) { result = i; return true; }
            if (value is JsonElement je && je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out result))
                return true;
            return false;
        }

        private static bool TryGetFloat(object? value, out float result)
        {
            result = 0;
            if (value is float f) { result = f; return true; }
            if (value is double d) { result = (float)d; return true; }
            if (value is JsonElement je && je.ValueKind == JsonValueKind.Number && je.TryGetDouble(out double dv))
            {
                result = (float)dv;
                return true;
            }
            return false;
        }

        private readonly TelemetryPipeline _pipeline;

        public TelemetryService(TelemetryPipeline pipeline)
        {
            _pipeline = pipeline;
        }


        private bool IsAnomalous(TelemetryIngestDto dto, string severity)
        {
            return severity == "High" || severity == "Medium";
        }

        private void CreateAlert(string macAddress, string severity, dynamic value)
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