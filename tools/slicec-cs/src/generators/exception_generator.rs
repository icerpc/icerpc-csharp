// Copyright (c) ZeroC, Inc.

use crate::builders::{AttributeBuilder, Builder, CommentBuilder, ContainerBuilder, FunctionBuilder, FunctionType};
use crate::decoding::decode_fields;
use crate::encoding::encode_fields;
use crate::member_util::*;
use crate::slicec_ext::*;
use slicec::code_block::CodeBlock;
use slicec::grammar::{Encoding, Exception, Member};

pub fn generate_exception(exception_def: &Exception) -> CodeBlock {
    let exception_name = exception_def.escape_identifier();
    let has_base = exception_def.base.is_some();

    let namespace = &exception_def.namespace();

    let fields = &exception_def.fields();

    let access = exception_def.access_modifier();

    let mut exception_class_builder = ContainerBuilder::new(&format!("{access} partial class"), &exception_name);

    if let Some(summary) = exception_def.formatted_doc_comment_summary() {
        exception_class_builder.add_comment("summary", summary);
    }
    exception_class_builder
        .add_generated_remark("class", exception_def)
        .add_comments(exception_def.formatted_doc_comment_seealso())
        .add_obsolete_attribute(exception_def)
        .add_type_id_attribute(exception_def);

    if let Some(base) = exception_def.base_exception() {
        exception_class_builder.add_base(base.escape_scoped_identifier(namespace));
    } else {
        exception_class_builder.add_base("SliceException".to_owned());
    }

    exception_class_builder.add_block(
        fields
            .iter()
            .map(|m| field_declaration(m))
            .collect::<Vec<_>>()
            .join("\n\n")
            .into(),
    );

    exception_class_builder.add_block(
        format!("private static readonly string SliceTypeId = typeof({exception_name}).GetSliceTypeId()!;").into(),
    );

    exception_class_builder.add_block(one_shot_constructor(exception_def));

    // constructor for decoding, generated only if necessary
    if !exception_def.all_fields().is_empty() {
        exception_class_builder.add_block(
            FunctionBuilder::new("public", "", &exception_name, FunctionType::BlockBody)
                .add_never_editor_browsable_attribute()
                .add_comment(
                    "summary",
                    format!(
                        r#"Constructs a new instance of <see cref="{}"/> for the Slice decoder."#,
                        &exception_name
                    ),
                )
                .add_parameter("string?", "message", None, None)
                .add_base_parameter("message")
                .add_parameter("global::System.Exception?", "innerException", None, None)
                .add_base_parameter("innerException")
                .set_body(initialize_required_fields(fields))
                .build(),
        );
    }

    exception_class_builder.add_block(
        FunctionBuilder::new("protected override", "void", "DecodeCore", FunctionType::BlockBody)
            .add_never_editor_browsable_attribute()
            .add_parameter("ref SliceDecoder", "decoder", None, None)
            .set_body(
                format!(
                    "\
decoder.StartSlice();
{decode_fields}
decoder.EndSlice();
{decode_base}",
                    decode_fields = decode_fields(fields, Encoding::Slice1),
                    decode_base = if has_base { "base.DecodeCore(ref decoder);" } else { "" },
                )
                .into(),
            )
            .build(),
    );

    exception_class_builder.add_block(encode_core_method(exception_def));

    exception_class_builder.build()
}

fn encode_core_method(exception_def: &Exception) -> CodeBlock {
    let fields = &exception_def.fields();
    let has_base = exception_def.base.is_some();

    FunctionBuilder::new("protected override", "void", "EncodeCore", FunctionType::BlockBody)
        .add_never_editor_browsable_attribute()
        .add_parameter("ref SliceEncoder", "encoder", None, None)
        .set_body(
            format!(
                "\
encoder.StartSlice(SliceTypeId);
{encode_fields}
encoder.EndSlice(lastSlice: {is_last_slice});
{encode_base}",
                encode_fields = encode_fields(fields, Encoding::Slice1),
                is_last_slice = !has_base,
                encode_base = if has_base { "base.EncodeCore(ref encoder);" } else { "" },
            )
            .into(),
        )
        .build()
}

fn one_shot_constructor(exception_def: &Exception) -> CodeBlock {
    let exception_name = exception_def.escape_identifier();

    let namespace = &exception_def.namespace();

    let all_fields = exception_def.all_fields();

    let message_parameter_name = escape_parameter_name(&all_fields, "message");
    let inner_exception_parameter_name = escape_parameter_name(&all_fields, "innerException");

    let base_parameters = if let Some(base) = exception_def.base_exception() {
        base.all_fields().iter().map(|m| m.parameter_name()).collect::<Vec<_>>()
    } else {
        vec![]
    };

    let mut ctor_builder = FunctionBuilder::new("public", "", &exception_name, FunctionType::BlockBody);

    ctor_builder.add_comment(
        "summary",
        format!(r#"Constructs a new instance of <see cref="{}" />."#, &exception_name),
    );

    for field in &all_fields {
        ctor_builder.add_parameter(
            &field.data_type().field_type_string(namespace, false),
            field.parameter_name().as_str(),
            None,
            field.formatted_doc_comment_summary(),
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
    for field in exception_def.fields() {
        let field_name = field.field_name();
        let parameter_name = field.parameter_name();

        writeln!(ctor_body, "this.{field_name} = {parameter_name};");
    }

    ctor_builder.set_body(ctor_body);

    ctor_builder.build()
}
