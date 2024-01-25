// Copyright (c) ZeroC, Inc.

use super::{EntityExt, EnumExt, PrimitiveExt};
use crate::cs_attributes::CsType;
use slicec::grammar::*;

pub trait TypeRefExt {
    /// Is this type known to map to a C# value type?
    fn is_value_type(&self) -> bool;

    // TODO add comments
    // TODO the functions have some shared logic that can be pulled out!
    fn field_type_string(&self, namespace: &str, ignore_optional: bool) -> String;
    fn incoming_type_string(&self, namespace: &str, ignore_optional: bool) -> String;
    fn outgoing_type_string(&self, namespace: &str, ignore_optional: bool) -> String;
}

impl<T: Type + ?Sized> TypeRefExt for TypeRef<T> {
    fn is_value_type(&self) -> bool {
        match self.concrete_type() {
            Types::Primitive(primitive) => !matches!(primitive, Primitive::String | Primitive::AnyClass),
            Types::Struct(_) => true,
            Types::Enum(enum_ref) => enum_ref.is_mapped_to_cs_enum(),
            _ => false,
        }
    }

    fn field_type_string(&self, namespace: &str, ignore_optional: bool) -> String {
        let type_string = match &self.concrete_typeref() {
            TypeRefs::Primitive(primitive_ref) => primitive_ref.cs_type().to_owned(),
            TypeRefs::Struct(struct_ref) => struct_ref.escape_scoped_identifier(namespace),
            TypeRefs::Class(class_ref) => class_ref.escape_scoped_identifier(namespace),
            TypeRefs::Enum(enum_ref) => enum_ref.escape_scoped_identifier(namespace),
            TypeRefs::CustomType(custom_type_ref) => {
                let attribute = custom_type_ref.definition().find_attribute::<CsType>();
                let attribute = attribute.expect("called 'type_string' on custom type with no 'cs::type' attribute!");
                attribute.type_string.clone()
            }
            TypeRefs::ResultType(result_type_ref) => {
                let success_type = result_type_ref.success_type.field_type_string(namespace, false);
                let failure_type = result_type_ref.failure_type.field_type_string(namespace, false);
                format!("Result<{success_type}, {failure_type}>")
            }
            TypeRefs::Sequence(sequence_ref) => {
                let element_type = sequence_ref.element_type.field_type_string(namespace, false);
                format!("global::System.Collections.Generic.IList<{element_type}>")
            }
            TypeRefs::Dictionary(dictionary_ref) => {
                let key_type = dictionary_ref.key_type.field_type_string(namespace, false);
                let value_type = dictionary_ref.value_type.field_type_string(namespace, false);
                format!("global::System.Collections.Generic.IDictionary<{key_type}, {value_type}>")
            }
        };

        if self.is_optional && !ignore_optional {
            type_string + "?"
        } else {
            type_string
        }
    }

    fn incoming_type_string(&self, namespace: &str, ignore_optional: bool) -> String {
        let type_string = match &self.concrete_typeref() {
            TypeRefs::Sequence(sequence_ref) => {
                let element_type = sequence_ref.element_type.field_type_string(namespace, false);
                let cs_type_attribute = sequence_ref.find_attribute::<CsType>();
                match cs_type_attribute {
                    Some(arg) => arg.type_string.clone(),
                    None => format!("{element_type}[]"),
                }
            }
            TypeRefs::Dictionary(dictionary_ref) => {
                let key_type = dictionary_ref.key_type.field_type_string(namespace, false);
                let value_type = dictionary_ref.value_type.field_type_string(namespace, false);
                let cs_type_attribute = dictionary_ref.find_attribute::<CsType>();
                match cs_type_attribute {
                    Some(arg) => arg.type_string.clone(),
                    None => format!("global::System.Collections.Generic.Dictionary<{key_type}, {value_type}>"),
                }
            }
            _ => self.field_type_string(namespace, true),
        };

        if self.is_optional && !ignore_optional {
            type_string + "?"
        } else {
            type_string
        }
    }

    fn outgoing_type_string(&self, namespace: &str, mut ignore_optional: bool) -> String {
        let type_string = match &self.concrete_typeref() {
            TypeRefs::Sequence(sequence_ref) => {
                let element_type = sequence_ref.element_type.field_type_string(namespace, false);
                let has_cs_type_attribute = self.has_attribute::<CsType>();
                if sequence_ref.has_fixed_size_primitive_elements() && !has_cs_type_attribute {
                    // If the underlying type is of fixed size, we map to `ReadOnlyMemory` instead,
                    // and the mapping is the same for optional and non-optional types.
                    ignore_optional = true;
                    format!("global::System.ReadOnlyMemory<{element_type}>")
                } else {
                    format!("global::System.Collections.Generic.IEnumerable<{element_type}>")
                }
            }
            TypeRefs::Dictionary(dictionary_ref) => {
                let key_type = dictionary_ref.key_type.field_type_string(namespace, false);
                let value_type = dictionary_ref.value_type.field_type_string(namespace, false);
                format!(
                    "global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<{key_type}, {value_type}>>"
                )
            }
            _ => self.field_type_string(namespace, true),
        };

        if self.is_optional && !ignore_optional {
            type_string + "?"
        } else {
            type_string
        }
    }
}
