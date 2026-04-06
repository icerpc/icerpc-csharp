// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;
using ZeroC.CodeBuilder;
using ZeroC.Slice.Generator;
using ZeroC.Slice.Symbols;

namespace IceRpc.Slice.Generator;

/// <summary>Extension methods for <see cref="Operation"/> and <see cref="Interface"/> used by the IceRpc
/// generators.</summary>
internal static class OperationExtensions
{
    extension(Operation op)
    {
        /// <summary>Gets the non-streamed parameters for the operation.</summary>
        internal ImmutableList<Field> NonStreamedParameters =>
            op.HasStreamedParameter
                ? op.Parameters.RemoveAt(op.Parameters.Count - 1)
                : op.Parameters;

        /// <summary>Gets the non-streamed return types for the operation.</summary>
        internal ImmutableList<Field> NonStreamedReturns =>
            op.HasStreamedReturn
                ? op.ReturnType.RemoveAt(op.ReturnType.Count - 1)
                : op.ReturnType;

        /// <summary>Gets the streamed parameter, or null if the operation has no streamed parameter.</summary>
        internal Field? StreamedParameter =>
            op.HasStreamedParameter ? op.Parameters[^1] : null;

        /// <summary>Gets the streamed return, or null if the operation has no streamed return.</summary>
        internal Field? StreamedReturn =>
            op.HasStreamedReturn ? op.ReturnType[^1] : null;

        /// <summary>Gets the escaped name for the injected "features" parameter, appending "_" if any operation
        /// parameter uses the name "features".</summary>
        internal string FeaturesParamName =>
            op.Parameters.Any(p => p.ParameterName == "features") ? "features_" : "features";

        /// <summary>Returns the C# type string for a streamed field (parameter or return).
        /// Non-optional stream uint8 maps to PipeReader, all others to IAsyncEnumerable&lt;T&gt;.</summary>
        internal static string GetStreamTypeString(Field streamField, string currentNamespace)
        {
            if (streamField.IsByteStream)
            {
                return "global::System.IO.Pipelines.PipeReader";
            }
            string elemType = streamField.DataType.FieldTypeString(streamField.DataTypeIsOptional, currentNamespace);
            return $"global::System.Collections.Generic.IAsyncEnumerable<{elemType}>";
        }

        /// <summary>Builds the EncodeStreamOf{Op} method for a streamed field (parameter or return value).</summary>
        internal CodeBlock BuildEncodeStreamMethod(Field streamParam, string currentNamespace)
        {
            string opName = op.Name;
            string streamType = GetStreamTypeString(streamParam, currentNamespace);

            FunctionBuilder fn = new FunctionBuilder(
                "public static",
                "global::System.IO.Pipelines.PipeReader",
                $"EncodeStreamOf{opName}",
                FunctionType.ExpressionBody)
                .AddComment(
                    "summary",
                    $"Encodes the stream argument of operation <c>{op.Name}</c> into a request payload continuation.")
                .AddParameter(streamType, streamParam.ParameterName)
                .AddParameter("SliceEncodeOptions?", "encodeOptions", "null", "The Slice encode options.")
                .AddComment("returns", "A new request payload continuation.");

            if (streamParam.IsByteStream)
            {
                // Non-optional byte stream: pass-through PipeReader
                fn.SetBody(streamParam.ParameterName);
            }
            else
            {
                // Other streams: use ToPipeReader with encode lambda
                string encodeLambda;
                bool containsOptionals;
                if (streamParam.DataTypeIsOptional)
                {
                    // Optional stream elements need inline bool markers
                    encodeLambda = GetStreamEncodeLambda(streamParam, currentNamespace);
                    containsOptionals = true;
                }
                else
                {
                    encodeLambda = streamParam.DataType.GetEncodeLambda(false, currentNamespace);
                    containsOptionals = streamParam.DataType.FixedSize is null;
                }
                fn.SetBody($$"""
                    {{streamParam.ParameterName}}.ToPipeReader(
                        {{encodeLambda}},
                        {{(containsOptionals ? "true" : "false")}},
                        encodeOptions)
                    """);
            }

            return fn.Build();
        }

        /// <summary>Returns the C# return type for an operation (Task, Task&lt;T&gt;, or Task&lt;tuple&gt;).
        /// Stream returns are included in the tuple with their stream type.</summary>
        internal string GetClientReturnType(string currentNamespace) =>
            BuildReturnType("global::System.Threading.Tasks.Task", op, currentNamespace, fieldType: false);

        /// <summary>Returns the C# return type for a service operation. For operations with
        /// <c>cs::encodedReturn</c>, returns <c>PipeReader</c> (or a tuple with PipeReader for streamed returns).
        /// Otherwise returns the standard return type with arrays for sequences.</summary>
        internal string GetServiceReturnType(string currentNamespace)
        {
            if (op.Attributes.HasAttribute(CSAttributes.CSEncodedReturn))
            {
                if (op.StreamedReturn is Field streamReturn)
                {
                    string streamType = GetStreamTypeString(streamReturn, currentNamespace);
                    return $"global::System.Threading.Tasks.ValueTask<(global::System.IO.Pipelines.PipeReader Payload, {streamType} {streamReturn.Name})>";
                }
                return op.NonStreamedReturns.Count > 0
                    ? "global::System.Threading.Tasks.ValueTask<global::System.IO.Pipelines.PipeReader>"
                    : "global::System.Threading.Tasks.ValueTask";
            }
            return BuildServiceReturnTypeCore("global::System.Threading.Tasks.ValueTask", op, currentNamespace);
        }

        /// <summary>Returns the ValueTask return type for a proxy response decode method.</summary>
        internal string GetProxyResponseReturnType(string currentNamespace) =>
            BuildReturnType("global::System.Threading.Tasks.ValueTask", op, currentNamespace, fieldType: false);

        /// <summary>Returns the ValueTask return type for a service request decode method.</summary>
        internal string GetServiceRequestReturnType(string currentNamespace)
        {
            string taskType = "global::System.Threading.Tasks.ValueTask";
            int count = op.NonStreamedParameters.Count + (op.StreamedParameter is not null ? 1 : 0);
            if (count == 0)
            {
                return taskType;
            }
            else
            {
                bool includeNames = count > 1;
                var parts = op.NonStreamedParameters
                    .Select(p =>
                    {
                        string type = p.DataType.IncomingParameterTypeString(p.DataTypeIsOptional, currentNamespace);
                        return includeNames ? $"{type} {p.Name}" : type;
                    })
                    .ToList();

                if (op.StreamedParameter is Field streamParam)
                {
                    string streamType = GetStreamTypeString(streamParam, currentNamespace);
                    parts.Add(includeNames ? $"{streamType} {streamParam.Name}" : streamType);
                }

                return count == 1 ? $"{taskType}<{parts[0]}>" : $"{taskType}<({string.Join(", ", parts)})>";
            }
        }
    }

    extension(ImmutableList<Field> fields)
    {
        /// <summary>Generates the encode body for operation parameters. Returns null for operations with no
        /// non-streamed fields to encode.</summary>
        /// <param name="currentNamespace">The current C# namespace.</param>
        /// <param name="useReadOnlyMemory">When true, fixed-size sequences encode via EncodeSpan (proxy
        /// Request.Encode). When false, uses EncodeSequence (service Response.Encode).</param>
        internal CodeBlock? GenerateEncodeBody(string currentNamespace, bool useReadOnlyMemory = false) =>
            fields.Count == 0 ?
                null :
                fields.GenerateEncodeBody(
                    currentNamespace,
                    paramPrefix: "",
                    encoderName: "encoder_",
                    useReadOnlyMemory: useReadOnlyMemory);

        /// <summary>Generates a decode lambda expression for decoding operation fields (parameters or return values).
        /// For a single non-optional, non-tagged field, returns a simple lambda. Otherwise returns a block body
        /// with bit sequence reader and tagged field handling as needed.</summary>
        internal string GenerateDecodeLambda(string currentNamespace)
        {
            // Single non-optional, non-tagged field: simple expression lambda
            if (fields.Count == 1 && !fields[0].IsTagged && !fields[0].DataTypeIsOptional)
            {
                Field field = fields[0];
                string decodeExpr = field.DataType.DecodeExpression(currentNamespace);
                return $"(ref SliceDecoder decoder) => {decodeExpr}";
            }

            IReadOnlyList<Field> sortedFields = fields.GetSortedFields();
            var body = new CodeBlock();

            int bitSequenceSize = fields.GetBitSequenceSize();
            if (bitSequenceSize > 0)
            {
                body.WriteLine($"var bitSequenceReader = decoder.GetBitSequenceReader({bitSequenceSize});");
            }

            foreach (Field field in sortedFields)
            {
                string decodeExpr = field.GetFieldDecodeExpression(currentNamespace, useIncomingType: true);
                body.WriteLine($"var sliceP_{field.ParameterName} = {decodeExpr};");
            }

            if (fields.Count == 1)
            {
                body.WriteLine($"return sliceP_{sortedFields[0].ParameterName};");
            }
            else
            {
                body.WriteLine($"return ({string.Join(", ", fields.Select(f => $"sliceP_{f.ParameterName}"))});");
            }

            return $$"""
                (ref SliceDecoder decoder) =>
                {
                    {{body.Indent()}}
                }
                """;
        }
    }

    extension(Interface interfaceDef)
    {
        /// <summary>Gets all base interfaces (direct and transitive).</summary>
        internal IEnumerable<Interface> AllBases
        {
            get
            {
                var visited = new HashSet<string>();
                var queue = new Queue<Interface>(interfaceDef.Bases);
                while (queue.Count > 0)
                {
                    Interface baseInterface = queue.Dequeue();
                    if (visited.Add(baseInterface.ScopedIdentifier))
                    {
                        yield return baseInterface;
                        foreach (Interface grandBase in baseInterface.Bases)
                        {
                            queue.Enqueue(grandBase);
                        }
                    }
                }
            }
        }
    }

    private static string BuildReturnType(string taskType, Operation op, string currentNamespace, bool fieldType)
    {
        int count = op.NonStreamedReturns.Count + (op.StreamedReturn is not null ? 1 : 0);
        if (count == 0)
        {
            return taskType;
        }
        else
        {
            bool includeNames = count > 1;

            var parts = op.NonStreamedReturns
                .Select(r =>
                {
                    string type = fieldType
                        ? r.DataType.FieldTypeString(r.DataTypeIsOptional, currentNamespace)
                        : r.DataType.IncomingParameterTypeString(r.DataTypeIsOptional, currentNamespace);
                    return includeNames ? $"{type} {r.Name}" : type;
                })
                .ToList();

            if (op.StreamedReturn is Field streamReturn)
            {
                string streamType = GetStreamTypeString(streamReturn, currentNamespace);
                parts.Add(includeNames ? $"{streamType} {streamReturn.Name}" : streamType);
            }

            return count == 1 ? $"{taskType}<{parts[0]}>" : $"{taskType}<({string.Join(", ", parts)})>";
        }
    }

    /// <summary>Builds a service return type using OutgoingParameterTypeString for non-streamed returns.</summary>
    private static string BuildServiceReturnTypeCore(string taskType, Operation op, string currentNamespace)
    {
        int count = op.NonStreamedReturns.Count + (op.StreamedReturn is not null ? 1 : 0);
        if (count == 0)
        {
            return taskType;
        }

        bool includeNames = count > 1;
        var parts = op.NonStreamedReturns
            .Select(r =>
            {
                string type = r.DataType.OutgoingParameterTypeString(r.DataTypeIsOptional, currentNamespace);
                return includeNames ? $"{type} {r.Name}" : type;
            })
            .ToList();

        if (op.StreamedReturn is Field streamReturn)
        {
            string streamType = OperationExtensions.GetStreamTypeString(streamReturn, currentNamespace);
            parts.Add(includeNames ? $"{streamType} {streamReturn.Name}" : streamType);
        }

        return count == 1 ? $"{taskType}<{parts[0]}>" : $"{taskType}<({string.Join(", ", parts)})>";
    }

    /// <summary>Returns an encode lambda for an optional stream element with inline bool markers.</summary>
    internal static string GetStreamEncodeLambda(Field streamField, string currentNamespace)
    {
        IType elemType = streamField.DataType.Type;
        string csType = streamField.DataType.FieldTypeString(true, currentNamespace);
        string valueExpr = streamField.DataType.IsValueType ? "value!.Value" : "value!";
        string encodeExpr = elemType.EncodeExpression(currentNamespace, valueExpr);
        return $$"""
            (ref SliceEncoder encoder, {{csType}} value) =>
            {
                encoder.EncodeBool(value is not null);
                if (value is not null)
                {
                    {{encodeExpr}};
                }
            }
            """;
    }

    /// <summary>Returns a decode lambda for an optional stream element with inline bool markers.</summary>
    internal static string GetStreamDecodeLambda(Field streamField, string currentNamespace)
    {
        IType elemType = streamField.DataType.Type;
        string csType = elemType.ToTypeString(currentNamespace);
        string decodeExpr = elemType.DecodeExpression(currentNamespace);
        return $"(ref SliceDecoder decoder) => decoder.DecodeBool() ? ({csType}?){decodeExpr} : null";
    }
}
