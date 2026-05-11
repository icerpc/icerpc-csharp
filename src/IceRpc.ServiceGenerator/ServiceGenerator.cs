// Copyright (c) ZeroC, Inc.

using IceRpc.ServiceGenerator.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Text;

namespace IceRpc.ServiceGenerator;

/// <summary>Provides a generator to implement <c>IceRpc.IDispatcher</c> for classes annotated with
/// the <c>IceRpc.ServiceAttribute</c> attribute.</summary>
[Generator]
public class ServiceGenerator : IIncrementalGenerator
{
    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // We accept any TypeDeclarationSyntax (in practice, a class or record class, since ServiceAttribute's
        // AttributeUsage restricts it to classes) so that record classes are picked up alongside plain classes.
        IncrementalValuesProvider<TypeDeclarationSyntax> typeDeclarations =
            context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    Parser.ServiceAttribute,
                    (node, _) => node is TypeDeclarationSyntax,
                    (context, _) => (TypeDeclarationSyntax)context.TargetNode);

        IncrementalValueProvider<(Compilation Compilation, ImmutableArray<TypeDeclarationSyntax> TypeDeclarations)> compilationAndTypes =
            context.CompilationProvider.Combine(typeDeclarations.Collect());

        context.RegisterSourceOutput(
            compilationAndTypes,
            static (context, source) => Execute(context, source.Compilation, source.TypeDeclarations));
    }

    private static void Execute(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<TypeDeclarationSyntax> types)
    {
        if (types.IsDefaultOrEmpty)
        {
            return;
        }

        var parser = new Parser(compilation, context.ReportDiagnostic, context.CancellationToken);
        var emitter = new Emitter();
        foreach (ServiceClass serviceClass in parser.GetServiceDefinitions(types.Distinct()))
        {
            string result = emitter.Emit(serviceClass, context.CancellationToken);
            string fullName = serviceClass.ContainingNamespace is null ?
                serviceClass.FullName :
                $"{serviceClass.ContainingNamespace}.{serviceClass.FullName}";

            context.AddSource($"{fullName}.g.cs", SourceText.From(result, Encoding.UTF8));
        }
    }
}
