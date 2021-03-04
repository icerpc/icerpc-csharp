//
// Copyright (c) ZeroC, Inc. All rights reserved.
//

#pragma once

#include <IceRpc/BuiltinSequences.ice>
#include <IceRpc/Identity.ice>

[[suppress-warning(reserved-identifier)]]

module IceRpc::Test::AMI
{

exception TestIntfException
{
}

enum CloseMode
{
    Forcefully,
    Gracefully
}

interface TestIntf
{
    void op();
    void opWithPayload(IceRpc::ByteSeq seq);
    int opWithResult();
    void opWithUE();
    void close(CloseMode mode);
    void sleep(int ms);
    [amd] void startDispatch();
    void finishDispatch();
    void shutdown();

    bool supportsAMD();
    bool supportsFunctionalTests();

    [amd] void opAsyncDispatch();
    [amd] int opWithResultAsyncDispatch();
    [amd] void opWithUEAsyncDispatch();

    int set(int value);
    [oneway] void setOneway(int previous, int value);
}

module Outer::Inner
{
    interface TestIntf
    {
        (int r1, int r2) op(int i);
    }
}

}
