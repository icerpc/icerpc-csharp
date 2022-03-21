// Copyright (c) ZeroC, Inc. All rights reserved.

use slice::grammar::SliceEncoding;

pub trait SliceEncodingExt {
    fn to_cs_encoding(&self) -> &str;
    fn encoding_name(&self) -> &str;
}

impl SliceEncodingExt for SliceEncoding {
    fn to_cs_encoding(&self) -> &str {
        match self {
            SliceEncoding::Slice11 => "IceRpc.Encoding.Slice11",
            SliceEncoding::Slice2 => "IceRpc.Encoding.Slice20",
        }
    }

    fn encoding_name(&self) -> &str {
        match self {
            SliceEncoding::Slice11 => "1.0",
            SliceEncoding::Slice2 => "2.0",
        }
    }
}
