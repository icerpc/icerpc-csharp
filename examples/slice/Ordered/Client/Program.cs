// Copyright (c) ZeroC, Inc.

using IceRpc;
using OrderedExample;
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

await simpleLogger.LogAsync(1, DateTime.Now, "Performing");
await simpleLogger.LogAsync(2, DateTime.Now, "Accomplishing");
await simpleLogger.LogAsync(3, DateTime.Now, "Executing");
await simpleLogger.LogAsync(4, DateTime.Now, "Completing");
await simpleLogger.LogAsync(5, DateTime.Now, "Achieving");
await simpleLogger.LogAsync(6, DateTime.Now, "Realizing");
await simpleLogger.LogAsync(7, DateTime.Now, "Effecting");
await simpleLogger.LogAsync(8, DateTime.Now, "Implementing");
await simpleLogger.LogAsync(9, DateTime.Now, "Delivering");
await simpleLogger.LogAsync(10, DateTime.Now, "Fulfilling");

Console.WriteLine(" done.");

// Give the server time to log all the entries before continuing with part 2.
await Task.Delay(TimeSpan.FromSeconds(1));

// Part 2: send all the log entries in a single oneway request, as a stream.
//
// The elements of a stream are delivered and dispatched in the order the client writes them, since they all belong to
// the same request. The serial number becomes unnecessary.

Console.Write("Sending log entries with StreamLogger, a single oneway request with a stream...");

var streamLogger = new StreamLoggerProxy(connection);

// The channel decouples the writing of the log entries from the sending of the stream: the invocation returns
// immediately and the IceRPC runtime sends the log entries in the background as they are written to the channel.
var channel = Channel.CreateUnbounded<LogEntry>();
await streamLogger.LogAsync(channel.Reader.ReadAllAsync());

ChannelWriter<LogEntry> logEntries = channel.Writer;

await logEntries.WriteAsync(new LogEntry { TimeStamp = DateTime.Now, Message = "Performing" });
await logEntries.WriteAsync(new LogEntry { TimeStamp = DateTime.Now, Message = "Accomplishing" });
await logEntries.WriteAsync(new LogEntry { TimeStamp = DateTime.Now, Message = "Executing" });
await logEntries.WriteAsync(new LogEntry { TimeStamp = DateTime.Now, Message = "Completing" });
await logEntries.WriteAsync(new LogEntry { TimeStamp = DateTime.Now, Message = "Achieving" });
await logEntries.WriteAsync(new LogEntry { TimeStamp = DateTime.Now, Message = "Realizing" });
await logEntries.WriteAsync(new LogEntry { TimeStamp = DateTime.Now, Message = "Effecting" });
await logEntries.WriteAsync(new LogEntry { TimeStamp = DateTime.Now, Message = "Implementing" });
await logEntries.WriteAsync(new LogEntry { TimeStamp = DateTime.Now, Message = "Delivering" });
await logEntries.WriteAsync(new LogEntry { TimeStamp = DateTime.Now, Message = "Fulfilling" });

// Completing the writer ends the stream.
logEntries.Complete();

// Shutting down the connection waits for the stream to be fully sent.
await connection.ShutdownAsync();

Console.WriteLine(" done.");
