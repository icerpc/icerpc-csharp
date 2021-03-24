// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

#pragma once

#include <OperationsTests.ice>
#include <EnumTests.ice>

module IceRpc::Tests::ClientServer
{
    struct MyStruct
    {
        int i;
        int j;
    }

    struct AnotherStruct
    {
        string str;
        Operations prx;
        MyEnum en;
        MyStruct st;
    }

    interface StructOperations
    {
        (MyStruct r1, MyStruct r2) opMyStruct(MyStruct p1, MyStruct p2);
        (AnotherStruct r1, AnotherStruct r2) opAnotherStruct(AnotherStruct p1, AnotherStruct p2);
    }
}
