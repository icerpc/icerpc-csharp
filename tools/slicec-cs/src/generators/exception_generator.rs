// Copyright (c) ZeroC, Inc.

use crate::builders::{
    AttributeBuilder, Builder, CommentBuilder, ContainerBuilder, FunctionBuilder, FunctionCallBuilder, FunctionType,
};
use crate::code_block::CodeBlock;
use crate::decoding::decode_fields;
use crate::encoding::encode_fields;
use crate::member_util::*;
use crate::slicec_ext::*;
use slicec::grammar::{Encoding, Exception, Field};

// Keep this file in sync with class_generator.rs

pub fn generate_exception(def: &Exception) -> CodeBlock {
    let class_name = def.escape_identifier();
    let namespace = def.namespace();

    let fields = def.fields();
    let base_fields = def.base_exception().map_or(vec![], Exception::all_fields);
    let all_fields = def.all_fields();

    let access = def.access_modifier();

    let mut non_nullable_fields = fields.clone();
    non_nullable_fields.retain(|f| !f.data_type.is_optional);

    let mut non_nullable_base_fields = base_fields.clone();
    non_nullable_base_fields.retain(|f| !f.data_type.is_optional);

    let mut builder = ContainerBuilder::new(&format!("{access} partial class"), &class_name);

    if let Some(summary) = def.formatted_doc_comment_summary() {
        builder.add_comment("summary", summary);
    }

    builder
        .add_generated_remark("class", def)
        .add_comments(def.formatted_doc_comment_seealso())
        .add_type_id_attribute(def)
        .add_obsolete_attribute(def);

    if let Some(base) = def.base_exception() {
        builder.add_base(base.escape_scoped_identifier(&namespace));
    } else {
        builder.add_base("SliceException".to_owned());
    }

    // Add class fields
    builder.add_block(
        fields
            .iter()
            .map(|m| field_declaration(m))
            .collect::<Vec<_>>()
            .join("\n\n")
            .into(),
    );

    builder.add_block(
        format!("private static readonly string SliceTypeId = typeof({class_name}).GetSliceTypeId()!;").into(),
    );

    let constructor_summary = format!(r#"Constructs a new instance of <see cref="{class_name}" />."#);

    if !all_fields.is_empty() {
        // parameterless constructor.
        // The constructor needs to be public for System.Activator.CreateInstance.
        let mut parameterless_constructor = FunctionBuilder::new("public", "", &class_name, FunctionType::BlockBody);
        parameterless_constructor.add_comment("summary", constructor_summary.clone());
        builder.add_block(parameterless_constructor.build());

        // The primary constructor.
        builder.add_block(constructor(
            &class_name,
            access,
            constructor_summary.clone(),
            &namespace,
            &fields,
            &base_fields,
        ));

        // Secondary constructor for all fields minus those that are nullable.
        // This constructor is only generated if necessary.
        let non_nullable_fields_len = non_nullable_fields.len() + non_nullable_base_fields.len();
        if non_nullable_fields_len > 0 && non_nullable_fields_len < all_fields.len() {
            builder.add_block(constructor(
                &class_name,
                access,
                constructor_summary,
                &namespace,
                &non_nullable_fields,
                &non_nullable_base_fields,
            ));
        }
    }
    // else, we rely on the default parameterless constructor.

    builder.add_block(encode_and_decode(def));

    builder.build()
}

fn constructor(
    escaped_name: &str,
    access: &str,
    summary_comment: String,
    namespace: &str,
    fields: &[&Field],
    base_fields: &[&Field],
) -> CodeBlock {
    let mut code = CodeBlock::default();

    let mut builder = FunctionBuilder::new(access, "", escaped_name, FunctionType::BlockBody);

    if fields.iter().any(|f| f.is_required()) || base_fields.iter().any(|f| f.is_required()) {
        builder.add_attribute("global::System.Diagnostics.CodeAnalysis.SetsRequiredMembers");
    }

    builder.add_comment("summary", summary_comment);

    builder.add_base_parameters(&base_fields.iter().map(|m| m.parameter_name()).collect::<Vec<String>>());

    for field in base_fields.iter().chain(fields.iter()) {
        builder.add_parameter(
            &field.data_type.field_type_string(namespace),
            &field.parameter_name(),
            None,
            field.formatted_doc_comment_summary(),
        );
    }

    builder.set_body({
        let mut code = CodeBlock::default();
        for field in fields {
            writeln!(code, "this.{} = {};", field.field_name(), field.parameter_name());
        }
        code
    });

    code.add_block(builder.build());

    code
}

fn encode_and_decode(def: &Exception) -> CodeBlock {
    let mut code = CodeBlock::default();

    let fields = def.fields();
    let has_base = def.base_exception().is_some();

    let encode_class = FunctionBuilder::new("protected override", "void", "EncodeCore", FunctionType::BlockBody)
        .add_parameter("ref SliceEncoder", "encoder", None, None)
        .set_body({
            let mut code = CodeBlock::default();

            code.writeln(
                &FunctionCallBuilder::new("encoder.StartSlice")
                    .add_argument("SliceTypeId")
                    .build(),
            );

            // classes and exceptions are Slice1 only
            code.writeln(&encode_fields(&fields, Encoding::Slice1));

            if has_base {
                code.writeln("encoder.EndSlice(false);");
                code.writeln("base.EncodeCore(ref encoder);");
            } else {
                code.writeln("encoder.EndSlice(true);"); // last slice
            }

            code
        })
        .add_never_editor_browsable_attribute()
        .build();

    let decode_class = FunctionBuilder::new("protected override", "void", "DecodeCore", FunctionType::BlockBody)
        .add_parameter("ref SliceDecoder", "decoder", None, None)
        .set_body({
            let mut code = CodeBlock::default();
            code.writeln("decoder.StartSlice();");
            code.writeln(&decode_fields(
                &fields,
                Encoding::Slice1, // classes and exceptions are Slice1 only
            ));
            code.writeln("decoder.EndSlice();");
            if has_base {
                code.writeln("base.DecodeCore(ref decoder);");
            }
            code
        })
        .add_never_editor_browsable_attribute()
        .build();

    code.add_block(encode_class);
    code.add_block(decode_class);

    code
}
