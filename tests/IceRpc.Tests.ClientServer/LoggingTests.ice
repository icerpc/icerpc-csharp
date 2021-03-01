// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::ClientServer
{
    interface GreeterTestService
    {
        void SayHello();
    }

    interface LoggingTestService
    {
        void op();
    }
}
