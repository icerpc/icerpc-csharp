// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::ClientServer
{
    exception CompressMyException
    {
        sequence<byte> bytes;
    }

    interface CompressTest
    {
        [compress(args)] void opCompressArgs(int size, sequence<byte> p1);
        [compress(return)] sequence<byte> opCompressReturn(int size);
        [compress(args, return)] sequence<byte> opCompressArgsAndReturn(sequence<byte> p1);
        [compress(args, return)] void opWithUserException(int size);

        [compress(args)] int opCompressStreamArg(stream byte p1);
        [compress(return)] stream byte opCompressReturnStream(int size);
        [compress(args, return)] stream byte opCompressStreamArgAndReturnStream(stream byte p1);
    }
}
