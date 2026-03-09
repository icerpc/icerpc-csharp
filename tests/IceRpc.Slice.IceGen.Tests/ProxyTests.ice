// Copyright (c) ZeroC, Inc.

module IceRpc::Slice::IceGen::Tests
{
    interface Pingable
    {
        void ping();
    }

    interface MyBaseInterface {}

    interface MyDerivedInterface : MyBaseInterface {}
}
