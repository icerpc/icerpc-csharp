// Copyright (c) ZeroC, Inc.

use crate::cs_attributes::match_cs_attribute;
use crate::cs_util::*;
use crate::slicec_ext::*;
use slicec::code_block::CodeBlock;
use slicec::grammar::{Attributable, Contained, Field, Member};
use slicec::utils::code_gen_util::TypeContext;

pub fn escape_parameter_name(parameters: &[&impl Member], name: &str) -> String {
    if parameters.iter().any(|p| p.parameter_name() == name) {
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

    let attributes = field.attributes().into_iter().filter_map(match_cs_attribute);

    for comment_tag in field.formatted_doc_comment() {
        prelude.writeln(&comment_tag)
    }
    prelude.writeln(&attributes.into_iter().collect::<CodeBlock>());
    if let Some(obsolete) = field.obsolete_attribute() {
        prelude.writeln(&format!("[{obsolete}]"));
    }

    // All field modifiers are based on the parent's modifiers.
    // It's safe to unwrap here since all fields have a parent.
    let modifiers = field.parent().unwrap().modifiers();
    let name = field.field_name(field_type);
    format!(
        "\
{prelude}
{modifiers} {type_string} {name};"
    )
}

pub fn initialize_required_fields(fields: &[&Field], field_type: FieldType) -> CodeBlock {
    // This helper should only be used for classes and exceptions
    debug_assert!(matches!(field_type, FieldType::Class | FieldType::Exception));

    let mut code = CodeBlock::default();

    for field in fields {
        let data_type = field.data_type();
        // `is_value_type` returns false for custom types since we can't know what type the user mapped it to.
        if !data_type.is_optional && !data_type.is_value_type() {
            // This is to suppress compiler warnings for non-nullable fields.
            writeln!(code, "this.{} = default!;", field.field_name(field_type));
        }
    }

    code
}
