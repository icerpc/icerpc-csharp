// Copyright (c) ZeroC, Inc.

using IceRpc;
using OpenMeteo;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;

#pragma warning disable CA1869

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

string query = "?name=Jupiter&count=2";
var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

// Create a PipeReader holding the query.
var pipe = new Pipe();
await pipe.Writer.WriteAsync(utf8.GetBytes(query));
pipe.Writer.Complete();

using var request = new OutgoingRequest(new ServiceAddress(new Uri("icerpc:/v1/search")))
{
    Operation = "GET",
    Payload = pipe.Reader // request takes ownership of the PipeReader
};

// Make the invocation: we send the request using the connection and then wait for the response.
IncomingResponse response = await connection.InvokeAsync(request);

if (response.StatusCode == StatusCode.Ok)
{
    GeoCodingResponse? geoCodingResponse = await JsonSerializer.DeserializeAsync<GeoCodingResponse>(response.Payload, options);
    response.Payload.Complete(); // DeserializeAsync reads to completion but does not complete the PipeReader.

    Debug.Assert(geoCodingResponse is not null);

    Console.WriteLine($"Found {geoCodingResponse.Results.Count} locations:");

    foreach (LocationInfo location in geoCodingResponse.Results)
    {
        Console.WriteLine($"{location.Name} ({location.Admin1}, {location.Country}) POP: {location.Population} LAT: {location.Latitude} LONG: {location.Longitude}");
    }
}
else
{
    Console.WriteLine($"Error: {response.StatusCode}");
}

await connection.ShutdownAsync();
