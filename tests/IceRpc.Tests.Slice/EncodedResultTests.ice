// Copyright (c) ZeroC, Inc. All rights reserved.

module IceRpc::Tests::Slice
{
    interface EncodedResultOperations
    {
        [cs:encoded-result] opAnotherStruct1(p1: AnotherStruct) -> AnotherStruct;
        [cs:encoded-result] opAnotherStruct2(p1: AnotherStruct) -> (r1: AnotherStruct, r2: AnotherStruct);

        [cs:encoded-result] opStringSeq1(p1: StringSeq) -> StringSeq;
        [cs:encoded-result] opStringSeq2(p1: StringSeq) -> (r1: StringSeq, r2: StringSeq);

        [cs:encoded-result] opStringDict1(p1: StringDict) -> StringDict;
        [cs:encoded-result] opStringDict2(p1: StringDict) -> (r1: StringDict, r2: StringDict);

        [cs:encoded-result] opMyClassA(p1: MyClassA) -> MyClassA;
    }
}
