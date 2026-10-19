using WeatherApp.Models;

namespace WeatherApp.Services;

// sample data for offline testing
public class MockWeatherService : IWeatherService
{
    public string SourceName => "Sample data (mock service)";

    // average temps per city
    private static readonly Dictionary<string, (double Minimum, double Maximum)> AverageTemperatures = new()
    {
        ["Perth"] = (13, 25),
        ["Sydney"] = (14, 22),
        ["Melbourne"] = (10, 20),
        ["Brisbane"] = (16, 27),
        ["Adelaide"] = (11, 23),
        ["Hobart"] = (8, 17),
        ["Darwin"] = (24, 33),
        ["Canberra"] = (6, 20),
    };

    public Task<WeatherForecast> GetForecastAsync(Location location, DateOnly date)
    {
        return Task.FromResult(BuildForecast(location, date));
    }

    public async Task<IReadOnlyList<WeatherForecast>> GetRangeAsync(Location location, DateOnly startDate, int numberOfDays = 7)
    {
        // run all days together
        IEnumerable<Task<WeatherForecast>> forecastTasks = Enumerable.Range(0, numberOfDays)
            .Select(dayOffset => GetForecastAsync(location, startDate.AddDays(dayOffset)));
        return await Task.WhenAll(forecastTasks);
    }

    public Task<CurrentObservation> GetCurrentObservationAsync(Location location)
    {
        // halfway between min and max
        WeatherForecast today = BuildForecast(location, DateOnly.FromDateTime(DateTime.UtcNow));
        double currentTemperature = Math.Round(((today.MinTempC ?? 15) + (today.MaxTempC ?? 25)) / 2, 1);
        return Task.FromResult(new CurrentObservation(currentTemperature, today.HumidityPercent, "Sample station", DateTimeOffset.UtcNow));
    }

    public Task<IReadOnlyList<Location>> SearchLocationsAsync(string searchText)
    {
        // only capitals
        IReadOnlyList<Location> matchingCapitals = LocationCatalog.All
            .Where(capital => capital.Suburb.Contains(searchText.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        return Task.FromResult(matchingCapitals);
    }

    private WeatherForecast BuildForecast(Location location, DateOnly date)
    {
        var randomGenerator = new Random(MakeSeed(location.Suburb, date));

        if (!AverageTemperatures.TryGetValue(location.Suburb, out var averageTemperature))
            averageTemperature = (15, 25);

        // warmer in summer
        double seasonalAdjustment = Math.Cos((date.DayOfYear - 15) / 365.0 * 2 * Math.PI) * 4;

        double minimumTemperature = Math.Round(averageTemperature.Minimum + seasonalAdjustment + randomGenerator.NextDouble() * 4 - 2, 1);
        double maximumTemperature = Math.Round(averageTemperature.Maximum + seasonalAdjustment + randomGenerator.NextDouble() * 4 - 2, 1);

        double rainfall = randomGenerator.NextDouble() < 0.3 ? Math.Round(randomGenerator.NextDouble() * 12, 1) : 0;
        int humidity = randomGenerator.Next(35, 80) + (rainfall > 0 ? 15 : 0);

        return new WeatherForecast(location, date, minimumTemperature, maximumTemperature, humidity, rainfall, SourceName);
    }

    // same seed every run
    private static int MakeSeed(string suburb, DateOnly date)
    {
        int seed = date.DayNumber;
        foreach (char character in suburb)
            seed = unchecked(seed * 31 + character);
        return seed;
    }
}
