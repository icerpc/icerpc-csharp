// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice.Generator;

/// <summary>Constants for the C#-specific Slice attribute directives.</summary>
internal static class CsAttributes
{
    /// <summary>The directive for the cs::attribute attribute.</summary>
    internal const string CsAttribute = "cs::attribute";

    /// <summary>The directive for the cs::identifier attribute.</summary>
    internal const string CsIdentifier = "cs::identifier";

    /// <summary>The directive for the cs::internal attribute.</summary>
    internal const string CsInternal = "cs::internal";

    /// <summary>The directive for the cs::namespace attribute.</summary>
    internal const string CsNamespace = "cs::namespace";

    /// <summary>The directive for the cs::readonly attribute.</summary>
    internal const string CsReadonly = "cs::readonly";

    /// <summary>The directive for the cs::type attribute.</summary>
    internal const string CsType = "cs::type";
}
