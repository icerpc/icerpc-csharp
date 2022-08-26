// Copyright (c) ZeroC, Inc. All rights reserved.

use crate::builders::{
    AttributeBuilder, Builder, CommentBuilder, ContainerBuilder, FunctionBuilder, FunctionCallBuilder, FunctionType,
};
use crate::code_block::CodeBlock;
use crate::comments::doc_comment_message;
use crate::cs_util::*;
use crate::decoding::decode_data_members;
use crate::encoding::encode_data_members;
use crate::generated_code::GeneratedCode;
use crate::member_util::*;
use crate::slicec_ext::*;

use slice::grammar::{Class, DataMember, Encoding};
use slice::utils::code_gen_util::TypeContext;
use slice::visitor::Visitor;

pub struct ClassVisitor<'a> {
    pub generated_code: &'a mut GeneratedCode,
}

impl Visitor for ClassVisitor<'_> {
    fn visit_class_start(&mut self, class_def: &Class) {
        let class_name = class_def.escape_identifier();
        let namespace = class_def.namespace();
        let has_base_class = class_def.base_class().is_some();

        let members = class_def.members();
        let base_members = if let Some(base) = class_def.base_class() {
            base.all_members()
        } else {
            vec![]
        };
        let access = class_def.access_modifier();

        let non_default_members = members
            .iter()
            .cloned()
            .filter(|m| !m.is_default_initialized())
            .collect::<Vec<_>>();

        let non_default_base_members = base_members
            .iter()
            .cloned()
            .filter(|m| !m.is_default_initialized())
            .collect::<Vec<_>>();

        let mut class_builder = ContainerBuilder::new(&format!("{} partial class", access), &class_name);

        class_builder
            .add_comment("summary", doc_comment_message(class_def))
            .add_type_id_attribute(class_def)
            .add_compact_type_id_attribute(class_def)
            .add_container_attributes(class_def);

        if let Some(base) = class_def.base_class() {
            class_builder.add_base(base.escape_scoped_identifier(&namespace));
        } else {
            class_builder.add_base("IceRpc.Slice.AnyClass".to_owned());
        }

        // Add class fields
        class_builder.add_block(
            members
                .iter()
                .map(|m| data_member_declaration(m, FieldType::Class))
                .collect::<Vec<_>>()
                .join("\n")
                .into(),
        );

        // Class static type ID string
        class_builder.add_block(
            format!(
                "{} static{} readonly string SliceTypeId = typeof({}).GetSliceTypeId()!;",
                &access,
                if has_base_class { " new" } else { "" },
                class_name,
            )
            .into(),
        );

        if class_def.compact_id.is_some() {
            class_builder.add_block(
                format!(
                    "private static readonly int _compactSliceTypeId = typeof({}).GetCompactSliceTypeId()!.Value;",
                    class_name
                )
                .into(),
            );
        }

        let constructor_summary = format!(r#"Constructs a new instance of <see cref="{}"/>."#, class_name);

        // One-shot ctor (may be parameterless)
        class_builder.add_block(constructor(
            &class_name,
            &access,
            &constructor_summary,
            &namespace,
            &members,
            &base_members,
        ));

        // Second public constructor for all data members minus those with a default initializer
        // This constructor is only generated if necessary
        if non_default_members.len() + non_default_base_members.len() < members.len() + base_members.len() {
            class_builder.add_block(constructor(
                &class_name,
                &access,
                &constructor_summary,
                &namespace,
                &non_default_members,
                &non_default_base_members,
            ));
        }

        // public constructor used for decoding
        // the decoder parameter is used to distinguish this ctor from the parameterless ctor that
        // users may want to add to the partial class. It's not used otherwise.
        let mut decode_constructor = FunctionBuilder::new(&access, "", &class_name, FunctionType::BlockBody);

        if !has_base_class {
            decode_constructor.add_attribute(
                r#"global::System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Microsoft.Performance",
    "CA1801: Review unused parameters",
    Justification="Special constructor used for Slice decoding")"#,
            );
        }

        decode_constructor.add_parameter("ref SliceDecoder", "decoder", None, None);
        if has_base_class {
            decode_constructor.add_base_parameter("ref decoder");
        }
        decode_constructor
            .set_body(initialize_non_nullable_fields(&members, FieldType::Class))
            .add_never_editor_browsable_attribute();

        class_builder.add_block(decode_constructor.build());

        class_builder.add_block(encode_and_decode(class_def));

        self.generated_code.insert_scoped(class_def, class_builder.build());
    }
}

fn constructor(
    escaped_name: &str,
    access: &str,
    summary_comment: &str,
    namespace: &str,
    members: &[&DataMember],
    base_members: &[&DataMember],
) -> CodeBlock {
    let mut code = CodeBlock::new();

    let mut builder = FunctionBuilder::new(access, "", escaped_name, FunctionType::BlockBody);

    builder.add_comment("summary", summary_comment);

    builder.add_base_parameters(&base_members.iter().map(|m| m.parameter_name()).collect::<Vec<String>>());

    for member in base_members.iter().chain(members.iter()) {
        builder.add_parameter(
            &member
                .data_type
                .cs_type_string(namespace, TypeContext::DataMember, false),
            &member.parameter_name(),
            None,
            Some(doc_comment_message(*member)),
        );
    }

    builder.set_body({
        let mut code = CodeBlock::new();
        for member in members {
            writeln!(
                code,
                "this.{} = {};",
                member.field_name(FieldType::Class),
                member.parameter_name()
            );
        }
        code
    });

    code.add_block(&builder.build());

    code
}

fn encode_and_decode(class_def: &Class) -> CodeBlock {
    let mut code = CodeBlock::new();

    let namespace = &class_def.namespace();
    let members = class_def.members();
    let has_base_class = class_def.base_class().is_some();

    let encode_class = FunctionBuilder::new("protected override", "void", "EncodeCore", FunctionType::BlockBody)
        .add_parameter("ref SliceEncoder", "encoder", None, None)
        .set_body({
            let mut code = CodeBlock::new();

            code.writeln(
                &FunctionCallBuilder::new("encoder.StartSlice")
                    .add_argument("SliceTypeId")
                    .add_argument_if(class_def.compact_id.is_some(), "_compactSliceTypeId")
                    .build(),
            );

            code.writeln(&encode_data_members(
                &members,
                namespace,
                FieldType::Class,
                Encoding::Slice1, // classes are Slice1 only
            ));

            if has_base_class {
                code.writeln("encoder.EndSlice(false);");
                code.writeln("base.EncodeCore(ref encoder);");
            } else {
                code.writeln("encoder.EndSlice(true);"); // last slice
            }

            code
        })
        .build();

    let decode_class = FunctionBuilder::new("protected override", "void", "DecodeCore", FunctionType::BlockBody)
        .add_parameter("ref SliceDecoder", "decoder", None, None)
        .set_body({
            let mut code = CodeBlock::new();
            code.writeln("decoder.StartSlice();");
            code.writeln(&decode_data_members(
                &members,
                namespace,
                FieldType::Class,
                Encoding::Slice1, // classes are Slice1 only
            ));
            code.writeln("decoder.EndSlice();");
            if has_base_class {
                code.writeln("base.DecodeCore(ref decoder);");
            }
            code
        })
        .build();

    code.add_block(&encode_class);
    code.add_block(&decode_class);

    code
}
