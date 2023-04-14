// Copyright (c) ZeroC, Inc.

use crate::builders::{AttributeBuilder, Builder, CommentBuilder, ContainerBuilder, FunctionBuilder, FunctionType};
use crate::generated_code::GeneratedCode;
use crate::slicec_ext::*;
use slice::code_block::CodeBlock;

use slice::convert_case::{Case, Casing};
use slice::grammar::*;
use slice::in_definite;
use slice::visitor::Visitor;

#[derive(Debug)]
pub struct EnumVisitor<'a> {
    pub generated_code: &'a mut GeneratedCode,
}

impl<'a> Visitor for EnumVisitor<'a> {
    fn visit_enum_start(&mut self, enum_def: &Enum) {
        let mut code = CodeBlock::default();
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
    .add_comments(enum_def.formatted_doc_comment())
    .add_comment(
        "remarks",
        format!(
            "The Slice compiler generated this enum from Slice enum <c>{}</c>.",
            &enum_def.module_scoped_identifier()
        ),
    )
    .add_container_attributes(enum_def)
    .add_base(enum_def.get_underlying_cs_type())
    .add_block(enum_values(enum_def))
    .build()
}

fn enum_values(enum_def: &Enum) -> CodeBlock {
    let mut code = CodeBlock::default();
    for enumerator in enum_def.enumerators() {
        let mut declaration = CodeBlock::default();

        for comment_tag in enumerator.formatted_doc_comment() {
            declaration.writeln(&comment_tag);
        }

        writeln!(
            declaration,
            "{} = {},",
            enumerator.cs_identifier(Case::Pascal),
            enumerator.value()
        );

        code.add_block(&declaration);
    }
    code
}

fn enum_underlying_extensions(enum_def: &Enum) -> CodeBlock {
    let access = enum_def.access_modifier();
    let escaped_identifier = enum_def.escape_identifier();
    let namespace = &enum_def.namespace();
    let cs_type = enum_def.get_underlying_cs_type();
    let mut builder = ContainerBuilder::new(
        &format!("{access} static class"),
        &format!(
            "{}{}Extensions",
            enum_def.cs_identifier(Case::Pascal),
            cs_type.to_case(Case::Pascal),
        ),
    );

    builder.add_comment(
        "summary",
        format!(
            r#"Provides an extension method for creating {} <see cref="{escaped_identifier}" /> from {} <see langword="{cs_type}" />."#,
            in_definite::get_a_or_an(&escaped_identifier),
            in_definite::get_a_or_an(&cs_type),
        ),
    ).add_comment(
        "remarks",
        format!(
            "The Slice compiler generated this static class from Slice enum <c>{}</c>.",
            &enum_def.module_scoped_identifier()
        ),
    );

    // When the number of enumerators is smaller than the distance between the min and max
    // values, the values are not consecutive and we need to use a set to validate the value
    // during decoding.
    // Note that the values are not necessarily in order, e.g. we can use a simple range check
    // for enum E { A = 3, B = 2, C = 1 } during decoding.
    let min_max_values = enum_def.get_min_max_values();
    let use_set = if let Some((min_value, max_value)) = min_max_values {
        !enum_def.is_unchecked && (enum_def.enumerators.len() as i128) < max_value - min_value + 1
    } else {
        // This means there are no enumerators.*
        true
    };

    if use_set {
        builder.add_block(
            format!(
                "\
private static readonly global::System.Collections.Generic.HashSet<{cs_type}> _enumeratorValues =
    new global::System.Collections.Generic.HashSet<{cs_type}> {{ {enum_values} }};",
                enum_values = enum_def
                    .enumerators()
                    .iter()
                    .map(|e| e.value().to_string())
                    .collect::<Vec<_>>()
                    .join(", "),
            )
            .into(),
        );
    }

    let mut as_enum_block = FunctionBuilder::new(
        format!("{access} static").as_str(),
        &escaped_identifier,
        format!("As{}", enum_def.cs_identifier(Case::Pascal)).as_str(),
        FunctionType::ExpressionBody,
    );
    as_enum_block
        .add_parameter(
            format!("this {cs_type}").as_str(),
            "value",
            None,
            Some("The value being converted.".to_owned()),
        )
        .add_comment(
            "summary",
            format!(
                r#"
Converts a <see langword="{cs_type}" /> into the corresponding <see cref="{escaped_identifier}" />
enumerator."#
            ),
        )
        .add_comment("returns", "The enumerator.")
        .set_body(if enum_def.is_unchecked {
            format!("({escaped_identifier})value").into()
        } else {
            format!(
                r#"
{check_enum} ?
    ({escaped_identifier})value :
    throw new global::System.IO.InvalidDataException($"invalid enumerator value '{{value}}' for {scoped}")"#,
                check_enum = match use_set {
                    true => "_enumeratorValues.Contains(value)".to_owned(),
                    false => format!(
                        "value is >= {min_value} and <= {max_value}",
                        min_value = min_max_values.unwrap().0,
                        max_value = min_max_values.unwrap().1,
                    ),
                },
                scoped = enum_def.escape_scoped_identifier(namespace),
            )
            .into()
        });

    if !enum_def.is_unchecked {
        as_enum_block.add_comment_with_attribute(
            "exception",
            "cref",
            "global::System.IO.InvalidDataException",
            "Thrown when the value does not correspond to one of the enumerators.",
        );
    }

    builder.add_block(as_enum_block.build());

    builder.build()
}

fn enum_encoder_extensions(enum_def: &Enum) -> CodeBlock {
    let access = enum_def.access_modifier();
    let escaped_identifier = enum_def.escape_identifier();
    let cs_type = enum_def.get_underlying_cs_type();
    let mut builder = ContainerBuilder::new(
        &format!("{access} static class"),
        &format!("{}SliceEncoderExtensions", enum_def.cs_identifier(Case::Pascal)),
    );

    builder.add_comment(
        "summary",
        format!(r#"Provides an extension method for encoding a <see cref="{escaped_identifier}" /> using a <see cref="SliceEncoder" />."#),
    ).add_comment(
        "remarks",
        format!(
            "The Slice compiler generated this static class from Slice enum <c>{}</c>.",
            &enum_def.module_scoped_identifier()
        ),
    );

    // Enum encoding
    builder.add_block(
        format!(
            r#"
/// <summary>Encodes a <see cref="{escaped_identifier}" /> enum.</summary>
/// <param name="encoder">The Slice encoder.</param>
/// <param name="value">The <see cref="{escaped_identifier}" /> enumerator value to encode.</param>
{access} static void Encode{identifier}(this ref SliceEncoder encoder, {escaped_identifier} value) =>
    {encode_enum}(({cs_type})value);"#,
            identifier = enum_def.cs_identifier(Case::Pascal),
            encode_enum = match &enum_def.underlying {
                Some(underlying) => format!("encoder.Encode{}", underlying.definition().type_suffix()),
                None => "encoder.EncodeSize".to_owned(),
            },
        )
        .into(),
    );

    builder.build()
}

fn enum_decoder_extensions(enum_def: &Enum) -> CodeBlock {
    let access = enum_def.access_modifier();
    let escaped_identifier = enum_def.escape_identifier();
    let cs_type = enum_def.get_underlying_cs_type();
    let mut builder = ContainerBuilder::new(
        &format!("{access} static class"),
        &format!("{}SliceDecoderExtensions", enum_def.cs_identifier(Case::Pascal)),
    );

    builder.add_comment(
        "summary",
        format!(r#"Provides an extension method for decoding a <see cref="{escaped_identifier}" /> using a <see cref="SliceDecoder" />."#),
    ).add_comment(
        "remarks",
        format!(
            "The Slice compiler generated this static class from Slice enum <c>{}</c>.",
            &enum_def.module_scoped_identifier()
        ),
    );

    let underlying_extensions_class = format!(
        "{}{}Extensions",
        enum_def.cs_identifier(Case::Pascal),
        cs_type.to_case(Case::Pascal),
    );

    // Enum decoding
    builder.add_block(
        format!(
            r#"
/// <summary>Decodes a <see cref="{escaped_identifier}" /> enum.</summary>
/// <param name="decoder">The Slice decoder.</param>
/// <returns>The decoded <see cref="{escaped_identifier}" /> enumerator value.</returns>
{access} static {escaped_identifier} Decode{identifier}(this ref SliceDecoder decoder) =>
    {underlying_extensions_class}.As{identifier}({decode_enum});"#,
            identifier = enum_def.cs_identifier(Case::Pascal),
            decode_enum = match &enum_def.underlying {
                Some(underlying) => format!("decoder.Decode{}()", underlying.definition().type_suffix()),
                _ => "decoder.DecodeSize()".to_owned(),
            },
        )
        .into(),
    );

    builder.build()
}
