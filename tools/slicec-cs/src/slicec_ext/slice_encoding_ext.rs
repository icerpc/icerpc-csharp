// Copyright (c) ZeroC, Inc. All rights reserved.

use slice::grammar::Encoding;

pub trait EncodingExt {
    fn to_cs_encoding(&self) -> &str;
    fn encoding_name(&self) -> &str;
}

impl EncodingExt for Encoding {
    fn to_cs_encoding(&self) -> &str {
        match self {
            Encoding::Slice11 => "SliceEncoding.Slice11",
            Encoding::Slice2 => "SliceEncoding.Slice20",
        }
    }

    // TODO: this can be removed once we rename the Slice2 encoding to "2"
    fn encoding_name(&self) -> &str {
        match self {
            Encoding::Slice11 => "1.1",
            Encoding::Slice2 => "2.0",
        }
    }
}
