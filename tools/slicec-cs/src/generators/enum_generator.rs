// Copyright (c) ZeroC, Inc.

use crate::builders::{AttributeBuilder, Builder, CommentBuilder, ContainerBuilder, FunctionBuilder, FunctionType};
use crate::comments::CommentTag;
use crate::cs_util::{CsCase, FieldType};
use crate::decoding::*;
use crate::encoding::*;
use crate::slicec_ext::*;
use convert_case::Case;
use slicec::code_block::CodeBlock;
use slicec::grammar::*;

pub fn generate_enum(enum_def: &Enum) -> CodeBlock {
    let mut code = CodeBlock::default();
    code.add_block(&enum_declaration(enum_def));

    if enum_def.is_mapped_to_cs_enum() {
        code.add_block(&enum_underlying_extensions(enum_def));
    }

    code.add_block(&enum_encoder_extensions(enum_def));
    code.add_block(&enum_decoder_extensions(enum_def));
    code
}

fn enum_declaration(enum_def: &Enum) -> CodeBlock {
    if enum_def.is_mapped_to_cs_enum() {
        // Mapped to a C# enum.
        let mut builder = ContainerBuilder::new(
            &format!("{} enum", enum_def.access_modifier()),
            &enum_def.escape_identifier(),
        );
        if let Some(summary) = enum_def.formatted_doc_comment_summary() {
            builder.add_comment("summary", summary);
        }
        builder
            .add_generated_remark("enum", enum_def)
            .add_comments(enum_def.formatted_doc_comment_seealso())
            .add_obsolete_attribute(enum_def)
            .add_base(enum_def.get_underlying_cs_type())
            .add_block(enumerators(enum_def));

        // Add cs::attribute
        for attribute in enum_def.cs_attributes() {
            builder.add_attribute(attribute);
        }
        builder.build()
    } else {
        // Mapped to a Dunet discriminated union.

        let mut builder = ContainerBuilder::new(
            &format!(
                r#"
[Dunet.Union]
{} abstract partial record class"#,
                enum_def.access_modifier(),
            ),
            &enum_def.escape_identifier(),
        );

        if let Some(summary) = enum_def.formatted_doc_comment_summary() {
            builder.add_comment("summary", summary);
        }
        builder
            .add_generated_remark("discriminated union", enum_def)
            .add_comments(enum_def.formatted_doc_comment_seealso())
            .add_obsolete_attribute(enum_def)
            .add_block(enumerators_as_nested_records(enum_def));

        // Add cs::attribute
        for attribute in enum_def.cs_attributes() {
            builder.add_attribute(attribute);
        }

        builder.add_block({
            let mut code = CodeBlock::default();
            code.writeln(&CommentTag::new(
                "summary",
                "The discriminant of this enumerator.".into(),
            ));
            code.writeln("public int Discriminant => GetDiscriminant();");
            code
        });

        builder.add_block(
            FunctionBuilder::new("internal abstract", "void", "Encode", FunctionType::Declaration)
                .add_comment("summary", "Encodes this enumerator into the given Slice encoder.")
                .add_parameter(
                    "ref SliceEncoder",
                    "encoder",
                    None,
                    Some("The Slice encoder.".to_owned()),
                )
                .build(),
        );

        builder.add_block(
            FunctionBuilder::new(
                "private protected abstract",
                "int",
                "GetDiscriminant",
                FunctionType::Declaration,
            )
            .build(),
        );

        builder.build()
    }
}

fn enumerators(enum_def: &Enum) -> CodeBlock {
    let mut code = CodeBlock::default();
    for enumerator in enum_def.enumerators() {
        let mut declaration = CodeBlock::default();

        if let Some(summary) = enumerator.formatted_doc_comment_summary() {
            declaration.writeln(&CommentTag::new("summary", summary));
        }

        for comment_tag in enumerator.formatted_doc_comment_seealso() {
            declaration.writeln(&comment_tag);
        }

        for cs_attribute in enumerator.cs_attributes() {
            writeln!(declaration, "[{cs_attribute}]")
        }

        if let Some(attribute) = enumerator.obsolete_attribute() {
            writeln!(declaration, "[{attribute}]");
        }

        writeln!(
            declaration,
            "{} = {},",
            enumerator.escape_identifier(),
            enumerator.value()
        );

        code.add_block(&declaration);
    }
    code
}

fn enumerators_as_nested_records(enum_def: &Enum) -> CodeBlock {
    let mut code = CodeBlock::default();
    let namespace = enum_def.namespace();

    for enumerator in enum_def.enumerators() {
        let escaped_identifier = enumerator.escape_identifier();
        let mut builder = ContainerBuilder::new(
            "public partial record class", // Dunet nested records are always public
            &escaped_identifier,
        );

        if let Some(summary) = enumerator.formatted_doc_comment_summary() {
            builder.add_comment("summary", summary);
        }
        builder
            .add_comments(enumerator.formatted_doc_comment_seealso())
            .add_obsolete_attribute(enumerator)
            .add_base(enum_def.escape_identifier())
            .add_fields(&enumerator.associated_fields().unwrap_or_default());

        // Add cs::attribute
        for attribute in enumerator.cs_attributes() {
            builder.add_attribute(attribute);
        }

        builder.add_block({
            let mut code = CodeBlock::default();
            code.writeln(&CommentTag::new(
                "summary",
                "The discriminant of this enumerator.".to_owned(),
            ));
            writeln!(code, "public new const int Discriminant = {};", enumerator.value());
            code
        });

        builder.add_block(
            FunctionBuilder::new(
                "internal static",
                &escaped_identifier,
                "FromSliceDecoder",
                FunctionType::BlockBody,
            )
            .add_comment(
                "summary",
                format!(
                    r#"Decodes a new instance of <see cref="{escaped_identifier}" /> from the given Slice decoder."#
                ),
            )
            .add_parameter(
                "ref SliceDecoder",
                "decoder",
                None,
                Some("The Slice decoder.".to_owned()),
            )
            .set_body({
                let mut code = CodeBlock::default();

                if enum_def.is_unchecked {
                    code.writeln("decoder.SkipSize();");
                }

                code.writeln(&decode_enum_fields(
                    &enumerator.associated_fields().unwrap_or_default(),
                    &escaped_identifier,
                    &namespace,
                    FieldType::NonMangled,
                    Encoding::Slice2,
                ));

                // TODO: only for non-compact enum
                code.writeln("decoder.SkipTagged();");

                code.writeln("return result;");
                code
            })
            .build()
        );

        builder.add_block(
            FunctionBuilder::new("internal override", "void", "Encode", FunctionType::BlockBody)
                .add_parameter("ref SliceEncoder", "encoder", None, None)
                .set_body({
                    let mut code = CodeBlock::default();
                    code.writeln("encoder.EncodeVarInt32(Discriminant);");

                    if enum_def.is_unchecked {
                        // TODO: oddly enough, an enumerator declared as E() or E does not result in the same
                        // associated fields.
                        if enumerator.associated_fields().unwrap_or_default().is_empty() {
                            // For the tag end marker. This assumes an unchecked enum with associated fields is never
                            // compact.
                            code.writeln("encoder.EncodeSize(1);");
                        } else {
                            code.writeln("var sizePlaceholder = encoder.GetPlaceholderSpan(4);");
                            code.writeln("int startPos = encoder.EncodedByteCount;");
                        }
                    }

                    code.writeln(&encode_fields(
                        &enumerator.associated_fields().unwrap_or_default(),
                        &namespace,
                        FieldType::NonMangled,
                        Encoding::Slice2,
                    ));

                    // TODO: only for non-compact enum
                    code.writeln("encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);");

                    if enum_def.is_unchecked && !enumerator.associated_fields().unwrap_or_default().is_empty() {
                        code.writeln("SliceEncoder.EncodeVarUInt62((ulong)(encoder.EncodedByteCount - startPos), sizePlaceholder);");
                    }

                    code
                })
                .build()
        );

        builder.add_block("private protected override int GetDiscriminant() => Discriminant;".into());

        code.add_block(&builder.build());
    }

    if enum_def.is_unchecked {
        // Generate the Unknown enumerator

        let mut builder = ContainerBuilder::new("public partial record class", "Unknown");

        builder.add_comment(
            "summary",
            format!(
                "Represents an enumerator not defined in the local Slice definition of unchecked enum '{enum_name}'.",
                enum_name = enum_def.identifier(),
            ),
        );

        builder.add_block({
            let mut code = CodeBlock::default();
            code.writeln(&CommentTag::new(
                "summary",
                "The fields of this unknown enumerator.".into(),
            ));
            writeln!(
                code,
                "public global::System.ReadOnlyMemory<byte> Fields {{ get; init; }}"
            );
            code
        });

        builder.add_block("private readonly int _discriminant;".into());

        builder.add_block(
            FunctionBuilder::new("public", "", "Unknown", FunctionType::BlockBody)
                .add_comment("summary", "Constructs a new instance of Unknown.")
                .add_comment_with_attribute(
                    "param",
                    "name",
                    "discriminant",
                    "The discriminant of this unknown enumerator.",
                )
                .add_comment_with_attribute(
                    "param",
                    "name",
                    "fields",
                    "The encoded fields of this unknown enumerator.",
                )
                .add_parameter("int", "discriminant", None, None)
                .add_parameter("global::System.ReadOnlyMemory<byte>", "fields", None, None)
                .set_body({
                    let mut code = CodeBlock::default();
                    code.writeln("_discriminant = discriminant;");
                    code.writeln("Fields = fields;");
                    code
                })
                .build(),
        );

        builder.add_block(
            FunctionBuilder::new("internal override", "void", "Encode", FunctionType::BlockBody)
                .add_parameter("ref SliceEncoder", "encoder", None, None)
                .set_body({
                    let mut code = CodeBlock::default();
                    code.writeln("encoder.EncodeVarInt32(_discriminant);");
                    code.writeln("encoder.EncodeSize(Fields.Length);");
                    code.writeln("encoder.WriteByteSpan(Fields.Span);");

                    code
                })
                .build(),
        );

        builder.add_block("private protected override int GetDiscriminant() => _discriminant;".into());

        builder.build();
        code.add_block(&builder.build());
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
            cs_type.to_cs_case(Case::Pascal),
        ),
    );

    builder.add_comment(
        "summary",
        format!(
            r#"Provides an extension method for creating {} <see cref="{escaped_identifier}" /> from {} <see langword="{cs_type}" />."#,
            in_definite::get_a_or_an(&escaped_identifier),
            in_definite::get_a_or_an(&cs_type),
        ),
    )
    .add_generated_remark("static class", enum_def);

    // When the number of enumerators is smaller than the distance between the min and max
    // values, the values are not consecutive and we need to use a set to validate the value
    // during decoding.
    // Note that the values are not necessarily in order, e.g. we can use a simple range check
    // for enum E { A = 3, B = 2, C = 1 } during decoding.
    let min_max_values = enum_def.get_min_max_values();
    let use_set = if let Some((min_value, max_value)) = min_max_values {
        !enum_def.is_unchecked && (enum_def.enumerators.len() as i128) < max_value - min_value + 1
    } else {
        // An unchecked enum with no enumerator.
        false
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
    throw new global::System.IO.InvalidDataException($"Invalid enumerator value '{{value}}' for {scoped}.")"#,
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
    )
    .add_generated_remark("static class", enum_def);

    let encode_body = if enum_def.is_mapped_to_cs_enum() {
        format!(
            "{encode_enum}(({cs_type})value);",
            encode_enum = match &enum_def.underlying {
                Some(underlying) => format!("encoder.Encode{}", underlying.definition().type_suffix()),
                None => "encoder.EncodeSize".to_owned(),
            },
        )
    } else {
        "value.Encode(ref encoder);".to_owned()
    };

    // Enum encoding
    builder.add_block(
        format!(
            r#"
/// <summary>Encodes a <see cref="{escaped_identifier}" /> enum.</summary>
/// <param name="encoder">The Slice encoder.</param>
/// <param name="value">The <see cref="{escaped_identifier}" /> enumerator value to encode.</param>
{access} static void Encode{identifier}(this ref SliceEncoder encoder, {escaped_identifier} value) =>
    {encode_body}"#,
            identifier = enum_def.cs_identifier(Case::Pascal),
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
    ).add_generated_remark("static class", enum_def);

    let decode_body = if enum_def.is_mapped_to_cs_enum() {
        let underlying_extensions_class = format!(
            "{}{}Extensions",
            enum_def.cs_identifier(Case::Pascal),
            cs_type.to_cs_case(Case::Pascal),
        );

        format!(
            "{underlying_extensions_class}.As{identifier}({decode_enum});",
            identifier = enum_def.cs_identifier(Case::Pascal),
            decode_enum = match &enum_def.underlying {
                Some(underlying) => format!("decoder.Decode{}()", underlying.definition().type_suffix()),
                _ => "decoder.DecodeSize()".to_owned(), // Slice1 only
            },
        )
    } else {
        let mut cases = CodeBlock::default();

        for enumerator in enum_def.enumerators() {
            let enumerator_class = format!(
                "{escaped_identifier}.{enumerator_name}",
                enumerator_name = enumerator.escape_identifier(),
            );

            writeln!(
                cases,
                "{enumerator_class}.Discriminant => {enumerator_class}.FromSliceDecoder(ref decoder),",
            )
        }
        cases.indent().indent();

        let fallback = if enum_def.is_unchecked {
            format!("int value => new {escaped_identifier}.Unknown(value, decoder.DecodeSequence<byte>())")
        } else {
            format!(
                r#"int value => throw new global::System.IO.InvalidDataException($"Received invalid discriminant value '{{value}}' for {scoped}.")"#,
                scoped = enum_def.escape_scoped_identifier(&enum_def.namespace()),
            )
        };

        format!(
            r#"decoder.DecodeVarInt32() switch
    {{
        {cases}
        {fallback}
    }};"#,
        )
    };

    // Enum decoding
    builder.add_block(
        format!(
            r#"
/// <summary>Decodes a <see cref="{escaped_identifier}" /> enum.</summary>
/// <param name="decoder">The Slice decoder.</param>
/// <returns>The decoded <see cref="{escaped_identifier}" /> enumerator value.</returns>
{access} static {escaped_identifier} Decode{identifier}(this ref SliceDecoder decoder) =>
    {decode_body}"#,
            identifier = enum_def.cs_identifier(Case::Pascal)
        )
        .into(),
    );

    builder.build()
}
