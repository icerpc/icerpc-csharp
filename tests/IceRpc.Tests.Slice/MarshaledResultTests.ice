// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::Slice
{
    interface  MarshaledResultOperations
    {
        [marshaled-result] AnotherStruct opAnotherStruct1(AnotherStruct p1);
        [marshaled-result] (AnotherStruct r1, AnotherStruct r2) opAnotherStruct2(AnotherStruct p1);

        [marshaled-result] StringSeq opStringSeq1(StringSeq p1);
        [marshaled-result] (StringSeq r1, StringSeq r2) opStringSeq2(StringSeq p1);

        [marshaled-result] StringDict opStringDict1(StringDict p1);
        [marshaled-result] (StringDict r1, StringDict r2) opStringDict2(StringDict p1);

        [marshaled-result] MyClassA opMyClassA(MyClassA p1);
    }
}
