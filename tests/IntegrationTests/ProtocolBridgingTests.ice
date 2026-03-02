// Copyright (c) ZeroC, Inc.

module IceRpc::IntegrationTests
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
        // ["oneway"] TODO: not implemented yet
        void opOneway(int x);

        // Operation that throws an exception
        void opException() throws ProtocolBridgingException;

        // Operation that throws NotFoundException (one of the special ice system exceptions)
        void opNotFoundException();

        // Check the context is correctly forwarded
        void opContext();

        // Operation that returns a new proxy to the same target
        ProtocolBridgingTest* opNewProxy();
    }
}
