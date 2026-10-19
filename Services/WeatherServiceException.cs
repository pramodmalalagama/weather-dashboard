namespace WeatherApp.Services;

// our own error type
public class WeatherServiceException : Exception
{
    public WeatherServiceException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
