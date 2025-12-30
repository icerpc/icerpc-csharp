// Copyright (c) ZeroC, Inc.

using CurrentWeatherClient;
using IceRpc;

// The geographical name we're searching for.
string geoName = args.Length > 0 ? args[0] : "Jupiter"; // ZeroC's home town

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

// The client for the GeoCoding API.
var geoClient = new GeoClient(connection);

// The client for the Weather Forecast API.
var forecastClient = new ForecastClient(connection);

// Use the GeoCoding API to find locations that match the specified name.
GeoCodingResponse geoCodingResponse = await geoClient.SearchAsync(geoName, count: 3);
if (geoCodingResponse.Results is null || geoCodingResponse.Results.Count == 0)
{
    Console.WriteLine($"No location found for '{geoName}'.");
    await connection.ShutdownAsync();
    return 1;
}

Console.WriteLine($"Current weather conditions for {geoName}:");
foreach (LocationInfo location in geoCodingResponse.Results)
{
    // Use the latitude and longitude in location to find the current weather conditions.
    CurrentWeatherResponse currentWeatherResponse =
        await forecastClient.GetCurrentWeatherAsync(location.Latitude, location.Longitude);

    // Display the current weather conditions.
    CurrentWeather current = currentWeatherResponse.Current;
    Console.WriteLine($"{location.Name}, {location.Admin1}, {location.Country}: {current.Temperature}°C {current.RelativeHumidity}% RH");
}

await connection.ShutdownAsync();
return 0;
