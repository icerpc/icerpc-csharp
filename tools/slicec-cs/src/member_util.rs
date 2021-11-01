// Copyright (c) ZeroC, Inc. All rights reserved.

use slice::ast::{Ast, Node};
use slice::grammar::{Member, NamedSymbol, Primitive};
use slice::util::TypeContext;

use crate::code_block::CodeBlock;
use crate::comments::{doc_comment_message, CommentTag};
use crate::cs_util::*;
use crate::slicec_ext::*;

pub fn escape_parameter_name(parameters: &[&Member], name: &str) -> String {
    if parameters.iter().any(|p| p.identifier() == name) {
        name.to_owned() + "_"
    } else {
        name.to_owned()
    }
}

pub fn data_member_declaration(
    data_member: &Member,
    is_readonly: bool,
    field_type: FieldType,
    ast: &Ast,
) -> String {
    let data_type = &data_member.data_type;

    let type_string =
        data_type.to_type_string(&data_member.namespace(), ast, TypeContext::DataMember);
    let mut prelude = CodeBlock::new();

    prelude.writeln(&CommentTag::new(
        "summary",
        &doc_comment_message(data_member),
    ));
    prelude.writeln(
        &data_member
            .custom_attributes()
            .into_iter()
            .collect::<CodeBlock>(),
    );
    if let Some(obsolete) = data_member.obsolete_attribute(true) {
        prelude.writeln(&format!("[{}]", obsolete));
    }

    format!(
        "\
{prelude}
public {readonly}{type_string} {name};",
        prelude = prelude,
        readonly = if is_readonly { "readonly " } else { "" },
        type_string = type_string,
        name = data_member.field_name(field_type)
    )
}

pub fn initialize_non_nullable_fields(
    members: &[&Member],
    field_type: FieldType,
    ast: &Ast,
) -> CodeBlock {
    // This helper should only be used for classes and exceptions
    assert!(field_type == FieldType::Class || field_type == FieldType::Exception);

    let mut code = CodeBlock::new();

    for member in members {
        let data_type = &member.data_type;
        let data_node = data_type.definition(ast);
        if data_type.is_optional {
            continue;
        }

        let suppress = match data_node {
            Node::Class(..) | Node::Sequence(..) | Node::Dictionary(..) => true,
            Node::Primitive(_, primitive) if matches!(primitive, Primitive::String) => true,
            _ => false,
        };

        if suppress {
            // This is to suppress compiler warnings for non-nullable fields.
            writeln!(code, "this.{} = null!;", member.field_name(field_type));
        }
    }

    code
}
