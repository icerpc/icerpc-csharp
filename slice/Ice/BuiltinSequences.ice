//
// Copyright (c) ZeroC, Inc. All rights reserved.
//

#pragma once

[[suppress-warning(reserved-identifier)]]

[[3.7]] // TODO, temporary

[cs:namespace(ZeroC)]
module Ice
{
    /// A sequence of bools.
    sequence<bool> BoolSeq;

    /// A sequence of bytes.
    sequence<byte> ByteSeq;

    /// A sequence of shorts.
    sequence<short> ShortSeq;

    /// A sequence of ints.
    sequence<int> IntSeq;

    /// A sequence of longs.
    sequence<long> LongSeq;

    /// A sequence of floats.
    sequence<float> FloatSeq;

    /// A sequence of doubles.
    sequence<double> DoubleSeq;

    /// A sequence of strings.
    sequence<string> StringSeq;

    /// A sequence of classes.
    sequence<AnyClass> ClassSeq;

    /// A sequence of proxies.
    sequence<Object?> ProxySeq;
}
