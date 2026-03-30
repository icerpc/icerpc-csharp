// Copyright (c) ZeroC, Inc.

module IceRpc::Ice::Generator::Tests
{
    interface Pingable
    {
        void ping();
    }

    interface MyBaseInterface {}

    interface MyDerivedInterface : MyBaseInterface {}

    interface ReceiveProxyTest
    {
        ReceiveProxyTest* receiveProxy();
    }

    interface SendProxyTest
    {
        void sendProxy(SendProxyTest* proxy);
    }

    interface SendTaggedProxyTest
    {
        void sendTaggedProxy(optional(0) SendTaggedProxyTest* proxy);
    }
}
