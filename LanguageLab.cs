using System.Text;
using System.Text.Json;

namespace WeatherApp;

// two C# problem tests, open /lab
public static class LanguageLab
{
    // problem 1: null

    // null! hides the warning
    private class NaiveDto
    {
        public string Icon { get; set; } = null!;
    }

    // required makes it fail early
    private class StricterDto
    {
        public required string Icon { get; set; }
    }

    public static string Run()
    {
        var outputText = new StringBuilder();
        outputText.AppendLine("PROBLEM 1 - null values slip past the compiler");
        outputText.AppendLine("JSON from the BOM with the 'icon' value missing: {}");

        // no warning but null
        NaiveDto naiveResult = JsonSerializer.Deserialize<NaiveDto>("{}")!;
        try
        {
            int iconLength = naiveResult.Icon.Length;
            outputText.AppendLine($"  Icon length = {iconLength}");
        }
        catch (NullReferenceException)
        {
            outputText.AppendLine("  NullReferenceException, even though the type says string (not string?)");
        }

        // required catches it
        try
        {
            JsonSerializer.Deserialize<StricterDto>("{}");
        }
        catch (JsonException)
        {
            outputText.AppendLine("  StricterDto: JsonException thrown straight away (better, but optional)");
        }

        outputText.AppendLine();
        outputText.AppendLine("PROBLEM 2 - DateTime quietly changes the time zone");

        // 16:00 UTC is next day in Perth
        string bomStyleTime = "2026-10-13T16:00:00Z";
        TimeZoneInfo perthTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Australia/Perth");

        DateTime naiveTime = DateTime.Parse(bomStyleTime);
        DateOnly naiveDate = DateOnly.FromDateTime(naiveTime);

        DateTimeOffset timeWithOffset = DateTimeOffset.Parse(bomStyleTime);
        DateOnly correctDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeWithOffset, perthTimeZone).DateTime);

        outputText.AppendLine($"  This computer's time zone: {TimeZoneInfo.Local.Id}");
        outputText.AppendLine($"  DateTime.Parse gives: {naiveTime:yyyy-MM-dd HH:mm} (Kind = {naiveTime.Kind}) -> date {naiveDate:yyyy-MM-dd}");
        outputText.AppendLine($"  Date in Perth should be: {correctDate:yyyy-MM-dd}");
        outputText.AppendLine(naiveDate == correctDate
            ? "  Same date here, but only because of this computer's time zone."
            : "  Different dates - the forecast would be shown on the wrong day.");

        return outputText.ToString();
    }
}
