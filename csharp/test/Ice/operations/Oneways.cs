// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Test.Operations
{
    public static class Oneways
    {
        internal static void Run(IMyClassPrx p)
        {
            p = p.Clone(oneway: true);
            p.OpVoid();
            p.OpIdempotent();
            p.OpOneway();
            p.OpOnewayMetadata();
            p.OpByte(0xff, 0x0f);
        }
    }
}
