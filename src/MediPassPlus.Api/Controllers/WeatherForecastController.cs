using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.Controllers;

/// <summary>
/// Sample controller from the ASP.NET Core template. It exists so the pipeline, OpenAPI document and Swagger UI can be
/// exercised before the MediPass+ modules exist; delete it when the first real controller lands.
/// </summary>
[ApiController]
[Route("[controller]")]
public class WeatherForecastController : ControllerBase
{
    private static readonly string[] Summaries =
    [
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    ];

    /// <summary>
    /// Returns five days of sample forecast data.
    /// </summary>
    /// <returns>A collection of <see cref="WeatherForecast"/> values.</returns>
    /// <response code="200">The sample forecast was generated.</response>
    [HttpGet(Name = "GetWeatherForecast")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(IEnumerable<WeatherForecast>), StatusCodes.Status200OK)]
    public IEnumerable<WeatherForecast> Get()
    {
        return Enumerable.Range(1, 5).Select(index => new WeatherForecast
        {
            Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            TemperatureC = Random.Shared.Next(-20, 55),
            Summary = Summaries[Random.Shared.Next(Summaries.Length)]
        })
        .ToArray();
    }
}
