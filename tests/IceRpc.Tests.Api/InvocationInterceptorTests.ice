// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::Api
{
    dictionary<string, string> Context;
    interface InvocationInterceptorTestService
    {
        Context opContext();
        int opInt(int value);
    }
}