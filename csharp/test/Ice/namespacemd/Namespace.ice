//
// Copyright (c) ZeroC, Inc. All rights reserved.
//

#pragma once

[cs:namespace(IceRpc.Test.NamespaceMD.WithNamespace)]
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

// Verifys that a top level module which contains __only__ submodules works
[cs:namespace(IceRpc.Test.NamespaceMD.M1)]
module M0::M2::M3
{
    struct S2
    {
        int i;
    }
}
