// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::DispatchInterceptors
{
    exception ForbiddenException
    {
    }

    interface TestService
    {
        int opInt(int value);
    }
}