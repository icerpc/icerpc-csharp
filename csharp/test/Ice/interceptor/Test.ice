//
// Copyright (c) ZeroC, Inc. All rights reserved.
//

#pragma once

[[suppress-warning(reserved-identifier)]]

#include <IceRpc/Context.ice>

module IceRpc::Test::Interceptor
{

sequence<byte> ByteSeq;

struct Token
{
    long expiration;
    string hash;
    ByteSeq payload;
}

exception InvalidInputException
{
    string message;
}

interface MyObject
{
    // A simple addition
    int add(int x, int y);

    // Will throw RetryException until current.Context["retry"] is "no"
    int addWithRetry(int x, int y);

    // Throws remote exception
    int badAdd(int x, int y);
    // Throws ONE
    int notExistAdd(int x, int y);
    void opWithBinaryContext(Token token);

    void op1();

    IceRpc::Context op2();

    int op3();

    void shutdown();
}

}
