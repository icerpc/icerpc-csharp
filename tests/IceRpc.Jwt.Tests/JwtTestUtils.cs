// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc;
using IceRpc.Slice;
using System.Buffers;
using System.IO.Pipelines;

internal static class JwtTestUtils
{
    internal static string DecodeJwtField(OutgoingFieldValue jwtField)
    {
        var pipe = new Pipe();
        var encoder = new SliceEncoder(pipe.Writer, SliceEncoding.Slice2);
        jwtField.Encode(ref encoder);
        pipe.Writer.Complete();

        pipe.Reader.TryRead(out ReadResult readResult);
        var decoder = new SliceDecoder(readResult.Buffer, SliceEncoding.Slice2);
        decoder.SkipSize();

        string token = decoder.DecodeString();
        pipe.Reader.Complete();
        return token;
    }

    internal static ReadOnlySequence<byte> EncodeJwtToken(string token)
    {
        var pipe = new Pipe();

        var encoder = new SliceEncoder(pipe.Writer, SliceEncoding.Slice2);
        // File size is encoded in two bytes varuint62
        encoder.EncodeString(token);
        pipe.Writer.Complete();

        pipe.Reader.TryRead(out ReadResult readResult);
        var buffer = new ReadOnlySequence<byte>(readResult.Buffer.ToArray());
        pipe.Reader.Complete();
        return buffer;
    }
}
