// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.BuildTelemetry;
using System.CommandLine;
using System.Net.Security;
using System.Reflection;
using System.Runtime.InteropServices;

var rootCommand = new RootCommand("Slice build telemetry reporter");

var compilationHashOption = new Option<string>(
    name: "--compilation-hash",
    description: "The compilation hash.")
{
    IsRequired = true,
};
rootCommand.AddOption(compilationHashOption);

var containsSlice1Option = new Option<bool>(
    name: "--contains-slice1",
    description: "Whether or not the build contains Slice1 definitions");
rootCommand.AddOption(containsSlice1Option);

var containsSlice2Option = new Option<bool>(
    name: "--contains-slice2",
    description: "Whether or not the build contains Slice2 definitions");
rootCommand.AddOption(containsSlice2Option);

var referenceFileCountOption = new Option<int>(
    name: "--ref-file-count",
    description: "The number of reference files included in the compilation.");
rootCommand.AddOption(referenceFileCountOption);

var sourceFileCountOption = new Option<int>(
    name: "--src-file-count",
    description: "The number of source files included in the compilation.");
rootCommand.AddOption(sourceFileCountOption);

rootCommand.SetHandler(
    async (
        string compilationHash,
        bool containsSlice1,
        bool containsSlice2,
        int referenceFileCount,
        int sourceFileCount) =>
    {
        const string uri = "icerpc://build-telemetry.icerpc.dev";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
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

        // Upload the telemetry to the server.
        await reporter.UploadAsync(new BuildTelemetry.Slice(sliceTelemetryData), cancellationToken: cts.Token);

        // Shutdown the connection.
        await connection.ShutdownAsync(cts.Token);
    },
    compilationHashOption,
    containsSlice1Option,
    containsSlice2Option,
    referenceFileCountOption,
    sourceFileCountOption);

return await rootCommand.InvokeAsync(args);

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
