// Copyright (c) ZeroC, Inc.

using ZeroC.Slice.Symbols;

namespace ZeroC.Slice.Generator;

/// <summary>C#-specific extension methods for <see cref="ResultType"/>.</summary>
internal static class ResultTypeExtensions
{
    extension(ResultType result)
    {
        /// <summary>Gets a value indicating whether the success and failure values are wrapped in
        /// <c>ZeroC.Slice.Success&lt;T&gt;</c> and <c>ZeroC.Slice.Failure&lt;T&gt;</c>. The union tells its two cases
        /// apart by their runtime types, so it needs the wrappers when a case is optional (null) or when both cases
        /// are the same type. The <c>cs::wrap</c> attribute on a reference to this type also forces the
        /// wrapping.</summary>
        internal bool WrapsValues =>
            result.SuccessTypeIsOptional ||
            result.FailureTypeIsOptional ||
            result.SuccessType.FieldTypeString(false, currentNamespace: "") ==
                result.FailureType.FieldTypeString(false, currentNamespace: "");
    }
}
