
using Microsoft.AspNetCore.DataProtection;
using SmartX.API.Models;
using SmartX.API.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddDataProtection();

builder.Services.AddScoped<ITelemetryService, TelemetryService>();
builder.Services.AddSingleton<TelemetryPipeline>();
builder.Services.AddHostedService<PipelineWorker>();
builder.Services.AddSingleton<CommandService>();
builder.Services.AddSingleton<InsightEngine>();

builder.Services.AddEndpointsApiExplorer();
// builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // app.UseSwagger();
    // app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");

var telemetryGroup = app.MapGroup("/api/telemetry")
    .WithName("Telemetry");

telemetryGroup.MapPost(
    "/ingest",
    async (TelemetryIngestDto dto, ITelemetryService service) =>
    {
        var result = await service.IngestTelemetryAsync(dto);
        return Results.Ok(result);
    })
    .WithName("IngestTelemetry");

telemetryGroup.MapGet(
    "/latest/{count:int}",
    async (int count, ITelemetryService service) =>
    {
        var result = await service.GetLatestTelemetryAsync(count);
        return Results.Ok(result);
    })
    .WithName("GetLatestTelemetry");

telemetryGroup.MapGet(
    "/history/{macAddress}",
    async (string macAddress, ITelemetryService service) =>
    {
        var result = await service.GetTelemetryHistoryAsync(macAddress);
        return Results.Ok(result);
    })
    .WithName("GetTelemetryHistory");

telemetryGroup.MapGet(
    "/alerts",
    async (ITelemetryService service) =>
    {
        var result = await service.GetAlertsAsync();
        return Results.Ok(result);
    })
    .WithName("GetAlerts");

telemetryGroup.MapPut(
    "/alerts/{id:int}",
    async (
        int id,
        UpdateAlertDto dto,
        ITelemetryService service) =>
    {
        var result = await service.UpdateAlertAsync(id, dto);
        return result is null
            ? Results.NotFound()
            : Results.Ok(result);
    })
    .WithName("UpdateAlert");

telemetryGroup.MapPost(
    "/register-sensor",
    async (
        RegisterSensorDto dto,
        ITelemetryService service) =>
    {
        var result = await service.RegisterSensorAsync(dto);
        return Results.Ok(result);
    })
    .WithName("RegisterSensor");

telemetryGroup.MapGet(
    "/sensors",
    async (ITelemetryService service) =>
    {
        var result = await service.GetAllSensorsAsync();
        return Results.Ok(result);
    })
    .WithName("GetAllSensors");

// Upload a sensor attachment as multipart/form-data.
// The form field must be named "file".
telemetryGroup.MapPost(
    "/sensors/{macAddress}/attachment",
    async (
        string macAddress,
        IFormFile file,
        ITelemetryService service) =>
    {
        var result = await service.SaveSensorAttachmentAsync(
            macAddress,
            file);

        return result is null
            ? Results.BadRequest(
                "Attachment rejected or sensor not registered.")
            : Results.Ok(result);
    })
    .DisableAntiforgery()
    .WithName("UploadSensorAttachment");

// The service decrypts the attachment before returning its bytes.
telemetryGroup.MapGet(
    "/sensors/{macAddress}/attachment",
    async (
        string macAddress,
        ITelemetryService service) =>
    {
        var attachment = await service.GetSensorAttachmentAsync(
            macAddress);

        return attachment is null
            ? Results.NotFound(
                "No attachment was found for this sensor.")
            : Results.File(
                attachment.Content,
                attachment.ContentType,
                attachment.FileName);
    })
    .WithName("DownloadSensorAttachment");

var streamGroup = app.MapGroup("/api/stream")
    .WithName("Stream");

streamGroup.MapGet(
    "/devices",
    (
        string? search,
        string? category,
        string? status,
        TelemetryPipeline pipeline) =>
        Results.Ok(pipeline.GetDevices(search, category, status)))
    .WithName("GetStreamDevices");

streamGroup.MapGet(
    "/devices/{macAddress}/timeline/{count:int}",
    (
        string macAddress,
        int count,
        TelemetryPipeline pipeline) =>
        Results.Ok(pipeline.GetTimeline(macAddress, count)))
    .WithName("GetDeviceTimeline");

streamGroup.MapGet(
    "/stats",
    (TelemetryPipeline pipeline) =>
        Results.Ok(pipeline.GetStats()))
    .WithName("GetPipelineStats");

streamGroup.MapGet(
    "/faults",
    (TelemetryPipeline pipeline) =>
        Results.Ok(pipeline.GetActiveFaults()))
    .WithName("GetActiveFaults");

streamGroup.MapPost(
    "/simulate-critical",
    async (ITelemetryService service) =>
    {
        var result = await service.IngestTelemetryAsync(
            new TelemetryIngestDto
            {
                MacAddress = "00:1A:2B:3C:4D:5E",
                LocationZone = "Zone 1",
                Category = "Power Consumption",
                SensorValue = 9900,
                Timestamp = DateTime.Now
            });

        return Results.Ok(result);
    })
    .WithName("SimulateCritical");

streamGroup.MapPost(
    "/commands",
    (
        CommandRequestDto dto,
        CommandService commands,
        InsightEngine insights) =>
    {
        if (string.IsNullOrWhiteSpace(dto.MacAddress) ||
            string.IsNullOrWhiteSpace(dto.Command))
        {
            return Results.BadRequest(
                "MacAddress and Command are required.");
        }

        var record = commands.Issue(dto);

        if (record is null)
        {
            return Results.NotFound(
                "Device not found. It must send telemetry first.");
        }

        insights.RecordAction(
            "command",
            record.MacAddress,
            record.Command);

        return Results.Ok(record);
    })
    .WithName("IssueCommand");

streamGroup.MapPost(
    "/commands/undo",
    (CommandService commands) =>
    {
        var record = commands.Undo();

        return record is null
            ? Results.NotFound("Nothing to undo.")
            : Results.Ok(record);
    })
    .WithName("UndoCommand");

streamGroup.MapGet(
    "/commands/history/{count:int}",
    (int count, CommandService commands) =>
        Results.Ok(commands.GetHistory(count)))
    .WithName("GetCommandHistory");

streamGroup.MapPost(
    "/actions",
    (ActionRecordDto dto, InsightEngine insights) =>
    {
        insights.RecordAction(
            dto.Kind,
            dto.Target,
            dto.Command);

        return Results.Ok();
    })
    .WithName("RecordAction");

streamGroup.MapGet(
    "/insights",
    (InsightEngine insights) =>
        Results.Ok(insights.GetInsights()))
    .WithName("GetInsights");

app.Run();
