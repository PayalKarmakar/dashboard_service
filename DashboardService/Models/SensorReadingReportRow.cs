namespace DashboardService.Models;

public class SensorReadingReportRow
{
    public long ReadingId { get; set; }

    public long ChamberId { get; set; }

    public string ChamberCode { get; set; } = string.Empty;

    public string ChamberName { get; set; } = string.Empty;

    public decimal? Temperature { get; set; }

    public decimal? Humidity { get; set; }

    public decimal? CO { get; set; }

    public decimal? CO2 { get; set; }

    public decimal? O2 { get; set; }

    public DateTime RecordedAt { get; set; }

    public string RecordedDisplay => RecordedAt.ToString("dd MMM yyyy hh:mm:ss tt");

    public string TemperatureDisplay => Format(Temperature, "0.0");

    public string HumidityDisplay => Format(Humidity, "0.0");

    public string CoDisplay => Format(CO, "0.0");

    public string Co2Display => Format(CO2, "0.0");

    public string O2Display => Format(O2, "0.0");

    private static string Format(decimal? value, string format) =>
        value.HasValue ? value.Value.ToString(format) : "—";
}
