// Copyright (c) ZeroC, Inc.

// cspell:ignore IRSG

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using System.Collections.Immutable;

namespace IceRpc.ServiceGenerator.Tests;

/// <summary>Tests that verify the diagnostics reported by the service generator for invalid service
/// declarations.</summary>
[Parallelizable(ParallelScope.All)]
public class ValidationTests
{
    /// <summary>Verifies that the service generator reports IRSG0001 when a derived service class adds an operation
    /// whose name collides with an operation declared by its base service class (via a different interface).</summary>
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

        // Act
        ImmutableArray<Diagnostic> diagnostics = RunGenerator(source, "DuplicateOpTest");

        // Assert
        Assert.That(
            diagnostics.Any(d =>
                d.Id == "IRSG0001" &&
                d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)
                    .Contains("ping", StringComparison.Ordinal)),
            Is.True,
            $"Expected IRSG0001 for operation 'ping'. Got: {string.Join(", ", diagnostics.Select(d => d.ToString()))}");
    }

    private static ImmutableArray<Diagnostic> RunGenerator(string source, string assemblyName)
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source);

        // Reference all assemblies currently loaded so the test source can resolve IceRpc types and BCL types.
        ImmutableArray<MetadataReference> references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .ToImmutableArray();

        var compilation = CSharpCompilation.Create(
            assemblyName: assemblyName,
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        _ = CSharpGeneratorDriver
            .Create(new IceRpc.ServiceGenerator.ServiceGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out _, out ImmutableArray<Diagnostic> diagnostics);

        return diagnostics;
    }
}
