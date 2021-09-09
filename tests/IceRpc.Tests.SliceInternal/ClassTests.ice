// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::SliceInternal
{
    class MyClassCustomFormat
    {
        string s;
    }

    [format(compact)]
    interface CompactFormatOperations
    {
        MyClassCustomFormat OpMyClass(MyClassCustomFormat p1);
    }

    [format(sliced)]
    interface SlicedFormatOperations
    {
        MyClassCustomFormat OpMyClass(MyClassCustomFormat p1);
    }

    interface ClassFormatOperations
    {
        MyClassCustomFormat OpMyClass(MyClassCustomFormat p1);
        [format(sliced)] MyClassCustomFormat OpMyClassSlicedFormat(MyClassCustomFormat p1);
    }

    class Recursive
    {
        Recursive? v;
    }

    interface ClassGraphOperations
    {
        void sendClassGraph(Recursive p1);
        Recursive receiveClassGraph(int size);
    }
}
