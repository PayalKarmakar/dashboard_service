using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace DashboardService.Services;

public class EmployeeRegistrationApiService
{
    private readonly ConfigurationService _configurationService = new();

    public async Task<string> WaitForCardScanAsync(
        long readerId,
        CancellationToken cancellationToken)
    {
        string baseUrl = _configurationService.GetRfidServiceBaseUrl();

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(90)
        };

        using var response = await httpClient.PostAsync(
            $"/api/employee-registration/scan?readerId={readerId}",
            content: null,
            cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<ScanResponse>(
            cancellationToken: cancellationToken);

        if (response.IsSuccessStatusCode
            && payload?.Success == true
            && !string.IsNullOrWhiteSpace(payload.CardUid))
        {
            return payload.CardUid.Trim();
        }

        string message = payload?.Message
            ?? $"RFID scan failed ({(int)response.StatusCode}).";

        if ((int)response.StatusCode == 408 || response.StatusCode == System.Net.HttpStatusCode.RequestTimeout)
        {
            throw new TimeoutException(message);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            throw new InvalidOperationException(message);
        }

        throw new Exception(message);
    }

    private sealed class ScanResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("cardUid")]
        public string? CardUid { get; set; }
    }
}
