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

        void waitForCancel();
    }

    interface ServerTest
    {
        void callback(ProxyTest callback);
    }

    interface FeatureService
    {
        int compute(int value);
        void fail();
    }
}
