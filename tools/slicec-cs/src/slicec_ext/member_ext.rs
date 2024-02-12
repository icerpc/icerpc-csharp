// Copyright (c) ZeroC, Inc.

use super::{EntityExt, TypeRefExt};
use crate::code_gen_util::TypeContext;
use crate::cs_attributes::CsReadonly;
use crate::cs_util::{escape_keyword, format_comment_message};
use convert_case::Case;
use slicec::grammar::*;

pub trait MemberExt {
    fn parameter_name(&self) -> String;
    fn parameter_name_with_prefix(&self) -> String;
    fn field_name(&self) -> String;
}

impl<T: Member> MemberExt for T {
    fn parameter_name(&self) -> String {
        escape_keyword(&self.cs_identifier(Case::Camel))
    }

    /// Returns this parameter's C# identifier (in camel case) with a `sliceP_` prefix.
    fn parameter_name_with_prefix(&self) -> String {
        format!("sliceP_{}", self.cs_identifier(Case::Camel))
    }

    fn field_name(&self) -> String {
        self.escape_identifier()
    }
}

pub trait FieldExt {
    /// Check if this field, or its parent struct, are marked with `cs::readonly`.
    fn is_cs_readonly(&self) -> bool;

    /// Returns the value of the `@param` doc-comment tag for this enumerator field, if a tag with this field name is
    /// present. Panics if this field is not an enumerator field.
    fn formatted_param_doc_comment(&self) -> Option<String>;
}

impl FieldExt for Field {
    fn is_cs_readonly(&self) -> bool {
        self.all_attributes()
            .concat()
            .into_iter()
            .any(|a| a.downcast::<CsReadonly>().is_some())
    }

    fn formatted_param_doc_comment(&self) -> Option<String> {
        if let Entities::Enumerator(enumerator) = self.parent().concrete_entity() {
            // Check if the enumerator has a doc comment on it.
            enumerator.comment().and_then(|comment| {
                // If it does, search the comment for a '@param' tag with this field's identifier and return it.
                comment
                    .params
                    .iter()
                    .find(|param_tag| param_tag.identifier.value == self.identifier())
                    .map(|param_tag| format_comment_message(&param_tag.message, &self.namespace()))
            })
        } else {
            panic!("Called 'formatted_param_doc_comment' on field outside of enumerator!");
        }
    }
}

pub trait ParameterExt {
    fn cs_type_string(&self, namespace: &str, context: TypeContext) -> String;

    /// Returns the message of the `@param` tag corresponding to this parameter from the operation it's part of.
    /// If the operation has no doc comment, or a matching `@param` tag, this returns `None`.
    fn formatted_param_doc_comment(&self) -> Option<String>;
}

impl ParameterExt for Parameter {
    fn cs_type_string(&self, namespace: &str, context: TypeContext) -> String {
        // TODO this can be further simplified.
        let type_str = match context {
            TypeContext::OutgoingParam => self.data_type().outgoing_parameter_type_string(namespace),
            TypeContext::IncomingParam => self.data_type().incoming_parameter_type_string(namespace),
            TypeContext::Field => unreachable!(),
        };

        if self.is_streamed {
            if type_str == "byte" {
                "global::System.IO.Pipelines.PipeReader".to_owned()
            } else {
                format!("global::System.Collections.Generic.IAsyncEnumerable<{type_str}>")
            }
        } else {
            type_str
        }
    }

    fn formatted_param_doc_comment(&self) -> Option<String> {
        // Check if this parameter's parent operation has a doc comment on it.
        self.parent().comment().and_then(|comment| {
            // If it does, search the comment for a '@param' tag with this parameter's identifier and return it.
            comment
                .params
                .iter()
                .find(|param_tag| param_tag.identifier.value == self.identifier())
                .map(|param_tag| format_comment_message(&param_tag.message, &self.namespace()))
        })
    }
}

pub trait ParameterSliceExt {
    fn to_argument_tuple(&self) -> String;
    fn to_tuple_type(&self, namespace: &str, context: TypeContext) -> String;
}

impl ParameterSliceExt for [&Parameter] {
    /// Convert each parameter in this slice to it's argument representation.
    /// This is the parameter's C# identifier (in camel case) with a `sliceP_` prefix.
    /// If there are more than 1 parameters in this slice, it returns them wrapped in parenthesis (a tuple).
    fn to_argument_tuple(&self) -> String {
        match self {
            [] => panic!("tuple type with no members"),
            [member] => member.parameter_name_with_prefix(),
            _ => format!(
                "({})",
                self.iter()
                    .map(|m| m.parameter_name_with_prefix())
                    .collect::<Vec<String>>()
                    .join(", "),
            ),
        }
    }

    fn to_tuple_type(&self, namespace: &str, context: TypeContext) -> String {
        match self {
            [] => panic!("tuple type with no members"),
            [member] => member.cs_type_string(namespace, context),
            _ => format!(
                "({})",
                self.iter()
                    .map(|m| m.cs_type_string(namespace, context) + " " + &m.field_name())
                    .collect::<Vec<String>>()
                    .join(", "),
            ),
        }
    }
}
