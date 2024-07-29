// Copyright (c) ZeroC, Inc.

use crate::code_block::CodeBlock;
use crate::comments::CommentTag;
use crate::slicec_ext::*;
use slicec::grammar::{Contained, Field, Member};

/// Takes a list of members and sorts them in the following order: [required members][tagged members]
/// Required members are left in the provided order. Tagged members are sorted so tag values are in increasing order.
pub fn get_sorted_members<'a, T: Member + ?Sized>(members: &[&'a T]) -> impl Iterator<Item = &'a T> {
    let (mut tagged, required): (Vec<&T>, Vec<&T>) = members.iter().partition(|member| member.is_tagged());
    tagged.sort_by_key(|member| member.tag().unwrap());
    required.into_iter().chain(tagged)
}

pub fn escape_parameter_name(parameters: &[&impl Member], name: &str) -> String {
    if parameters.iter().any(|p| p.parameter_name() == name) {
        name.to_owned() + "_"
    } else {
        name.to_owned()
    }
}

pub fn field_declaration(field: &Field) -> String {
    let type_string = field.data_type().field_type_string(&field.namespace());
    let mut prelude = CodeBlock::default();

    if let Some(summary) = field.formatted_doc_comment_summary() {
        prelude.writeln(&CommentTag::new("summary", summary))
    }

    for cs_attribute in field.cs_attributes() {
        writeln!(prelude, "[{cs_attribute}]")
    }

    if let Some(obsolete) = field.obsolete_attribute() {
        writeln!(prelude, "[{obsolete}]");
    }

    format!(
        "\
{prelude}
{access}{required} {type_string} {name} {{ get; {setter}; }}",
        access = field.parent().access_modifier(),
        required = if field.is_required() { " required" } else { "" },
        name = field.field_name(),
        setter = if field.is_cs_readonly() { "init" } else { "set" },
    )
}
