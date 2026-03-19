// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;
using ZeroC.CodeBuilder;
using ZeroC.Slice.Symbols;

using static ZeroC.Slice.Generator.OperationExtensions;

namespace ZeroC.Slice.Generator;

/// <summary>Helper methods for generating operation encode/decode code blocks.</summary>
internal static class OperationHelpers
{
    /// <summary>Returns the C# return type for an operation (Task, Task&lt;T&gt;, or Task&lt;tuple&gt;).
    /// Stream returns are included in the tuple with their stream type.</summary>
    internal static string GetClientReturnType(Operation op, string currentNamespace) =>
        BuildReturnType("global::System.Threading.Tasks.Task", op, currentNamespace, fieldType: true);

    /// <summary>Returns the C# return type for a service operation (ValueTask, ValueTask&lt;T&gt;, or
    /// ValueTask&lt;tuple&gt;). Stream returns are included with their outgoing stream type.</summary>
    internal static string GetServiceReturnType(Operation op, string currentNamespace) =>
        BuildReturnType("global::System.Threading.Tasks.ValueTask", op, currentNamespace, fieldType: true);

    /// <summary>Returns the ValueTask return type for a proxy response decode method.</summary>
    internal static string GetProxyResponseReturnType(Operation op, string currentNamespace) =>
        BuildReturnType("global::System.Threading.Tasks.ValueTask", op, currentNamespace, fieldType: false);

    /// <summary>Returns the ValueTask return type for a service request decode method.</summary>
    internal static string GetServiceRequestReturnType(Operation op, string currentNamespace)
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

    /// <summary>Generates the encode body for operation parameters (used in proxy Request.Encode and service
    /// Response.Encode). Returns null for operations with no non-streamed fields to encode.</summary>
    internal static CodeBlock? GenerateEncodeBody(ImmutableList<Field> fields, string currentNamespace) =>
        fields.Count == 0 ?
            null :
            fields.GenerateEncodeBody(currentNamespace, paramPrefix: "", encoderName: "encoder_");

    /// <summary>Generates a decode lambda expression for decoding operation fields (parameters or return values).
    /// For a single field, returns a simple lambda. For multiple fields, returns a lambda with a block body
    /// that decodes each field and returns a tuple.</summary>
    internal static string GenerateDecodeLambda(ImmutableList<Field> fields, string currentNamespace)
    {
        if (fields.Count == 1)
        {
            Field field = fields[0];
            string decodeExpr = field.DataType.DecodeExpression(currentNamespace);
            return $"(ref SliceDecoder decoder) => {decodeExpr}";
        }

        var body = new CodeBlock();
        foreach (Field field in fields)
        {
            string decodeExpr = field.DataType.DecodeExpression(currentNamespace);
            body.WriteLine($"var sliceP_{field.ParameterName} = {decodeExpr};");
        }
        body.WriteLine($"return ({string.Join(", ", fields.Select(f => $"sliceP_{f.ParameterName}"))});");

        return $$"""
            (ref SliceDecoder decoder) =>
            {
                {{body.Indent()}}
            }
            """;
    }

    // --- Private helpers ---

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
}
