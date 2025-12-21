// Copyright (c) ZeroC, Inc.

#pragma warning disable CA1812 // We use reflection to deserialize JSON into these types.

namespace OpenMeteo;

/// <summary>Represents the JSON-encoded response returned by a successful GET on the GeoCoding API at
/// https://geocoding-api.open-meteo.com/v1/search.</summary>
internal class GeoCodingResponse
{
    public List<LocationInfo> Results { get; set; } = [];
}

/// <summary>Represents a subset of the location details returned by the GeoCoding API.</summary>
internal class LocationInfo
{
    public required string Name { get; set; }

    public int Population { get; set; }

    public string? Country { get; set; }

    public string? Admin1 { get; set; }

    public double Latitude { get; set; }

    public double Longitude { get; set; }
}
