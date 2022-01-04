// Copyright (c) ZeroC, Inc. All rights reserved.

module IceRpc::Tests::ClientServer
{
    exception ProtocolBridgingException
    {
        number: int,
    }

    interface ProtocolBridgingTest
    {
        // Simple operations
        op(x: int) -> int;
        opVoid();

        // Oneway operation
        [oneway] opOneway(x: int);

        // Operation that throws remote exception
        opException();

        // Operation that throws ServiceNotFoundException (one of the special ice1 system exceptions)
        opServiceNotFoundException();

        // Check the context is correctly forwarded
        opContext();

        // Operation that returns a new proxy
        opNewProxy() -> ProtocolBridgingTest;
    }
}
