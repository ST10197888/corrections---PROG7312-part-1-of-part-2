using SmartX.API.Models;

namespace SmartX.API.Services
{
    public interface ITelemetryService
    {
        Task<TelemetryResponseDto> IngestTelemetryAsync(TelemetryIngestDto dto);
        Task<TelemetryHistoryDto> GetTelemetryHistoryAsync(string macAddress);
        Task<List<AlertDto>> GetAlertsAsync();
        Task<AlertDto?> UpdateAlertAsync(int id, UpdateAlertDto dto);
        Task<SensorDto> RegisterSensorAsync(RegisterSensorDto dto);
        Task<List<TelemetryResponseDto>> GetLatestTelemetryAsync(int count);
        Task<List<SensorDto>> GetAllSensorsAsync();
    }
}