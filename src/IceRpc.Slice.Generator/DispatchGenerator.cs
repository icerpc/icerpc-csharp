// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;
using ZeroC.CodeBuilder;
using ZeroC.Slice.Symbols;

using static ZeroC.Slice.Generator.OperationHelpers;

namespace ZeroC.Slice.Generator;

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

        var builder = new ContainerBuilder($"{accessModifier} partial interface", $"I{name}Service");
        builder.AddComment(
            "remarks",
            $"""
            The Slice compiler generated this server-side interface from Slice interface <c>{scopedId}</c>.
            Your service implementation must implement this interface.
            """);
        builder.AddAttribute($"""IceRpc.DefaultServicePath("{defaultServicePath}")""");

        // Inherit from base service interfaces
        foreach (Interface baseInterface in interfaceDef.Bases)
        {
            builder.AddBase($"I{baseInterface.Name}Service");
        }

        builder.AddBlock(BuildServiceRequestClass(interfaceDef, scopedId, currentNamespace));
        builder.AddBlock(BuildServiceResponseClass(interfaceDef, scopedId, currentNamespace));

        foreach (Operation op in interfaceDef.Operations)
        {
            builder.AddBlock(BuildServiceOperationDeclaration(op, currentNamespace));
        }

        return builder.Build();
    }

    private static CodeBlock BuildServiceRequestClass(Interface interfaceDef, string scopedId, string currentNamespace)
    {
        // Use "new" keyword when the interface inherits operations from a base (to hide base's Request class)
        bool hasInheritedOps = interfaceDef.Bases.Any(b => b.Operations.Count > 0 || b.AllBases.Any(bb => bb.Operations.Count > 0));
        string classModifier = hasInheritedOps ? "public static new class" : "public static class";
        var request = new ContainerBuilder(classModifier, "Request");
        request.AddComment("summary", "Provides static methods that decode request payloads.");
        request.AddComment(
            "remarks",
            $"The Slice compiler generated this static class from the Slice interface <c>{scopedId}</c>.");

        foreach (Operation op in interfaceDef.Operations)
        {
            string opName = op.Name;
            ImmutableList<Field> nonStreamedParams = op.NonStreamedParameters;
            Field? streamParam = op.StreamedParameter;
            string returnType = GetServiceRequestReturnType(op, currentNamespace);

            if (streamParam is null)
            {
                // Non-streaming: expression body
                var fn = new FunctionBuilder(
                    "public static",
                    returnType,
                    $"Decode{opName}Async",
                    FunctionType.ExpressionBody);
                fn.AddComment("summary", $"Decodes the request payload of operation <c>{op.Identifier}</c>.");
                fn.AddParameter("IceRpc.IncomingRequest", "request", docComment: "The incoming request.");
                fn.AddParameter(
                    "global::System.Threading.CancellationToken",
                    "cancellationToken",
                    docComment: "A cancellation token that receives the cancellation requests.");

                if (nonStreamedParams.Count == 0)
                {
                    fn.SetBody("request.DecodeEmptyArgsAsync(cancellationToken)");
                }
                else
                {
                    string decodeLambda = GenerateDecodeLambda(nonStreamedParams, currentNamespace);
                    fn.SetBody(new CodeBlock($$"""
                        request.DecodeArgsAsync(
                            {{decodeLambda}},
                            cancellationToken)
                        """));
                }

                request.AddBlock(fn.Build());
            }
            else
            {
                // Streaming: async block body
                var fn = new FunctionBuilder(
                    "public static async",
                    returnType,
                    $"Decode{opName}Async",
                    FunctionType.BlockBody);
                fn.AddComment("summary", $"Decodes the request payload of operation <c>{op.Identifier}</c>.");
                fn.AddParameter("IceRpc.IncomingRequest", "request", docComment: "The incoming request.");
                fn.AddParameter(
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
                    string decodeLambda = GenerateDecodeLambda(nonStreamedParams, currentNamespace);
                    string varName = nonStreamedParams.Count == 1
                        ? $"sliceP_{nonStreamedParams[0].ParameterName}"
                        : $"({string.Join(", ", nonStreamedParams.Select(p => $"sliceP_{p.ParameterName}"))})";
                    body.WriteLine($$"""
                        var {{varName}} = await request.DecodeArgsAsync(
                            {{decodeLambda}},
                            cancellationToken).ConfigureAwait(false);
                        """);
                }

                // Decode stream param
                if (OperationExtensions.IsByteStream(streamParam))
                {
                    body.WriteLine("var sliceP_stream = IceRpc.IncomingFrameExtensions.DetachPayload(request);");
                }
                else
                {
                    body.WriteLine("var payloadContinuation = IceRpc.IncomingFrameExtensions.DetachPayload(request);");
                    string streamElemType = streamParam.DataType.FieldTypeString(streamParam.DataTypeIsOptional, currentNamespace);
                    string decodeLambda = streamParam.DataType.Type.GetDecodeLambda(streamParam.DataTypeIsOptional, currentNamespace);

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
                    body.Write("\nreturn sliceP_stream;");
                }
                else
                {
                    var returnParts = nonStreamedParams.Select(p => $"sliceP_{p.ParameterName}").ToList();
                    returnParts.Add("sliceP_stream");
                    body.Write($"\nreturn ({string.Join(", ", returnParts)});");
                }

                fn.SetBody(body);
                request.AddBlock(fn.Build());
            }
        }

        return request.Build();
    }

    private static CodeBlock BuildServiceResponseClass(Interface interfaceDef, string scopedId, string currentNamespace)
    {
        bool hasInheritedOps = interfaceDef.Bases.Any(b => b.Operations.Count > 0 || b.AllBases.Any(bb => bb.Operations.Count > 0));
        string classModifier = hasInheritedOps ? "public static new class" : "public static class";
        var response = new ContainerBuilder(classModifier, "Response");
        response.AddComment("summary", "Provides static methods that encode return values into response payloads.");
        response.AddComment(
            "remarks",
            $"The Slice compiler generated this static class from the Slice interface <c>{scopedId}</c>.");

        foreach (Operation op in interfaceDef.Operations)
        {

            string opName = op.Name;
            ImmutableList<Field> nonStreamedReturns = op.NonStreamedReturns;
            CodeBlock? encodeBody = GenerateEncodeBody(nonStreamedReturns, currentNamespace);

            if (encodeBody is null)
            {
                string emptyPayload = op.HasStreamedReturn
                    ? "System.IO.Pipelines.PipeReader.CreateEmptySliceStructPayload()"
                    : "IceRpc.EmptyPipeReader.Instance";

                var fn = new FunctionBuilder(
                    "public static",
                    "global::System.IO.Pipelines.PipeReader",
                    $"Encode{opName}",
                    FunctionType.ExpressionBody);
                fn.AddComment("summary", $"Encodes the return value of operation <c>{op.Identifier}</c> into a response payload.");
                fn.AddParameter("SliceEncodeOptions?", "encodeOptions", "null", "The Slice encode options.");
                fn.AddComment("returns", "A new response payload.");
                fn.SetBody(emptyPayload);
                response.AddBlock(fn.Build());
            }
            else
            {
                var fn = new FunctionBuilder(
                    "public static",
                    "global::System.IO.Pipelines.PipeReader",
                    $"Encode{opName}",
                    FunctionType.BlockBody);
                fn.AddComment("summary", $"Encodes the return value of operation <c>{op.Identifier}</c> into a response payload.");

                foreach (Field ret in nonStreamedReturns)
                {
                    fn.AddParameter(
                        ret.DataType.FieldTypeString(ret.DataTypeIsOptional, currentNamespace),
                        ret.ParameterName);
                }

                fn.AddParameter("SliceEncodeOptions?", "encodeOptions", "null", "The Slice encode options.");
                fn.AddComment("returns", "A new response payload.");
                fn.SetBody(ProxyGenerator.BuildPipeEncodeBody(encodeBody));
                response.AddBlock(fn.Build());
            }

            // Add EncodeStreamOf method for streamed returns
            if (op.StreamedReturn is Field streamReturn)
            {
                response.AddBlock(ProxyGenerator.BuildEncodeStreamMethod(op, streamReturn, currentNamespace));
            }
        }

        return response.Build();
    }

    private static CodeBlock BuildServiceOperationDeclaration(Operation op, string currentNamespace)
    {
        string opName = op.Name;
        ImmutableList<Field> nonStreamedParams = op.NonStreamedParameters;
        string returnType = GetServiceReturnType(op, currentNamespace);

        var fn = new FunctionBuilder("public", returnType, $"{opName}Async", FunctionType.Declaration);

        foreach (Field param in nonStreamedParams)
        {
            fn.AddParameter(
                param.DataType.FieldTypeString(param.DataTypeIsOptional, currentNamespace),
                param.ParameterName);
        }

        if (op.StreamedParameter is Field streamParam)
        {
            fn.AddParameter(OperationExtensions.GetStreamTypeString(streamParam, currentNamespace), streamParam.ParameterName);
        }

        string featuresParam = op.FeaturesParamName;
        fn.AddParameter("IceRpc.Features.IFeatureCollection", featuresParam, docComment: "The dispatch features.");
        fn.AddParameter(
            "global::System.Threading.CancellationToken",
            "cancellationToken",
            docComment: "A cancellation token that receives the cancellation requests.");

        if (op.NonStreamedReturns.Count == 0 && !op.HasStreamedReturn)
        {
            fn.AddComment("returns", "A value task that completes when this implementation completes.");
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
        if (op.Attributes.FindAttribute("compress") is { } compressAttr
            && compressAttr.Args.Any(a => a == "Return"))
        {
            attrParts.Add("CompressReturn = true");
        }
        fn.AddAttribute($"SliceOperation({string.Join(", ", attrParts)})");
        return fn.Build();
    }
}
