using SmartX.API.Models;

namespace SmartX.API.Services
{
    public class InsightEngine
    {
        private const int MaxRecentActions = 200;
        private const int MinSupport = 2;
        private const int MaxSuggestions = 5;
        private const int MaxInsights = 8;
        private static readonly TimeSpan SequenceWindow = TimeSpan.FromMinutes(2);

        private readonly object _lock = new();
        private readonly Dictionary<string, Dictionary<string, int>> _transitions = new(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<(string Key, DateTime At)> _recentActions = new();
        private readonly TelemetryPipeline _pipeline;
        private string? _lastActionKey;
        private DateTime _lastActionAt;

        public InsightEngine(TelemetryPipeline pipeline)
        {
            _pipeline = pipeline;
        }

        public void RecordAction(string? kind, string? target, string? command)
        {
            if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(target))
                return;

            string key = BuildKey(kind.Trim().ToLowerInvariant(), target.Trim(), command?.Trim());
            DateTime now = DateTime.Now;

            var contexts = new List<string>();
            foreach (string fault in _pipeline.GetActiveFaults())
                contexts.Add("fault|" + fault);

            lock (_lock)
            {
                if (_lastActionKey != null &&
                    now - _lastActionAt <= SequenceWindow &&
                    !string.Equals(_lastActionKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    contexts.Add("action|" + _lastActionKey);
                }

                foreach (string context in contexts)
                    Increment(context, key);

                _lastActionKey = key;
                _lastActionAt = now;

                _recentActions.Enqueue((key, now));
                if (_recentActions.Count > MaxRecentActions)
                    _recentActions.Dequeue();
            }
        }

        public InsightsResponseDto GetInsights()
        {
            var faults = _pipeline.GetActiveFaults();
            var alertingDevices = _pipeline.GetDevices(null, null, "Any Alert");

            return new InsightsResponseDto
            {
                SuggestedActions = BuildSuggestions(faults),
                Insights = BuildInsights(alertingDevices)
            };
        }

        private List<SuggestedActionDto> BuildSuggestions(List<string> faults)
        {
            var queue = new PriorityQueue<SuggestedActionDto, double>();

            lock (_lock)
            {
                var contexts = new List<(string ContextKey, string Label)>();

                foreach (string fault in faults)
                    contexts.Add(("fault|" + fault, "when " + DescribeFault(fault)));

                if (_lastActionKey != null && DateTime.Now - _lastActionAt <= SequenceWindow)
                    contexts.Add(("action|" + _lastActionKey, "right after " + DescribeAction(_lastActionKey)));

                foreach (var (contextKey, label) in contexts)
                {
                    if (!_transitions.TryGetValue(contextKey, out var next))
                        continue;

                    int total = next.Values.Sum();

                    foreach (var (actionKey, count) in next)
                    {
                        if (count < MinSupport)
                            continue;

                        double confidence = (double)count / total;
                        double score = (double)count / (total + 1);

                        var suggestion = ParseAction(actionKey);
                        suggestion.Reason = "You usually do this " + label;
                        suggestion.Confidence = Math.Round(confidence, 2);
                        suggestion.Occurrences = count;

                        queue.Enqueue(suggestion, -score);
                    }
                }
            }

            var result = new List<SuggestedActionDto>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (result.Count < MaxSuggestions && queue.TryDequeue(out var item, out _))
            {
                string identity = item.Kind + "|" + item.Target + "|" + item.Command;
                if (seen.Add(identity))
                    result.Add(item);
            }

            return result;
        }

        private static List<InsightDto> BuildInsights(List<DeviceStateDto> devices)
        {
            var queue = new PriorityQueue<InsightDto, int>();
            DateTime now = DateTime.Now;

            foreach (var device in devices)
            {
                string place = $"{device.LocationZone} ({device.Category})";

                if (device.Status == "Critical")
                {
                    queue.Enqueue(new InsightDto
                    {
                        Severity = "Critical",
                        MacAddress = device.MacAddress,
                        Title = $"Critical reading on {device.MacAddress}",
                        Detail = $"{place} reported {device.LastValue}. Investigate immediately."
                    }, 0);
                }
                else if (device.Status == "Disconnected")
                {
                    int silentSeconds = (int)(now - device.LastSeen).TotalSeconds;
                    queue.Enqueue(new InsightDto
                    {
                        Severity = "Disconnected",
                        MacAddress = device.MacAddress,
                        Title = $"No data from {device.MacAddress}",
                        Detail = $"{place} has been silent for {silentSeconds}s. Check power and connectivity."
                    }, 1);
                }
                else if (device.Status == "Warning")
                {
                    queue.Enqueue(new InsightDto
                    {
                        Severity = "Warning",
                        MacAddress = device.MacAddress,
                        Title = $"Reading drifting on {device.MacAddress}",
                        Detail = $"{place} reported {device.LastValue}. Keep an eye on it."
                    }, 2);
                }
            }

            var result = new List<InsightDto>();
            while (result.Count < MaxInsights && queue.TryDequeue(out var insight, out _))
                result.Add(insight);

            return result;
        }

        private void Increment(string context, string actionKey)
        {
            if (!_transitions.TryGetValue(context, out var next))
            {
                next = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                _transitions[context] = next;
            }

            next[actionKey] = next.GetValueOrDefault(actionKey) + 1;
        }

        private static string BuildKey(string kind, string target, string? command) =>
            kind == "command" ? $"command|{target}|{command}" : $"{kind}|{target}";

        private static SuggestedActionDto ParseAction(string actionKey)
        {
            string[] parts = actionKey.Split('|', 3);

            return new SuggestedActionDto
            {
                Kind = parts[0],
                Target = parts.Length > 1 ? parts[1] : string.Empty,
                Command = parts.Length > 2 ? parts[2] : null
            };
        }

        private static string DescribeAction(string actionKey)
        {
            var action = ParseAction(actionKey);

            return action.Kind == "command"
                ? $"sending \"{action.Command}\" to {action.Target}"
                : $"searching \"{action.Target}\"";
        }

        private static string DescribeFault(string fault)
        {
            string[] parts = fault.Split('|', 2);
            string state = parts.Length > 1 && parts[1] == "DISCONNECTED" ? "Disconnected" : "Critical";

            return $"{parts[0]} is {state}";
        }
    }
}