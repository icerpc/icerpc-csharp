// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

#include <A.ice>

module IceRpc::Tests::Api
{
    class MyClassB : MyClassA
    {
        string bValue;
    }

    class MyCompactClassB(2) : MyCompactClassA
    {
        string bValue;
    }

    exception MyExceptionB : MyExceptionA
    {
        string bValue;
    }
}
