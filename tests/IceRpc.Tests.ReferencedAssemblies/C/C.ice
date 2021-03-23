// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

#include <B.ice>

module IceRpc::Tests::Internal
{
    class MyClassC : MyClassB
    {
        string cValue;
    }

    exception MyExceptionC : MyExceptionB
    {
        string cValue;
    }
}
