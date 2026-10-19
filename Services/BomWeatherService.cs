using System.Text.Json;
using System.Text.Json.Serialization;
using WeatherApp.Models;

namespace WeatherApp.Services;

// gets live data from the BOM
public class BomWeatherService : IWeatherService
{
    private readonly HttpClient _httpClient;

    public string SourceName => "Bureau of Meteorology (live)";

    public BomWeatherService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<WeatherForecast> GetForecastAsync(Location location, DateOnly date)
    {
        IReadOnlyList<WeatherForecast> forecastsForDay = await GetRangeAsync(location, date, 1);
        return forecastsForDay[0];
    }

    public async Task<IReadOnlyList<WeatherForecast>> GetRangeAsync(Location location, DateOnly startDate, int numberOfDays = 7)
    {
        try
        {
            // start all requests together
            Task<string> dailyResponseTask = _httpClient.GetStringAsync($"locations/{location.Geohash}/forecasts/daily");
            Task<string> hourlyResponseTask = _httpClient.GetStringAsync($"locations/{location.Geohash}/forecasts/hourly");
            Task<string?> threeHourlyResponseTask = TryGetStringAsync($"locations/{location.Geohash}/forecasts/3-hourly");
            await Task.WhenAll(new Task[] { dailyResponseTask, hourlyResponseTask, threeHourlyResponseTask });

            TimeZoneInfo locationTimeZone = TimeZoneInfo.FindSystemTimeZoneById(location.TimeZoneId);
            Dictionary<DateOnly, int> humidityByDate = ParseHourlyHumidity(hourlyResponseTask.Result, locationTimeZone);

            // add humidity for later days
            if (threeHourlyResponseTask.Result is string threeHourlyText)
            {
                foreach (var (forecastDate, averageHumidity) in ParseHourlyHumidity(threeHourlyText, locationTimeZone))
                    humidityByDate.TryAdd(forecastDate, averageHumidity);
            }
            Dictionary<DateOnly, double> lowestTemperatureByDate = ParseHourlyLowestTemperature(hourlyResponseTask.Result, locationTimeZone);
            List<WeatherForecast> allForecasts = ParseDaily(dailyResponseTask.Result, location, locationTimeZone,
                humidityByDate, lowestTemperatureByDate);

            List<WeatherForecast> requestedForecasts = allForecasts
                .Where(forecast => forecast.Date >= startDate && forecast.Date < startDate.AddDays(numberOfDays))
                .ToList();

            if (requestedForecasts.Count == 0)
                throw new WeatherServiceException("There is no forecast for those dates. Forecasts only cover today and the next six days.");

            return requestedForecasts;
        }
        catch (HttpRequestException exception)
        {
            throw new WeatherServiceException("Could not reach the weather service. Check your internet connection and try again.", exception);
        }
        catch (TaskCanceledException exception)
        {
            throw new WeatherServiceException("The weather service took too long to answer. Try again in a moment.", exception);
        }
        catch (JsonException exception)
        {
            throw new WeatherServiceException("The weather data was not in the expected format.", exception);
        }
    }

    // returns null if it fails
    private async Task<string?> TryGetStringAsync(string requestPath)
    {
        try
        {
            return await _httpClient.GetStringAsync(requestPath);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    public async Task<CurrentObservation> GetCurrentObservationAsync(Location location)
    {
        try
        {
            string responseText = await _httpClient.GetStringAsync($"locations/{location.Geohash}/observations");
            ObservationResponse observationResponse = JsonSerializer.Deserialize<ObservationResponse>(responseText)
                ?? throw new WeatherServiceException("No current reading is available.");
            ObservationData observation = observationResponse.Data
                ?? throw new WeatherServiceException("No current reading is available.");

            return new CurrentObservation(observation.Temperature, observation.Humidity,
                observation.Station?.Name, observationResponse.Metadata?.ObservationTime);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new WeatherServiceException("The current temperature could not be loaded.", exception);
        }
    }

    public async Task<IReadOnlyList<Location>> SearchLocationsAsync(string searchText)
    {
        // needs 3 or more letters
        if (string.IsNullOrWhiteSpace(searchText) || searchText.Trim().Length < 3)
            return Array.Empty<Location>();

        try
        {
            string responseText = await _httpClient.GetStringAsync($"locations?search={Uri.EscapeDataString(searchText.Trim())}");
            LocationSearchResponse searchResponse = JsonSerializer.Deserialize<LocationSearchResponse>(responseText)
                ?? throw new WeatherServiceException("The location search returned no data.");

            // keep 6 geohash characters
            return searchResponse.Data
                .Where(searchItem => searchItem.Geohash is { Length: >= 6 } && !string.IsNullOrEmpty(searchItem.Name))
                .Select(searchItem => new Location(searchItem.Name!, searchItem.State ?? "", searchItem.Geohash![..6],
                    Location.TimeZoneForState(searchItem.State ?? "")))
                .DistinctBy(location => location.DisplayName)
                .Take(10)
                .ToList();
        }
        catch (HttpRequestException exception)
        {
            throw new WeatherServiceException("Could not search for the location because the weather service could not be reached. Check your internet connection.", exception);
        }
        catch (TaskCanceledException exception)
        {
            throw new WeatherServiceException("The location search took too long. Try again in a moment.", exception);
        }
        catch (JsonException exception)
        {
            throw new WeatherServiceException("The location search result was not in the format we expected.", exception);
        }
    }

    // read the daily JSON
    public static List<WeatherForecast> ParseDaily(
        string jsonText, Location location, TimeZoneInfo locationTimeZone,
        IReadOnlyDictionary<DateOnly, int> humidityByDate, IReadOnlyDictionary<DateOnly, double> lowestTemperatureByDate)
    {
        DailyResponse dailyResponse = JsonSerializer.Deserialize<DailyResponse>(jsonText)
            ?? throw new WeatherServiceException("The weather service sent an empty answer.");

        var forecasts = new List<WeatherForecast>();
        foreach (DailyItem dailyItem in dailyResponse.Data)
        {
            // UTC to local date
            DateOnly forecastDate = ToLocalDate(dailyItem.Date, locationTimeZone);

            // middle of the rain range
            double rainfall = 0;
            if (dailyItem.Rain?.Amount is { } rainAmount)
                rainfall = ((rainAmount.Minimum ?? 0) + (rainAmount.Maximum ?? rainAmount.Minimum ?? 0)) / 2.0;

            int? humidityPercent = humidityByDate.TryGetValue(forecastDate, out int averageHumidity) ? averageHumidity : null;

            // use hourly low if min is missing
            double? minimumTemperature = dailyItem.TempMin
                ?? (lowestTemperatureByDate.TryGetValue(forecastDate, out double lowestHourly) ? lowestHourly : null);

            forecasts.Add(new WeatherForecast(location, forecastDate, minimumTemperature, dailyItem.TempMax,
                humidityPercent, rainfall, "Bureau of Meteorology (live)"));
        }

        // fill missing humidity with the average
        List<int> knownHumidity = forecasts
            .Where(forecast => forecast.HumidityPercent.HasValue)
            .Select(forecast => forecast.HumidityPercent!.Value)
            .ToList();

        if (knownHumidity.Count > 0)
        {
            int estimatedHumidity = (int)Math.Round(knownHumidity.Average());
            for (int dayIndex = 0; dayIndex < forecasts.Count; dayIndex++)
            {
                if (forecasts[dayIndex].HumidityPercent is null)
                    forecasts[dayIndex] = forecasts[dayIndex] with { HumidityPercent = estimatedHumidity };
            }
        }
        return forecasts;
    }

    // average humidity per day
    public static Dictionary<DateOnly, int> ParseHourlyHumidity(string jsonText, TimeZoneInfo locationTimeZone)
    {
        HourlyResponse hourlyResponse = JsonSerializer.Deserialize<HourlyResponse>(jsonText)
            ?? throw new WeatherServiceException("The weather service sent an empty answer.");

        return hourlyResponse.Data
            .Where(hourlyItem => hourlyItem.RelativeHumidity.HasValue)
            .GroupBy(hourlyItem => ToLocalDate(hourlyItem.Time, locationTimeZone))
            .ToDictionary(
                dayGroup => dayGroup.Key,
                dayGroup => (int)Math.Round(dayGroup.Average(hourlyItem => hourlyItem.RelativeHumidity!.Value)));
    }

    // lowest temperature per day
    public static Dictionary<DateOnly, double> ParseHourlyLowestTemperature(string jsonText, TimeZoneInfo locationTimeZone)
    {
        HourlyResponse hourlyResponse = JsonSerializer.Deserialize<HourlyResponse>(jsonText)
            ?? throw new WeatherServiceException("The weather service sent an empty answer.");

        return hourlyResponse.Data
            .Where(hourlyItem => hourlyItem.Temperature.HasValue)
            .GroupBy(hourlyItem => ToLocalDate(hourlyItem.Time, locationTimeZone))
            .ToDictionary(dayGroup => dayGroup.Key, dayGroup => dayGroup.Min(hourlyItem => hourlyItem.Temperature!.Value));
    }

    private static DateOnly ToLocalDate(DateTimeOffset utcTime, TimeZoneInfo locationTimeZone)
    {
        DateTimeOffset localTime = TimeZoneInfo.ConvertTime(utcTime, locationTimeZone);
        return DateOnly.FromDateTime(localTime.DateTime);
    }

    // JSON classes
    private class DailyResponse
    {
        [JsonPropertyName("data")] public List<DailyItem> Data { get; set; } = new();
    }

    private class DailyItem
    {
        [JsonPropertyName("date")] public DateTimeOffset Date { get; set; }
        [JsonPropertyName("temp_max")] public double? TempMax { get; set; }
        [JsonPropertyName("temp_min")] public double? TempMin { get; set; }
        [JsonPropertyName("rain")] public RainInfo? Rain { get; set; }
    }

    private class RainInfo
    {
        [JsonPropertyName("amount")] public RainAmount? Amount { get; set; }
    }

    private class RainAmount
    {
        [JsonPropertyName("min")] public double? Minimum { get; set; }
        [JsonPropertyName("max")] public double? Maximum { get; set; }
    }

    private class HourlyResponse
    {
        [JsonPropertyName("data")] public List<HourlyItem> Data { get; set; } = new();
    }

    private class HourlyItem
    {
        [JsonPropertyName("time")] public DateTimeOffset Time { get; set; }
        [JsonPropertyName("relative_humidity")] public int? RelativeHumidity { get; set; }
        [JsonPropertyName("temp")] public double? Temperature { get; set; }
    }

    private class ObservationResponse
    {
        [JsonPropertyName("data")] public ObservationData? Data { get; set; }
        [JsonPropertyName("metadata")] public ObservationMetadata? Metadata { get; set; }
    }

    private class ObservationMetadata
    {
        [JsonPropertyName("observation_time")] public DateTimeOffset? ObservationTime { get; set; }
    }

    private class ObservationData
    {
        [JsonPropertyName("temp")] public double? Temperature { get; set; }
        [JsonPropertyName("humidity")] public int? Humidity { get; set; }
        [JsonPropertyName("station")] public StationInfo? Station { get; set; }
    }

    private class StationInfo
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    private class LocationSearchResponse
    {
        [JsonPropertyName("data")] public List<LocationSearchItem> Data { get; set; } = new();
    }

    private class LocationSearchItem
    {
        [JsonPropertyName("geohash")] public string? Geohash { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("state")] public string? State { get; set; }
    }
}
