// Copyright (c) ZeroC, Inc.

use crate::builders::{
    AttributeBuilder, Builder, CommentBuilder, ContainerBuilder, EncodingBlockBuilder, FunctionBuilder, FunctionType,
};
use crate::cs_util::*;
use crate::decoding::decode_data_members;
use crate::encoding::encode_data_members;
use crate::generated_code::GeneratedCode;
use crate::member_util::*;
use crate::slicec_ext::*;
use slice::code_block::CodeBlock;

use slice::grammar::{Encoding, Exception, Member, Type};
use slice::utils::code_gen_util::TypeContext;
use slice::visitor::Visitor;

pub struct ExceptionVisitor<'a> {
    pub generated_code: &'a mut GeneratedCode,
}

impl Visitor for ExceptionVisitor<'_> {
    fn visit_exception_start(&mut self, exception_def: &Exception) {
        let exception_name = exception_def.escape_identifier();
        let has_base = exception_def.base.is_some();

        let namespace = &exception_def.namespace();

        let members = exception_def.members();

        let access = exception_def.access_modifier();

        let mut exception_class_builder = ContainerBuilder::new(&format!("{access} partial class"), &exception_name);

        exception_class_builder
            .add_comments(exception_def.formatted_doc_comment())
            .add_container_attributes(exception_def);

        if exception_def.supported_encodings().supports(&Encoding::Slice1) {
            exception_class_builder.add_type_id_attribute(exception_def);
        }

        if let Some(base) = exception_def.base_exception() {
            exception_class_builder.add_base(base.escape_scoped_identifier(namespace));
        } else {
            exception_class_builder.add_base("SliceException".to_owned());
        }

        exception_class_builder.add_block(
            members
                .iter()
                .map(|m| data_member_declaration(m, FieldType::Exception))
                .collect::<Vec<_>>()
                .join("\n")
                .into(),
        );

        if exception_def.supported_encodings().supports(&Encoding::Slice1) {
            exception_class_builder.add_block(
                format!(
                    "public static{}readonly string SliceTypeId = typeof({exception_name}).GetSliceTypeId()!;",
                    if has_base { " new " } else { " " },
                )
                .into(),
            );
        }

        exception_class_builder.add_block(one_shot_constructor(exception_def));

        if has_base {
            exception_class_builder.add_block(
                FunctionBuilder::new("public", "", &exception_name, FunctionType::BlockBody)
                    .add_parameter("ref SliceDecoder", "decoder", None, None)
                    .add_parameter("string?", "message", Some("null"), None)
                    .add_base_parameter("ref decoder")
                    .add_base_parameter("message")
                    .set_body(initialize_non_nullable_fields(&members, FieldType::Exception))
                    .add_never_editor_browsable_attribute()
                    .build(),
            );
        } else {
            // With Slice1, this constructor should be called only by the Activator and not directly by the application
            // or generated code. With Slice2, it's a regular decoding constructor that can be called directly by the
            // generated code or the application. Hence no "never editor browsable" attribute.
            exception_class_builder.add_block(
                FunctionBuilder::new("public", "", &exception_name, FunctionType::BlockBody)
                    .add_parameter("ref SliceDecoder", "decoder", None, None)
                    .add_parameter("string?", "message", Some("null"), None)
                    .add_base_parameter("message")
                    .set_body(
                        EncodingBlockBuilder::new(
                            "decoder.Encoding",
                            &exception_name,
                            exception_def.supported_encodings(),
                            false,
                        )
                        .add_encoding_block(Encoding::Slice1, || {
                            format!(
                                "\
{}
ConvertToUnhandled = true;",
                                initialize_non_nullable_fields(&members, FieldType::Exception),
                            )
                            .into()
                        })
                        .add_encoding_block(Encoding::Slice2, || {
                            format!(
                                "\
{}
decoder.SkipTagged(useTagEndMarker: true);
ConvertToUnhandled = true;",
                                decode_data_members(&members, namespace, FieldType::Exception, Encoding::Slice2,),
                            )
                            .into()
                        })
                        .build(),
                    )
                    .build(),
            );
        }

        if exception_def.supported_encodings().supports(&Encoding::Slice1) {
            exception_class_builder.add_block(
                FunctionBuilder::new("protected override", "void", "DecodeCore", FunctionType::BlockBody)
                    .add_parameter("ref SliceDecoder", "decoder", None, None)
                    .set_body({
                        let mut code = CodeBlock::default();
                        code.writeln("decoder.StartSlice();");
                        code.writeln(&decode_data_members(
                            &members,
                            namespace,
                            FieldType::Exception,
                            Encoding::Slice1,
                        ));
                        code.writeln("decoder.EndSlice();");

                        if has_base {
                            code.writeln("base.DecodeCore(ref decoder);");
                        }
                        code
                    })
                    .build(),
            );
        }

        exception_class_builder.add_block(encode_core_method(exception_def));

        self.generated_code
            .insert_scoped(exception_def, exception_class_builder.build());
    }
}

fn encode_core_method(exception_def: &Exception) -> CodeBlock {
    let members = &exception_def.members();
    let namespace = &exception_def.namespace();
    let has_base = exception_def.base.is_some();

    let body = EncodingBlockBuilder::new(
        "encoder.Encoding",
        &exception_def.escape_identifier(),
        exception_def.supported_encodings(),
        true,
    )
    .add_encoding_block(Encoding::Slice1, || {
        format!(
            "\
encoder.StartSlice(SliceTypeId);
{encode_data_members}
encoder.EndSlice(lastSlice: {is_last_slice});
{encode_base}",
            encode_data_members = &encode_data_members(members, namespace, FieldType::Exception, Encoding::Slice1),
            is_last_slice = !has_base,
            encode_base = if has_base { "base.EncodeCore(ref encoder);" } else { "" },
        )
        .into()
    })
    .add_encoding_block(Encoding::Slice2, || {
        format!(
            "\
{encode_data_members}
encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);",
            encode_data_members = &encode_data_members(members, namespace, FieldType::Exception, Encoding::Slice2),
        )
        .into()
    })
    .build();

    FunctionBuilder::new("protected override", "void", "EncodeCore", FunctionType::BlockBody)
        .add_parameter("ref SliceEncoder", "encoder", None, None)
        .set_body(body)
        .build()
}

fn one_shot_constructor(exception_def: &Exception) -> CodeBlock {
    let exception_name = exception_def.escape_identifier();

    let namespace = &exception_def.namespace();

    let all_data_members = exception_def.all_members();

    let message_parameter_name = escape_parameter_name(&all_data_members, "message");
    let inner_exception_parameter_name = escape_parameter_name(&all_data_members, "innerException");

    let base_parameters = if let Some(base) = exception_def.base_exception() {
        base.all_members()
            .iter()
            .map(|m| m.parameter_name())
            .collect::<Vec<_>>()
    } else {
        vec![]
    };

    let mut ctor_builder = FunctionBuilder::new("public", "", &exception_name, FunctionType::BlockBody);

    ctor_builder.add_comment(
        "summary",
        format!(r#"Constructs a new instance of <see cref="{}" />."#, &exception_name),
    );

    for member in &all_data_members {
        ctor_builder.add_parameter(
            &member
                .data_type()
                .cs_type_string(namespace, TypeContext::DataMember, false),
            member.parameter_name().as_str(),
            None,
            member.formatted_doc_comment_summary(),
        );
    }
    ctor_builder.add_base_parameters(&base_parameters);

    ctor_builder
        .add_parameter(
            "string?",
            &message_parameter_name,
            Some("null"),
            Some("A message that describes the exception.".to_owned()),
        )
        .add_base_parameter(&message_parameter_name)
        .add_parameter(
            "global::System.Exception?",
            &inner_exception_parameter_name,
            Some("null"),
            Some("The exception that is the cause of the current exception.".to_owned()),
        )
        .add_base_parameter(&inner_exception_parameter_name);

    // ctor impl
    let mut ctor_body = CodeBlock::default();
    for member in exception_def.members() {
        let member_name = member.field_name(FieldType::Exception);
        let parameter_name = member.parameter_name();

        writeln!(ctor_body, "this.{member_name} = {parameter_name};");
    }

    ctor_builder.set_body(ctor_body);

    ctor_builder.build()
}
