// Copyright (c) ZeroC, Inc.

mod comment_ext;
mod entity_ext;
mod enum_ext;
mod interface_ext;
mod member_ext;
mod module_ext;
mod operation_ext;
mod primitive_ext;
mod slice_encoding_ext;
mod type_ref_ext;

pub use comment_ext::CommentExt;
pub use entity_ext::EntityExt;
pub use enum_ext::EnumExt;
pub use interface_ext::InterfaceExt;
pub use member_ext::{FieldExt, MemberExt, ParameterExt, ParameterSliceExt};
pub use module_ext::ModuleExt;
pub use operation_ext::OperationExt;
pub use primitive_ext::PrimitiveExt;
pub use slice_encoding_ext::EncodingExt;
pub use type_ref_ext::TypeRefExt;

fn scoped_identifier(identifier: String, identifier_namespace: String, current_namespace: &str) -> String {
    if current_namespace == identifier_namespace {
        identifier
    } else {
        format!("global::{identifier_namespace}.{identifier}")
    }
}
