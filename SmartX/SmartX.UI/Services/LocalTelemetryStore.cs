
using SmartX.Core;
using SmartX.UI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
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
        private const int HistoryCapacity = 5000;
        private const int RecentReadingCapacity = 10;

        private static readonly Random _random = new();

        private static readonly HttpClient _apiClient = ApiClientFactory.Client;

        private static readonly GenericRingBuffer<DisplayTelemetryModel>
            _history = new(HistoryCapacity);

        private static readonly List<DisplayTelemetryModel> _apiHistory = new();
        private static readonly List<SensorModel> _registeredSensors = new();

        private static int _registeredSensorIndex;
        private static int _totalGenerated;

        private static DispatcherTimer? _generationTimer;
        private static bool _generationInProgress;

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

        // Each zone owns a fixed-size array containing its most recent readings.
        private static readonly double[][] _recentReadingsByZone =
            Zones.Select(_ => new double[RecentReadingCapacity]).ToArray();

        private static readonly int[] _recentReadingCounts =
            new int[Zones.Length];

        private static readonly int[] _recentReadingNextIndexes =
            new int[Zones.Length];

        public static event EventHandler<DisplayTelemetryModel>? PacketGenerated;

        public static event EventHandler<bool>? ApiStatusChanged;

        public static int TotalGenerated => _totalGenerated;

        public static bool ApiOnline { get; private set; }

        public static IReadOnlyList<DisplayTelemetryModel> History => _history;

        public static IReadOnlyList<DisplayTelemetryModel> ApiHistory => _apiHistory;

        public static IReadOnlyList<SensorModel> RegisteredSensors =>
            _registeredSensors;

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

        public static void RegisterLocalSensor(SensorModel sensor)
        {
            if (string.IsNullOrWhiteSpace(sensor.MacAddress))
                return;

            var existing = _registeredSensors.FirstOrDefault(s =>
                string.Equals(
                    s.MacAddress,
                    sensor.MacAddress,
                    StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                int index = _registeredSensors.IndexOf(existing);
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
            return await GenerateAndStoreAsync(GenerateReading());
        }

        public static async Task<DisplayTelemetryModel?> GeneratePowerTestReadingAsync(
            bool highSeverity)
        {
            int wattage = highSeverity
                ? _random.Next(9500, 10000)
                : _random.Next(7500, 9500);

            int sensorIndex = _random.Next(0, Macs.Length);

            var generated = new DisplayTelemetryModel
            {
                MacAddress = Macs[sensorIndex],
                LocationZone = Zones[sensorIndex],
                Category = "Power Consumption",
                SensorValue = wattage,
                Timestamp = DateTime.Now,
                Source = "(Local)"
            };

            return await GenerateAndStoreAsync(generated);
        }

        private static async Task<DisplayTelemetryModel?> GenerateAndStoreAsync(
            DisplayTelemetryModel generated)
        {
            if (_generationInProgress)
                return null;

            _generationInProgress = true;

            try
            {
                DisplayTelemetryModel finalRecord;
                bool apiSucceeded = false;

                try
                {
                    using var response = await _apiClient.PostAsJsonAsync(
                        "api/telemetry/ingest",
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
                        string json = await response.Content.ReadAsStringAsync();

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
                        }
                        else
                        {
                            finalRecord = new DisplayTelemetryModel
                            {
                                MacAddress = generated.MacAddress,
                                LocationZone = generated.LocationZone,
                                Category = generated.Category,
                                SensorValue = generated.SensorValue,
                                Timestamp = generated.Timestamp,
                                Source = "(API)"
                            };
                        }

                        apiSucceeded = true;
                        _apiHistory.Add(finalRecord);
                    }
                    else
                    {
                        finalRecord = CreateLocalRecord(generated);
                    }
                }
                catch
                {
                    finalRecord = CreateLocalRecord(generated);
                }

                AddToHistory(finalRecord);

                PacketGenerated?.Invoke(null, finalRecord);

                SetApiStatus(apiSucceeded);

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
                .OrderByDescending(record => record.Timestamp)
                .ToList();
        }

        public static async Task<int> GenerateBatchAsync(int count)
        {
            if (count <= 0)
                return 0;

            int generated = 0;

            for (int i = 0; i < count; i++)
            {
                DisplayTelemetryModel? result = await GenerateOneAsync();

                if (result != null)
                    generated++;
            }

            return generated;
        }

        public static IReadOnlyList<DisplayTelemetryModel> GetLatest(int count)
        {
            if (count <= 0)
                return Array.Empty<DisplayTelemetryModel>();

            return _history
                .OrderByDescending(record => record.Timestamp)
                .Take(count)
                .ToList();
        }

        public static string GetRecentReadingsSummary()
        {
            var summaries = new List<string>();

            for (int zoneIndex = 0; zoneIndex < Zones.Length; zoneIndex++)
            {
                int count = _recentReadingCounts[zoneIndex];

                if (count == 0)
                {
                    summaries.Add($"{Zones[zoneIndex]}: no numeric readings");
                    continue;
                }

                var readings = new List<double>();

                for (int i = 0; i < count; i++)
                {
                    int index =
                        (_recentReadingNextIndexes[zoneIndex]
                         - count + i + RecentReadingCapacity)
                        % RecentReadingCapacity;

                    readings.Add(_recentReadingsByZone[zoneIndex][index]);
                }

                summaries.Add(
                    $"{Zones[zoneIndex]}: " +
                    string.Join(
                        ", ",
                        readings.Select(value => value.ToString(
                            "0.##",
                            CultureInfo.InvariantCulture))));
            }

            return string.Join(" | ", summaries);
        }


        private static DisplayTelemetryModel GenerateReading()
        {
            int sensorIndex = _random.Next(0, Macs.Length);
            string category = Categories[_random.Next(0, Categories.Length)];

            return new DisplayTelemetryModel
            {
                MacAddress = Macs[sensorIndex],
                LocationZone = Zones[sensorIndex],
                Category = category,
                SensorValue = GenerateValueForCategory(category),
                Timestamp = DateTime.Now,
                Source = "(API)"
            };
        }

        private static object GenerateValueForCategory(string? category)
        {
            switch (category)
            {
                case "Environmental":
                    return (float)Math.Round(
                        _random.NextDouble() * 100,
                        2);

                case "Power Consumption":
                    return _random.Next(100, 10000);

                case "Actuator":
                    return _random.Next(0, 2) == 1;

                default:
                    return 0;
            }
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
            _totalGenerated++;

            int zoneIndex = Array.IndexOf(Zones, record.LocationZone);

            if (zoneIndex < 0)
                return;

            if (record.SensorValue is null)
                return;

            if (record.SensorValue is JsonElement element &&
                (element.ValueKind == JsonValueKind.Null ||
                 element.ValueKind == JsonValueKind.Undefined))
            {
                return;
            }

            if (!TryConvertToDouble(record.SensorValue, out double reading))
                return;

            // Store the reading in this zone's circular buffer.
            int writeIndex = _recentReadingNextIndexes[zoneIndex];

            _recentReadingsByZone[zoneIndex][writeIndex] = reading;

            // Move to the next position, wrapping around when full.
            _recentReadingNextIndexes[zoneIndex] =
                (writeIndex + 1) % RecentReadingCapacity;

            // Track how many valid readings are currently stored.
            if (_recentReadingCounts[zoneIndex] < RecentReadingCapacity)
            {
                _recentReadingCounts[zoneIndex]++;
            }
        }

        private static bool TryConvertToDouble(object? value, out double result)
        {
            result = 0;

            switch (value)
            {
                case int i:
                    result = i;
                    return true;

                case long l:
                    result = l;
                    return true;

                case float f:
                    result = f;
                    return true;

                case double d:
                    result = d;
                    return true;

                case decimal m:
                    result = (double)m;
                    return true;

                case System.Text.Json.JsonElement element
                    when element.ValueKind == System.Text.Json.JsonValueKind.Number:
                    return element.TryGetDouble(out result);

                default:
                    return false;
            }
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
