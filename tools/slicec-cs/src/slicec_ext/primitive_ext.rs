// Copyright (c) ZeroC, Inc. All rights reserved.

use slice::grammar::Primitive;

pub trait PrimitiveExt {
    /// The primitive's type stuff used as the suffix to encoder and decoder operations.
    fn type_suffix(&self) -> &'static str;

    /// The C# keyword corresponding to the primitive type.
    fn cs_keyword(&self) -> &'static str;
}

impl PrimitiveExt for Primitive {
    fn type_suffix(&self) -> &'static str {
        match self {
            Primitive::Bool => "Bool",
            Primitive::UInt8 => "Byte",
            Primitive::Int16 => "Short",
            Primitive::UInt16 => "UShort",
            Primitive::Int32 => "Int",
            Primitive::UInt32 => "UInt",
            Primitive::VarInt32 => "VarInt",
            Primitive::VarUInt32 => "VarUInt",
            Primitive::Int64 => "Long",
            Primitive::UInt64 => "ULong",
            Primitive::VarInt62 => "VarLong",
            Primitive::VarUInt62 => "VarULong",
            Primitive::Float32 => "Float",
            Primitive::Float64 => "Double",
            Primitive::String => "String",
            Primitive::AnyClass => "Class",
        }
    }

    fn cs_keyword(&self) -> &'static str {
        match self {
            Primitive::Bool => "bool",
            Primitive::Byte => "byte",
            Primitive::Short => "short",
            Primitive::UShort => "ushort",
            Primitive::Int => "int",
            Primitive::UInt => "uint",
            Primitive::VarInt => "int",
            Primitive::VarUInt => "uint",
            Primitive::Long => "long",
            Primitive::ULong => "ulong",
            Primitive::VarLong => "long",
            Primitive::VarULong => "ulong",
            Primitive::Float => "float",
            Primitive::Double => "double",
            Primitive::String => "string",
            Primitive::AnyClass => "IceRpc.Slice.AnyClass",
        }
    }
}
