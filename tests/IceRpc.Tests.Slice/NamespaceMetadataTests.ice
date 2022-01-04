// Copyright (c) ZeroC, Inc. All rights reserved.

[cs:namespace("IceRpc.Tests.Slice.NamespaceMD.WithNamespace")]
module WithNamespace
{
    module N1::N2
    {
        struct S1
        {
            i: int,
        }
    }

    class C1
    {
        i: int,
    }

    class C2 : C1
    {
        l: long,
    }

    exception E1
    {
        i: int,
    }

    exception E2 : E1
    {
        l: long,
    }
}

// Verify that a top level module which contains __only__ submodules works
[cs:namespace("IceRpc.Tests.Slice.NamespaceMD.M1")]
module M0
{
    module M2::M3
    {
        struct S2
        {
            i: int,
        }
    }
}

module IceRpc::Tests::Slice
{
    interface NamespaceMDOperations
    {
        getWithNamespaceC2AsC1() -> WithNamespace::C1;
        getWithNamespaceC2AsC2() -> WithNamespace::C2;
        getWithNamespaceN1N2S1() -> WithNamespace::N1::N2::S1;
        getNestedM0M2M3S2() -> M0::M2::M3::S2;
        throwWithNamespaceE1();
        throwWithNamespaceE2();
    }
}
