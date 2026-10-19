using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using WeatherApp.Models;
using WeatherApp.Services;

namespace WeatherApp;

// data sent with the event
public class ForecastLoadedEventArgs : EventArgs
{
    public Location Location { get; }
    public DateOnly Date { get; }
    public string Source { get; }
    public long Milliseconds { get; }

    public ForecastLoadedEventArgs(Location location, DateOnly date, string source, long milliseconds)
    {
        Location = location;
        Date = date;
        Source = source;
        Milliseconds = milliseconds;
    }
}

// search result
public record LocationSearchOutcome(Location ChosenLocation, IReadOnlyList<Location> OtherMatches, string? NoticeMessage);

// main dashboard logic
public class WeatherDashboard
{
    public const int ForecastDays = 7;

    private static readonly CultureInfo AustralianCulture = CultureInfo.GetCultureInfo("en-AU");

    private readonly IWeatherService _weatherService;

    // raised after a forecast loads
    public event EventHandler<ForecastLoadedEventArgs>? ForecastLoaded;

    public WeatherDashboard(IWeatherService weatherService)
    {
        _weatherService = weatherService;
    }

    public async Task<string> ShowForecastAsync(string? suburb, DateOnly? date)
    {
        var loadTimer = Stopwatch.StartNew();
        string? errorMessage = null;

        // find the place
        LocationSearchOutcome searchOutcome;
        try
        {
            searchOutcome = await FindLocationAsync(suburb);
        }
        catch (WeatherServiceException exception)
        {
            searchOutcome = new LocationSearchOutcome(LocationCatalog.Default, Array.Empty<Location>(), null);
            errorMessage = exception.Message;
        }
        Location location = searchOutcome.ChosenLocation;
        string? noticeMessage = searchOutcome.NoticeMessage;

        // check the date
        DateOnly firstAvailableDate = TodayAt(location);
        DateOnly lastAvailableDate = firstAvailableDate.AddDays(ForecastDays - 1);
        DateOnly selectedDate = date ?? firstAvailableDate;

        if (selectedDate < firstAvailableDate || selectedDate > lastAvailableDate)
        {
            noticeMessage = $"Forecasts are only available from {firstAvailableDate.ToString("d MMMM", AustralianCulture)} " +
                            $"to {lastAvailableDate.ToString("d MMMM yyyy", AustralianCulture)}. Showing today instead.";
            selectedDate = firstAvailableDate;
        }

        // load forecast and current reading
        IReadOnlyList<WeatherForecast> week = Array.Empty<WeatherForecast>();
        CurrentObservation? currentObservation = null;
        if (errorMessage is null)
        {
            Task<IReadOnlyList<WeatherForecast>> weekTask = _weatherService.GetRangeAsync(location, firstAvailableDate, ForecastDays);
            Task<CurrentObservation?> observationTask = TryGetCurrentObservationAsync(location);
            try
            {
                await Task.WhenAll(weekTask, observationTask);
                week = weekTask.Result;
            }
            catch (WeatherServiceException exception)
            {
                // show the error on the page
                errorMessage = exception.Message;
            }
            currentObservation = await observationTask;   // already done
        }

        loadTimer.Stop();
        if (errorMessage is null)
            ForecastLoaded?.Invoke(this, new ForecastLoadedEventArgs(location, selectedDate, _weatherService.SourceName, loadTimer.ElapsedMilliseconds));

        return BuildPage(location, selectedDate, firstAvailableDate, lastAvailableDate, week, currentObservation,
            searchOutcome.OtherMatches, noticeMessage, errorMessage);
    }

    // null if the reading fails
    private async Task<CurrentObservation?> TryGetCurrentObservationAsync(Location location)
    {
        try
        {
            return await _weatherService.GetCurrentObservationAsync(location);
        }
        catch (WeatherServiceException)
        {
            return null;
        }
    }

    // text to a place
    public async Task<LocationSearchOutcome> FindLocationAsync(string? searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
            return new LocationSearchOutcome(LocationCatalog.Default, Array.Empty<Location>(), null);

        // split name and state
        string[] nameParts = searchText.Split(',', 2, StringSplitOptions.TrimEntries);
        string wantedSuburb = nameParts[0];
        string wantedState = nameParts.Length == 2 ? nameParts[1] : "";

        // capitals need no search
        if (LocationCatalog.FindCapital(wantedSuburb) is Location capital)
            return new LocationSearchOutcome(capital, Array.Empty<Location>(), null);

        IReadOnlyList<Location> matches = await _weatherService.SearchLocationsAsync(wantedSuburb);
        if (matches.Count == 0)
        {
            return new LocationSearchOutcome(LocationCatalog.Default, Array.Empty<Location>(),
                $"No Australian location matches \"{searchText.Trim()}\". Showing {LocationCatalog.Default.DisplayName} instead.");
        }

        // best match first
        Location chosenLocation =
            matches.FirstOrDefault(match => match.Suburb.Equals(wantedSuburb, StringComparison.OrdinalIgnoreCase)
                                         && (wantedState.Length == 0 || match.State.Equals(wantedState, StringComparison.OrdinalIgnoreCase)))
            ?? matches[0];

        List<Location> otherMatches = matches.Where(match => match != chosenLocation).Take(5).ToList();
        return new LocationSearchOutcome(chosenLocation, otherMatches, null);
    }

    // hottest day and total rain
    public static (WeatherForecast? Hottest, double TotalRainMm) Summarise(IReadOnlyList<WeatherForecast> week)
    {
        WeatherForecast? hottest = week.Where(forecast => forecast.MaxTempC.HasValue).MaxBy(forecast => forecast.MaxTempC);
        double totalRain = week.Sum(forecast => forecast.RainfallMm);
        return (hottest, totalRain);
    }

    // today at that place
    public static DateOnly TodayAt(Location location)
    {
        TimeZoneInfo locationTimeZone = TimeZoneInfo.FindSystemTimeZoneById(location.TimeZoneId);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, locationTimeZone).DateTime);
    }

    // page building

    private static string Encode(string text) => WebUtility.HtmlEncode(text);

    private static string Show(double? value) =>
        value.HasValue ? value.Value.ToString("0.#", CultureInfo.InvariantCulture) : "n/a";

    private static string Show(int? value) => value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "n/a";

    private static string Temperature(double? value) => value.HasValue ? $"{Show(value)}&#176;C" : "n/a";

    private static string Percentage(int? value) => value.HasValue ? $"{value.Value}%" : "n/a";

    private static string IsoDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string DayLink(Location location, DateOnly date) =>
        $"/?suburb={Uri.EscapeDataString(location.DisplayName)}&amp;date={IsoDate(date)}";

    private string BuildPage(Location location, DateOnly selectedDate, DateOnly firstAvailableDate, DateOnly lastAvailableDate,
        IReadOnlyList<WeatherForecast> week, CurrentObservation? currentObservation, IReadOnlyList<Location> otherMatches,
        string? noticeMessage, string? errorMessage)
    {
        var html = new StringBuilder();
        html.Append("<!DOCTYPE html><html lang=\"en-AU\"><head><meta charset=\"utf-8\">");
        html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        html.Append($"<title>{Encode(location.DisplayName)} forecast | WeatherDashboard</title><style>").Append(Css).Append("</style></head><body>");

        html.Append("<header class=\"site-header\"><div class=\"inner\"><span class=\"brand\">WeatherDashboard</span>");
        html.Append("</div></header>");

        // search form
        html.Append("<section class=\"search-band\"><div class=\"inner\"><form method=\"get\" action=\"/\" class=\"search-form\">");
        html.Append("<label class=\"field field-wide\"><span>Location</span>");
        html.Append($"<input type=\"text\" name=\"suburb\" required value=\"{Encode(location.DisplayName)}\">");
        html.Append("<small>Australian town or suburb, for example Geelong or Alice Springs</small></label>");
        html.Append("<label class=\"field\"><span>Date</span>");
        html.Append($"<input type=\"date\" name=\"date\" value=\"{IsoDate(selectedDate)}\" min=\"{IsoDate(firstAvailableDate)}\" max=\"{IsoDate(lastAvailableDate)}\">");
        html.Append("</label>");
        html.Append("<button type=\"submit\">Show forecast</button></form>");
        html.Append("<p class=\"capitals\">Capital cities:");
        foreach (Location capital in LocationCatalog.All)
            html.Append($" <a href=\"/?suburb={Uri.EscapeDataString(capital.Suburb)}\">{Encode(capital.Suburb)}</a>");
        html.Append("</p></div></section>");

        html.Append("<main class=\"inner\">");
        if (noticeMessage is not null)
            html.Append($"<p class=\"notice\">{Encode(noticeMessage)}</p>");
        if (otherMatches.Count > 0)
        {
            html.Append($"<p class=\"notice\">Showing {Encode(location.DisplayName)}. Other places with a similar name:");
            foreach (Location otherMatch in otherMatches)
                html.Append($" <a href=\"{DayLink(otherMatch, selectedDate)}\">{Encode(otherMatch.DisplayName)}</a>");
            html.Append("</p>");
        }

        if (errorMessage is not null)
        {
            html.Append($"<section class=\"card error\"><h1>{Encode(location.DisplayName)}</h1><h2>No forecast available</h2>");
            html.Append($"<p>{Encode(errorMessage)}</p></section>");
        }
        else
        {
            WeatherForecast? selectedForecast = week.FirstOrDefault(forecast => forecast.Date == selectedDate);
            if (currentObservation is not null)
                AddCurrentObservation(html, location, currentObservation);
            if (selectedForecast is not null)
                AddCard(html, selectedForecast);

            AddWeek(html, week, selectedDate);
        }

        html.Append("</main><footer class=\"site-footer\"><div class=\"inner\">");
        html.Append("WeatherDashboard. Dates and times follow each location's local time.");
        html.Append("</div></footer></body></html>");
        return html.ToString();
    }

    private static void AddCurrentObservation(StringBuilder html, Location location, CurrentObservation observation)
    {
        html.Append($"<section class=\"now\"><span class=\"now-place\">{Encode(location.Suburb)}</span>");
        html.Append($"<span class=\"now-value\">{Temperature(observation.TemperatureC)}</span>");
        html.Append($"<span>Humidity {Percentage(observation.HumidityPercent)}</span>");
        if (observation.ObservedAt is DateTimeOffset observedAt)
        {
            TimeZoneInfo locationTimeZone = TimeZoneInfo.FindSystemTimeZoneById(location.TimeZoneId);
            string localTime = TimeZoneInfo.ConvertTime(observedAt, locationTimeZone).ToString("h:mm tt", AustralianCulture);
            html.Append($"<span class=\"now-time\">{localTime}</span>");
        }
        html.Append("</section>");
    }

    private static void AddCard(StringBuilder html, WeatherForecast forecast)
    {
        string longDate = forecast.Date.ToString("dddd d MMMM yyyy", AustralianCulture);

        html.Append("<section class=\"card day\">");
        html.Append($"<h1>{Encode(forecast.Location.DisplayName)}</h1>");
        html.Append($"<p class=\"date\">{longDate}</p>");
        html.Append($"<p class=\"conditions\">{Encode(forecast.Conditions)}</p>");
        html.Append("<div class=\"temperatures\">");
        html.Append($"<div><span class=\"label\">Maximum</span><span class=\"value maximum\">{Temperature(forecast.MaxTempC)}</span></div>");
        html.Append($"<div><span class=\"label\">Minimum</span><span class=\"value minimum\">{Temperature(forecast.MinTempC)}</span></div>");
        html.Append("</div><div class=\"details\">");
        html.Append($"<div><span class=\"label\">Humidity</span><span class=\"detail-value\">{Percentage(forecast.HumidityPercent)}</span></div>");
        html.Append($"<div><span class=\"label\">Rainfall</span><span class=\"detail-value\">{Show(forecast.RainfallMm)} mm</span></div>");
        html.Append("</div></section>");
    }

    private static void AddWeek(StringBuilder html, IReadOnlyList<WeatherForecast> week, DateOnly selectedDate)
    {
        if (week.Count == 0) return;

        html.Append("<section class=\"card\"><h2>Next 7 days</h2><table>");
        html.Append("<thead><tr><th>Date</th><th class=\"number\">Min</th><th class=\"number\">Max</th>");
        html.Append("<th class=\"number\">Humidity</th><th class=\"number\">Rainfall</th><th>Conditions</th></tr></thead><tbody>");
        foreach (WeatherForecast forecast in week)
        {
            string rowClass = forecast.Date == selectedDate ? " class=\"selected\"" : "";
            html.Append($"<tr{rowClass}><td><a href=\"{DayLink(forecast.Location, forecast.Date)}\">{forecast.Date.ToString("ddd d MMM", AustralianCulture)}</a></td>");
            html.Append($"<td class=\"number\">{Temperature(forecast.MinTempC)}</td><td class=\"number\">{Temperature(forecast.MaxTempC)}</td>");
            html.Append($"<td class=\"number\">{Percentage(forecast.HumidityPercent)}</td>");
            html.Append($"<td class=\"number\">{Show(forecast.RainfallMm)} mm</td>");
            html.Append($"<td>{Encode(forecast.Conditions)}</td></tr>");
        }
        html.Append("</tbody></table>");

        var (hottest, totalRain) = Summarise(week);
        html.Append("<p class=\"summary\">");
        if (hottest is not null)
            html.Append($"Hottest day: <b>{hottest.Date.ToString("dddd d MMM", AustralianCulture)}</b> ({Temperature(hottest.MaxTempC)}). ");
        html.Append($"Total rain over {week.Count} days: <b>{Show(totalRain)} mm</b>.</p>");
        html.Append("</section>");
    }

    private const string Css = """
        * { box-sizing: border-box; }
        body { margin: 0; font-family: "Segoe UI", "Helvetica Neue", Arial, sans-serif; font-size: 16px; line-height: 1.5;
               color: #15283f; background: #f2f5f8; }
        a { color: #1d5fa6; }
        .inner { max-width: 960px; margin: 0 auto; padding: 0 20px; }
        .site-header { background: #13304f; color: #fff; }
        .site-header .inner { display: flex; align-items: baseline; gap: 24px; flex-wrap: wrap; padding-top: 16px; padding-bottom: 16px; }
        .brand { font-size: 20px; font-weight: 700; }
        .search-band { background: #fff; border-bottom: 1px solid #d8e0e9; padding: 18px 0 10px; }
        .search-form { display: flex; gap: 14px; align-items: flex-start; flex-wrap: wrap; }
        .field { display: flex; flex-direction: column; gap: 4px; font-weight: 600; font-size: 15px; }
        .field-wide { flex: 1 1 300px; }
        .field small { font-weight: 400; font-size: 13px; color: #4d5e73; }
        input, button { font: inherit; padding: 9px 12px; border-radius: 6px; border: 1px solid #b9c6d4; min-height: 42px; }
        button { margin-top: 27px; background: #1d5fa6; color: #fff; border: none; font-weight: 600; padding: 9px 20px; cursor: pointer; }
        button:hover { background: #164b84; }
        .capitals { margin: 12px 0 0; font-size: 15px; color: #4d5e73; }
        .capitals a { margin-left: 12px; }
        main.inner { padding-top: 20px; padding-bottom: 40px; }
        .notice { background: #e9f1fa; border-radius: 6px; padding: 10px 14px; margin: 0 0 16px; }
        .notice a { margin-left: 10px; white-space: nowrap; }
        .card { background: #fff; border: 1px solid #d8e0e9; border-radius: 6px; padding: 22px 26px; margin-bottom: 20px; }
        .card h1 { margin: 0; font-size: 28px; }
        .card h2 { margin: 0 0 8px; font-size: 19px; }
        .day { border-top: 4px solid #b8661a; }
        .date { margin: 2px 0 0; color: #4d5e73; font-size: 17px; }
        .conditions { margin: 16px 0 4px; font-size: 20px; font-weight: 600; }
        .temperatures { display: flex; gap: 40px; flex-wrap: wrap; }
        .temperatures div, .details div { display: flex; flex-direction: column; }
        .label { font-size: 14px; color: #4d5e73; }
        .value { font-size: 54px; font-weight: 700; line-height: 1.05; }
        .maximum { color: #b8661a; }
        .minimum { color: #2a7c96; }
        .details { display: flex; gap: 40px; margin-top: 18px; padding-top: 12px; border-top: 1px solid #e8edf3; }
        .detail-value { font-size: 22px; font-weight: 600; }
        .note { margin: 16px 0 0; color: #4d5e73; font-size: 13px; }
        .error { border-left: 5px solid #a3271f; }
        .now { display: flex; align-items: center; gap: 24px; flex-wrap: wrap; background: #13304f; color: #fff;
               border-radius: 6px; padding: 12px 22px; margin-bottom: 16px; }
        .now-place { font-size: 17px; font-weight: 600; }
        .now-value { font-size: 26px; font-weight: 700; }
        .now-time { color: #c9d6e6; font-size: 14px; }
        .error h2 { color: #a3271f; margin-top: 12px; }
        table { width: 100%; border-collapse: collapse; font-size: 15px; }
        th, td { text-align: left; padding: 9px 8px; border-bottom: 1px solid #e8edf3; white-space: nowrap; }
        th { color: #4d5e73; font-weight: 600; border-bottom-color: #d8e0e9; }
        .number { text-align: right; }
        tr.selected td { background: #f6efe6; font-weight: 600; }
        .summary { margin: 14px 0 0; }
        .site-footer { border-top: 1px solid #d8e0e9; background: #fff; color: #4d5e73; font-size: 14px; padding: 16px 0; text-align: left; }
        @media (max-width: 640px) {
            .value { font-size: 42px; }
            button { margin-top: 0; width: 100%; }
            th:nth-child(4), td:nth-child(4), th:nth-child(6), td:nth-child(6) { display: none; }
        }
        """;
}
