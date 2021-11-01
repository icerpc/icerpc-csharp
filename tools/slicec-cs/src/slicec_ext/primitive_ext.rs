// Copyright (c) ZeroC, Inc. All rights reserved.

use slice::grammar::Primitive;

pub trait PrimitiveExt {
    /// The primitive's type stuff used as the suffix to encoder and decoder operations.
    fn type_suffix(&self) -> &'static str;
}

impl PrimitiveExt for Primitive {
    fn type_suffix(&self) -> &'static str {
        match self {
            Primitive::Bool => "Bool",
            Primitive::Byte => "Byte",
            Primitive::Short => "Short",
            Primitive::UShort => "UShort",
            Primitive::Int => "Int",
            Primitive::UInt => "UInt",
            Primitive::VarInt => "VarInt",
            Primitive::VarUInt => "VarUInt",
            Primitive::Long => "Long",
            Primitive::ULong => "ULong",
            Primitive::VarLong => "VarLong",
            Primitive::VarULong => "VarULong",
            Primitive::Float => "Float",
            Primitive::Double => "Double",
            Primitive::String => "String",
        }
    }
}
