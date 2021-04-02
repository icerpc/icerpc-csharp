//
// Copyright (c) ZeroC, Inc. All rights reserved.
//

#pragma once

[cs:namespace(IceRpc.Tests.CodeGeneration.NamespaceMD.WithNamespace)]
module WithNamespace
{
    module N1::N2
    {
        struct S1
        {
            int i;
        }
    }

    class C1
    {
        int i;
    }

    class C2 : C1
    {
        long l;
    }

    exception E1
    {
        int i;
    }

    exception E2 : E1
    {
        long l;
    }
}

// Verify that a top level module which contains __only__ submodules works
[cs:namespace(IceRpc.Tests.CodeGeneration.NamespaceMD.M1)]
module M0::M2::M3
{
    struct S2
    {
        int i;
    }
}

module IceRpc::Tests::CodeGeneration
{
    interface NamespaceMDOperations
    {
        WithNamespace::C1 getWithNamespaceC2AsC1();
        WithNamespace::C2 getWithNamespaceC2AsC2();
        WithNamespace::N1::N2::S1 getWithNamespaceN1N2S1();
        M0::M2::M3::S2 getNestedM0M2M3S2();
        void throwWithNamespaceE1();
        void throwWithNamespaceE2();
    }
}
