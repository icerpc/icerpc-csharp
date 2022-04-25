// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice.Tests;

public record struct MyCustomType
{
    public bool flag;
    public int value;
}

public static class SliceEncoderCustomTypeExtensions
{
    public static void EncodeCustomType(this ref SliceEncoder encoder, MyCustomType myCustom)
    {
        encoder.EncodeBool(myCustom.flag);
        encoder.EncodeInt32(myCustom.value);
    }
}

public static class SliceDecoderCustomTypeExtensions
{
    public static MyCustomType DecodeCustomType(this ref SliceDecoder decoder)
    {
        MyCustomType myCustom;
        myCustom.flag = decoder.DecodeBool();
        myCustom.value = decoder.DecodeInt32();
        return myCustom;
    }
}
