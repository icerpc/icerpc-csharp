// Copyright (c) ZeroC, Inc.

use slice::grammar::Primitive;

pub trait PrimitiveExt {
    /// The primitive's type stuff used as the suffix to encoder and decoder operations.
    fn type_suffix(&self) -> &'static str;

    /// The C# keyword corresponding to the primitive type.
    fn cs_type(&self) -> &'static str;
}

impl PrimitiveExt for Primitive {
    fn type_suffix(&self) -> &'static str {
        match self {
            Primitive::Bool => "Bool",
            Primitive::Int8 => "Int8",
            Primitive::UInt8 => "UInt8",
            Primitive::Int16 => "Int16",
            Primitive::UInt16 => "UInt16",
            Primitive::Int32 => "Int32",
            Primitive::UInt32 => "UInt32",
            Primitive::VarInt32 => "VarInt32",
            Primitive::VarUInt32 => "VarUInt32",
            Primitive::Int64 => "Int64",
            Primitive::UInt64 => "UInt64",
            Primitive::VarInt62 => "VarInt62",
            Primitive::VarUInt62 => "VarUInt62",
            Primitive::Float32 => "Float32",
            Primitive::Float64 => "Float64",
            Primitive::String => "String",
            Primitive::ServiceAddress => "ServiceAddress",
            Primitive::AnyClass => "Class",
        }
    }

    fn cs_type(&self) -> &'static str {
        match self {
            Primitive::Bool => "bool",
            Primitive::Int8 => "sbyte",
            Primitive::UInt8 => "byte",
            Primitive::Int16 => "short",
            Primitive::UInt16 => "ushort",
            Primitive::Int32 => "int",
            Primitive::UInt32 => "uint",
            Primitive::VarInt32 => "int",
            Primitive::VarUInt32 => "uint",
            Primitive::Int64 => "long",
            Primitive::UInt64 => "ulong",
            Primitive::VarInt62 => "long",
            Primitive::VarUInt62 => "ulong",
            Primitive::Float32 => "float",
            Primitive::Float64 => "double",
            Primitive::String => "string",
            Primitive::ServiceAddress => "IceRpc.ServiceAddress",
            Primitive::AnyClass => "SliceClass",
        }
    }
}
