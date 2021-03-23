// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

#include <C.ice>

module IceRpc::Tests::Internal
{
    class MyClassD : MyClassC
    {
        string dValue;
    }

    exception MyExceptionD : MyExceptionC
    {
        string dValue;
    }
}
