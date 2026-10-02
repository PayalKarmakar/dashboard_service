namespace DashboardService.Models;

public static class DoorLineOrientation
{
    public const string Horizontal = "HORIZONTAL";
    public const string Vertical = "VERTICAL";
    public const string Diagonal = "DIAGONAL";

    public static string Normalize(string? value)
    {
        string v = (value ?? Horizontal).Trim().ToUpperInvariant();
        return v switch
        {
            Vertical => Vertical,
            Diagonal or "CORNER" or "CORNER_WISE" => Diagonal,
            _ => Horizontal
        };
    }
}
