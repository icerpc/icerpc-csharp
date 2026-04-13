// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;
using ZeroC.CodeBuilder;
using ZeroC.Slice.Generator;
using ZeroC.Slice.Symbols;

namespace IceRpc.Slice.Generator;

/// <summary>Generates C# server-side code from a Slice interface: the service interface with nested Request/Response
/// classes and operation declarations.</summary>
internal static class DispatchGenerator
{
    /// <summary>Generates all dispatch-side code blocks for the given interface.</summary>
    internal static CodeBlock Generate(Interface interfaceDef)
    {
        string name = interfaceDef.Name;
        string scopedId = interfaceDef.ScopedIdentifier;
        string accessModifier = interfaceDef.AccessModifier;
        string currentNamespace = interfaceDef.Namespace;
        string defaultServicePath = $"/{scopedId.Replace("::", ".", StringComparison.Ordinal)}";

        ContainerBuilder builder = new ContainerBuilder($"{accessModifier} partial interface", $"I{name}Service")
            .AddDocCommentSummary(interfaceDef.Comment, currentNamespace)
            .AddComment(
                "remarks",
                $"""
                The Slice compiler generated this server-side interface from Slice interface <c>{scopedId}</c>.
                Your service implementation must implement this interface.
                """)
            .AddDocCommentSeeAlso(interfaceDef.Comment, currentNamespace)
            .AddDeprecatedAttribute(interfaceDef.Attributes)
            .AddAttribute($"IceRpc.DefaultServicePath(\"{defaultServicePath}\")");

        // Inherit from base service interfaces
        foreach (Interface baseInterface in interfaceDef.Bases)
        {
            string baseName = currentNamespace == baseInterface.Namespace
                ? $"I{baseInterface.Name}Service"
                : $"global::{baseInterface.Namespace}.I{baseInterface.Name}Service";
            builder.AddBase(baseName);
        }

        if (interfaceDef.Operations.Count > 0)
        {
            builder.AddBlock(BuildServiceRequestClass(interfaceDef, scopedId, accessModifier, currentNamespace));
            builder.AddBlock(BuildServiceResponseClass(interfaceDef, scopedId, accessModifier, currentNamespace));

            foreach (Operation op in interfaceDef.Operations)
            {
                builder.AddBlock(BuildServiceOperationDeclaration(op, currentNamespace));
            }
        }

        return builder.Build();
    }

    private static CodeBlock BuildServiceRequestClass(
        Interface interfaceDef,
        string scopedId,
        string accessModifier,
        string currentNamespace)
    {
        // Use "new" keyword when the interface inherits operations from a base (to hide base's Request class)
        bool hasInheritedOps = interfaceDef.AllBases.Any(b => b.Operations.Count > 0);
        string classModifier = hasInheritedOps ? $"{accessModifier} static new class" : $"{accessModifier} static class";
        ContainerBuilder request = new ContainerBuilder(classModifier, "Request")
            .AddComment("summary", "Provides static methods that decode request payloads.")
            .AddComment(
                "remarks",
                $"The Slice compiler generated this static class from the Slice interface <c>{scopedId}</c>.");

        foreach (Operation op in interfaceDef.Operations)
        {
            string opName = op.Name;
            ImmutableList<Field> nonStreamedParams = op.NonStreamedParameters;
            Field? streamParam = op.StreamedParameter;
            string returnType = op.GetServiceRequestReturnType(currentNamespace);

            if (streamParam is null)
            {
                // Non-streaming: expression body
                FunctionBuilder decodeBuilder = new FunctionBuilder(
                    $"{accessModifier} static",
                    returnType,
                    $"Decode{opName}Async",
                    FunctionType.ExpressionBody)
                    .AddComment("summary", $"Decodes the request payload of operation <c>{op.Identifier}</c>.")
                    .AddParameter("IceRpc.IncomingRequest", "request", docComment: "The incoming request.")
                    .AddParameter(
                        "global::System.Threading.CancellationToken",
                        "cancellationToken",
                        docComment: "A cancellation token that receives the cancellation requests.");

                if (nonStreamedParams.Count == 0)
                {
                    decodeBuilder.SetBody("request.DecodeEmptyArgsAsync(cancellationToken)");
                }
                else
                {
                    string decodeLambda = nonStreamedParams.GenerateDecodeLambda(currentNamespace);
                    decodeBuilder.SetBody(new CodeBlock($$"""
                        request.DecodeArgsAsync(
                            {{decodeLambda}},
                            cancellationToken)
                        """));
                }
                request.AddBlock(decodeBuilder.Build());
            }
            else
            {
                // Streaming: async block body
                FunctionBuilder decodeBuilder = new FunctionBuilder(
                    $"{accessModifier} static async",
                    returnType,
                    $"Decode{opName}Async",
                    FunctionType.BlockBody)
                    .AddComment("summary", $"Decodes the request payload of operation <c>{op.Identifier}</c>.")
                    .AddParameter("IceRpc.IncomingRequest", "request", docComment: "The incoming request.")
                    .AddParameter(
                        "global::System.Threading.CancellationToken",
                        "cancellationToken",
                        docComment: "A cancellation token that receives the cancellation requests.");

                var body = new CodeBlock();

                // Decode non-streamed params
                if (nonStreamedParams.Count == 0)
                {
                    body.WriteLine("await request.DecodeEmptyArgsAsync(cancellationToken).ConfigureAwait(false);");
                }
                else
                {
                    string decodeLambda = nonStreamedParams.GenerateDecodeLambda(currentNamespace);
                    string varName = nonStreamedParams.Count == 1 ?
                        $"sliceP_{nonStreamedParams[0].ParameterName}" :
                        $"({string.Join(", ", nonStreamedParams.Select(p => $"sliceP_{p.ParameterName}"))})";
                    body.WriteLine($$"""
                        var {{varName}} = await request.DecodeArgsAsync(
                            {{decodeLambda}},
                            cancellationToken).ConfigureAwait(false);
                        """);
                }

                // Decode stream param
                if (streamParam.IsByteStream)
                {
                    body.WriteLine("var sliceP_stream = IceRpc.IncomingFrameExtensions.DetachPayload(request);");
                }
                else
                {
                    body.WriteLine("var payloadContinuation = IceRpc.IncomingFrameExtensions.DetachPayload(request);");
                    string streamElemType = streamParam.DataType.FieldTypeString(
                        streamParam.DataTypeIsOptional,
                        currentNamespace);
                    string decodeLambda = streamParam.DataTypeIsOptional ?
                        OperationExtensions.GetStreamOfOptionalDecodeLambda(streamParam, currentNamespace) :
                        streamParam.DataType.Type.GetDecodeLambda(isOptional: false, currentNamespace);

                    if (streamParam.DataType.FixedSize is int fixedSize && !streamParam.DataTypeIsOptional)
                    {
                        body.WriteLine($$"""
                            var sliceP_stream = payloadContinuation.ToAsyncEnumerable<{{streamElemType}}>(
                                {{decodeLambda}},
                                {{fixedSize}});
                            """);
                    }
                    else
                    {
                        body.WriteLine($$"""
                            var sliceP_stream = payloadContinuation.ToAsyncEnumerable<{{streamElemType}}>(
                                {{decodeLambda}},
                                sliceFeature: request.Features.Get<IceRpc.Features.ISliceFeature>());
                            """);
                    }
                }

                // Build return
                if (nonStreamedParams.Count == 0)
                {
                    body.WriteLine("return sliceP_stream;");
                }
                else
                {
                    var returnParts = nonStreamedParams.Select(p => $"sliceP_{p.ParameterName}").ToList();
                    returnParts.Add("sliceP_stream");
                    body.WriteLine($"return ({string.Join(", ", returnParts)});");
                }

                decodeBuilder.SetBody(body);
                request.AddBlock(decodeBuilder.Build());
            }
        }

        return request.Build();
    }

    private static CodeBlock BuildServiceResponseClass(
        Interface interfaceDef,
        string scopedId,
        string accessModifier,
        string currentNamespace)
    {
        bool hasInheritedOps = interfaceDef.AllBases.Any(b => b.Operations.Count > 0);
        string classModifier = hasInheritedOps ? $"{accessModifier} static new class" : $"{accessModifier} static class";
        ContainerBuilder response = new ContainerBuilder(classModifier, "Response")
            .AddComment("summary", "Provides static methods that encode return values into response payloads.")
            .AddComment(
                "remarks",
                $"The Slice compiler generated this static class from the Slice interface <c>{scopedId}</c>.");

        foreach (Operation op in interfaceDef.Operations)
        {

            string opName = op.Name;
            ImmutableList<Field> nonStreamedReturns = op.NonStreamedReturns;
            CodeBlock? encodeBody = nonStreamedReturns.GenerateEncodeBody(currentNamespace);

            if (encodeBody is null)
            {
                string emptyPayload = op.HasStreamedReturn ?
                    "System.IO.Pipelines.PipeReader.CreateEmptySliceStructPayload()" :
                    "IceRpc.EmptyPipeReader.Instance";

                response.AddBlock(
                    new FunctionBuilder(
                        $"{accessModifier} static",
                        "global::System.IO.Pipelines.PipeReader",
                        $"Encode{opName}",
                        FunctionType.ExpressionBody)
                        .AddComment(
                            "summary",
                            $"Encodes the return value of operation <c>{op.Identifier}</c> into a response payload.")
                        .AddParameter("SliceEncodeOptions?", "encodeOptions", "null", "The Slice encode options.")
                        .AddComment("returns", "A new response payload.")
                        .SetBody(emptyPayload)
                        .Build());
            }
            else
            {
                FunctionBuilder encodeBuilder = new FunctionBuilder(
                    $"{accessModifier} static",
                    "global::System.IO.Pipelines.PipeReader",
                    $"Encode{opName}",
                    FunctionType.BlockBody)
                    .AddComment(
                        "summary",
                        $"Encodes the return value of operation <c>{op.Identifier}</c> into a response payload.");

                foreach (Field ret in nonStreamedReturns)
                {
                    encodeBuilder.AddParameter(
                        ret.DataType.OutgoingParameterTypeString(ret.DataTypeIsOptional, currentNamespace),
                        ret.ParameterName);
                }

                response.AddBlock(
                    encodeBuilder
                        .AddParameter("SliceEncodeOptions?", "encodeOptions", "null", "The Slice encode options.")
                        .AddComment("returns", "A new response payload.")
                        .SetBody(EncodeHelper.BuildEncodeBody(encodeBody))
                        .Build());
            }

            // Add EncodeStreamOf method for streamed returns
            if (op.StreamedReturn is Field streamReturn)
            {
                response.AddBlock(op.BuildEncodeStreamMethod(streamReturn, currentNamespace));
            }
        }

        return response.Build();
    }

    private static CodeBlock BuildServiceOperationDeclaration(Operation op, string currentNamespace)
    {
        string opName = op.Name;
        ImmutableList<Field> nonStreamedParams = op.NonStreamedParameters;
        string returnType = op.GetServiceReturnType(currentNamespace);

        var operationBuilder = new FunctionBuilder(access: "", returnType, $"{opName}Async", FunctionType.Declaration)
            .AddDocCommentSummary(op.Comment, currentNamespace)
            .AddDeprecatedAttribute(op.Attributes);

        foreach (Field param in nonStreamedParams)
        {
            operationBuilder.AddParameter(
                param.DataType.IncomingParameterTypeString(param.DataTypeIsOptional, currentNamespace),
                param.ParameterName,
                docComment: DocCommentFormatter.FormatOverview(param.Comment, currentNamespace));
        }

        if (op.StreamedParameter is Field streamParam)
        {
            operationBuilder.AddParameter(
                OperationExtensions.GetStreamTypeString(streamParam, currentNamespace),
                streamParam.ParameterName,
                docComment: DocCommentFormatter.FormatOverview(streamParam.Comment, currentNamespace));
        }

        string featuresParam = op.FeaturesParamName;
        operationBuilder.AddParameter(
            "IceRpc.Features.IFeatureCollection",
            featuresParam,
            docComment: "The dispatch features.");
        operationBuilder.AddParameter(
            "global::System.Threading.CancellationToken",
            "cancellationToken",
            docComment: "A cancellation token that receives the cancellation requests.");

        if (op.NonStreamedReturns.Count == 0 && !op.HasStreamedReturn)
        {
            operationBuilder.AddComment("returns", "A value task that completes when this implementation completes.");
        }

        // Build SliceOperation attribute with optional named parameters
        var attrParts = new List<string> { $"\"{op.Identifier}\"" };
        if (op.IsIdempotent)
        {
            attrParts.Add("Idempotent = true");
        }
        if (op.Attributes.HasAttribute(CSAttributes.CSEncodedReturn))
        {
            attrParts.Add("EncodedReturn = true");
        }
        // Check for [compress(Return)] attribute
        if (op.Attributes.FindAttribute("compress") is ZeroC.Slice.Symbols.Attribute compressAttr &&
            compressAttr.Args.Any(a => a == "Return"))
        {
            attrParts.Add("CompressReturn = true");
        }
        operationBuilder
            .AddDocCommentSeeAlso(op.Comment, currentNamespace)
            .AddAttribute($"SliceOperation({string.Join(", ", attrParts)})");
        return operationBuilder.Build();
    }
}
