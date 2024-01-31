// Copyright (c) ZeroC, Inc.

use crate::builders::{
    AttributeBuilder, Builder, CommentBuilder, ContainerBuilder, FunctionBuilder, FunctionCallBuilder, FunctionType,
};
use crate::decoding::decode_fields;
use crate::encoding::encode_fields;
use crate::member_util::*;
use crate::slicec_ext::*;
use slicec::code_block::CodeBlock;
use slicec::grammar::{Class, Encoding, Field};

pub fn generate_class(class_def: &Class) -> CodeBlock {
    let class_name = class_def.escape_identifier();
    let namespace = class_def.namespace();

    let fields = class_def.fields();
    let base_fields = class_def.base_class().map_or(vec![], Class::all_fields);

    let access = class_def.access_modifier();

    let mut non_nullable_fields = fields.clone();
    non_nullable_fields.retain(|f| !f.data_type.is_optional);

    let mut non_nullable_base_fields = base_fields.clone();
    non_nullable_base_fields.retain(|f| !f.data_type.is_optional);

    let mut class_builder = ContainerBuilder::new(&format!("{access} partial class"), &class_name);

    if let Some(summary) = class_def.formatted_doc_comment_summary() {
        class_builder.add_comment("summary", summary);
    }

    class_builder
        .add_generated_remark("class", class_def)
        .add_comments(class_def.formatted_doc_comment_seealso())
        .add_type_id_attribute(class_def)
        .add_compact_type_id_attribute(class_def)
        .add_obsolete_attribute(class_def);

    if let Some(base) = class_def.base_class() {
        class_builder.add_base(base.escape_scoped_identifier(&namespace));
    } else {
        class_builder.add_base("SliceClass".to_owned());
    }

    // Add class fields
    class_builder.add_block(
        fields
            .iter()
            .map(|m| field_declaration(m))
            .collect::<Vec<_>>()
            .join("\n\n")
            .into(),
    );

    // Class static type ID string
    class_builder.add_block(
        format!("private static readonly string SliceTypeId = typeof({class_name}).GetSliceTypeId()!;").into(),
    );

    if class_def.compact_id.is_some() {
        class_builder.add_block(
                format!(
                    "private static readonly int _compactSliceTypeId = typeof({class_name}).GetCompactSliceTypeId()!.Value;"
                )
                .into(),
            );
    }

    let constructor_summary = format!(r#"Constructs a new instance of <see cref="{class_name}" />."#);

    // The primary constructor (may be parameterless)
    class_builder.add_block(constructor(
        &class_name,
        access,
        constructor_summary.clone(),
        &namespace,
        &fields,
        &base_fields,
    ));

    // Secondary constructor for all fields minus those with optional types.
    // This constructor is only generated if necessary
    if non_nullable_fields.len() + non_nullable_base_fields.len() < fields.len() + base_fields.len() {
        class_builder.add_block(constructor(
            &class_name,
            access,
            constructor_summary,
            &namespace,
            &non_nullable_fields,
            &non_nullable_base_fields,
        ));
    }

    // parameterless constructor for decoding, generated only if the preceding constructors are not parameterless
    if non_nullable_fields.len() + non_nullable_base_fields.len() > 0 {
        let mut decode_constructor = FunctionBuilder::new(access, "", &class_name, FunctionType::BlockBody);
        decode_constructor.add_never_editor_browsable_attribute();
        decode_constructor.add_comment(
            "summary",
            format!(r#"Constructs a new instance of <see cref="{class_name}"/> for the Slice decoder."#),
        );
        decode_constructor.set_body(initialize_required_fields(&fields));
        class_builder.add_block(decode_constructor.build());
    }

    class_builder.add_block(encode_and_decode(class_def));

    class_builder.build()
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

    builder.add_comment("summary", summary_comment);

    builder.add_base_parameters(&base_fields.iter().map(|m| m.parameter_name()).collect::<Vec<String>>());

    for field in base_fields.iter().chain(fields.iter()) {
        builder.add_parameter(
            &field.data_type.field_type_string(namespace, false),
            &field.parameter_name(),
            None,
            field.formatted_doc_comment_summary(),
        );
    }

    builder.set_body({
        let mut code = CodeBlock::default();
        for field in fields {
            writeln!(code, "this.{} = {};", field.field_name(), field.parameter_name(),);
        }
        code
    });

    code.add_block(builder.build());

    code
}

fn encode_and_decode(class_def: &Class) -> CodeBlock {
    let mut code = CodeBlock::default();

    let fields = class_def.fields();
    let has_base_class = class_def.base_class().is_some();

    let encode_class = FunctionBuilder::new("protected override", "void", "EncodeCore", FunctionType::BlockBody)
        .add_parameter("ref SliceEncoder", "encoder", None, None)
        .set_body({
            let mut code = CodeBlock::default();

            code.writeln(
                &FunctionCallBuilder::new("encoder.StartSlice")
                    .add_argument("SliceTypeId")
                    .add_argument_if(class_def.compact_id.is_some(), "_compactSliceTypeId")
                    .build(),
            );

            // classes are Slice1 only
            code.writeln(&encode_fields(&fields, Encoding::Slice1));

            if has_base_class {
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
                Encoding::Slice1, // classes are Slice1 only
            ));
            code.writeln("decoder.EndSlice();");
            if has_base_class {
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
