// Copyright (c) ZeroC, Inc. All rights reserved.

module IceRpc::Tests::Slice::InterfaceInheritance
{
    // Classic diamond-shaped inheritance

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
