// Copyright (c) ZeroC, Inc.

module IceRpc::Ice::CodeGen::Tests
{
    interface Pingable
    {
        void ping();
    }

    interface MyBaseInterface {}

    interface MyDerivedInterface : MyBaseInterface {}
}
