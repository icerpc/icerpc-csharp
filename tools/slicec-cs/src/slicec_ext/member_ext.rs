// Copyright (c) ZeroC, Inc. All rights reserved.

use super::{NamedSymbolExt, TypeRefExt};
use crate::cs_util::{escape_keyword, mangle_name, FieldType};
use slice::ast::{Ast, Node};
use slice::grammar::{Member, NamedSymbol};
use slice::util::{fix_case, CaseStyle, TypeContext};

pub trait MemberExt {
    fn parameter_name(&self) -> String;
    fn parameter_name_with_prefix(&self, prefix: &str) -> String;
    fn field_name(&self, field_type: FieldType) -> String;
    fn is_default_initialized(&self, ast: &Ast) -> bool;
}

impl MemberExt for Member {
    fn parameter_name(&self) -> String {
        escape_keyword(&fix_case(self.identifier(), CaseStyle::Camel))
    }

    fn parameter_name_with_prefix(&self, prefix: &str) -> String {
        let name = prefix.to_owned() + &fix_case(self.identifier(), CaseStyle::Camel);
        escape_keyword(&name)
    }

    fn field_name(&self, field_type: FieldType) -> String {
        mangle_name(&self.escape_identifier(), field_type)
    }

    fn is_default_initialized(&self, ast: &Ast) -> bool {
        let data_type = &self.data_type;

        if data_type.is_optional {
            return true;
        }

        match data_type.definition(ast) {
            Node::Struct(_, struct_def) => struct_def
                .members(ast)
                .iter()
                .all(|m| m.is_default_initialized(ast)),
            _ => data_type.is_value_type(ast),
        }
    }
}

pub trait MemberSliceExt {
    fn to_argument_tuple(&self, prefix: &str) -> String;
    fn to_tuple_type(&self, namespace: &str, ast: &Ast, context: TypeContext) -> String;
    fn to_return_type(&self, namespace: &str, ast: &Ast, context: TypeContext) -> String;
}

impl MemberSliceExt for [&Member] {
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

    fn to_tuple_type(&self, namespace: &str, ast: &Ast, context: TypeContext) -> String {
        match self {
            [] => panic!("tuple type with no members"),
            [member] => member.data_type.to_type_string(namespace, ast, context),
            _ => format!(
                "({})",
                self.iter()
                    .map(|m| m.data_type.to_type_string(namespace, ast, context)
                        + " "
                        + &m.field_name(FieldType::NonMangled))
                    .collect::<Vec<String>>()
                    .join(", ")
            ),
        }
    }

    fn to_return_type(&self, namespace: &str, ast: &Ast, context: TypeContext) -> String {
        let value_task = "global::System.Threading.Tasks.ValueTask";
        match self {
            [] => value_task.to_owned(),
            [e] => {
                format!(
                    "{}<{}>",
                    value_task,
                    &e.data_type.to_type_string(namespace, ast, context)
                )
            }
            _ => {
                format!(
                    "{}<({})>",
                    value_task,
                    self.iter()
                        .map(|e| {
                            format!(
                                "{} {}",
                                &e.data_type.to_type_string(namespace, ast, context),
                                e.identifier()
                            )
                        })
                        .collect::<Vec<String>>()
                        .join(", ")
                )
            }
        }
    }
}
