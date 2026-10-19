using WeatherApp.Models;

namespace WeatherApp.Services;

// what every weather source must do
public interface IWeatherService
{
    string SourceName { get; }

    // one day
    Task<WeatherForecast> GetForecastAsync(Location location, DateOnly date);

    // several days
    Task<IReadOnlyList<WeatherForecast>> GetRangeAsync(Location location, DateOnly startDate, int numberOfDays = 7);

    // current reading
    Task<CurrentObservation> GetCurrentObservationAsync(Location location);

    // search places
    Task<IReadOnlyList<Location>> SearchLocationsAsync(string searchText);
}
