namespace logirack.Services;

public record Coordinates(double Latitude, double Longitude);

public interface ICoordinateService
{
    Task<Coordinates> GetCoordinatesForCityAsync(string? city);
    Coordinates GetCoordinatesForCity(string? city);
}
