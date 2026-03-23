// Copyright (c) ZeroC, Inc.

["cs:identifier:IceRpc.Ice.Generator.Tests.CsIdentifierAttribute.MappedNamespace"]
module WithCsIdentifier
{
    struct S1
    {
        int i;
    }
}

module IceRpc::Ice::Generator::Tests
{
    interface CsIdentifierOperations
    {
        WithCsIdentifier::S1 op1(WithCsIdentifier::S1 p);
    }
}
