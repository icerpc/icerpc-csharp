// Copyright (c) ZeroC, Inc.

using ZeroC.CodeBuilder;

namespace IceRpc.ServiceGenerator.Internal;

/// <summary>Represents an abstract method in a generated XxxService interface decorated with an IDL-specific attribute.
/// </summary>
internal interface IServiceMethod
{
    /// <summary>Gets the IDL of the source file.</summary>
    Idl Idl { get; }

    /// <summary>Gets the name of the RPC operation, for example: "findObjectById".</summary>
    string OperationName { get; }

    /// <summary>Generates the dispatch case body for this method.</summary>
    /// <returns>The dispatch case body.</returns>
    CodeBlock GenerateDispatchCaseBody();
}
