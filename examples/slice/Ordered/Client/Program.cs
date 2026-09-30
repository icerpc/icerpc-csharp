// Copyright (c) ZeroC, Inc.

using IceRpc;
using Journal;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Channels;

// Load the test root CA certificate in order to connect to the server that uses a test server certificate.
using X509Certificate2 rootCA = X509CertificateLoader.LoadCertificateFromFile("../../../../certs/cacert.der");

// Create a secure connection to the server using the default transport (QUIC).
await using var connection = new ClientConnection(
    new Uri("icerpc://localhost"),
    clientAuthenticationOptions: CreateClientAuthenticationOptions(rootCA));

// Part 1: send each log entry in its own oneway request.
//
// A oneway invocation completes as soon as the request is sent, without waiting for the server to dispatch it. The
// server dispatches these requests concurrently, so a dispatch can overtake another and the server can log the entries
// out of order. With QUIC, a request can also overtake another during transmission, since each request travels in its
// own QUIC stream.

Console.Write("Sending log entries with SimpleLogger, one oneway request per entry...");

var simpleLogger = new SimpleLoggerProxy(connection);

for (int i = 1; i <= 10; ++i)
{
    await simpleLogger.LogAsync($"Performed step #{i}");
}

Console.WriteLine(" done.");

// Give the server time to log all the entries before continuing with part 2.
await Task.Delay(TimeSpan.FromSeconds(1));

// Part 2: send all the log entries in a single request, as a stream.
//
// The elements of a stream are delivered in the order the client writes them, since they all belong to the same
// request.

Console.Write("Sending log entries with StreamLogger, a single request with a stream...");

var streamLogger = new StreamLoggerProxy(connection);

// The channel decouples the writing of the log entries from the sending of the stream: the IceRPC runtime sends the
// log entries as they are written to the channel.
var channel = Channel.CreateUnbounded<string>();
Task logTask = streamLogger.LogAsync(channel.Reader.ReadAllAsync());

ChannelWriter<string> messages = channel.Writer;

for (int i = 1; i <= 10; ++i)
{
    await messages.WriteAsync($"Performed step #{i}");
}

// Completing the writer ends the stream.
messages.Complete();

// The invocation completes once the server has logged all the entries.
await logTask;

Console.WriteLine(" done.");

await connection.ShutdownAsync();
