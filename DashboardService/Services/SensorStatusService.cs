using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DashboardService.Models;

namespace DashboardService.Services;

public class SensorStatusService
{
    private readonly ConfigurationService _configurationService = new();

    public async Task<SensorStatusSnapshot> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        string baseUrl = _configurationService.GetSensorServiceBaseUrl();

        try
        {
            using var httpClient = new HttpClient
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromSeconds(5)
            };

            using var response = await httpClient.GetAsync(
                "/api/sensors/status",
                cancellationToken);

            var payload = await response.Content.ReadFromJsonAsync<StatusResponse>(
                cancellationToken: cancellationToken);

            if (!response.IsSuccessStatusCode || payload?.Success != true)
            {
                return new SensorStatusSnapshot
                {
                    ServiceAvailable = false,
                    Message = payload?.Message
                        ?? $"Sensor service returned {(int)response.StatusCode}."
                };
            }

            return new SensorStatusSnapshot
            {
                ServiceAvailable = true,
                Message = payload.Message,
                Connected = payload.Connected?
                    .Select(MapSensor)
                    .ToList()
                    ?? [],
                Disconnected = payload.Disconnected?
                    .Select(MapSensor)
                    .ToList()
                    ?? []
            };
        }
        catch (TaskCanceledException)
        {
            return new SensorStatusSnapshot
            {
                ServiceAvailable = false,
                Message = "Sensor service did not respond in time."
            };
        }
        catch (Exception ex)
        {
            return new SensorStatusSnapshot
            {
                ServiceAvailable = false,
                Message = $"Sensor service is not reachable ({ex.Message})."
            };
        }
    }

    private static SensorLiveStatus MapSensor(SensorPayload sensor)
    {
        string name = string.IsNullOrWhiteSpace(sensor.SensorName)
            ? (sensor.SensorType ?? sensor.SensorModel ?? $"Sensor {sensor.SensorId}")
            : sensor.SensorName!;

        string location = string.IsNullOrWhiteSpace(sensor.Port)
            ? $"Chamber {sensor.ChamberId}"
            : $"{sensor.Port} · Chamber {sensor.ChamberId}";

        return new SensorLiveStatus
        {
            SensorId = sensor.SensorId,
            SensorName = name.Trim(),
            LocationDisplay = location,
            DetailDisplay = string.IsNullOrWhiteSpace(sensor.Detail)
                ? (sensor.SensorModel?.Trim() ?? string.Empty)
                : sensor.Detail.Trim()
        };
    }

    private sealed class StatusResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("connected")]
        public List<SensorPayload>? Connected { get; set; }

        [JsonPropertyName("disconnected")]
        public List<SensorPayload>? Disconnected { get; set; }
    }

    private sealed class SensorPayload
    {
        [JsonPropertyName("sensorId")]
        public long SensorId { get; set; }

        [JsonPropertyName("sensorName")]
        public string? SensorName { get; set; }

        [JsonPropertyName("sensorType")]
        public string? SensorType { get; set; }

        [JsonPropertyName("sensorModel")]
        public string? SensorModel { get; set; }

        [JsonPropertyName("port")]
        public string? Port { get; set; }

        [JsonPropertyName("chamberId")]
        public long ChamberId { get; set; }

        [JsonPropertyName("detail")]
        public string? Detail { get; set; }
    }
}
