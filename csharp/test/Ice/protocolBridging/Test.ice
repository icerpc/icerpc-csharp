//
// Copyright (c) ZeroC, Inc. All rights reserved.
//

#pragma once

[[suppress-warning(reserved-identifier)]]

module IceRpc::Test::ProtocolBridging
{
    exception MyError
    {
        int number;
    }

    interface TestIntf
    {
        // Simple operations
        int op(int x);
        void opVoid();

        // Operation with both return and out
        int opReturnOut(int x, out string y);

        // Oneway operation
        [oneway] void opOneway(int x);

        // Operation that throws remote exception
        void opMyError();

        // Operation that throws ServiceNotFoundException (one of the special
        // ice1 system exceptions)
        void opServiceNotFoundException();

        // Operation that returns a new proxy
        TestIntf opNewProxy();

        void shutdown();
    }
}
