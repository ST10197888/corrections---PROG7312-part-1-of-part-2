using SmartX.API.Models;

namespace SmartX.API.Services
{
    public class CommandService
    {
        private readonly object _lock = new();
        private readonly Stack<CommandRecordDto> _undoStack = new();
        private readonly List<CommandRecordDto> _history = new();
        private readonly TelemetryPipeline _pipeline;
        private int _idCounter = 1;

        public CommandService(TelemetryPipeline pipeline)
        {
            _pipeline = pipeline;
        }

        public CommandRecordDto? Issue(CommandRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.MacAddress) || string.IsNullOrWhiteSpace(request.Command))
                return null;

            lock (_lock)
            {
                var device = _pipeline.GetDevice(request.MacAddress.Trim());
                if (device is null)
                    return null;

                var record = new CommandRecordDto
                {
                    Id = _idCounter++,
                    MacAddress = device.MacAddress,
                    Command = request.Command.Trim(),
                    PreviousCommand = device.LastCommand,
                    IssuedAt = DateTime.Now
                };

                _pipeline.SetLastCommand(device.MacAddress, record.Command);
                _undoStack.Push(record);
                _history.Add(record);

                return record.Clone();
            }
        }

        public CommandRecordDto? Undo()
        {
            lock (_lock)
            {
                if (!_undoStack.TryPop(out var record))
                    return null;

                _pipeline.SetLastCommand(record.MacAddress, record.PreviousCommand);
                record.IsUndone = true;

                return record.Clone();
            }
        }

        public List<CommandRecordDto> GetHistory(int count)
        {
            lock (_lock)
            {
                return Enumerable.Reverse(_history)
                    .Take(count)
                    .Select(r => r.Clone())
                    .ToList();
            }
        }
    }
}