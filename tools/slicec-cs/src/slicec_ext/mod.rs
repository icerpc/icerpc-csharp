// Copyright (c) ZeroC, Inc. All rights reserved.

mod attribute_ext;
mod entity_ext;
mod enum_ext;
mod interface_ext;
mod member_ext;
mod operation_ext;
mod primitive_ext;
mod slice_encoding_ext;
mod type_ref_ext;

pub use attribute_ext::AttributeExt;
pub use entity_ext::EntityExt;
pub use enum_ext::EnumExt;
pub use interface_ext::InterfaceExt;
pub use member_ext::{MemberExt, ParameterExt, ParameterSliceExt};
pub use operation_ext::OperationExt;
pub use primitive_ext::PrimitiveExt;
pub use slice_encoding_ext::EncodingExt;
pub use type_ref_ext::TypeRefExt;

pub mod cs_attributes {
    pub const ATTRIBUTE: &str = "cs::attribute";
    pub const ENCODED_RESULT: &str = "cs::encodedResult";
    pub const GENERIC: &str = "cs::generic";
    pub const INTERNAL: &str = "cs::internal";
    pub const NAMESPACE: &str = "cs::namespace";
    pub const READONLY: &str = "cs::readonly";
    pub const TYPE: &str = "cs::type";
}
