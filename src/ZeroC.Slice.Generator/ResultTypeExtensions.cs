// Copyright (c) ZeroC, Inc.

using ZeroC.Slice.Symbols;

namespace ZeroC.Slice.Generator;

/// <summary>C#-specific extension methods for <see cref="ResultType"/>.</summary>
internal static class ResultTypeExtensions
{
    extension(ResultType result)
    {
        /// <summary>Gets a value indicating whether the success and failure values are wrapped in
        /// <c>ZeroC.Slice.Success&lt;T&gt;</c> and <c>ZeroC.Slice.Failure&lt;T&gt;</c>. The union stores its value as
        /// an object, so it can't tell the two cases apart when they map to the same C# type or when a null value
        /// is possible.</summary>
        internal bool WrapsValues =>
            result.SuccessTypeIsOptional ||
            result.FailureTypeIsOptional ||
            result.SuccessType.FieldTypeString(false, currentNamespace: "") ==
                result.FailureType.FieldTypeString(false, currentNamespace: "");
    }
}
