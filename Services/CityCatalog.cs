namespace TrayClock.Services;

/// <summary>A city and the Windows time zone it observes.</summary>
internal sealed record City(string Name, string ZoneId)
{
    public TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById(ZoneId);
}

/// <summary>Curated list of world cities. Zones missing on this machine are dropped.</summary>
internal static class CityCatalog
{
    private static readonly City[] Candidates =
    [
        new("Amsterdam", "W. Europe Standard Time"), new("Anchorage", "Alaskan Standard Time"),
        new("Athens", "GTB Standard Time"), new("Auckland", "New Zealand Standard Time"),
        new("Bangkok", "SE Asia Standard Time"), new("Berlin", "W. Europe Standard Time"),
        new("Bogotá", "SA Pacific Standard Time"), new("Buenos Aires", "Argentina Standard Time"),
        new("Cairo", "Egypt Standard Time"), new("Chicago", "Central Standard Time"),
        new("Delhi", "India Standard Time"), new("Denver", "Mountain Standard Time"),
        new("Dhaka", "Bangladesh Standard Time"), new("Dubai", "Arabian Standard Time"),
        new("Dublin", "GMT Standard Time"), new("Helsinki", "FLE Standard Time"),
        new("Hong Kong", "China Standard Time"), new("Honolulu", "Hawaiian Standard Time"),
        new("Istanbul", "Turkey Standard Time"), new("Jakarta", "SE Asia Standard Time"),
        new("Johannesburg", "South Africa Standard Time"), new("Karachi", "Pakistan Standard Time"),
        new("Kathmandu", "Nepal Standard Time"), new("Lagos", "W. Central Africa Standard Time"),
        new("Lisbon", "GMT Standard Time"), new("London", "GMT Standard Time"),
        new("Los Angeles", "Pacific Standard Time"), new("Madrid", "Romance Standard Time"),
        new("Manila", "Singapore Standard Time"), new("Mexico City", "Central Standard Time (Mexico)"),
        new("Moscow", "Russian Standard Time"), new("Nairobi", "E. Africa Standard Time"),
        new("New York", "Eastern Standard Time"), new("Paris", "Romance Standard Time"),
        new("Perth", "W. Australia Standard Time"), new("Rome", "W. Europe Standard Time"),
        new("São Paulo", "E. South America Standard Time"), new("Seoul", "Korea Standard Time"),
        new("Shanghai", "China Standard Time"), new("Singapore", "Singapore Standard Time"),
        new("Stockholm", "W. Europe Standard Time"), new("Sydney", "AUS Eastern Standard Time"),
        new("Tehran", "Iran Standard Time"), new("Tokyo", "Tokyo Standard Time"),
        new("Toronto", "Eastern Standard Time"), new("Vancouver", "Pacific Standard Time"),
    ];

    public static IReadOnlyList<City> All { get; } = Candidates.Where(IsAvailable).ToList();

    public static City? Find(string name) => All.FirstOrDefault(c => c.Name == name);

    private static bool IsAvailable(City city)
    {
        try
        {
            _ = city.Zone;
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }
}
