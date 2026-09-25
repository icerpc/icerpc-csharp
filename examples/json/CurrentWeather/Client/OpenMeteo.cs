// Copyright (c) ZeroC, Inc.

// We use reflection to deserialize JSON into the types below.
#pragma warning disable CA1812 // Avoid uninstantiated internal classes.

using IceRpc;
using System.Text.Json.Serialization;

namespace CurrentWeatherClient;

/// <summary>Represents a RPC client for the OpenMeteo GeoCoding API.</summary>
internal class GeoClient
{
    private readonly RpcClient _rpcClient;

    internal GeoClient(IInvoker invoker) =>
        _rpcClient = new RpcClient(invoker, new ServiceAddress(new Uri("icerpc:/v1/search")));

    /// <summary>Searches for locations that match the specified name.</summary>
    /// <param name="location">The name of the location to search for.</param>
    /// <param name="count">The maximum number of results to return.</param>
    /// <returns>The response from the GeoCoding API containing matching locations.</returns>
    internal Task<GeoCodingResponse> SearchAsync(string location, int count) =>
        _rpcClient.GetAsync<GeoCodingResponse>($"?name={location}&count={count}");
}

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

/// <summary>Represents a RPC client for the OpenMeteo Weather Forecast API. </summary>
internal class ForecastClient
{
    private readonly RpcClient _rpcClient;

    internal ForecastClient(IInvoker invoker) =>
        _rpcClient = new RpcClient(invoker, new ServiceAddress(new Uri("icerpc:/v1/forecast")));

    /// <summary>Retrieves the current weather conditions for the specified latitude and longitude.</summary>
    /// <param name="latitude">The latitude of the location.</param>
    /// <param name="longitude">The longitude of the location.</param>
    /// <returns>The response from the Weather Forecast API containing current weather conditions.</returns>
    internal Task<CurrentWeatherResponse> GetCurrentWeatherAsync(double latitude, double longitude) =>
        _rpcClient.GetAsync<CurrentWeatherResponse>(FormattableString.Invariant(
            $"?latitude={latitude}&longitude={longitude}&current=temperature_2m,relative_humidity_2m"));
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
