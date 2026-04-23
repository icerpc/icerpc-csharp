// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.BuildTelemetry;
using IceRpc.Transports.Quic;
using IceRpc.Transports.Slic;
using IceRpc.Transports.Tcp;
using System.Collections.Immutable;
using System.IO.Pipelines;
using System.Net.Quic;
using System.Net.Security;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ZeroC.Slice.Symbols;

// slicec executes this program as a generator plug-in. It writes the serialized generator request to the
// plug-in's standard input and expects a serialized generator response on standard output.

using Stream stdin = Console.OpenStandardInput();
var reader = PipeReader.Create(stdin);

using Stream stdout = Console.OpenStandardOutput();
var writer = PipeWriter.Create(stdout);

await Generator.RunAsync(reader, writer, BuildResponse).ConfigureAwait(false);

static GeneratorResponse BuildResponse(
    ImmutableList<SliceFile> symbolFiles,
    Dictionary<string, string> options)
{
    if (symbolFiles.Count == 0)
    {
        return new GeneratorResponse { GeneratedFiles = [], Diagnostics = [] };
    }

    byte[] hashBytes = [];

    foreach (SliceFile file in symbolFiles)
    {
        byte[] content;
        try
        {
            content = File.ReadAllBytes(file.Path);
        }
        catch
        {
            continue;
        }

        byte[] newHash = SHA256.HashData(content);
        hashBytes = hashBytes.Length == 0 ? newHash : SHA256.HashData(newHash.Concat(hashBytes).ToArray());
    }

    // Determine the IceRPC version using the assembly version.
    var assembly = Assembly.GetExecutingAssembly();
    string toolVersion = assembly!.GetName().Version!.ToString();

    string compilationHash = hashBytes.Length == 0 ?
        string.Empty : Convert.ToHexString(hashBytes).ToLowerInvariant();

    var sliceTelemetryData = new SliceTelemetryData(
        RuntimeInformation.ProcessArchitecture.ToString(),
        compilationHash,
        containsSlice1: false,
        containsSlice2: true,
        IsCi(),
        RuntimeInformation.OSDescription,
        referenceFileCount: 0,
        sourceFileCount: symbolFiles.Count,
        new TargetLanguage.CSharp(Environment.Version.ToString()),
        toolVersion);

    string responseContent = UploadTelemetry(sliceTelemetryData);

    string fileName = compilationHash.Length >= 8 ?
        $"{compilationHash[..8]}.icerpc_build_telemetry.txt" : "icerpc_build_telemetry.txt";

    return new GeneratorResponse
    {
        GeneratedFiles = [new GeneratedFile { Path = fileName, Contents = responseContent }],
        Diagnostics = [],
    };
}

static string UploadTelemetry(SliceTelemetryData data)
{
    const string uri = "icerpc://build-telemetry.icerpc.dev";

    try
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        // Create a client connection to the telemetry server. We use QUIC when supported, otherwise Slic over TCP.
        var connection = new ClientConnection(
            new Uri(uri),
            new SslClientAuthenticationOptions(),
            multiplexedClientTransport: QuicConnection.IsSupported ?
                new QuicClientTransport() : new SlicClientTransport(new TcpClientTransport()));

        try
        {
            var reporter = new ReporterProxy(connection);
            reporter.UploadAsync(new BuildTelemetry.Slice(data), cancellationToken: cts.Token)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            connection.ShutdownAsync(cts.Token).ConfigureAwait(false).GetAwaiter().GetResult();
        }
        finally
        {
            connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        return @$"Build telemetry reported successfully to {uri}.

{data}";
    }
    catch (OperationCanceledException)
    {
        return $"Failed to report build telemetry to {uri}: operation canceled";
    }
    catch (IceRpcException ex) when (ex.IceRpcError is IceRpcError.InvocationCanceled or IceRpcError.ServerUnreachable)
    {
        return $"Failed to report build telemetry to {uri}: {ex.IceRpcError}";
    }
    catch (Exception ex)
    {
        string message = ex.Message;
        if (ex.InnerException is not null)
        {
            message += $"\n  Inner exception: {ex.InnerException.Message}";
        }
        return $"Failed to report build telemetry to {uri}: {message}";
    }
}

static bool IsCi()
{
    string[] ciEnvironmentVariables =
    [
        "TF_BUILD", // Azure Pipelines / DevOpsServer
        "GITHUB_ACTIONS", // GitHub Actions
        "APPVEYOR", // AppVeyor
        "CI", // General, set by many build agents
        "TRAVIS", // Travis CI
        "CIRCLECI", // Circle CI
        "CODEBUILD_BUILD_ID", // AWS CodeBuild
        "AWS_REGION", // AWS CodeBuild region
        "BUILD_ID", // Jenkins, Google Cloud Build
        "BUILD_URL", // Jenkins
        "PROJECT_ID", // Google Cloud Build
        "TEAMCITY_VERSION", // TeamCity
        "JB_SPACE_API_URL" // JetBrains Space
    ];

    return ciEnvironmentVariables.Any(
        name => Environment.GetEnvironmentVariable(name) is string value && value.Length != 0);
}
