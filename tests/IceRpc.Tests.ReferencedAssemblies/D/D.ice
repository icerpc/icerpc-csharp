// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

#include <C.ice>

module IceRpc::Tests::Api
{
    class MyClassD : MyClassC
    {
        string dValue;
    }

    class MyCompactClassD(4) : MyCompactClassC
    {
        string dValue;
    }

    exception MyExceptionD : MyExceptionC
    {
        string dValue;
    }
}
