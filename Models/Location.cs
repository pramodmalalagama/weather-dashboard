namespace WeatherApp.Models;

// a place in Australia
public record Location(
    string Suburb,
    string State,
    string Geohash,
    string TimeZoneId)
{
    // e.g. Geelong, VIC
    public string DisplayName => $"{Suburb}, {State}";

    // time zone for each state
    public static string TimeZoneForState(string stateCode) => stateCode.ToUpperInvariant() switch
    {
        "WA" => "Australia/Perth",
        "SA" => "Australia/Adelaide",
        "NT" => "Australia/Darwin",
        "QLD" => "Australia/Brisbane",
        "VIC" => "Australia/Melbourne",
        "TAS" => "Australia/Hobart",
        "NSW" or "ACT" => "Australia/Sydney",
        _ => "Australia/Sydney"
    };
}

// capital city links
public static class LocationCatalog
{
    public static readonly IReadOnlyList<Location> All = new List<Location>
    {
        new("Perth",     "WA",  "qd66hr", "Australia/Perth"),
        new("Sydney",    "NSW", "r3gx2f", "Australia/Sydney"),
        new("Melbourne", "VIC", "r1r0fs", "Australia/Melbourne"),
        new("Brisbane",  "QLD", "r7hgdp", "Australia/Brisbane"),
        new("Adelaide",  "SA",  "r1f93c", "Australia/Adelaide"),
        new("Hobart",    "TAS", "r22u09", "Australia/Hobart"),
        new("Darwin",    "NT",  "qvv117", "Australia/Darwin"),
        new("Canberra",  "ACT", "r3dp39", "Australia/Sydney"),
    };

    // first page shows Perth
    public static Location Default => All[0];

    // null if not a capital
    public static Location? FindCapital(string? suburb) =>
        All.FirstOrDefault(capital => capital.Suburb.Equals(suburb?.Trim(), StringComparison.OrdinalIgnoreCase));
}
