// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

#include <IceRpc/BuiltinSequences.ice>

module IceRpc::Tests::ClientServer
{
    exception CompressMyException
    {
        IceRpc::ByteSeq bytes;
    }

    interface CompressService
    {
        [compress(args)] void opCompressArgs(int size, IceRpc::ByteSeq p1);
        [compress(return)] IceRpc::ByteSeq opCompressReturn(int size);
        [compress(args, return)] IceRpc::ByteSeq opCompressArgsAndReturn(IceRpc::ByteSeq p1);
        [compress(args, return)] void opWithUserException(int size);
    }
}
