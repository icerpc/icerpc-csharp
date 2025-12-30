// Copyright (c) ZeroC, Inc.

using IceRpc;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Text;

namespace CurrentWeatherServer;

/// <summary>Implements a dispatcher that adapts and forwards requests using an HttpClient.</summary>
/// <remarks>This class must be kept in sync with the client's RpcClient class.</remarks>
internal class WebService : IDispatcher
{
    private const int MaxQueryLength = 256;
    private static readonly UTF8Encoding _utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;

    public ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken) =>
        // We only implement the IceRPC operation "GET".
        request.Operation switch
        {
            "GET" => GetAsync(request, cancellationToken),
            _ => throw new DispatchException(
                StatusCode.NotImplemented,
                $"{request.Path} does not implement operation '{request.Operation}'.")
        };

    /// <summary>Constructs a new <see cref="WebService"/> instance.</summary>
    /// <param name="httpClient">The HttpClient used to send web requests.</param>
    /// <param name="baseUri">The base URI of the web service.</param>
    internal WebService(HttpClient httpClient, Uri baseUri)
    {
        _httpClient = httpClient;
        _baseUri = baseUri;
    }

    /// <summary>Converts an incoming IceRPC GET request into an HTTP GET request, sends it using the HttpClient,
    /// and converts the HTTP response into an outgoing IceRPC response.</summary>
    /// <param name="request">The incoming IceRPC request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The outgoing IceRPC response.</returns>
    private async ValueTask<OutgoingResponse> GetAsync(IncomingRequest request, CancellationToken cancellationToken)
    {
        // Read the query string.
        ReadResult readResult = await request.Payload.ReadAtLeastAsync(MaxQueryLength, cancellationToken);
        if (!readResult.IsCompleted)
        {
            throw new DispatchException(StatusCode.InvalidData, "Query string too long.");
        }

        // Decode the query string from the request payload.
        string query = _utf8.GetString(readResult.Buffer);
        request.Payload.AdvanceTo(readResult.Buffer.End); // Reading a PipeReader is a two-step process.
        request.Payload.Complete(); // Done with the request payload.

        // Create the request URI.
        var requestUri = new Uri(_baseUri, query);

        // Send the HTTP request.
        Console.WriteLine($"GET {requestUri}");
        using HttpResponseMessage httpResponse = await _httpClient.GetAsync(requestUri, cancellationToken);

        try
        {
            httpResponse.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException e)
        {
            Console.WriteLine($"HTTP request failed: {e.Message}");
            throw new DispatchException(StatusCode.InternalError, "HTTP request failed");
        }

        // Create and return the IceRPC response.
        // Note that we just send the bytes read from the HTTP response content: we don't care how these bytes are
        // encoded.
        byte[] bytes = await httpResponse.Content.ReadAsByteArrayAsync(cancellationToken);
        var outgoingResponse = new OutgoingResponse(request)
        {
            Payload = PipeReader.Create(new ReadOnlySequence<byte>(bytes))
        };
        return outgoingResponse;
    }
}
