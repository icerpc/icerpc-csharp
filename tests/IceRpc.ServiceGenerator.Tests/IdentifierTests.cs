// Copyright (c) ZeroC, Inc.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using System.Collections.Immutable;
using System.IO.Pipelines;

namespace IceRpc.ServiceGenerator.Tests;

[Parallelizable(ParallelScope.All)]
public class IdentifierTests
{
    [Test]
    public void Tuple_field_names_are_escaped([Values("Slice", "Ice")] string idl)
    {
        string definitions = $$"""
            using IceRpc;
            using IceRpc.Features;
            using IceRpc.{{idl}};
            using IceRpc.{{idl}}.Operations;
            using System.IO.Pipelines;
            using System.Threading;
            using System.Threading.Tasks;

            public interface ITestService
            {
                [{{idl}}Operation("subscribe")]
                ValueTask<(string User, string @return)> SubscribeAsync(
                    string user, string @event, IFeatureCollection features, CancellationToken cancellationToken);

                public static class Request
                {
                    public static ValueTask<(string User, string @event)> DecodeSubscribeAsync(
                        IncomingRequest request, CancellationToken cancellationToken) => default;
                }

                public static class Response
                {
                    public static PipeReader EncodeSubscribe(
                        string user, string @return, {{idl}}EncodeOptions? encodeOptions) => null!;
                }
            }
            """;

        const string implementation = """
            using IceRpc;
            using IceRpc.Features;
            using System.Threading;
            using System.Threading.Tasks;

            [Service]
            public partial class TestService : ITestService
            {
                public ValueTask<(string User, string @return)> SubscribeAsync(
                    string user, string @event, IFeatureCollection features, CancellationToken cancellationToken) =>
                    new((user, @event));
            }
            """;

        AssertGeneratedCodeCompiles(definitions, implementation);
    }

    [Test]
    public void Streamed_return_field_names_are_escaped([Values] bool encodedReturn)
    {
        string returnType = encodedReturn ? "PipeReader" : "string";
        string definitions = $$"""
            using IceRpc;
            using IceRpc.Features;
            using IceRpc.Slice;
            using IceRpc.Slice.Operations;
            using System.Collections.Generic;
            using System.IO.Pipelines;
            using System.Threading;
            using System.Threading.Tasks;

            public interface ITestService
            {
                [SliceOperation("subscribe", EncodedReturn = {{(encodedReturn ? "true" : "false")}})]
                ValueTask<({{returnType}} @return, IAsyncEnumerable<string> @event)> SubscribeAsync(
                    IFeatureCollection features, CancellationToken cancellationToken);

                public static class Request
                {
                    public static ValueTask DecodeSubscribeAsync(
                        IncomingRequest request, CancellationToken cancellationToken) => default;
                }

                public static class Response
                {
                    public static PipeReader EncodeSubscribe(string @return, SliceEncodeOptions? encodeOptions) => null!;

                    public static PipeReader EncodeStreamOfSubscribe(
                        IAsyncEnumerable<string> @event, SliceEncodeOptions? encodeOptions) => null!;
                }
            }
            """;

        string implementation = $$"""
            using IceRpc;
            using IceRpc.Features;
            using System.Collections.Generic;
            using System.IO.Pipelines;
            using System.Threading;
            using System.Threading.Tasks;

            [Service]
            public partial class TestService : ITestService
            {
                public ValueTask<({{returnType}} @return, IAsyncEnumerable<string> @event)> SubscribeAsync(
                    IFeatureCollection features, CancellationToken cancellationToken) => default;
            }
            """;

        AssertGeneratedCodeCompiles(definitions, implementation);
    }

    [Test]
    public void Qualified_names_are_escaped()
    {
        const string definitions = """
            using Google.Protobuf.WellKnownTypes;
            using IceRpc.Features;
            using IceRpc.Protobuf.RpcMethods;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Test.@namespace;

            public static class @class
            {
                public interface @interface
                {
                    [RpcMethod("Echo")]
                    ValueTask<Empty> EchoAsync(
                        Empty input, IFeatureCollection features, CancellationToken cancellationToken);
                }
            }
            """;

        const string implementation = """
            using Google.Protobuf.WellKnownTypes;
            using IceRpc;
            using IceRpc.Features;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Test.@namespace;

            [Service]
            public partial class @struct : @class.@interface
            {
                public ValueTask<Empty> EchoAsync(
                    Empty input, IFeatureCollection features, CancellationToken cancellationToken) => new(input);
            }
            """;

        AssertGeneratedCodeCompiles(definitions, implementation);
    }

    private static void AssertGeneratedCodeCompiles(string definitions, string implementation)
    {
        IEnumerable<string> assemblyPaths =
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Concat(
            [
                typeof(ServiceAttribute).Assembly.Location,
                typeof(Slice.Operations.SliceOperationAttribute).Assembly.Location,
                typeof(Ice.Operations.IceOperationAttribute).Assembly.Location,
                typeof(Protobuf.RpcMethods.RpcMethodAttribute).Assembly.Location,
                typeof(Google.Protobuf.WellKnownTypes.Empty).Assembly.Location,
                typeof(PipeReader).Assembly.Location,
            ]);
        IEnumerable<MetadataReference> references = assemblyPaths.Distinct()
            .Select(path => MetadataReference.CreateFromFile(path));

        var compilation = CSharpCompilation.Create(
            "Service",
            [CSharpSyntaxTree.ParseText(definitions), CSharpSyntaxTree.ParseText(implementation)],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        Assert.That(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ServiceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation output,
            out ImmutableArray<Diagnostic> diagnostics);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(driver.GetRunResult().GeneratedTrees, Has.Length.EqualTo(1));
        Assert.That(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty);
    }
}
