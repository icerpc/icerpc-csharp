// Copyright (c) ZeroC, Inc.

use super::{EntityExt, TypeRefExt};
use crate::cs_util::{escape_keyword, format_comment_message, mangle_name, FieldType};
use convert_case::Case;
use slicec::grammar::*;
use slicec::utils::code_gen_util::TypeContext;

pub trait MemberExt {
    fn parameter_name(&self) -> String;
    fn parameter_name_with_prefix(&self, prefix: &str) -> String;
    fn field_name(&self, field_type: FieldType) -> String;
}

impl<T: Member> MemberExt for T {
    fn parameter_name(&self) -> String {
        escape_keyword(&self.cs_identifier(Case::Camel))
    }

    fn parameter_name_with_prefix(&self, prefix: &str) -> String {
        let name = prefix.to_owned() + &self.cs_identifier(Case::Camel);
        escape_keyword(&name)
    }

    fn field_name(&self, field_type: FieldType) -> String {
        mangle_name(&self.escape_identifier(), field_type)
    }
}

pub trait ParameterExt {
    fn cs_type_string(&self, namespace: &str, context: TypeContext, ignore_optional: bool) -> String;

    /// Returns the message of the `@param` tag corresponding to this parameter from the operation it's part of.
    /// If the operation has no doc comment, or a matching `@param` tag, this returns `None`.
    fn formatted_parameter_doc_comment(&self) -> Option<String>;
}

impl ParameterExt for Parameter {
    fn cs_type_string(&self, namespace: &str, context: TypeContext, ignore_optional: bool) -> String {
        let type_str = self.data_type().cs_type_string(namespace, context, ignore_optional);
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

    fn formatted_parameter_doc_comment(&self) -> Option<String> {
        // Check if this parameter's parent operation had a doc comment on it.
        self.parent().comment().and_then(|comment| {
            // If it did, search the comment for a '@param' tag with this parameter's identifier and return it.
            comment
                .params
                .iter()
                .find(|param_tag| param_tag.identifier.value == self.identifier())
                .map(|param_tag| format_comment_message(&param_tag.message, &self.namespace()))
        })
    }
}

pub trait ParameterSliceExt {
    fn to_argument_tuple(&self, prefix: &str) -> String;
    fn to_tuple_type(&self, namespace: &str, context: TypeContext, ignore_optional: bool) -> String;
}

impl ParameterSliceExt for [&Parameter] {
    fn to_argument_tuple(&self, prefix: &str) -> String {
        match self {
            [] => panic!("tuple type with no members"),
            [member] => member.parameter_name_with_prefix(prefix),
            _ => format!(
                "({})",
                self.iter()
                    .map(|m| m.parameter_name_with_prefix(prefix))
                    .collect::<Vec<String>>()
                    .join(", "),
            ),
        }
    }

    fn to_tuple_type(&self, namespace: &str, context: TypeContext, ignore_optional: bool) -> String {
        match self {
            [] => panic!("tuple type with no members"),
            [member] => member.cs_type_string(namespace, context, ignore_optional),
            _ => format!(
                "({})",
                self.iter()
                    .map(|m| m.cs_type_string(namespace, context, ignore_optional)
                        + " "
                        + &m.field_name(FieldType::NonMangled))
                    .collect::<Vec<String>>()
                    .join(", "),
            ),
        }
    }
}
