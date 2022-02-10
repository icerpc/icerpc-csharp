use crate::builders::{AttributeBuilder, CommentBuilder, ContainerBuilder};
use crate::code_block::CodeBlock;
use crate::comments::{doc_comment_message, CommentTag};
use crate::generated_code::GeneratedCode;
use crate::slicec_ext::*;

use slice::code_gen_util::{fix_case, CaseStyle};
use slice::grammar::*;
use slice::visitor::Visitor;

#[derive(Debug)]
pub struct EnumVisitor<'a> {
    pub generated_code: &'a mut GeneratedCode,
}

impl<'a> Visitor for EnumVisitor<'a> {
    fn visit_enum_start(&mut self, enum_def: &Enum) {
        let mut code = CodeBlock::new();
        code.add_block(&enum_declaration(enum_def));
        code.add_block(&enum_underlying_extensions(enum_def));
        code.add_block(&enum_encoder_extensions(enum_def));
        code.add_block(&enum_decoder_extensions(enum_def));
        self.generated_code.insert_scoped(enum_def, code);
    }
}

fn enum_declaration(enum_def: &Enum) -> CodeBlock {
    ContainerBuilder::new(
        &format!("{} enum", enum_def.access_modifier()),
        &enum_def.escape_identifier(),
    )
    .add_comment("summary", &doc_comment_message(enum_def))
    .add_container_attributes(enum_def)
    .add_base(enum_def.underlying_type().cs_keyword().to_owned())
    .add_block(enum_values(enum_def))
    .build()
    .into()
}

fn enum_values(enum_def: &Enum) -> CodeBlock {
    let mut code = CodeBlock::new();
    for enumerator in enum_def.enumerators() {
        // Use CodeBlock here in case the comment is empty. It automatically whitespace
        code.add_block(&CodeBlock::from(format!(
            "{}\n{} = {},",
            CommentTag::new("summary", &doc_comment_message(enumerator)),
            enumerator.identifier(),
            enumerator.value
        )));
    }
    code
}

fn enum_underlying_extensions(enum_def: &Enum) -> CodeBlock {
    let access = enum_def.access_modifier();
    let escaped_identifier = enum_def.escape_identifier();
    let namespace = &enum_def.namespace();
    let mut builder = ContainerBuilder::new(
        &format!("{} static class", access),
        &format!(
            "{}{}Extensions",
            fix_case(enum_def.underlying_type().cs_keyword(), CaseStyle::Pascal),
            fix_case(enum_def.identifier(), CaseStyle::Pascal)
        ),
    );

    // When the number of enumerators is smaller than the distance between the min and max
    // values, the values are not consecutive and we need to use a set to validate the value
    // during unmarshaling.
    // Note that the values are not necessarily in order, e.g. we can use a simple range check
    // for enum E { A = 3, B = 2, C = 1 } during unmarshaling.
    let min_max_values = enum_def.get_min_max_values();
    let use_set = if let Some((min_value, max_value)) = min_max_values {
        !enum_def.is_unchecked && (enum_def.enumerators.len() as i64) < max_value - min_value + 1
    } else {
        // This means there are no enumerators.*
        true
    };

    if use_set {
        builder.add_block(
            format!(
                "\
{access} static readonly global::System.Collections.Generic.HashSet<{underlying}> EnumeratorValues =
    new global::System.Collections.Generic.HashSet<{underlying}> {{ {enum_values} }};",
                access = access,
                underlying = enum_def.underlying_type().cs_keyword(),
                enum_values = enum_def
                    .enumerators()
                    .iter()
                    .map(|e| e.value.to_string())
                    .collect::<Vec<_>>()
                    .join(", ")
            )
            .into(),
        );
    }

    let mut as_enum: CodeBlock = if enum_def.is_unchecked {
        format!("({})value", escaped_identifier).into()
    } else {
        format!(
            r#"
{check_enum} ?
    ({escaped_identifier})value :
    throw new IceRpc.InvalidDataException($"invalid enumerator value '{{value}}' for {scoped}")"#,
            check_enum = match use_set {
                true => "EnumeratorValues.Contains(value)".to_owned(),
                false => format!(
                    "{min_value} <= value && value <= {max_value}",
                    min_value = min_max_values.unwrap().0,
                    max_value = min_max_values.unwrap().1,
                ),
            },
            escaped_identifier = escaped_identifier,
            scoped = enum_def.escape_scoped_identifier(namespace),
        )
        .into()
    };

    builder.add_block(
        format!(
            r#"
{access} static {escaped_identifier} As{identifier}(this {underlying_type} value) =>
    {as_enum};"#,
            access = access,
            identifier = enum_def.identifier(),
            escaped_identifier = escaped_identifier,
            underlying_type = enum_def.underlying_type().cs_keyword(),
            as_enum = as_enum.indent()
        )
        .into(),
    );

    builder.build().into()
}

fn enum_encoder_extensions(enum_def: &Enum) -> CodeBlock {
    let access = enum_def.access_modifier();
    let escaped_identifier = enum_def.escape_identifier();
    let mut builder = ContainerBuilder::new(
        &format!("{} static class", access),
        &format!(
            "SliceEncoder{}Extensions",
            fix_case(enum_def.identifier(), CaseStyle::Pascal),
        ),
    );

    builder.add_comment(
        "summary",
        &format!(
            r#"Provide extension methods for encoding <see cref="{}"/>."#,
            escaped_identifier
        ),
    );

    // Enum encoding
    builder.add_block(
        format!(
            r#"
{access} static void Encode{identifier}(this ref SliceEncoder encoder, {escaped_identifier} value) =>
    {encode_enum}(({underlying_type})value);"#,
            access = access,
            identifier = enum_def.identifier(),
            escaped_identifier = escaped_identifier,
            encode_enum = match &enum_def.underlying {
                Some(underlying) =>
                    format!("encoder.Encode{}", underlying.definition().type_suffix()),
                None => "encoder.EncodeSize".to_owned(),
            },
            underlying_type = enum_def.underlying_type().cs_keyword()
        )
        .into(),
    );

    builder.build().into()
}

fn enum_decoder_extensions(enum_def: &Enum) -> CodeBlock {
    let access = enum_def.access_modifier();
    let escaped_identifier = enum_def.escape_identifier();
    let mut builder = ContainerBuilder::new(
        &format!("{} static class", access),
        &format!(
            "SliceDecoder{}Extensions",
            fix_case(enum_def.identifier(), CaseStyle::Pascal),
        ),
    );

    builder.add_comment(
        "summary",
        &format!(
            r#"Provide extension methods for encoding <see cref="{}"/>."#,
            escaped_identifier
        ),
    );

    // Enum decoding
    builder.add_block(
        format!(
            r#"
{access} static {escaped_identifier} Decode{identifier}(this ref SliceDecoder decoder) =>
    {underlying_extensions_class}.As{identifier}({decode_enum});"#,
            access = access,
            identifier = enum_def.identifier(),
            escaped_identifier = escaped_identifier,
            underlying_extensions_class = format!(
                "{}{}Extensions",
                fix_case(enum_def.underlying_type().cs_keyword(), CaseStyle::Pascal),
                fix_case(enum_def.identifier(), CaseStyle::Pascal)
            ),
            decode_enum = match &enum_def.underlying {
                Some(underlying) =>
                    format!("decoder.Decode{}()", underlying.definition().type_suffix()),
                _ => "decoder.DecodeSize()".to_owned(),
            }
        )
        .into(),
    );

    builder.build().into()
}
