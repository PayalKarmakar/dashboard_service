using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DashboardService.Models;

namespace DashboardService.Services;

public class RfidReaderStatusService
{
    private readonly ConfigurationService _configurationService = new();

    public async Task<RfidReaderStatusSnapshot> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        string baseUrl = _configurationService.GetRfidServiceBaseUrl();

        try
        {
            using var httpClient = new HttpClient
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromSeconds(5)
            };

            using var response = await httpClient.GetAsync(
                "/api/readers/status",
                cancellationToken);

            var payload = await response.Content.ReadFromJsonAsync<StatusResponse>(
                cancellationToken: cancellationToken);

            if (!response.IsSuccessStatusCode || payload?.Success != true)
            {
                return new RfidReaderStatusSnapshot
                {
                    ServiceAvailable = false,
                    Message = payload?.Message
                        ?? $"RFID service returned {(int)response.StatusCode}."
                };
            }

            return new RfidReaderStatusSnapshot
            {
                ServiceAvailable = true,
                Connected = payload.Connected?
                    .Select(MapReader)
                    .ToList()
                    ?? [],
                Disconnected = payload.Disconnected?
                    .Select(MapReader)
                    .ToList()
                    ?? []
            };
        }
        catch (TaskCanceledException)
        {
            return new RfidReaderStatusSnapshot
            {
                ServiceAvailable = false,
                Message = "RFID service did not respond in time."
            };
        }
        catch (Exception ex)
        {
            return new RfidReaderStatusSnapshot
            {
                ServiceAvailable = false,
                Message = $"RFID service is not reachable ({ex.Message})."
            };
        }
    }

    private static RfidReaderLiveStatus MapReader(ReaderPayload reader)
    {
        return new RfidReaderLiveStatus
        {
            ReaderId = reader.ReaderId,
            ReaderName = reader.ReaderName?.Trim() ?? string.Empty,
            IpAddress = reader.IpAddress?.Trim() ?? string.Empty,
            Port = reader.Port,
            ReaderPurpose = reader.ReaderPurpose?.Trim() ?? string.Empty
        };
    }

    private sealed class StatusResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("connected")]
        public List<ReaderPayload>? Connected { get; set; }

        [JsonPropertyName("disconnected")]
        public List<ReaderPayload>? Disconnected { get; set; }
    }

    private sealed class ReaderPayload
    {
        [JsonPropertyName("readerId")]
        public long ReaderId { get; set; }

        [JsonPropertyName("readerName")]
        public string? ReaderName { get; set; }

        [JsonPropertyName("ipAddress")]
        public string? IpAddress { get; set; }

        [JsonPropertyName("port")]
        public int Port { get; set; }

        [JsonPropertyName("readerPurpose")]
        public string? ReaderPurpose { get; set; }
    }
}
