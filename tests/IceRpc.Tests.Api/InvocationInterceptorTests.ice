// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::Api
{
    interface InvocationInterceptorTestService
    {
        int opInt(int value);
    }
}