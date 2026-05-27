// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Slice.BuildTelemetry;
using IceRpc.Transports.Quic;
using IceRpc.Transports.Slic;
using IceRpc.Transports.Tcp;
using System.Collections.Immutable;
using System.IO.Pipelines;
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.InteropServices;
using ZeroC.Slice.Symbols;

// The slicec compiler executes this program as a generator plug-in. It writes the serialized generator request to the
// generator's standard input and expects a serialized generator response on standard output.

using Stream stdin = Console.OpenStandardInput();
var reader = PipeReader.Create(stdin);

using Stream stdout = Console.OpenStandardOutput();
var writer = PipeWriter.Create(stdout);

await Generator.RunAsync(reader, writer, BuildResponseAsync);

static async Task<GeneratorResponse> BuildResponseAsync(
    ImmutableList<SliceFile> symbolFiles,
    (string key, string value)[] options)
{
    if (symbolFiles.Count == 0)
    {
        return new GeneratorResponse { GeneratedFiles = [], Diagnostics = [] };
    }

    int interfaceCount = 0;
    int operationCount = 0;
    int typeCount = 0;

    foreach (SliceFile file in symbolFiles)
    {
        foreach (ISymbol symbol in file.Contents)
        {
            if (symbol is Interface interfaceSymbol)
            {
                interfaceCount++;
                operationCount += interfaceSymbol.Operations.Count;
            }
            else if (symbol is IType)
            {
                // Struct, BasicEnum, VariantEnum, TypeAlias, CustomType.
                typeCount++;
            }
        }
    }

    bool debug = false;
    bool dryRun = false;
    bool ci = false;
    string slicecVersion = "";
    ToolchainInfo? toolchain = null;
    var generators = new List<GeneratorInfo>();

    foreach ((string key, string value) in options)
    {
        switch (key)
        {
            case "debug":
                if (value.Length > 0)
                {
                    return ErrorResponse("The 'debug' option does not accept any value.");
                }
                debug = true;
                break;
            case "dry_run":
                if (value.Length > 0)
                {
                    return ErrorResponse("The 'dry_run' option does not accept any value.");
                }
                dryRun = true;
                break;
            case "ci":
                if (value.Length > 0)
                {
                    return ErrorResponse("The 'ci' option does not accept any value.");
                }
                ci = true;
                break;
            case "slicec_version":
                if (value.Length == 0)
                {
                    return ErrorResponse("The 'slicec_version' option requires a non-empty value.");
                }
                slicecVersion = value;
                break;
            case "toolchain":
                string[] toolchainInfo = value.Split(':', 2);
                string toolchainName = toolchainInfo[0].Trim();
                string toolchainVersion = toolchainInfo.Length > 1 ? toolchainInfo[1].Trim() : "";
                if (toolchainName.Length == 0 || toolchainVersion.Length == 0)
                {
                    return ErrorResponse("The 'toolchain' option requires a value in the format '<name>:<version>'.");
                }
                toolchain = new ToolchainInfo(toolchainName, toolchainVersion);
                break;
            case "generator":
                string[] generatorInfo = value.Split(':', 2);
                string generatorName = generatorInfo[0].Trim();
                string generatorVersion = generatorInfo.Length > 1 ? generatorInfo[1].Trim() : "";
                if (generatorName.Length == 0 || generatorVersion.Length == 0)
                {
                    return ErrorResponse("The 'generator' option requires a value in the format '<name>:<version>'.");
                }
                generators.Add(new GeneratorInfo(generatorName, generatorVersion));
                break;
            default:
                return ErrorResponse($"Unknown option: '{key}'");
        }
    }

    var telemetryData = new TelemetryData(
        RuntimeInformation.ProcessArchitecture.ToString(),
        RuntimeInformation.OSDescription,
        slicecVersion,
        toolchain,
        dryRun,
        ci,
        generators,
        (uint)interfaceCount,
        (uint)operationCount,
        (uint)typeCount);

    string uri = "icerpc://build-telemetry.icerpc.dev";
    string? failure = null;

    try
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        // Create a client connection to the telemetry server. We use QUIC when supported, otherwise Slic over TCP.
        await using var connection = new ClientConnection(
            new Uri(uri),
            new SslClientAuthenticationOptions(),
            multiplexedClientTransport: QuicConnection.IsSupported ?
                new QuicClientTransport() : new SlicClientTransport(new TcpClientTransport()));

        // Use a one-way invocation unless we're in debug mode.
        var invoker = new InlineInvoker((request, cancellationToken) =>
        {
            request.IsOneway = !debug;
            return connection.InvokeAsync(request, cancellationToken);
        });

        var buildObserver = new BuildObserverProxy(invoker);

        // Add path to URI.
        uri += buildObserver.ServiceAddress.Path;

        await buildObserver.ReportSliceBuildAsync(telemetryData, cancellationToken: cts.Token);
        await connection.ShutdownAsync(cts.Token);
    }
    catch (OperationCanceledException)
    {
        failure = $"Failed to report build telemetry to {uri}: operation canceled";
    }
    catch (IceRpcException ex) when (ex.IceRpcError is IceRpcError.InvocationCanceled or IceRpcError.ServerUnreachable)
    {
        failure = $"Failed to report build telemetry to {uri}: {ex.IceRpcError}";
    }
    catch (Exception ex)
    {
        failure = $"Failed to report build telemetry to {uri}: {ex.Message}";
    }

    if (!debug)
    {
        // Silent in non-debug mode: no transcript file, swallow failures.
        return new GeneratorResponse { GeneratedFiles = [], Diagnostics = [] };
    }

    if (failure is not null)
    {
        return ErrorResponse(failure);
    }
    else
    {
        string sliceFileList = string.Join(
            "\n",
            symbolFiles.Select(file => $"  - {Path.GetFileName(file.Path)}"));

        // We derive the transcript file name from the first Slice file in the compilation. It's possible to compile
        // multiple Slice files in the same directory using different options, and as a result we can't use a fixed
        // name for the transcript file.
        return new GeneratorResponse
        {
            GeneratedFiles =
            [
                new GeneratedFile
                {
                    Path = $"{Path.GetFileNameWithoutExtension(symbolFiles[0].Path)}.BuildTelemetry.txt",
                    Contents =
                        $"Slice files compiled in this run:\n{sliceFileList}\n\n" +
                        $"Build telemetry reported to {uri}:\n{telemetryData}",
                }
            ],
            Diagnostics = [],
        };
    }

    static GeneratorResponse ErrorResponse(string message) => new()
    {
        GeneratedFiles = [],
        Diagnostics = [Diagnostic.Error(message)],
    };
}
