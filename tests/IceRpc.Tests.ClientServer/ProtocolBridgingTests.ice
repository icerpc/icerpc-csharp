// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

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

        // Operation that throws ServiceNotFoundException (one of the special ice1 system exceptions)
        void opServiceNotFoundException();

        // Operation that returns a new proxy
        ProtocolBridgingTest opNewProxy();
    }
}
