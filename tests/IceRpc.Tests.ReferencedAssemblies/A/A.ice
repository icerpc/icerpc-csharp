// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::Api
{
    class MyClassA
    {
        string aValue;
    }

    class MyCompactClassA(1)
    {
        string aValue;
    }

    exception MyExceptionA
    {
        string aValue;
    }
}
