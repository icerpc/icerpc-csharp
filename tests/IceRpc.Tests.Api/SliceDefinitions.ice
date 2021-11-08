// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::Api
{
    interface Greeter
    {
        void SayHello();
    }

    interface InterceptorTest
    {
        Context opContext();
        int opInt(int value);
    }

    interface ProxyTest
    {
        ProxyTest? receiveProxy();
        void sendProxy(ProxyTest proxy);
    }

    interface FeatureTest
    {
        int compute(int value);
        void failWithRemote();
        void failWithUnhandled();
    }

    interface BaseA {}
    interface DerivedA : BaseA {}
    interface MostDerivedA : DerivedA {}

    interface BaseB {}
    interface DerivedB : BaseB, BaseA {}
    interface MostDerivedB : DerivedB, DerivedA {}

    interface BaseC {}
    interface DerivedC : BaseC, BaseB, BaseA {}
    interface MostDerivedC : DerivedC, DerivedB, DerivedA {}
}
