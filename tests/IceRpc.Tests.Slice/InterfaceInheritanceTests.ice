// Copyright (c) ZeroC, Inc. All rights reserved.

#pragma once

[[suppress-warning(reserved-identifier)]]

#include <IceRpc/Service.ice>

module IceRpc::Tests::Slice::InterfaceInheritance
{
    // Classic diamond-shaped inheritance

    interface D;

    interface A
    {
        D opA(A p);
    }

    interface B : A
    {
        B opB(B p);
    }

    interface C : A, Service
    {
        C opC(C p);
    }

    interface D : B, C
    {
        A opD(D p);
    }
}
