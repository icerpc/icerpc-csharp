// Copyright (c) ZeroC, Inc.

use crate::builders::{
    AttributeBuilder, Builder, CommentBuilder, ContainerBuilder, EncodingBlockBuilder, FunctionBuilder, FunctionType,
};
use crate::comments::doc_comment_message;
use crate::cs_util::FieldType;
use crate::decoding::*;
use crate::encoding::*;
use crate::generated_code::GeneratedCode;
use crate::member_util::*;
use crate::slicec_ext::{EntityExt, MemberExt, TypeRefExt};
use slice::code_block::CodeBlock;

use slice::grammar::*;
use slice::utils::code_gen_util::*;
use slice::visitor::Visitor;

#[derive(Debug)]
pub struct StructVisitor<'a> {
    pub generated_code: &'a mut GeneratedCode,
}

impl<'a> Visitor for StructVisitor<'a> {
    fn visit_struct_start(&mut self, struct_def: &Struct) {
        let escaped_identifier = struct_def.escape_identifier();
        let members = struct_def.members();
        let namespace = struct_def.namespace();

        let mut builder = ContainerBuilder::new(
            &format!("{} partial record struct", struct_def.modifiers()),
            &escaped_identifier,
        );
        builder
            .add_comment("summary", doc_comment_message(struct_def))
            .add_container_attributes(struct_def);

        builder.add_block(
            members
                .iter()
                .map(|m| data_member_declaration(m, FieldType::NonMangled))
                .collect::<Vec<_>>()
                .join("\n")
                .into(),
        );

        let mut main_constructor = FunctionBuilder::new(
            &struct_def.access_modifier(),
            "",
            &escaped_identifier,
            FunctionType::BlockBody,
        );
        main_constructor.add_comment(
            "summary",
            &format!(r#"Constructs a new instance of <see cref="{escaped_identifier}" />."#),
        );

        for member in &members {
            main_constructor.add_parameter(
                &member
                    .data_type()
                    .cs_type_string(&namespace, TypeContext::DataMember, false),
                member.parameter_name().as_str(),
                None,
                Some(doc_comment_message(*member)),
            );
        }
        main_constructor.set_body({
            let mut code = CodeBlock::default();
            for member in &members {
                writeln!(
                    code,
                    "this.{} = {};",
                    member.field_name(FieldType::NonMangled),
                    member.parameter_name(),
                );
            }
            code
        });
        builder.add_block(main_constructor.build());

        // Decode constructor
        let mut decode_body = EncodingBlockBuilder::new(
            "decoder.Encoding",
            &struct_def.escape_identifier(),
            struct_def.supported_encodings(),
            false, // No encoding check for structs
        )
        .add_encoding_block(Encoding::Slice1, || {
            decode_data_members(&members, &namespace, FieldType::NonMangled, Encoding::Slice1)
        })
        .add_encoding_block(Encoding::Slice2, || {
            decode_data_members(&members, &namespace, FieldType::NonMangled, Encoding::Slice2)
        })
        .build();

        if !struct_def.is_compact {
            writeln!(decode_body, "decoder.SkipTagged(useTagEndMarker: true);");
        }
        builder.add_block(
            FunctionBuilder::new(
                &struct_def.access_modifier(),
                "",
                &escaped_identifier,
                FunctionType::BlockBody,
            )
            .add_comment(
                "summary",
                &format!(r#"Constructs a new instance of <see cref="{escaped_identifier}" /> from a decoder."#),
            )
            .add_parameter("ref SliceDecoder", "decoder", None, Some("The decoder."))
            .set_body(decode_body)
            .build(),
        );

        // Encode method
        let mut encode_body = EncodingBlockBuilder::new(
            "encoder.Encoding",
            &struct_def.escape_identifier(),
            struct_def.supported_encodings(),
            false, // No encoding check for structs
        )
        .add_encoding_block(Encoding::Slice1, || {
            encode_data_members(&members, &namespace, FieldType::NonMangled, Encoding::Slice1)
        })
        .add_encoding_block(Encoding::Slice2, || {
            encode_data_members(&members, &namespace, FieldType::NonMangled, Encoding::Slice2)
        })
        .build();

        if !struct_def.is_compact {
            writeln!(encode_body, "encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);");
        }
        builder.add_block(
            FunctionBuilder::new(
                &(struct_def.access_modifier() + " readonly"),
                "void",
                "Encode",
                FunctionType::BlockBody,
            )
            .add_comment("summary", "Encodes the fields of this struct.")
            .add_parameter("ref SliceEncoder", "encoder", None, Some("The encoder."))
            .set_body(encode_body)
            .build(),
        );

        self.generated_code.insert_scoped(struct_def, builder.build());
    }
}
