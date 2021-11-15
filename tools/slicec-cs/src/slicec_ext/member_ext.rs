// Copyright (c) ZeroC, Inc. All rights reserved.

use super::{EntityExt, TypeRefExt};
use crate::cs_util::{escape_keyword, mangle_name, FieldType};
use slice::code_gen_util::{fix_case, CaseStyle, TypeContext};
use slice::grammar::{AsTypes, Member, Parameter, Types};

pub trait MemberExt {
    fn parameter_name(&self) -> String;
    fn parameter_name_with_prefix(&self, prefix: &str) -> String;
    fn field_name(&self, field_type: FieldType) -> String;
    fn is_default_initialized(&self) -> bool;
}

impl<T: Member> MemberExt for T {
    fn parameter_name(&self) -> String {
        escape_keyword(&fix_case(self.identifier(), CaseStyle::Camel))
    }

    fn parameter_name_with_prefix(&self, prefix: &str) -> String {
        let name = prefix.to_owned() + &fix_case(self.identifier(), CaseStyle::Camel);
        escape_keyword(&name)
    }

    fn field_name(&self, field_type: FieldType) -> String {
        mangle_name(
            &fix_case(self.escape_identifier(), CaseStyle::Pascal),
            field_type,
        )
    }

    fn is_default_initialized(&self) -> bool {
        let data_type = self.data_type();

        if data_type.is_optional {
            return true;
        }

        match data_type.concrete_type() {
            Types::Struct(struct_def) => struct_def
                .members()
                .iter()
                .all(|m| m.is_default_initialized()),
            _ => data_type.is_value_type(),
        }
    }
}

pub trait ParameterExt {
    fn to_type_string(&self, namespace: &str, context: TypeContext) -> String;
}

impl ParameterExt for Parameter {
    fn to_type_string(&self, namespace: &str, context: TypeContext) -> String {
        if self.is_streamed {
            let type_str = self
                .data_type()
                .to_non_optional_type_string(namespace, context);
            if type_str == "byte" {
                "global::System.IO.Stream".to_owned()
            } else {
                format!(
                    "global::System.Collections.Generic.IAsyncEnumerable<{}>",
                    type_str
                )
            }
        } else {
            self.data_type().to_type_string(namespace, context)
        }
    }
}

pub trait ParameterSliceExt {
    fn to_argument_tuple(&self, prefix: &str) -> String;
    fn to_tuple_type(&self, namespace: &str, context: TypeContext) -> String;
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
                    .join(", ")
            ),
        }
    }

    fn to_tuple_type(&self, namespace: &str, context: TypeContext) -> String {
        match self {
            [] => panic!("tuple type with no members"),
            [member] => member.to_type_string(namespace, context),
            _ => format!(
                "({})",
                self.iter()
                    .map(|m| m.to_type_string(namespace, context)
                        + " "
                        + &m.field_name(FieldType::NonMangled))
                    .collect::<Vec<String>>()
                    .join(", ")
            ),
        }
    }
}
