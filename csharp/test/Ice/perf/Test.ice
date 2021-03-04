//
// Copyright (c) ZeroC, Inc. All rights reserved.
//

#pragma once

[[suppress-warning(reserved-identifier)]]

module ZeroC::Ice::Test::Perf
{

sequence<byte> ByteSeq;

interface Performance
{
    void sendBytes(ByteSeq bytes);
    ByteSeq receiveBytes(int size);
    void shutdown();
}

}
