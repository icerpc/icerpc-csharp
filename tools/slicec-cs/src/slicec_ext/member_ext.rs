// Copyright (c) ZeroC, Inc.

use super::{EntityExt, TypeRefExt};
use crate::cs_util::{escape_keyword, mangle_name, FieldType};

use slice::convert_case::Case;
use slice::grammar::{Member, Parameter, Types};
use slice::utils::code_gen_util::TypeContext;

pub trait MemberExt {
    fn parameter_name(&self) -> String;
    fn parameter_name_with_prefix(&self, prefix: &str) -> String;
    fn field_name(&self, field_type: FieldType) -> String;
    fn is_default_initialized(&self) -> bool;
}

impl<T: Member> MemberExt for T {
    fn parameter_name(&self) -> String {
        escape_keyword(&self.cs_identifier(Some(Case::Camel)))
    }

    fn parameter_name_with_prefix(&self, prefix: &str) -> String {
        let name = prefix.to_owned() + &self.cs_identifier(Some(Case::Camel));
        escape_keyword(&name)
    }

    fn field_name(&self, field_type: FieldType) -> String {
        mangle_name(&self.escape_identifier(), field_type)
    }

    fn is_default_initialized(&self) -> bool {
        let data_type = self.data_type();

        if data_type.is_optional {
            return true;
        }

        match data_type.concrete_type() {
            Types::Struct(struct_def) => struct_def.members().iter().all(|m| m.is_default_initialized()),
            _ => data_type.is_value_type(),
        }
    }
}

pub trait ParameterExt {
    fn cs_type_string(&self, namespace: &str, context: TypeContext, ignore_optional: bool) -> String;
}

impl ParameterExt for Parameter {
    fn cs_type_string(&self, namespace: &str, context: TypeContext, ignore_optional: bool) -> String {
        if self.is_streamed {
            let type_str = self.data_type().cs_type_string(namespace, context, true);
            if type_str == "byte" {
                "global::System.IO.Pipelines.PipeReader".to_owned()
            } else {
                format!("global::System.Collections.Generic.IAsyncEnumerable<{type_str}>")
            }
        } else {
            self.data_type().cs_type_string(namespace, context, ignore_optional)
        }
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
