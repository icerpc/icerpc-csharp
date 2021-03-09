// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::ClientServer
{
    sequence<byte> CompressByteSeq;

    exception CompressMyException
    {
        CompressByteSeq bytes;
    }

    interface CompressService
    {
        [compress(args)] void opCompressArgs(int size, CompressByteSeq p1);
        [compress(return)] CompressByteSeq opCompressReturn(int size);
        [compress(args, return)] CompressByteSeq opCompressArgsAndReturn(CompressByteSeq p1);

        void opWithUserException(int size);
    }
}
