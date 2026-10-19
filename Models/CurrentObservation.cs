namespace WeatherApp.Models;

// current reading at the nearest station
public record CurrentObservation(
    double? TemperatureC,
    int? HumidityPercent,
    string? StationName,
    DateTimeOffset? ObservedAt);
