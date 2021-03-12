//
// Copyright (c) ZeroC, Inc. All rights reserved.
//

#pragma once

[[suppress-warning(reserved-identifier)]]

module IceRpc::Test::NamespaceMD::NoNamespace
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

exception notify /* Test keyword escape. */
{
    int i;
}
}
