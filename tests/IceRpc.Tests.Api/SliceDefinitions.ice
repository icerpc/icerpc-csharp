// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::Api
{
    interface GreeterService
    {
        void SayHello();
    }

    interface MiddlewareTestService
    {
        void Op();
    }

    dictionary<string, string> Context;
    interface InvocationInterceptorTestService
    {
        Context opContext();
        int opInt(int value);
    }

    interface ProxyTest
    {
        ProxyTest receiveProxy();
        void sendProxy(ProxyTest proxy);
    }

    interface FeatureService
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
