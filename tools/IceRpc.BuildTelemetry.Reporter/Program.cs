// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.BuildTelemetry;
using Microsoft.Extensions.Logging;
using System.Net.Security;
using System.Reflection;

var timeout = TimeSpan.FromSeconds(3); // The timeout for the complete telemetry upload process.

const string uri = "icerpc://telemetry.icerpc.dev"; // The URI of the server.

// Create a cancellation token source to cancel the telemetry upload if it
// takes too long.
using var cts = new CancellationTokenSource(timeout);

// Create a client connection
var clientAuthenticationOptions = new SslClientAuthenticationOptions();
await using var connection = new ClientConnection(new Uri(uri), clientAuthenticationOptions);

// Create a reporter proxy with this client connection.
var reporter = new ReporterProxy(connection);

// Parse the IDL
string? idl = args
    .SkipWhile(arg => arg != "--idl")
    .Skip(1)
    .FirstOrDefault() ?? "unknown";

// Determine the IceRPC version using the assembly version. The assembly version is kept in sync with the
// IceRPC version.
var assembly = Assembly.GetAssembly(typeof(SliceTelemetryData));
string version = assembly!.GetName().Version!.ToString();

// Parse the compilation hash
string compilationHash = args
    .SkipWhile(arg => arg != "--hash")
    .Skip(1)
    .FirstOrDefault() ?? "unknown";

// Parse the contains-slice1 argument
bool containsSlice1 = bool.TryParse(args
    .SkipWhile(arg => arg != "--contains-slice1")
    .Skip(1)
    .FirstOrDefault(), out bool result) && result;

// Parse the contains-slice2 argument
bool containsSlice2 = bool.TryParse(args
    .SkipWhile(arg => arg != "--contains-slice2")
    .Skip(1)
    .FirstOrDefault(), out result) && result;

// Parse the src-file-count argument
int sourceFileCount = int.TryParse(args
    .SkipWhile(arg => arg != "--src-file-count")
    .Skip(1)
    .FirstOrDefault(), out int sourceFileCountResult) ? sourceFileCountResult : 0;

// Parse the ref-file-count argument
int referenceFileCount = int.TryParse(args
    .SkipWhile(arg => arg != "--ref-file-count")
    .Skip(1)
    .FirstOrDefault(), out int referenceFileCountResult) ? referenceFileCountResult : 0;

BuildTelemetry buildTelemetry = idl.ToLower() switch
{
    "slice" => new BuildTelemetry.Slice(new SliceTelemetryData(
        version,
        SystemHelpers.GetOperatingSystem(),
        SystemHelpers.GetArchitecture(),
        SystemHelpers.IsCi(),
        new TargetLanguage.CSharp(Environment.Version.ToString()),
        compilationHash,
        containsSlice1,
        containsSlice2,
        sourceFileCount,
        referenceFileCount)),
    "protobuf" => new BuildTelemetry.Protobuf(new ProtobufTelemetryData(
        version,
        SystemHelpers.GetOperatingSystem(),
        SystemHelpers.GetArchitecture(),
        SystemHelpers.IsCi(),
        new TargetLanguage.CSharp(Environment.Version.ToString()),
        sourceFileCount,
        compilationHash)),
    _ => throw new ArgumentException($"Unknown IDL: {idl}") // This should never happen
};

// Upload the telemetry to the server.
await reporter.UploadAsync(buildTelemetry, cancellationToken: cts.Token);

// Shutdown the connection.
await connection.ShutdownAsync(cts.Token);
