// Copyright (c) ZeroC, Inc.

using IceRpc.Slice.Generators.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Text;

namespace IceRpc.Slice.Generators;

/// <summary>Provides a generator to implement IceRpc.IDispatcher for classes annotated with
/// IceRpc.Slice.SliceServiceAttribute.</summary>
[Generator]
public class ServiceGenerator : IIncrementalGenerator
{
    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<ClassDeclarationSyntax> classDeclarations =
            context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    "IceRpc.Slice.SliceServiceAttribute",
                    (node, _) => node is ClassDeclarationSyntax,
                    (context, _) => (ClassDeclarationSyntax)context.TargetNode);

        IncrementalValueProvider<(Compilation Compilation, ImmutableArray<ClassDeclarationSyntax> ClassDeclarations)> compilationAndClasses =
            context.CompilationProvider.Combine(classDeclarations.Collect());

        context.RegisterSourceOutput(
            compilationAndClasses,
            static (context, source) => Execute(context, source.Compilation, source.ClassDeclarations));
    }

    private static void Execute(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<ClassDeclarationSyntax> classes)
    {
        if (classes.IsDefaultOrEmpty)
        {
            return;
        }

        IEnumerable<ClassDeclarationSyntax> distinctClasses = classes.Distinct();

        var parser = new Parser(compilation, context.ReportDiagnostic, context.CancellationToken);
        IReadOnlyList<ServiceClass> serviceClasses = parser.GetServiceDefinitions(distinctClasses);
        if (serviceClasses.Count > 0)
        {
            var emitter = new Emitter();
            string result = emitter.Emit(serviceClasses, context.CancellationToken);
            context.AddSource("Service.g.cs", SourceText.From(result, Encoding.UTF8));
        }
    }
}
