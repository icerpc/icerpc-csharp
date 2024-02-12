// Copyright (c) ZeroC, Inc.

use super::{EntityExt, EnumExt, PrimitiveExt};
use crate::cs_attributes::CsType;
use slicec::grammar::*;

pub trait TypeRefExt {
    /// Is this type known to map to a C# value type?
    fn is_value_type(&self) -> bool;

    fn field_type_string(&self, namespace: &str) -> String;
    fn incoming_parameter_type_string(&self, namespace: &str) -> String;
    fn outgoing_parameter_type_string(&self, namespace: &str) -> String;
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

    fn field_type_string(&self, namespace: &str) -> String {
        let type_string = match &self.concrete_typeref() {
            TypeRefs::Primitive(primitive_ref) => primitive_ref.cs_type().to_owned(),
            TypeRefs::Struct(struct_ref) => struct_ref.escape_scoped_identifier(namespace),
            TypeRefs::Class(class_ref) => class_ref.escape_scoped_identifier(namespace),
            TypeRefs::Enum(enum_ref) => enum_ref.escape_scoped_identifier(namespace),
            TypeRefs::ResultType(result_type_ref) => {
                let success_type = result_type_ref.success_type.field_type_string(namespace);
                let failure_type = result_type_ref.failure_type.field_type_string(namespace);
                format!("Result<{success_type}, {failure_type}>")
            }
            TypeRefs::CustomType(custom_type_ref) => {
                let attribute = custom_type_ref.definition().find_attribute::<CsType>();
                let attribute = attribute.expect("called 'type_string' on custom type with no 'cs::type' attribute!");
                attribute.type_string.clone()
            }
            TypeRefs::Sequence(sequence_ref) => {
                let element_type = sequence_ref.element_type.field_type_string(namespace);
                format!("global::System.Collections.Generic.IList<{element_type}>")
            }
            TypeRefs::Dictionary(dictionary_ref) => {
                let key_type = dictionary_ref.key_type.field_type_string(namespace);
                let value_type = dictionary_ref.value_type.field_type_string(namespace);
                format!("global::System.Collections.Generic.IDictionary<{key_type}, {value_type}>")
            }
        };

        set_optional_modifier_for(type_string, self.is_optional)
    }

    fn incoming_parameter_type_string(&self, namespace: &str) -> String {
        let type_string = match &self.concrete_typeref() {
            TypeRefs::Sequence(sequence_ref) => {
                match sequence_ref.find_attribute::<CsType>() {
                    Some(argument) => argument.type_string.clone(),
                    None => {
                        let element_type = sequence_ref.element_type.field_type_string(namespace);
                        format!("{element_type}[]")
                    }
                }
            }
            TypeRefs::Dictionary(dictionary_ref) => {
                match dictionary_ref.find_attribute::<CsType>() {
                    Some(argument) => argument.type_string.clone(),
                    None => {
                        let key_type = dictionary_ref.key_type.field_type_string(namespace);
                        let value_type = dictionary_ref.value_type.field_type_string(namespace);
                        format!("global::System.Collections.Generic.Dictionary<{key_type}, {value_type}>")
                    }
                }
            }
            _ => return self.field_type_string(namespace),
        };

        set_optional_modifier_for(type_string, self.is_optional)
    }

    fn outgoing_parameter_type_string(&self, namespace: &str) -> String {
        let mut ignore_optional = false;
        let type_string = match &self.concrete_typeref() {
            TypeRefs::Sequence(sequence_ref) => {
                let element_type = sequence_ref.element_type.field_type_string(namespace);
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
                let key_type = dictionary_ref.key_type.field_type_string(namespace);
                let value_type = dictionary_ref.value_type.field_type_string(namespace);
                format!(
                    "global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<{key_type}, {value_type}>>"
                )
            }
            _ => return self.field_type_string(namespace),
        };

        set_optional_modifier_for(type_string, self.is_optional && !ignore_optional)
    }
}

fn set_optional_modifier_for(type_string: String, is_optional: bool) -> String {
    match is_optional {
        true => type_string + "?",
        false => type_string,
    }
}
