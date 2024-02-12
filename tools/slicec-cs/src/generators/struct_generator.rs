// Copyright (c) ZeroC, Inc.

use crate::builders::{AttributeBuilder, Builder, CommentBuilder, ContainerBuilder, FunctionBuilder, FunctionType};
use crate::code_block::CodeBlock;
use crate::cs_attributes::CsReadonly;
use crate::decoding::*;
use crate::encoding::*;
use crate::member_util::*;
use crate::slicec_ext::{CommentExt, EntityExt, MemberExt, TypeRefExt};
use slicec::grammar::*;
use slicec::supported_encodings::SupportedEncodings;

pub fn generate_struct(struct_def: &Struct) -> CodeBlock {
    let escaped_identifier = struct_def.escape_identifier();
    let fields = struct_def.fields();
    let namespace = struct_def.namespace();

    let mut declaration = vec![struct_def.access_modifier()];
    if struct_def.has_attribute::<CsReadonly>() {
        declaration.push("readonly");
    }
    declaration.extend(["partial", "record", "struct"]);

    let mut builder = ContainerBuilder::new(&declaration.join(" "), &escaped_identifier);
    if let Some(summary) = struct_def.formatted_doc_comment_summary() {
        builder.add_comment("summary", summary);
    }
    builder
        .add_generated_remark("record struct", struct_def)
        .add_comments(struct_def.formatted_doc_comment_seealso())
        .add_obsolete_attribute(struct_def);

    builder.add_block(
        fields
            .iter()
            .map(|m| field_declaration(m))
            .collect::<Vec<_>>()
            .join("\n\n")
            .into(),
    );

    let mut main_constructor = FunctionBuilder::new(
        struct_def.access_modifier(),
        "",
        &escaped_identifier,
        FunctionType::BlockBody,
    );
    main_constructor.add_comment(
        "summary",
        format!(r#"Constructs a new instance of <see cref="{escaped_identifier}" />."#),
    );

    for field in &fields {
        main_constructor.add_parameter(
            &field.data_type().field_type_string(&namespace),
            field.parameter_name().as_str(),
            None,
            field.formatted_doc_comment_summary(),
        );
    }
    main_constructor.set_body({
        let mut code = CodeBlock::default();
        for field in &fields {
            writeln!(code, "this.{} = {};", field.field_name(), field.parameter_name(),);
        }
        code
    });
    builder.add_block(main_constructor.build());

    // Decode constructor
    let mut decode_body = generate_encoding_blocks(&fields, struct_def.supported_encodings(), decode_fields, "decoder");

    if !struct_def.is_compact {
        writeln!(decode_body, "decoder.SkipTagged();");
    }
    builder.add_block(
            FunctionBuilder::new(
                struct_def.access_modifier(),
                "",
                &escaped_identifier,
                FunctionType::BlockBody,
            )
            .add_comment(
                "summary",
                format!(r#"Constructs a new instance of <see cref="{escaped_identifier}" /> and decodes its fields from a Slice decoder."#),
            )
            .add_parameter(
                "ref SliceDecoder",
                "decoder",
                None,
                Some("The Slice decoder.".to_owned()),
            )
            .set_body(decode_body)
            .build(),
        );

    // Encode method
    let mut encode_body = generate_encoding_blocks(&fields, struct_def.supported_encodings(), encode_fields, "encoder");

    if !struct_def.is_compact {
        writeln!(encode_body, "encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);");
    }
    builder.add_block(
        FunctionBuilder::new(
            &(struct_def.access_modifier().to_owned() + " readonly"),
            "void",
            "Encode",
            FunctionType::BlockBody,
        )
        .add_comment("summary", "Encodes the fields of this struct with a Slice encoder.")
        .add_parameter(
            "ref SliceEncoder",
            "encoder",
            None,
            Some("The Slice encoder.".to_owned()),
        )
        .set_body(encode_body)
        .build(),
    );

    builder.build()
}

/// Generates an expression for encoding or decoding the fields of a struct.
/// It checks which encodings this struct supports, and only generates code for the necessary encodings.
fn generate_encoding_blocks(
    fields: &[&Field],
    encodings: SupportedEncodings,
    encoding_fn: fn(&[&Field], Encoding) -> CodeBlock,
    encoding_source: &'static str,
) -> CodeBlock {
    match encodings[..] {
        [] => unreachable!("No supported encodings"),
        [encoding] => encoding_fn(fields, encoding),
        _ => {
            let slice1_block = encoding_fn(fields, Encoding::Slice1);
            let slice2_block = encoding_fn(fields, Encoding::Slice2);

            // The encoding blocks are only empty for empty structs. But `Slice1` doesn't support empty structs.
            // So this branch of the match statement is never hit, since it's for structs that support both encodings.
            assert!(!slice1_block.is_empty() && !slice2_block.is_empty());

            // Only write one encoding block if `slice1_block` and `slice2_block` are the same.
            if slice1_block.to_string() == slice2_block.to_string() {
                return slice2_block;
            }

            format!(
                "\
if ({encoding_source}.Encoding == SliceEncoding.Slice1)
{{
{slice1_block}
}}
else // Slice2
{{
{slice2_block}
}}
",
                slice1_block = slice1_block.indent(),
                slice2_block = slice2_block.indent(),
            )
            .into()
        }
    }
}
