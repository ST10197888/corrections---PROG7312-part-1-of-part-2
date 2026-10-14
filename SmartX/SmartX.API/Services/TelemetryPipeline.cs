using SmartX.API.Models;

namespace SmartX.API.Services
{
    public class TelemetryPipeline
    {
        private const int MaxTimelinePerDevice = 500;

        private readonly object _lock = new();
        private readonly Queue<TelemetryResponseDto> _standardQueue = new();
        private readonly PriorityQueue<TelemetryResponseDto, int> _criticalQueue = new();
        private readonly Dictionary<string, DeviceStateDto> _devices = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SortedDictionary<DateTime, TelemetryResponseDto>> _timelines = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _activeFaults = new(StringComparer.OrdinalIgnoreCase);

        private long _totalIngested;
        private long _totalProcessed;
        private long _criticalProcessed;
        private long _duplicatesBlocked;

        public void Enqueue(TelemetryResponseDto packet)
        {
            lock (_lock)
            {
                _totalIngested++;

                if (packet.SeverityLevel == "High")
                    _criticalQueue.Enqueue(packet, 0);
                else
                    _standardQueue.Enqueue(packet);
            }
        }

        public int ProcessBatch(int max)
        {
            int processed = 0;

            lock (_lock)
            {
                while (processed < max)
                {
                    TelemetryResponseDto packet;
                    bool critical = false;

                    if (_criticalQueue.TryDequeue(out var crit, out _))
                    {
                        packet = crit;
                        critical = true;
                    }
                    else if (_standardQueue.TryDequeue(out var std))
                    {
                        packet = std;
                    }
                    else
                    {
                        break;
                    }

                    ApplyPacket(packet);
                    _totalProcessed++;
                    if (critical) _criticalProcessed++;
                    processed++;
                }
            }

            return processed;
        }

        private void ApplyPacket(TelemetryResponseDto packet)
        {
            string mac = packet.MacAddress ?? "UNKNOWN";

            if (!_devices.TryGetValue(mac, out var device))
            {
                device = new DeviceStateDto { MacAddress = mac };
                _devices[mac] = device;
            }

            device.LocationZone = packet.LocationZone;
            device.Category = packet.Category;
            device.LastValue = packet.SensorValue;
            device.SeverityLevel = packet.SeverityLevel;
            device.LastSeen = DateTime.Now;
            device.PacketCount++;
            device.Status = packet.SeverityLevel == "High" ? "Critical"
                          : packet.SeverityLevel == "Medium" ? "Warning"
                          : "Online";

            _activeFaults.Remove(mac + "|DISCONNECTED");

            string criticalKey = mac + "|CRITICAL";
            if (packet.SeverityLevel == "High")
            {
                if (!_activeFaults.Add(criticalKey))
                    _duplicatesBlocked++;
            }
            else
            {
                _activeFaults.Remove(criticalKey);
            }

            if (!_timelines.TryGetValue(mac, out var timeline))
            {
                timeline = new SortedDictionary<DateTime, TelemetryResponseDto>();
                _timelines[mac] = timeline;
            }

            DateTime key = packet.Timestamp;
            while (timeline.ContainsKey(key))
                key = key.AddTicks(1);

            timeline[key] = packet;

            if (timeline.Count > MaxTimelinePerDevice)
                timeline.Remove(timeline.Keys.First());
        }

        public int DetectDisconnects(TimeSpan timeout)
        {
            int newlyDisconnected = 0;
            DateTime now = DateTime.Now;

            lock (_lock)
            {
                foreach (var device in _devices.Values)
                {
                    if (now - device.LastSeen <= timeout)
                        continue;

                    device.Status = "Disconnected";

                    if (_activeFaults.Add(device.MacAddress + "|DISCONNECTED"))
                        newlyDisconnected++;
                    else
                        _duplicatesBlocked++;
                }
            }

            return newlyDisconnected;
        }

        public List<DeviceStateDto> GetDevices(string? search, string? category, string? status)
        {
            lock (_lock)
            {
                IEnumerable<DeviceStateDto> query = _devices.Values;

                if (!string.IsNullOrWhiteSpace(search))
                {
                    string term = search.Trim();
                    query = query.Where(d =>
                        Matches(d.MacAddress, term) ||
                        Matches(d.LocationZone, term) ||
                        Matches(d.Category, term));
                }

                if (!string.IsNullOrWhiteSpace(category) && category != "All")
                    query = query.Where(d => string.Equals(d.Category, category, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(status) && status != "All")
                {
                    if (status == "Any Alert")
                        query = query.Where(d => d.Status != "Online");
                    else
                        query = query.Where(d => string.Equals(d.Status, status, StringComparison.OrdinalIgnoreCase));
                }

                return query.OrderBy(d => d.MacAddress).Select(d => d.Clone()).ToList();
            }
        }

        public DeviceStateDto? GetDevice(string macAddress)
        {
            lock (_lock)
            {
                return _devices.TryGetValue(macAddress, out var device) ? device.Clone() : null;
            }
        }

        public bool SetLastCommand(string macAddress, string? command)
        {
            lock (_lock)
            {
                if (!_devices.TryGetValue(macAddress, out var device))
                    return false;

                device.LastCommand = command;
                return true;
            }
        }

        public List<TelemetryResponseDto> GetTimeline(string macAddress, int count)
        {
            lock (_lock)
            {
                if (!_timelines.TryGetValue(macAddress, out var timeline))
                    return new List<TelemetryResponseDto>();

                return timeline.Reverse().Take(count).Select(kv => kv.Value).ToList();
            }
        }

        public List<string> GetActiveFaults()
        {
            lock (_lock)
            {
                return _activeFaults.OrderBy(f => f).ToList();
            }
        }

        public QueueStatsDto GetStats()
        {
            lock (_lock)
            {
                return new QueueStatsDto
                {
                    StandardQueueDepth = _standardQueue.Count,
                    CriticalQueueDepth = _criticalQueue.Count,
                    TotalIngested = _totalIngested,
                    TotalProcessed = _totalProcessed,
                    CriticalProcessed = _criticalProcessed,
                    DuplicateAlertsBlocked = _duplicatesBlocked,
                    ActiveFaultCount = _activeFaults.Count,
                    DeviceCount = _devices.Count
                };
            }
        }

        private static bool Matches(string? value, string term) =>
            value != null && value.Contains(term, StringComparison.OrdinalIgnoreCase);
    }
}