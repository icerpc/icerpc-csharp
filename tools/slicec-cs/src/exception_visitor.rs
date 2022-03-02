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
use slice::code_gen_util::TypeContext;
use slice::grammar::{Exception, Member};
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
        let access = exception_def.access_modifier();

        let mut exception_class_builder =
            ContainerBuilder::new(&format!("{} partial class", access), &exception_name);

        exception_class_builder
            .add_comment("summary", &doc_comment_message(exception_def))
            .add_type_id_attribute(exception_def)
            .add_container_attributes(exception_def);

        if let Some(base) = exception_def.base_exception() {
            exception_class_builder.add_base(base.escape_scoped_identifier(namespace));
        } else {
            exception_class_builder.add_base("IceRpc.Slice.RemoteException".to_owned());
        }

        exception_class_builder.add_block(
            members
                .iter()
                .map(|m| data_member_declaration(m, FieldType::Exception))
                .collect::<Vec<_>>()
                .join("\n")
                .into(),
        );

        exception_class_builder.add_block(
            format!(
                "private static readonly string _sliceTypeId = typeof({}).GetSliceTypeId()!;",
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
                FunctionBuilder::new(&access, "", &exception_name, FunctionType::BlockBody)
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
            FunctionBuilder::new(&access, "", &exception_name, FunctionType::BlockBody)
                .add_parameter("ref SliceDecoder", "decoder", None, None)
                .add_base_parameter("ref decoder")
                .set_body({
                    let mut code = CodeBlock::new();
                    if !has_base && !members.is_empty() && !exception_def.uses_classes() {
                        writeln!(
                            code,
                            "\
if (decoder.Encoding == IceRpc.Encoding.Slice11)
{{
    {initialize_non_nullable_fields}
}}
else
{{
    {decode_data_members}
}}
                        ",
                            initialize_non_nullable_fields =
                                initialize_non_nullable_fields(&members, FieldType::Exception)
                                    .indent(),
                            decode_data_members =
                                decode_data_members(&members, namespace, FieldType::Exception)
                                    .indent()
                        )
                    } else {
                        code.writeln(&initialize_non_nullable_fields(
                            &members,
                            FieldType::Exception,
                        ))
                    }
                    code
                })
                .add_never_editor_browsable_attribute()
                .build(),
        );

        // Remote exceptions are always "preserved".
        exception_class_builder.add_block(
            FunctionBuilder::new(
                "protected override",
                "void",
                "DecodeCore",
                FunctionType::BlockBody,
            )
            .add_parameter("ref SliceDecoder", "decoder", None, None)
            .set_body({
                let mut code = CodeBlock::new();
                code.writeln("decoder.StartSlice();");
                code.writeln(&decode_data_members(
                    &members,
                    namespace,
                    FieldType::Exception,
                ));
                code.writeln("decoder.EndSlice();");

                if has_base {
                    code.writeln("base.DecodeCore(ref decoder);");
                }
                code
            })
            .build(),
        );

        exception_class_builder.add_block(encode_method(exception_def));
        exception_class_builder.add_block(encode_trait_method(exception_def));
        exception_class_builder.add_block(encode_core_method(exception_def));

        self.generated_code
            .insert_scoped(exception_def, exception_class_builder.build().into());
    }
}

fn encode_method(exception_def: &Exception) -> CodeBlock {
    let members = &exception_def.members();
    let namespace = &exception_def.namespace();
    let has_base = exception_def.base.is_some();

    let body = CodeBlock::from(format!(
        r#"
if (encoder.Encoding == IceRpc.Encoding.Slice11)
{{
    throw new InvalidOperationException("encoding an exception by its fields isn't supported with the 1.1 encoding");
}}

encoder.EncodeString(Message);
{encode_data_members}
        "#,
        encode_data_members = &encode_data_members(members, namespace, FieldType::Exception),
    ));

    let qualifier = if has_base { "public new" } else { "public" };

    FunctionBuilder::new(qualifier, "void", "Encode", FunctionType::BlockBody)
        .add_comment("summary", "Encodes the fields of this exception.")
        .add_parameter("ref SliceEncoder", "encoder", None, Some("The encoder."))
        .set_body(body)
        .build()
}

fn encode_trait_method(exception_def: &Exception) -> CodeBlock {
    let has_base = exception_def.base.is_some();

    // Exception inheritance is only supported with the 1.1 encoding,
    // so for 2.0 we only encode the least-derived base exception.
    let mut body_block = CodeBlock::from(if has_base {
        r#"
base.EncodeTrait(ref encoder);
        "#
    } else {
        r#"
encoder.EncodeString(_sliceTypeId);
this.Encode(ref encoder);
        "#
    });

    let body = CodeBlock::from(format!(
        r#"
if (encoder.Encoding == IceRpc.Encoding.Slice11)
{{
    this.EncodeCore(ref encoder);
}}
else
{{
    {body_block}
}}
        "#,
        body_block = body_block.indent(),
    ));

    FunctionBuilder::new("public override", "void", "EncodeTrait", FunctionType::BlockBody)
        .add_comment(
            "summary",
            "Encodes this exception as a trait, by encoding its Slice type ID followed by its fields.",
        )
        .add_parameter("ref SliceEncoder", "encoder", None, Some("The encoder."))
        .set_body(body)
        .build()
}

fn encode_core_method(exception_def: &Exception) -> CodeBlock {
    let members = &exception_def.members();
    let namespace = &exception_def.namespace();
    let has_base = exception_def.base.is_some();

    let body = CodeBlock::from(format!(
        r#"
if (encoder.Encoding != IceRpc.Encoding.Slice11)
{{
    throw new InvalidOperationException("encoding an exception in slices is only supported with the 1.1 encoding");
}}

encoder.StartSlice(_sliceTypeId);
{encode_data_members}
encoder.EndSlice(lastSlice: {is_last_slice});
{encode_base}
        "#,
        encode_data_members = &encode_data_members(members, namespace, FieldType::Exception),
        is_last_slice = (!has_base).to_string(),
        encode_base = if has_base { "base.EncodeCore(ref encoder);" } else { "" },
    ));

    FunctionBuilder::new("protected override", "void", "EncodeCore", FunctionType::BlockBody)
        .set_inherit_doc(true)
        .add_parameter("ref SliceEncoder", "encoder", None, None)
        .set_body(body)
        .build()
}

fn one_shot_constructor(
    exception_def: &Exception,
    add_message_and_exception_parameters: bool,
) -> CodeBlock {
    let access = exception_def.access_modifier();
    let exception_name = exception_def.escape_identifier();

    let namespace = &exception_def.namespace();

    let all_data_members = exception_def.all_members();

    if all_data_members.is_empty() && !add_message_and_exception_parameters {
        return CodeBlock::new();
    }

    let message_parameter_name = escape_parameter_name(&all_data_members, "message");
    let inner_exception_parameter_name = escape_parameter_name(&all_data_members, "innerException");
    let retry_policy_parameter_name = escape_parameter_name(&all_data_members, "retryPolicy");

    let base_parameters = if let Some(base) = exception_def.base_exception() {
        base.all_members()
            .iter()
            .map(|m| m.parameter_name())
            .collect::<Vec<_>>()
    } else {
        vec![]
    };

    let mut ctor_builder =
        FunctionBuilder::new(&access, "", &exception_name, FunctionType::BlockBody);

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

    for member in &all_data_members {
        ctor_builder.add_parameter(
            &member
                .data_type()
                .to_type_string(namespace, TypeContext::DataMember, false),
            member.parameter_name().as_str(),
            None,
            Some(&doc_comment_message(*member)),
        );
    }
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
