namespace MediPassPlus.Api;

/// <summary>
/// Sample payload returned by <see cref="Controllers.WeatherForecastController"/>. Placeholder scaffolding; not
/// part of the MediPass+ domain.
/// </summary>
public class WeatherForecast
{
    /// <summary>Date the forecast applies to.</summary>
    public DateOnly Date { get; set; }

    /// <summary>Temperature in degrees Celsius.</summary>
    public int TemperatureC { get; set; }

    /// <summary>Temperature in degrees Fahrenheit, derived from <see cref="TemperatureC"/>.</summary>
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);

    /// <summary>Short textual description of the conditions.</summary>
    public string? Summary { get; set; }
}
