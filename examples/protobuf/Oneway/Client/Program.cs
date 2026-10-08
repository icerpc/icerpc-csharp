// Copyright (c) ZeroC, Inc.

using Google.Protobuf.WellKnownTypes;
using IceRpc;
using Journal;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;

// Load the test root CA certificate in order to connect to the server that uses a test server certificate.
using var rootCA = X509CertificateLoader.LoadCertificateFromFile("../../../../certs/cacert.der");

// Create a secure connection to the server using the default transport (QUIC).
await using var connection = new ClientConnection(
    new Uri("icerpc://localhost"),
    clientAuthenticationOptions: CreateClientAuthenticationOptions(rootCA));

// Part 1: send each log entry in a two-way request.
//
// A two-way invocation completes when the client receives the response, after the server has dispatched the request.

var logger = new SimpleLoggerClient(connection);

Console.Write("Sending log entries with two-way requests...");
var stopwatch = Stopwatch.StartNew();

for (int i = 1; i <= 10; ++i)
{
    await logger.LogAsync(CreateLogEntry(i));
}

Console.WriteLine($" done in {stopwatch.ElapsedMilliseconds} ms.");

// Part 2: send each log entry in a oneway request.
//
// A oneway invocation completes as soon as the request is sent, without waiting for the server to dispatch it.
// Protobuf provides no way to mark an RPC as oneway, so a dedicated pipeline with an interceptor marks each request
// sent through it as oneway.

Pipeline onewayPipeline = new Pipeline()
    .Use(next => new InlineInvoker((request, cancellationToken) =>
    {
        request.IsOneway = true;
        return next.InvokeAsync(request, cancellationToken);
    }))
    .Into(connection);

var onewayLogger = new SimpleLoggerClient(onewayPipeline);

Console.Write("Sending log entries with oneway requests...");
stopwatch.Restart();

for (int i = 1; i <= 10; ++i)
{
    await onewayLogger.LogAsync(CreateLogEntry(i));
}

Console.WriteLine($" done in {stopwatch.ElapsedMilliseconds} ms.");

await connection.ShutdownAsync();

static LogEntry CreateLogEntry(int step) =>
    new() { Timestamp = Timestamp.FromDateTime(DateTime.UtcNow), Message = $"Performed step #{step}" };
