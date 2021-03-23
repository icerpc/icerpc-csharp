// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

#include <A.ice>

module IceRpc::Tests::Internal
{
    class MyClassB : MyClassA
    {
        string bValue;
    }

    exception MyExceptionB : MyExceptionA
    {
        string bValue;
    }
}
