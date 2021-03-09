// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::CodeGeneration
{
    [cs:generic(List)] sequence<byte> LByteS;

    interface SequenceMappingTestService
    {
        (LByteS r1, LByteS r2) opLByteS(LByteS i);
    }
}
