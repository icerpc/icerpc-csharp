// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;
using ZeroC.Slice.Symbols;

namespace ZeroC.Slice.Generator;

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
            string elemType = streamField.DataType.FieldTypeString(streamField.DataTypeIsOptional, currentNamespace);
            if (elemType == "byte" && !streamField.DataTypeIsOptional)
            {
                return "global::System.IO.Pipelines.PipeReader";
            }
            return $"global::System.Collections.Generic.IAsyncEnumerable<{elemType}>";
        }

        /// <summary>Returns true if the streamed field is a raw byte stream (non-optional uint8).</summary>
        internal static bool IsByteStream(Field streamField) =>
            streamField.DataType.Type is Builtin b && b.Kind == BuiltinKind.UInt8 && !streamField.DataTypeIsOptional;
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
}
