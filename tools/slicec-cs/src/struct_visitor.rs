// Copyright (c) ZeroC, Inc. All rights reserved.

use crate::builders::EncodingBlockBuilder;
use crate::builders::{
    AttributeBuilder, CommentBuilder, ContainerBuilder, FunctionBuilder, FunctionType,
};
use crate::code_block::CodeBlock;
use crate::comments::doc_comment_message;
use crate::cs_util::FieldType;
use crate::decoding::*;
use crate::encoding::*;
use crate::generated_code::GeneratedCode;
use crate::member_util::*;
use crate::slicec_ext::{EntityExt, MemberExt, TypeRefExt};

use slice::code_gen_util::*;
use slice::grammar::*;
use slice::visitor::Visitor;

#[derive(Debug)]
pub struct StructVisitor<'a> {
    pub generated_code: &'a mut GeneratedCode,
}

impl<'a> Visitor for StructVisitor<'a> {
    fn visit_struct_start(&mut self, struct_def: &Struct) {
        // If the compact struct is using cs::type attribute we don't generate any code
        if !struct_def.has_attribute("cs::type", false) {
            let escaped_identifier = struct_def.escape_identifier();
            let members = struct_def.members();
            let namespace = struct_def.namespace();

            let mut builder = ContainerBuilder::new(
                &format!("{} partial record struct", struct_def.modifiers()),
                &escaped_identifier,
            );
            builder
                .add_comment("summary", &doc_comment_message(struct_def))
                .add_type_id_attribute(struct_def)
                .add_container_attributes(struct_def)
                .add_base("IceRpc.Slice.ITrait".to_owned());

            builder.add_block(
                format!(
                    "public static readonly string SliceTypeId = typeof({}).GetSliceTypeId()!;",
                    &escaped_identifier
                )
                .into(),
            );

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
                &format!(
                    r#"Constructs a new instance of <see cref="{}"/>."#,
                    &escaped_identifier
                ),
            );

            for member in &members {
                main_constructor.add_parameter(
                    &member
                        .data_type()
                        .to_type_string(&namespace, TypeContext::DataMember, false),
                    member.parameter_name().as_str(),
                    None,
                    Some(&doc_comment_message(*member)),
                );
            }
            main_constructor.set_body({
                let mut code = CodeBlock::new();
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

            let contains_nullable_proxies = contains_nullable_proxies(&members);

            // Decode constructor

            let mut decode_body = if !contains_nullable_proxies
                && struct_def.supported_encodings().supports(&Encoding::Slice1)
                && struct_def.supported_encodings().supports(&Encoding::Slice2)
            {
                // If the struct doesn't contain nullable proxies and it supports both encoding, we need a
                // single decode code block.
                decode_data_members(&members, &namespace, FieldType::NonMangled, None)
            } else {
                let mut encode_block_builder = EncodingBlockBuilder::new(
                    "decoder.Encoding",
                    &struct_def.escape_identifier(),
                    struct_def.supported_encodings(),
                    false, // No encoding check for structs
                );

                // If the struct contains nullable proxies and it supports the Slice1 encoding or if the struct only
                // support the Slice1 encoding add Slice1 encoding block.
                if (contains_nullable_proxies
                    && struct_def.supported_encodings().supports(&Encoding::Slice1))
                    || !struct_def.supported_encodings().supports(&Encoding::Slice2)
                {
                    encode_block_builder.add_encoding_block(
                        Encoding::Slice1,
                        decode_data_members(
                            &members,
                            &namespace,
                            FieldType::NonMangled,
                            Some(Encoding::Slice1),
                        ),
                    );
                }

                // If the struct contains nullable proxies and it supports the Slice2 encoding or if the struct only
                // support the Slice2 encoding add Slice2 encoding block.
                if (contains_nullable_proxies
                    && struct_def.supported_encodings().supports(&Encoding::Slice2))
                    || !struct_def.supported_encodings().supports(&Encoding::Slice1)
                {
                    encode_block_builder.add_encoding_block(
                        Encoding::Slice2,
                        decode_data_members(
                            &members,
                            &namespace,
                            FieldType::NonMangled,
                            Some(Encoding::Slice2),
                        ),
                    );
                }
                encode_block_builder.build()
            };

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
                    &format!(
                        r#"Constructs a new instance of <see cref="{}"/> from a decoder."#,
                        &escaped_identifier
                    ),
                )
                .add_parameter("ref SliceDecoder", "decoder", None, Some("The decoder."))
                .set_body(decode_body)
                .build(),
            );

            // Encode method
            let mut encode_body = if !contains_nullable_proxies
                && struct_def.supported_encodings().supports(&Encoding::Slice1)
                && struct_def.supported_encodings().supports(&Encoding::Slice2)
            {
                // If the struct doesn't contain nullable proxies and it supports both encoding, we need a
                // single encode code block.
                encode_data_members(&members, &namespace, FieldType::NonMangled, None)
            } else {
                let mut encode_block_builder = EncodingBlockBuilder::new(
                    "encoder.Encoding",
                    &struct_def.escape_identifier(),
                    struct_def.supported_encodings(),
                    false, // No encoding check for structs
                );

                if struct_def.supported_encodings().supports(&Encoding::Slice1) {
                    encode_block_builder.add_encoding_block(
                        Encoding::Slice1,
                        encode_data_members(
                            &members,
                            &namespace,
                            FieldType::NonMangled,
                            Some(Encoding::Slice1),
                        ),
                    );
                }

                if struct_def.supported_encodings().supports(&Encoding::Slice2) {
                    encode_block_builder.add_encoding_block(
                        Encoding::Slice2,
                        encode_data_members(
                            &members,
                            &namespace,
                            FieldType::NonMangled,
                            Some(Encoding::Slice2),
                        ),
                    );
                }
                encode_block_builder.build()
            };

            if !struct_def.is_compact {
                writeln!(
                    encode_body,
                    "encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);"
                );
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

            // EncodeTrait method
            builder.add_block(
                    FunctionBuilder::new(
                        "public readonly",
                        "void",
                        "EncodeTrait",
                        FunctionType::BlockBody,
                    )
                        .add_comment(
                            "summary",
                            "Encodes this struct as a trait, by encoding its Slice type ID followed by its fields.",
                        )
                        .add_parameter("ref SliceEncoder", "encoder", None, Some("The encoder."))
                        .set_body(
                            r#"
encoder.EncodeString(SliceTypeId);
this.Encode(ref encoder);"#.into(),
                        )
                        .build(),
                );

            self.generated_code
                .insert_scoped(struct_def, builder.build().into());
        }
    }
}

// TODO move to icerpc
fn contains_nullable_proxies(members: &[&impl Member]) -> bool {
    members
        .iter()
        .any(|member| match member.data_type().concrete_typeref() {
            TypeRefs::Dictionary(dictionary_ref) => {
                matches!(
                    dictionary_ref.value_type.concrete_typeref(),
                    TypeRefs::Interface(interface_def) if interface_def.is_optional
                )
            }
            TypeRefs::Interface(interface_def) => interface_def.is_optional,
            TypeRefs::Sequence(sequence_ref) => {
                matches!(
                    sequence_ref.element_type.concrete_typeref(),
                    TypeRefs::Interface(interface_def) if interface_def.is_optional
                )
            }
            _ => false,
        })
}
