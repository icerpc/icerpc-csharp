// Copyright (c) ZeroC, Inc.

using IceRpc;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Text;

namespace Spider;

/// <summary>Implements a dispatcher that sends requests using HttpClient.</summary>
internal class WebService : IDispatcher
{
    private static readonly UTF8Encoding _utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;

    public ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken) =>
        // We only implement the IceRPC operation "GET".
        request.Operation switch
        {
            "GET" => GetAsync(request, cancellationToken),
            _ => new ValueTask<OutgoingResponse>(new OutgoingResponse(request, StatusCode.NotImplemented)),
        };

    internal WebService(HttpClient httpClient, Uri baseUri)
    {
        _httpClient = httpClient;
        _baseUri = baseUri;
    }

    // Sends a GET web request and converts the response into an IceRPC response.
    private async ValueTask<OutgoingResponse> GetAsync(IncomingRequest request, CancellationToken cancellationToken)
    {
        // Read the entire payload.
        ReadResult readResult = await request.Payload.ReadAtLeastAsync(int.MaxValue, cancellationToken);
        Debug.Assert(readResult.IsCompleted);

        // Decode the query string.
        string query = _utf8.GetString(readResult.Buffer);
        request.Payload.Complete(); // Don't need to read more.

        // Create the request URI.
        var requestUri = new Uri(_baseUri, query);

        // Send the HTTP request.
        Console.WriteLine($"GET {requestUri}");
        using HttpResponseMessage httpResponse = await _httpClient.GetAsync(requestUri, cancellationToken);
        httpResponse.EnsureSuccessStatusCode();

        // Create and return the IceRPC response.
        byte[] bytes = await httpResponse.Content.ReadAsByteArrayAsync(cancellationToken);
        var outgoingResponse = new OutgoingResponse(request)
        {
            Payload = PipeReader.Create(new ReadOnlySequence<byte>(bytes))
        };
        return outgoingResponse;
    }
}
