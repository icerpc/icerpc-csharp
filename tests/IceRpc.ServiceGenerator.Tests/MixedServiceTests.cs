// Copyright (c) ZeroC, Inc.

// cspell:ignore IRSG

using Google.Protobuf.WellKnownTypes;
using IceRpc.Features;
using IceRpc.Tests.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using System.Collections.Immutable;

namespace IceRpc.ServiceGenerator.Tests;

[Parallelizable(ParallelScope.All)]
public partial class MixedServiceTests
{
    /// <summary>Verifies that a single service class can implement an Ice interface, a Slice interface, and a
    /// Protobuf service, and dispatch operations from all three.</summary>
    [Test]
    public async Task Service_implements_ice_slice_and_protobuf_interfaces()
    {
        // Arrange
        var service = new MixedService();
        var invoker = new ColocInvoker(service);

        var iceProxy = new GreeterProxy(invoker);
        var sliceProxy = new SliceGreeterProxy(invoker);
        var protobufClient = new ProtobufGreeterClient(invoker);

        // Act/Assert
        Assert.That(async () => await iceProxy.OpIceAsync(), Throws.Nothing);
        Assert.That(await iceProxy.OpIceWithArgsAsync("Ice"), Is.EqualTo("Ice"));
        Assert.That(async () => await sliceProxy.OpSliceAsync(), Throws.Nothing);
        Assert.That(await sliceProxy.OpSliceWithArgsAsync("Slice"), Is.EqualTo("Slice"));
        Assert.That(async () => await protobufClient.OpProtoAsync(new Empty()), Throws.Nothing);
        Assert.That(
            (await protobufClient.OpProtoWithArgsAsync(new ProtoMessage { Message = "Proto" })).Message,
            Is.EqualTo("Proto"));
    }

    [Service]
    internal partial class MixedService : IGreeterService, ISliceGreeterService, IProtobufGreeterService
    {
        public ValueTask OpIceAsync(IFeatureCollection features, CancellationToken cancellationToken) => default;

        public ValueTask<string> OpIceWithArgsAsync(
            string message,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(message);

        public ValueTask OpSliceAsync(IFeatureCollection features, CancellationToken cancellationToken) => default;

        public ValueTask<string> OpSliceWithArgsAsync(
            string message,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(message);

        public ValueTask<Empty> OpProtoAsync(
            Empty message,
            IFeatureCollection? features,
            CancellationToken cancellationToken) => new(new Empty());

        public ValueTask<ProtoMessage> OpProtoWithArgsAsync(
            ProtoMessage message,
            IFeatureCollection? features,
            CancellationToken cancellationToken) => new(message);
    }

    /// <summary>Verifies that the service generator reports IRSG0001 when a derived service class adds an
    /// operation whose name collides with an operation declared by its base service class (via a different
    /// interface). Without the fix, the colliding derived operation would be silently dropped.</summary>
    [Test]
    public void Derived_service_with_operation_name_colliding_with_base_reports_diagnostic()
    {
        // Arrange
        const string source = """
            using IceRpc;
            using IceRpc.Features;
            using IceRpc.Protobuf.RpcMethods;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestNs;

            public interface IBaseTestService
            {
                [RpcMethod("ping")]
                ValueTask<string> BasePingAsync(
                    string input, IFeatureCollection? features, CancellationToken cancellationToken);
            }

            public interface IDerivedTestService
            {
                [RpcMethod("ping")]
                ValueTask<string> DerivedPingAsync(
                    string input, IFeatureCollection? features, CancellationToken cancellationToken);
            }

            [Service]
            public partial class BaseTestService : IBaseTestService
            {
                public ValueTask<string> BasePingAsync(
                    string input, IFeatureCollection? features, CancellationToken cancellationToken) =>
                    new(input);
            }

            [Service]
            public partial class DerivedTestService : BaseTestService, IDerivedTestService
            {
                public ValueTask<string> DerivedPingAsync(
                    string input, IFeatureCollection? features, CancellationToken cancellationToken) =>
                    new(input);
            }
            """;

        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source);

        // Reference all assemblies currently loaded so the test source can resolve IceRpc types and BCL types.
        ImmutableArray<MetadataReference> references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .ToImmutableArray();

        var compilation = CSharpCompilation.Create(
            assemblyName: "DuplicateOpTest",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // Act
        _ = CSharpGeneratorDriver
            .Create(new global::IceRpc.ServiceGenerator.ServiceGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out _, out ImmutableArray<Diagnostic> diagnostics);

        // Assert
        Assert.That(
            diagnostics.Any(d =>
                d.Id == "IRSG0001" &&
                d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)
                    .Contains("ping", StringComparison.Ordinal)),
            Is.True,
            $"Expected IRSG0001 for operation 'ping'. Got: {string.Join(", ", diagnostics.Select(d => d.ToString()))}");
    }
}
