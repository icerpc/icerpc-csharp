// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice.Generator;

/// <summary>Constants for the C#-specific Slice attribute directives.</summary>
internal static class CSAttributes
{
    /// <summary>The directive for the <c>>cs::attribute</c> attribute.</summary>
    internal const string CSAttribute = "cs::attribute";

    /// <summary>The directive for the <c>cs::encodedReturn</c> attribute.</summary>
    internal const string CSEncodedReturn = "cs::encodedReturn";

    /// <summary>The directive for the <c>cs::identifier</c> attribute.</summary>
    internal const string CSIdentifier = "cs::identifier";

    /// <summary>The directive for the <c>cs::internal</c> attribute.</summary>
    internal const string CSInternal = "cs::internal";

    /// <summary>The directive for the <c>cs::namespace</c> attribute.</summary>
    internal const string CSNamespace = "cs::namespace";

    /// <summary>The directive for the <c>cs::readonly</c> attribute.</summary>
    internal const string CSReadonly = "cs::readonly";

    /// <summary>The directive for the <c>cs::type</c> attribute.</summary>
    internal const string CSType = "cs::type";
}
