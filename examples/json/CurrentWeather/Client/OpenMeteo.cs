// Copyright (c) ZeroC, Inc.

// We use reflection to deserialize JSON into the types below.
#pragma warning disable CA1812 // Avoid uninstantiated internal classes.

using System.Text.Json.Serialization;

namespace OpenMeteo;

/// <summary>The response returned by a successful GET on the OpenMeteo GeoCoding API.</summary>
internal record struct GeoCodingResponse
{
    public List<LocationInfo>? Results { get; set; }
}

/// <summary>A subset of the location details returned by the GeoCoding API.</summary>
internal record struct LocationInfo
{
    public required string Name { get; set; }

    public string? Country { get; set; }

    public string? Admin1 { get; set; }

    public double Latitude { get; set; }

    public double Longitude { get; set; }
}

/// <summary>The response returned by a successful GET on the OpenMeteo Weather Forecast API.</summary>
internal record struct CurrentWeatherResponse
{
    public required CurrentWeather Current { get; set; }
}

/// <summary>A subset of the current weather conditions returned by the Weather Forecast API.</summary>
internal record struct CurrentWeather
{
    [JsonPropertyName("temperature_2m")]
    public double Temperature { get; set; }

    [JsonPropertyName("relative_humidity_2m")]
    public double RelativeHumidity { get; set; }
}
