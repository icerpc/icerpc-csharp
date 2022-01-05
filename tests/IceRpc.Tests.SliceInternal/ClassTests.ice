// Copyright (c) ZeroC, Inc. All rights reserved.

module IceRpc::Tests::SliceInternal
{
    class MyClassCustomFormat
    {
        s: string,
    }

    [format(compact)]
    interface CompactFormatOperations
    {
        OpMyClass(p1: MyClassCustomFormat) -> MyClassCustomFormat;
    }

    [format(sliced)]
    interface SlicedFormatOperations
    {
        OpMyClass(p1: MyClassCustomFormat) -> MyClassCustomFormat;
    }

    interface ClassFormatOperations
    {
        OpMyClass(p1: MyClassCustomFormat) -> MyClassCustomFormat;
        [format(sliced)] OpMyClassSlicedFormat(p1: MyClassCustomFormat) -> MyClassCustomFormat;
    }

    class Recursive
    {
        v: Recursive?,
    }

    interface ClassGraphOperations
    {
        sendClassGraph(p1: Recursive);
        receiveClassGraph(size: int) -> Recursive;
    }
}
