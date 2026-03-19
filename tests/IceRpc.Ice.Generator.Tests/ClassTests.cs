// Copyright (c) ZeroC, Inc.

using IceRpc.Ice.Codec;
using NUnit.Framework;
using System.IO.Pipelines;
namespace IceRpc.Ice.Generator.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class ClassTests
{
    [Test]
    public void Operation_request_with_compact_format([Values] bool anyClass)
    {
        // Act
        var payload = anyClass ?
            ClassOperationsProxy.Request.EncodeOpAnyClassCompact(new MyClassB()) :
            ClassOperationsProxy.Request.EncodeOpMyClassCompact(new MyClassB());

        // Assert
        Assert.That(payload.TryRead(out ReadResult readResult), Is.True);
        Assert.That(readResult.IsCompleted, Is.True);
        var decoder = new IceDecoder(readResult.Buffer);

        // MyClassB instance encoded with compact format (2 Slices)

        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // Instance marker

        // First Slice
        Assert.That(decoder.DecodeByte(), Is.EqualTo((byte)IceEncodingDefinitions.TypeIdKind.String));
        Assert.That(decoder.DecodeString(), Is.EqualTo(typeof(MyClassB).GetIceTypeId()));
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null instance

        // Second Slice
        Assert.That(decoder.DecodeByte(), Is.EqualTo((byte)IceEncodingDefinitions.SliceFlags.IsLastSlice));
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null instance
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null instance
        Assert.That(decoder.Consumed, Is.EqualTo(readResult.Buffer.Length));
    }

    [Test]
    public void Operation_request_with_sliced_format([Values] bool anyClass)
    {
        // Act
        var payload = anyClass ?
            ClassOperationsProxy.Request.EncodeOpAnyClassSliced(new MyClassB()) :
            ClassOperationsProxy.Request.EncodeOpMyClassSliced(new MyClassB());

        // Assert
        Assert.That(payload.TryRead(out ReadResult readResult), Is.True);
        Assert.That(readResult.IsCompleted, Is.True);
        var decoder = new IceDecoder(readResult.Buffer);

        // MyClassB instance encoded with sliced format (2 Slices)

        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // Instance marker

        // First Slice
        Assert.That(
            decoder.DecodeByte(),
            Is.EqualTo((byte)IceEncodingDefinitions.TypeIdKind.String | (byte)IceEncodingDefinitions.SliceFlags.HasSliceSize));
        Assert.That(decoder.DecodeString(), Is.EqualTo(typeof(MyClassB).GetIceTypeId()));
        Assert.That(decoder.DecodeInt(), Is.EqualTo(5));
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null instance

        // Second Slice
        Assert.That(decoder.DecodeByte(), Is.EqualTo(
            (byte)IceEncodingDefinitions.TypeIdKind.String |
            (byte)IceEncodingDefinitions.SliceFlags.HasSliceSize |
            (byte)IceEncodingDefinitions.SliceFlags.IsLastSlice));
        Assert.That(decoder.DecodeString(), Is.EqualTo(typeof(MyClassA).GetIceTypeId()));
        Assert.That(decoder.DecodeInt(), Is.EqualTo(6));
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null instance
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null instance
        Assert.That(decoder.Consumed, Is.EqualTo(readResult.Buffer.Length));
    }

    [Test]
    public void Operation_response_with_compact_format([Values] bool anyClass)
    {
        // Act
        var payload = anyClass ?
            IClassOperationsService.Response.EncodeOpAnyClassCompact(new MyClassB()) :
            IClassOperationsService.Response.EncodeOpMyClassCompact(new MyClassB());

        // Assert
        Assert.That(payload.TryRead(out ReadResult readResult), Is.True);
        Assert.That(readResult.IsCompleted, Is.True);
        var decoder = new IceDecoder(readResult.Buffer);

        // MyClassB instance encoded with compact format (2 Slices)

        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // Instance marker

        // First Slice
        Assert.That(decoder.DecodeByte(), Is.EqualTo((byte)IceEncodingDefinitions.TypeIdKind.String));
        Assert.That(decoder.DecodeString(), Is.EqualTo(typeof(MyClassB).GetIceTypeId()));
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null instance

        // Second Slice
        Assert.That(decoder.DecodeByte(), Is.EqualTo((byte)IceEncodingDefinitions.SliceFlags.IsLastSlice));
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null instance
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null instance
        Assert.That(decoder.Consumed, Is.EqualTo(readResult.Buffer.Length));
    }

    [Test]
    public void Operation_response_with_sliced_format([Values] bool anyClass)
    {
        // Act
        var payload = anyClass ?
            IClassOperationsService.Response.EncodeOpAnyClassSliced(new MyClassB()) :
            IClassOperationsService.Response.EncodeOpMyClassSliced(new MyClassB());

        // Assert
        Assert.That(payload.TryRead(out ReadResult readResult), Is.True);
        Assert.That(readResult.IsCompleted, Is.True);
        var decoder = new IceDecoder(readResult.Buffer);

        // MyClassB instance encoded with sliced format (2 Slices)

        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // Instance marker

        // First Slice
        Assert.That(
            decoder.DecodeByte(),
            Is.EqualTo((byte)IceEncodingDefinitions.TypeIdKind.String | (byte)IceEncodingDefinitions.SliceFlags.HasSliceSize));
        Assert.That(decoder.DecodeString(), Is.EqualTo(typeof(MyClassB).GetIceTypeId()));
        Assert.That(decoder.DecodeInt(), Is.EqualTo(5));
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null instance

        // Second Slice
        Assert.That(decoder.DecodeByte(), Is.EqualTo(
            (byte)IceEncodingDefinitions.TypeIdKind.String |
            (byte)IceEncodingDefinitions.SliceFlags.HasSliceSize |
            (byte)IceEncodingDefinitions.SliceFlags.IsLastSlice));
        Assert.That(decoder.DecodeString(), Is.EqualTo(typeof(MyClassA).GetIceTypeId()));
        Assert.That(decoder.DecodeInt(), Is.EqualTo(6));
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null instance
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null instance
        Assert.That(decoder.Consumed, Is.EqualTo(readResult.Buffer.Length));
    }

    // Copied from Ice/Codec/Internal/IceEncodingDefinitions.cs
    internal static class IceEncodingDefinitions
    {
        [Flags]
        internal enum SliceFlags : byte
        {
            TypeIdMask = 3,
            HasTaggedFields = 4,
            HasIndirectionTable = 8,
            HasSliceSize = 16,
            IsLastSlice = 32
        }

        internal enum TypeIdKind : byte
        {
            None = 0,
            String = 1,
            Index = 2,
            CompactId = 3,
        }
    }
}
