// Copyright (c) ZeroC, Inc. All rights reserved.

module IceRpc::Tests::Slice::InterfaceInheritance
{
    // Classic diamond-shaped inheritance

    interface A
    {
        opA(p: A) -> D;
    }

    interface B : A
    {
        opB(p: B) -> B;
    }

    interface C : A, Service
    {
        opC(p: C) -> C;
    }

    interface D : B, C
    {
        opD(p: D) -> A;
    }
}
