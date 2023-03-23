// Copyright (c) ZeroC, Inc.

use crate::slicec_ext::primitive_ext::PrimitiveExt;
use slice::grammar::Enum;

pub trait EnumExt {
    fn get_underlying_cs_type(&self) -> String;
}

impl EnumExt for Enum {
    fn get_underlying_cs_type(&self) -> String {
        match &self.underlying {
            Some(underlying) => underlying.cs_type(),
            None => "int",
        }
        .to_owned()
    }
}
