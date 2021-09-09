// Copyright (c) ZeroC, Inc. All rights reserved.

#pragma once

[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::Slice
{
    exception MyExceptionA
    {
        int m1;
    }

    exception MyExceptionB
    {
        int m1;
    }

    interface ExceptionOperations
    {
        void throwA(int a);
        void throwAorB(int a);
        void throwRemoteException();
    }
}
