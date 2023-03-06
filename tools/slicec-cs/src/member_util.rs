// Copyright (c) ZeroC, Inc.

use crate::cs_attributes::match_cs_attribute;
use crate::cs_util::*;
use crate::slicec_ext::*;
use slice::code_block::CodeBlock;

use slice::grammar::{Attributable, Field, Member, Primitive, Types};
use slice::utils::code_gen_util::TypeContext;

pub fn escape_parameter_name(parameters: &[&impl Member], name: &str) -> String {
    if parameters.iter().any(|p| p.cs_identifier(None) == name) {
        name.to_owned() + "_"
    } else {
        name.to_owned()
    }
}

pub fn field_declaration(field: &Field, field_type: FieldType) -> String {
    let type_string = field
        .data_type()
        .cs_type_string(&field.namespace(), TypeContext::Field, false);
    let mut prelude = CodeBlock::default();

    let attributes = field.attributes(false).into_iter().filter_map(match_cs_attribute);

    for comment_tag in field.formatted_doc_comment() {
        prelude.writeln(&comment_tag)
    }
    prelude.writeln(&attributes.into_iter().collect::<CodeBlock>());
    if let Some(obsolete) = field.obsolete_attribute(true) {
        prelude.writeln(&format!("[{obsolete}]"));
    }
    let modifiers = field.modifiers();
    let name = field.field_name(field_type);
    format!(
        "\
{prelude}
{modifiers} {type_string} {name};"
    )
}

pub fn initialize_non_nullable_fields(fields: &[&Field], field_type: FieldType) -> CodeBlock {
    // This helper should only be used for classes and exceptions
    assert!(field_type == FieldType::Class || field_type == FieldType::Exception);

    let mut code = CodeBlock::default();

    for field in fields {
        let data_type = field.data_type();

        if data_type.is_optional {
            continue;
        }

        let suppress = match data_type.concrete_type() {
            Types::Class(_) | Types::Sequence(_) | Types::Dictionary(_) => true,
            Types::Primitive(primitive)
                if matches!(
                    primitive,
                    Primitive::String | Primitive::AnyClass | Primitive::ServiceAddress,
                ) =>
            {
                true
            }
            _ => false,
        };

        if suppress {
            // This is to suppress compiler warnings for non-nullable fields.
            writeln!(code, "this.{} = null!;", field.field_name(field_type));
        }
    }

    code
}
