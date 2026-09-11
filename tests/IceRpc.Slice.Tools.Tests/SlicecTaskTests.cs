// Copyright (c) ZeroC, Inc.

using NUnit.Framework;

namespace IceRpc.Slice.Tools.Tests;

[TestFixture]
public class SlicecTaskTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void Telemetry_reports_only_invoked_bundled_generators(bool iceRpc)
    {
        var task = new TestSlicecTask
        {
            Generators = iceRpc ?
                ["/tools/slicec-csharp-generator.sh", "/tools/slicec-icerpc-csharp-generator.sh"] :
                ["/tools/slicec-csharp-generator.sh"],
            BundledGenerators = ["/tools/slicec-csharp-generator.sh", "/tools/slicec-icerpc-csharp-generator.sh"],
            BuildTelemetryGenerator = "/tools/slicec-build-telemetry.sh,dry_run,ci",
            IceRpcSliceToolsVersion = "1.2.3",
        };

        string commandLine = task.GetCommandLine();

        Assert.That(commandLine, Does.Contain(
            "--generator \"/tools/slicec-build-telemetry.sh,dry_run,ci,generator=slicec-csharp-generator:1.2.3"));
        Assert.That(commandLine, Does.Contain(
            iceRpc ?
                ",generator=slicec-icerpc-csharp-generator:1.2.3,generator=slicec-build-telemetry:1.2.3" :
                ",generator=slicec-build-telemetry:1.2.3"));
        Assert.That(
            commandLine.Contains("slicec-icerpc-csharp-generator", StringComparison.Ordinal),
            Is.EqualTo(iceRpc));
        Assert.That(task.GetCommandLine(), Is.EqualTo(commandLine));
    }

    [TestCase("/private/tools/custom.sh,secret=value", "custom")]
    [TestCase(@"C:\private\tools\custom.bat,secret=value", "custom")]
    [TestCase(@"C:\private\tools\custom.exe,secret=value", "custom")]
    [TestCase("/private/tools/custom.generator,secret=value", "custom.generator")]
    [TestCase("/private/tools/slicec-csharp-generator.sh", "slicec-csharp-generator")]
    [TestCase("/private/tools/slicec-icerpc-csharp-generator.sh", "slicec-icerpc-csharp-generator")]
    [TestCase("/private/tools/slicec-build-telemetry.sh", "slicec-build-telemetry")]
    [TestCase(@"C:\private\tools\slicec-csharp-generator.bat", "slicec-csharp-generator")]
    [TestCase(@"C:\private\tools\slicec-icerpc-csharp-generator.bat", "slicec-icerpc-csharp-generator")]
    [TestCase(@"C:\private\tools\slicec-build-telemetry.bat", "slicec-build-telemetry")]
    public void Telemetry_reports_custom_generator_names_without_paths_or_options(string generator, string name)
    {
        var task = new TestSlicecTask
        {
            Generators = [generator],
            BundledGenerators = ["/tools/slicec-csharp-generator.sh", "/tools/slicec-icerpc-csharp-generator.sh"],
            BuildTelemetryGenerator = "/tools/slicec-build-telemetry.sh",
            IceRpcSliceToolsVersion = "1.2.3",
        };

        string commandLine = task.GetCommandLine();
        string telemetryArgument = commandLine[commandLine.IndexOf(
            "--generator \"/tools/slicec-build-telemetry.sh", StringComparison.Ordinal)..];

        Assert.That(telemetryArgument, Does.Contain($",generator={name}:unknown"));
        Assert.That(telemetryArgument, Does.Not.Contain("private"));
        Assert.That(telemetryArgument, Does.Not.Contain("secret"));
        Assert.That(telemetryArgument, Does.Contain(",generator=slicec-build-telemetry:1.2.3"));
    }

    [Test]
    public void Disabled_telemetry_does_not_add_a_generator()
    {
        var task = new TestSlicecTask { Generators = ["/tools/slicec-csharp-generator.sh"] };

        Assert.That(
            task.GetCommandLine(),
            Is.EqualTo("--generator /tools/slicec-csharp-generator.sh --diagnostic-format=json"));
    }

    private sealed class TestSlicecTask : SlicecTask
    {
        public string GetCommandLine() => GenerateCommandLineCommands();
    }
}
