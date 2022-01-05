// Copyright (c) ZeroC, Inc. All rights reserved.

module IceRpc::Tests::ClientServer
{

    exception ProtocolBridgingException
    {
        int number;
    }

    interface ProtocolBridgingTest
    {
        // Simple operations
        int op(int x);
        void opVoid();

        // Oneway operation
        [oneway] void opOneway(int x);

        // Operation that throws remote exception
        void opException();

        // Operation that throws ServiceNotFoundException (one of the special ice system exceptions)
        void opServiceNotFoundException();

        // Check the context is correctly forwarded
        void opContext();

        // Operation that returns a new proxy
        ProtocolBridgingTest opNewProxy();
    }
}
