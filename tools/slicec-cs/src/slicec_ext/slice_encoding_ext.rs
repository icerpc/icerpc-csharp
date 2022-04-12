// Copyright (c) ZeroC, Inc. All rights reserved.

use slice::grammar::Encoding;

pub trait EncodingExt {
    fn to_cs_encoding(&self) -> &str;
}

impl EncodingExt for Encoding {
    fn to_cs_encoding(&self) -> &str {
        match self {
            Encoding::Slice11 => "SliceEncoding.Slice1",
            Encoding::Slice2 => "SliceEncoding.Slice2",
        }
    }
}
