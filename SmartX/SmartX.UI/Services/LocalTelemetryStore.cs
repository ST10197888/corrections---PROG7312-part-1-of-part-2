using SmartX.UI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace SmartX.UI.Services
{
    public static class LocalTelemetryStore
    {
        private const string API_BASE = "https://localhost:7001";

        private static readonly Random _random = new();
        private static readonly HttpClient _apiClient = new()
        {
            BaseAddress = new Uri(API_BASE),
            Timeout = TimeSpan.FromSeconds(3)
        };

        private static readonly List<DisplayTelemetryModel> _history = new();
        private static readonly List<DisplayTelemetryModel> _apiHistory = new();
        private static readonly List<SensorModel> _registeredSensors = new();
        private static int _registeredSensorIndex;

        public static IReadOnlyList<DisplayTelemetryModel> ApiHistory => _apiHistory;
        public static IReadOnlyList<SensorModel> RegisteredSensors => _registeredSensors;

        private static DispatcherTimer? _generationTimer;
        private static bool _generationInProgress;

        public static event EventHandler<DisplayTelemetryModel>? PacketGenerated;

        public static event EventHandler<bool>? ApiStatusChanged;

        public static int TotalGenerated => _history.Count;

        public static bool ApiOnline { get; private set; }

        public static IReadOnlyList<DisplayTelemetryModel> History => _history;

        private static readonly string[] Zones =
        {
            "Zone 1",
            "Zone 2",
            "Sub-Zone B",
            "Sub-Zone C"
        };

        private static readonly string[] Categories =
        {
            "Environmental",
            "Power Consumption",
            "Actuator"
        };

        private static readonly string[] Macs =
        {
            "00:1A:2B:3C:4D:5E",
            "00:1A:2B:9F:8E:7D",
            "AA:BB:CC:DD:EE:FF",
            "11:22:33:44:55:66"
        };

        /// <summary>
        /// Replaces the locally cached sensor definitions with the latest definitions
        /// returned by the API. These definitions are also used by the telemetry
        /// generator so registered sensors actually produce telemetry.
        /// </summary>
        public static void SetRegisteredSensors(IEnumerable<SensorModel> sensors)
        {
            _registeredSensors.Clear();

            foreach (var sensor in sensors.Where(s =>
                         s.IsActive &&
                         !string.IsNullOrWhiteSpace(s.MacAddress) &&
                         !string.IsNullOrWhiteSpace(s.LocationZone) &&
                         !string.IsNullOrWhiteSpace(s.Category)))
            {
                _registeredSensors.Add(sensor);
            }

            if (_registeredSensorIndex >= _registeredSensors.Count)
                _registeredSensorIndex = 0;
        }

        /// <summary>
        /// Keeps a newly registered sensor usable immediately, including when
        /// the API is unavailable.
        /// </summary>
        public static void RegisterLocalSensor(SensorModel sensor)
        {
            if (string.IsNullOrWhiteSpace(sensor.MacAddress))
                return;

            var existing = _registeredSensors.FirstOrDefault(
                s => string.Equals(s.MacAddress, sensor.MacAddress, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                var index = _registeredSensors.IndexOf(existing);
                _registeredSensors[index] = sensor;
            }
            else
            {
                _registeredSensors.Add(sensor);
            }
        }

        public static IReadOnlyList<SensorModel> GetRegisteredSensors()
        {
            return _registeredSensors.ToList();
        }

        public static void EnsureStarted()
        {
            if (_generationTimer != null)
                return;

            _generationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };

            _generationTimer.Tick += async (sender, args) =>
            {
                await GenerateOneAsync();
            };

            _generationTimer.Start();
        }

        public static async Task<DisplayTelemetryModel?> GenerateOneAsync()
        {
            if (_generationInProgress)
                return null;

            _generationInProgress = true;

            try
            {
                var generated = GenerateReading();

                DisplayTelemetryModel finalRecord;

                try
                {
                    var response = await _apiClient.PostAsJsonAsync(
                        "/api/telemetry/ingest",
                        new
                        {
                            macAddress = generated.MacAddress,
                            locationZone = generated.LocationZone,
                            category = generated.Category,
                            sensorValue = generated.SensorValue,
                            timestamp = generated.Timestamp
                        });

                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();

                        var apiRecord =
                            JsonSerializer.Deserialize<ApiTelemetryModel>(
                                json,
                                new JsonSerializerOptions
                                {
                                    PropertyNameCaseInsensitive = true
                                });

                        if (apiRecord != null)
                        {
                            finalRecord = new DisplayTelemetryModel
                            {
                                MacAddress = apiRecord.MacAddress,
                                LocationZone = apiRecord.LocationZone,
                                Category = apiRecord.Category,
                                SensorValue = apiRecord.SensorValue,
                                Timestamp = apiRecord.Timestamp,
                                Source = apiRecord.IsAnomalous
                                    ? $"(API - {apiRecord.SeverityLevel})"
                                    : "(API)"
                            };

                            _apiHistory.Add(finalRecord);

                            SetApiStatus(true);
                        }
                        else
                        {
                            finalRecord = CreateLocalRecord(generated);
                            SetApiStatus(false);
                        }
                    }
                    else
                    {
                        finalRecord = CreateLocalRecord(generated);
                        SetApiStatus(false);
                    }
                }
                catch
                {
                    finalRecord = CreateLocalRecord(generated);
                    SetApiStatus(false);
                }

                AddToHistory(finalRecord);

                PacketGenerated?.Invoke(null, finalRecord);

                return finalRecord;
            }
            finally
            {
                _generationInProgress = false;
            }
        }

        public static IReadOnlyList<DisplayTelemetryModel> GetApiHistory()
        {
            return _apiHistory
                .OrderByDescending(x => x.Timestamp)
                .ToList();
        }

        public static async Task<int> GenerateBatchAsync(int count)
        {
            int generated = 0;

            for (int i = 0; i < count; i++)
            {
                var result = await GenerateOneAsync();

                if (result != null)
                    generated++;
            }

            return generated;
        }

        public static IReadOnlyList<DisplayTelemetryModel> GetLatest(int count)
        {
            return _history
                .OrderByDescending(x => x.Timestamp)
                .Take(count)
                .ToList();
        }

        private static DisplayTelemetryModel GenerateReading()
        {
            int typeSelector = _random.Next(0, Categories.Length);

            string category = Categories[typeSelector];

            object sensorValue;

            switch (category)
            {
                case "Environmental":
                    sensorValue = (float)Math.Round(
                        _random.NextDouble() * 100,
                        2);
                    break;

                case "Power Consumption":
                    sensorValue = _random.Next(100, 10000);
                    break;

                case "Actuator":
                    sensorValue = _random.Next(0, 2) == 1;
                    break;

                default:
                    sensorValue = 0;
                    break;
            }

            int sensorIndex = _random.Next(0, Macs.Length);

            return new DisplayTelemetryModel
            {
                MacAddress = Macs[sensorIndex],
                LocationZone = Zones[sensorIndex],
                Category = category,
                SensorValue = sensorValue,
                Timestamp = DateTime.Now,
                Source = "(API)"
            };
        }

        private static DisplayTelemetryModel CreateLocalRecord(
            DisplayTelemetryModel generated)
        {
            return new DisplayTelemetryModel
            {
                MacAddress = generated.MacAddress,
                LocationZone = generated.LocationZone,
                Category = generated.Category,
                SensorValue = generated.SensorValue,
                Timestamp = generated.Timestamp,
                Source = "(WPF - API Offline)"
            };
        }

        private static void AddToHistory(DisplayTelemetryModel record)
        {
            _history.Add(record);
        }

        private static void SetApiStatus(bool online)
        {
            if (ApiOnline == online)
                return;

            ApiOnline = online;
            ApiStatusChanged?.Invoke(null, online);
        }
    }
}