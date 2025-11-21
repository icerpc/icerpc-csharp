// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.BuildTelemetry;
using System.CommandLine;
using System.Diagnostics;
using System.Net.Security;
using System.Reflection;
using System.Runtime.InteropServices;

var rootCommand = new RootCommand("Slice build telemetry reporter");

var compilationHashOption = new Option<string>(name: "--compilation-hash")
{
    Description = "The compilation hash.",
    Required = true,
};
rootCommand.Options.Add(compilationHashOption);

var containsSlice1Option = new Option<bool>(name: "--contains-slice1")
{
    Description = "Whether or not the build contains Slice1 definitions"
};
rootCommand.Options.Add(containsSlice1Option);

var containsSlice2Option = new Option<bool>(name: "--contains-slice2")
{
    Description = "Whether or not the build contains Slice2 definitions"
};
rootCommand.Options.Add(containsSlice2Option);

var referenceFileCountOption = new Option<int>(name: "--ref-file-count")
{
    Description = "The number of reference files included in the compilation."
};
rootCommand.Options.Add(referenceFileCountOption);

var sourceFileCountOption = new Option<int>(name: "--src-file-count")
{
    Description = "The number of source files included in the compilation."
};
rootCommand.Options.Add(sourceFileCountOption);

rootCommand.SetAction(
    async (parseResult, cancellationToken) =>
    {
        string? compilationHash = parseResult.GetValue(compilationHashOption);
        Debug.Assert(compilationHash is not null);
        bool containsSlice1 = parseResult.GetValue(containsSlice1Option);
        bool containsSlice2 = parseResult.GetValue(containsSlice2Option);
        int referenceFileCount = parseResult.GetValue(referenceFileCountOption);
        int sourceFileCount = parseResult.GetValue(sourceFileCountOption);

        const string uri = "icerpc://build-telemetry.icerpc.dev";
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(3));

        await using var connection = new ClientConnection(new Uri(uri), new SslClientAuthenticationOptions());

        // Create a reporter proxy with this client connection.
        var reporter = new ReporterProxy(connection);

        // Determine the IceRPC version using the assembly version.
        var assembly = Assembly.GetAssembly(typeof(SliceTelemetryData));
        string toolsVersion = assembly!.GetName().Version!.ToString();

        var sliceTelemetryData = new SliceTelemetryData(
            RuntimeInformation.ProcessArchitecture.ToString(),
            compilationHash,
            containsSlice1,
            containsSlice2,
            IsCi(),
            RuntimeInformation.OSDescription,
            referenceFileCount,
            sourceFileCount,
            new TargetLanguage.CSharp(Environment.Version.ToString()),
            toolsVersion);

        try
        {
            // Upload the telemetry to the server.
            await reporter.UploadAsync(new BuildTelemetry.Slice(sliceTelemetryData), cancellationToken: cts.Token);

            // Shutdown the connection.
            await connection.ShutdownAsync(cts.Token);
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken == cts.Token)
        {
            Console.Error.WriteLine($"Failed to report slice build telemetry: InvocationCanceled");
            return 0;
        }
        catch (IceRpcException ex) when (ex.IceRpcError is IceRpcError.InvocationCanceled or IceRpcError.ServerUnreachable)
        {
            Console.Error.WriteLine($"Failed to report slice build telemetry: {ex.IceRpcError}");
            return 0;
        }
        catch (Exception ex)
        {
            string message = ex.Message;
            if (ex.InnerException is not null)
            {
                message += $"\n  Inner exception: {ex.InnerException.Message}";
            }
            Console.Error.WriteLine(message);
            return 1;
        }

        return 0;
    });

return await rootCommand.Parse(args).InvokeAsync();

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

    // Check if any of the CI environment variables are set
    return ciEnvironmentVariables.Any(
        name => Environment.GetEnvironmentVariable(name) is string value && value.Length != 0);
}
