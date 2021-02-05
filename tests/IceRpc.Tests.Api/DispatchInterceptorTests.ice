// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::Api
{
    exception DispatchInterceptorForbiddenException
    {
    }

    interface DispatchInterceptorTestService
    {
        void Op();
    }
}
