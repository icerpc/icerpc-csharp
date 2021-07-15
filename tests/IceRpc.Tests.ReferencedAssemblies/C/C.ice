// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

#include <B.ice>

module IceRpc::Tests::Api
{
    class MyClassC : MyClassB
    {
        string cValue;
    }

    class MyCompactClassC(3) : MyCompactClassB
    {
        string cValue;
    }

    exception MyExceptionC : MyExceptionB
    {
        string cValue;
    }
}
