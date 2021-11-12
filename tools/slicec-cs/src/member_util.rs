// Copyright (c) ZeroC, Inc. All rights reserved.

use slice::code_gen_util::TypeContext;
use slice::grammar::{AsTypes, DataMember, Member, Primitive, Types};

use crate::code_block::CodeBlock;
use crate::comments::{doc_comment_message, CommentTag};
use crate::cs_util::*;
use crate::slicec_ext::*;

pub fn escape_parameter_name(parameters: &[&impl Member], name: &str) -> String {
    if parameters.iter().any(|p| p.identifier() == name) {
        name.to_owned() + "_"
    } else {
        name.to_owned()
    }
}

pub fn data_member_declaration(data_member: &DataMember, field_type: FieldType) -> String {
    let type_string = data_member
        .data_type()
        .to_type_string(&data_member.namespace(), TypeContext::DataMember);
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
{modifiers} {type_string} {name};",
        prelude = prelude,
        modifiers = data_member.modifiers(),
        type_string = type_string,
        name = data_member.field_name(field_type)
    )
}

pub fn initialize_non_nullable_fields(
    members: &[&impl Member],
    field_type: FieldType,
) -> CodeBlock {
    // This helper should only be used for classes and exceptions
    assert!(field_type == FieldType::Class || field_type == FieldType::Exception);

    let mut code = CodeBlock::new();

    for member in members {
        let data_type = member.data_type();

        if data_type.is_optional {
            continue;
        }

        let suppress = match data_type.concrete_type() {
            Types::Class(_) | Types::Sequence(_) | Types::Dictionary(_) => true,
            Types::Primitive(primitive) if matches!(primitive, Primitive::String) => true,
            _ => false,
        };

        if suppress {
            // This is to suppress compiler warnings for non-nullable fields.
            writeln!(code, "this.{} = null!;", member.field_name(field_type));
        }
    }

    code
}
