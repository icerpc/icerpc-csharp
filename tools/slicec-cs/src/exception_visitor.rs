// Copyright (c) ZeroC, Inc. All rights reserved.

use crate::builders::{
    AttributeBuilder, CommentBuilder, ContainerBuilder, FunctionBuilder, FunctionType,
};
use crate::code_block::CodeBlock;
use crate::comments::doc_comment_message;
use crate::cs_util::*;
use crate::decoding::decode_data_members;
use crate::encoding::encode_data_members;
use crate::generated_code::GeneratedCode;
use crate::member_util::*;
use crate::slicec_ext::*;
use slice::grammar::Exception;
use slice::code_gen_util::TypeContext;
use slice::visitor::Visitor;

pub struct ExceptionVisitor<'a> {
    pub generated_code: &'a mut GeneratedCode,
}

impl<'a> Visitor for ExceptionVisitor<'_> {
    fn visit_exception_start(&mut self, exception_def: &Exception) {
        let exception_name = exception_def.escape_identifier();
        let has_base = exception_def.base.is_some();

        let namespace = &exception_def.namespace();

        let members = exception_def.members();

        let has_public_parameter_constructor = exception_def
            .all_members()
            .iter()
            .all(|m| m.is_default_initialized());

        let mut exception_class_builder =
            ContainerBuilder::new("public partial class", &exception_name);

        exception_class_builder
            .add_comment("summary", &doc_comment_message(exception_def))
            .add_type_id_attribute(exception_def)
            .add_container_attributes(exception_def);

        if let Some(base) = exception_def.base_exception() {
            exception_class_builder.add_base(base.escape_scoped_identifier(namespace));
        } else {
            exception_class_builder.add_base("IceRpc.RemoteException".to_owned());
        }

        exception_class_builder.add_block(
            members
                .iter()
                .map(|m| data_member_declaration(m, false, FieldType::Exception))
                .collect::<Vec<_>>()
                .join("\n")
                .into(),
        );

        exception_class_builder.add_block(
            format!(
                "private static readonly string _iceTypeId = typeof({}).GetIceTypeId()!;",
                exception_name
            )
            .into(),
        );

        exception_class_builder
            .add_block(one_shot_constructor(exception_def, false))
            .add_block(one_shot_constructor(exception_def, true));

        // public parameter-less constructor
        if has_public_parameter_constructor {
            exception_class_builder.add_block(
                FunctionBuilder::new("public", "", &exception_name, FunctionType::BlockBody)
                    .add_parameter(
                        "IceRpc.RetryPolicy?",
                        "retryPolicy",
                        Some("null"),
                        Some("The retry policy for the exception"),
                    )
                    .add_base_parameter("retryPolicy")
                    .build(),
            );
        }

        exception_class_builder.add_block(
            FunctionBuilder::new("public", "", &exception_name, FunctionType::BlockBody)
                .add_parameter("Ice11Decoder", "decoder", None, None)
                .add_base_parameter("decoder")
                .set_body(initialize_non_nullable_fields(
                    &members,
                    FieldType::Exception,
                ))
                .add_never_editor_browsable_attribute()
                .build(),
        );

        if !has_base && !exception_def.uses_classes() {
            // public constructor used for Ice 2.0 decoding
            exception_class_builder.add_block(
                FunctionBuilder::new("public", "", &exception_name, FunctionType::BlockBody)
                    .add_parameter("Ice20Decoder", "decoder", None, None)
                    .add_base_parameter("decoder")
                    .set_body(decode_data_members(
                        &members,
                        namespace,
                        FieldType::Exception,
                    ))
                    .build(),
            );
        }

        // Remote exceptions are always "preserved".
        exception_class_builder.add_block(
            FunctionBuilder::new(
                "protected override",
                "void",
                "IceDecode",
                FunctionType::BlockBody,
            )
            .add_parameter("Ice11Decoder", "decoder", None, None)
            .set_body({
                let mut code = CodeBlock::new();
                code.writeln("decoder.IceStartSlice();");
                code.writeln(&decode_data_members(
                    &members,
                    namespace,
                    FieldType::Exception,
                ));
                code.writeln("decoder.IceEndSlice();");

                if has_base {
                    code.writeln("base.IceDecode(decoder);");
                }
                code
            })
            .build(),
        );

        exception_class_builder.add_block(
            FunctionBuilder::new(
                "protected override",
                "void",
                "IceEncode",
                FunctionType::BlockBody,
            )
            .add_parameter("Ice11Encoder", "encoder", None, None)
            .set_body({
                let mut code = CodeBlock::new();
                code.writeln("encoder.IceStartSlice(_iceTypeId);");
                code.writeln(&encode_data_members(
                    &members,
                    namespace,
                    FieldType::Exception,
                ));

                if has_base {
                    code.writeln("encoder.IceEndSlice(lastSlice: false);");
                    code.writeln("base.IceEncode(encoder);");
                } else {
                    code.writeln("encoder.IceEndSlice(lastSlice: true);")
                }

                code
            })
            .build(),
        );

        if !has_base && !exception_def.uses_classes() {
            exception_class_builder.add_block(
                FunctionBuilder::new(
                    "protected override",
                    "void",
                    "IceEncode",
                    FunctionType::BlockBody,
                )
                .add_parameter("Ice20Encoder", "encoder", None, None)
                .set_body(
                    format!(
                        "\
encoder.EncodeString(_iceTypeId);
encoder.EncodeString(Message);
Origin.Encode(encoder);
{}",
                        &encode_data_members(&members, namespace, FieldType::Exception)
                    )
                    .into(),
                )
                .build(),
            );
        }

        self.generated_code
            .insert_scoped(exception_def, exception_class_builder.build().into());
    }
}

fn one_shot_constructor(
    exception_def: &Exception,
    add_message_and_exception_parameters: bool,
) -> CodeBlock {
    let exception_name = exception_def.escape_identifier();

    let namespace = &exception_def.namespace();

    let all_data_members = exception_def.all_members();

    if all_data_members.is_empty() && !add_message_and_exception_parameters {
        return CodeBlock::new();
    }

    let message_parameter_name = escape_parameter_name(&all_data_members, "message");
    let inner_exception_parameter_name = escape_parameter_name(&all_data_members, "innerException");
    let retry_policy_parameter_name = escape_parameter_name(&all_data_members, "retryPolicy");

    let all_parameters = all_data_members
        .iter()
        .map(|m| {
            let member_type = m
                .data_type
                .to_type_string(namespace, TypeContext::DataMember);
            let member_name = m.parameter_name();
            format!("{} {}", member_type, member_name)
        })
        .collect::<Vec<_>>();

    let base_parameters = if let Some(base) = exception_def.base_exception() {
        base.all_members()
            .iter()
            .map(|m| m.parameter_name())
            .collect::<Vec<_>>()
    } else {
        vec![]
    };

    let mut ctor_builder =
        FunctionBuilder::new("public", "", &exception_name, FunctionType::BlockBody);

    ctor_builder.add_comment(
        "summary",
        &format!(
            r#"Constructs a new instance of <see cref="{}"/>."#,
            &exception_name
        ),
    );

    if add_message_and_exception_parameters {
        ctor_builder.add_parameter(
            "string?",
            &message_parameter_name,
            None,
            Some("Message that describes the exception."),
        );
        ctor_builder.add_base_parameter(&message_parameter_name);
    }

    ctor_builder.add_parameters(&all_parameters);
    ctor_builder.add_base_parameters(&base_parameters);

    if add_message_and_exception_parameters {
        ctor_builder.add_parameter(
            "global::System.Exception?",
            &inner_exception_parameter_name,
            Some("null"),
            Some("The exception that is the cause of the current exception."),
        );
        ctor_builder.add_base_parameter(&inner_exception_parameter_name);
    }

    ctor_builder.add_parameter(
        "IceRpc.RetryPolicy?",
        &retry_policy_parameter_name,
        Some("null"),
        Some("The retry policy for the exception."),
    );
    ctor_builder.add_base_parameter(&retry_policy_parameter_name);

    // ctor impl
    let mut ctor_body = CodeBlock::new();
    for member in exception_def.members() {
        let member_name = member.field_name(FieldType::Exception);
        let parameter_name = member.parameter_name();

        writeln!(ctor_body, "this.{} = {};", member_name, parameter_name);
    }

    ctor_builder.set_body(ctor_body);

    ctor_builder.build()
}
