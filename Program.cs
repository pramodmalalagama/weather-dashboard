using WeatherApp;
using WeatherApp.Models;
using WeatherApp.Services;

var builder = WebApplication.CreateBuilder(args);

// pick live or sample data
string weatherSource = builder.Configuration["WeatherSource"] ?? "Bom";

if (weatherSource.Equals("Mock", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IWeatherService, MockWeatherService>();
}
else
{
    builder.Services.AddHttpClient<IWeatherService, BomWeatherService>(httpClient =>
    {
        httpClient.BaseAddress = new Uri("https://api.weather.bom.gov.au/v1/");
        httpClient.Timeout = TimeSpan.FromSeconds(10);
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WeatherDashboard-StudentProject/1.0");
    });
}

builder.Services.AddSingleton<WeatherDashboard>();

var app = builder.Build();

// log each forecast
var dashboard = app.Services.GetRequiredService<WeatherDashboard>();
dashboard.ForecastLoaded += (sender, eventDetails) =>
    Console.WriteLine($"[forecast loaded] {eventDetails.Location.DisplayName} {eventDetails.Date:yyyy-MM-dd} " +
                      $"from {eventDetails.Source} in {eventDetails.Milliseconds} ms");

// main page
app.MapGet("/", async (string? suburb, DateOnly? date, WeatherDashboard weatherDashboard) =>
{
    string html = await weatherDashboard.ShowForecastAsync(suburb, date);
    return Results.Content(html, "text/html; charset=utf-8");
});

// forecast as JSON
app.MapGet("/api/forecast", async (string? suburb, DateOnly? date, WeatherDashboard weatherDashboard, IWeatherService weatherService) =>
{
    try
    {
        LocationSearchOutcome searchOutcome = await weatherDashboard.FindLocationAsync(suburb);
        Location place = searchOutcome.ChosenLocation;
        DateOnly forecastDate = date ?? WeatherDashboard.TodayAt(place);
        return Results.Ok(await weatherService.GetForecastAsync(place, forecastDate));
    }
    catch (WeatherServiceException exception)
    {
        return Results.Problem(exception.Message, statusCode: 502);
    }
});

// language tests
app.MapGet("/lab", () => Results.Text(LanguageLab.Run(), "text/plain; charset=utf-8"));

app.Run();
