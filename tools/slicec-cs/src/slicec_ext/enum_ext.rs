// Copyright (c) ZeroC, Inc.

use crate::slicec_ext::primitive_ext::PrimitiveExt;
use slicec::grammar::*;

pub trait EnumExt {
    fn get_underlying_cs_type(&self) -> String; // TODO This could become 'str'!

    fn is_mapped_to_cs_enum(&self) -> bool;
}

impl EnumExt for Enum {
    fn get_underlying_cs_type(&self) -> String {
        match &self.underlying {
            Some(underlying) => underlying.cs_type(),
            None => "int",
        }
        .to_owned()
    }

    fn is_mapped_to_cs_enum(&self) -> bool {
        self.supported_encodings().supports(Encoding::Slice1) || self.underlying.is_some()
    }
}
