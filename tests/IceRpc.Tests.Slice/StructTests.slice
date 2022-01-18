// Copyright (c) ZeroC, Inc. All rights reserved.

module IceRpc::Tests::Slice
{
    struct MyStruct
    {
        i: int,
        j: int,
    }

    struct AnotherStruct
    {
        str: string,
        prx: Operations,
        en: MyEnum,
        st: MyStruct,
    }

    interface StructOperations
    {
        opMyStruct(p1: MyStruct, p2: MyStruct) -> (r1: MyStruct, r2: MyStruct);
        opAnotherStruct(p1: AnotherStruct, p2: AnotherStruct) -> (r1: AnotherStruct, r2: AnotherStruct);
    }
}
