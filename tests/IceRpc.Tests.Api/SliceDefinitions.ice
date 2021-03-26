// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::Api
{
    interface GreeterService
    {
        void SayHello();
    }

    interface DispatchInterceptorTestService
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
        void sendProxy(ProxyTest proxy);
        ProxyTest receiveProxy();
    }
}
