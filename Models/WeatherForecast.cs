namespace WeatherApp.Models;

// one day's forecast
public record WeatherForecast(
    Location Location,
    DateOnly Date,
    double? MinTempC,
    double? MaxTempC,
    int? HumidityPercent,
    double RainfallMm,
    string Source)
{
    // rain amount as words
    public string Conditions => RainfallMm switch
    {
        < 0.1 => "Dry",
        < 2   => "Light rain",
        < 10  => "Showers",
        _     => "Heavy rain"
    };
}
