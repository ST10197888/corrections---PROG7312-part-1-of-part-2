namespace SmartX.API.Services
{
    public class PipelineWorker : BackgroundService
    {
        private static readonly TimeSpan DisconnectTimeout = TimeSpan.FromSeconds(30);

        private readonly TelemetryPipeline _pipeline;

        public PipelineWorker(TelemetryPipeline pipeline)
        {
            _pipeline = pipeline;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            int tick = 0;

            while (!stoppingToken.IsCancellationRequested)
            {
                _pipeline.ProcessBatch(50);

                tick++;
                if (tick % 20 == 0)
                    _pipeline.DetectDisconnects(DisconnectTimeout);

                try
                {
                    await Task.Delay(250, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}