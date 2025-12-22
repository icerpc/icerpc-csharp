// Copyright (c) ZeroC, Inc.

using IceRpc;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;

namespace CurrentWeatherClient;

/// <summary>Represents a client for an IceRPC service that implements a single GET operation.</summary>
/// <remarks>This class must be kept in sync with the server's WebService class.</remarks>
internal class RpcClient
{
    // We need a case-insensitive mapping since all OpenMeteo JSON property names are lowercase or snake-case.
    private static readonly JsonSerializerOptions _serializerOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly UTF8Encoding _utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly IInvoker _invoker;
    private readonly ServiceAddress _serviceAddress;

    /// <summary>Constructs a new <see cref="RpcClient"/> instance.</summary>
    /// <param name="invoker">The connection or connection-like object used to communicate with the remote service.
    /// </param>
    /// <param name="serviceAddress">The address of the remote service.</param>
    internal RpcClient(IInvoker invoker, ServiceAddress serviceAddress)
    {
        _invoker = invoker;
        _serviceAddress = serviceAddress;
    }

    /// <summary>Makes a GET request to the remote service with the specified query string and deserializes the JSON
    /// response into an instance of type <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The type of the deserialized response object.</typeparam>
    internal async Task<T> GetAsync<T>(string query) where T : struct
    {
        // We encode the query string into an array of UTF-8 bytes. It's straightforward but not extensible: later
        // on, we won't be able to add an extra parameter in a wire-compatible manner.
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(_utf8.GetBytes(query));
        pipe.Writer.Complete();

        using var request = new OutgoingRequest(_serviceAddress)
        {
            Operation = "GET",
            Payload = pipe.Reader // request takes ownership of the PipeReader
        };

        // Make the invocation: we send the request using the invoker and then wait for the response.
        IncomingResponse response = await _invoker.InvokeAsync(request);

        if (response.StatusCode == StatusCode.Ok)
        {
            // OutgoingRequest.Dispose completes Response.Payload, so we don't need to worry about it.
            return await JsonSerializer.DeserializeAsync<T>(response.Payload, _serializerOptions);
        }
        else
        {
            throw new DispatchException(response.StatusCode, response.ErrorMessage);
        }
    }
}
