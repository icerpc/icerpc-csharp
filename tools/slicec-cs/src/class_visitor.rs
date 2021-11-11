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
use slice::grammar::{Class, DataMember};
use slice::visitor::Visitor;

pub struct ClassVisitor<'a> {
    pub generated_code: &'a mut GeneratedCode,
}

impl<'a> Visitor for ClassVisitor<'_> {
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

        let mut class_builder =
            ContainerBuilder::new(&format!("{} partial class", access), &class_name);

        class_builder
            .add_comment("summary", &doc_comment_message(class_def))
            .add_type_id_attribute(class_def)
            .add_compact_type_id_attribute(class_def)
            .add_container_attributes(class_def);

        if let Some(base) = class_def.base_class() {
            class_builder.add_base(base.escape_scoped_identifier(&namespace));
        } else {
            class_builder.add_base("IceRpc.AnyClass".to_owned());
        }

        // Add class fields
        class_builder.add_block(
            members
                .iter()
                .map(|m| data_member_declaration(m, false, FieldType::Class))
                .collect::<Vec<_>>()
                .join("\n")
                .into(),
        );

        // Class static TypeId string
        class_builder.add_block(
            format!(
                "{} static{} readonly string IceTypeId = typeof({}).GetIceTypeId()!;",
                &access,
                if has_base_class { " new" } else { "" },
                class_name,
            )
            .into(),
        );

        if class_def.compact_id.is_some() {
            class_builder.add_block(
                format!(
                "private static readonly int _compactTypeId = typeof({}).GetIceCompactTypeId()!.Value;",
                class_name
            ).into());
        }

        let constructor_summary = format!(
            r#"Constructs a new instance of <see cref="{}"/>."#,
            class_name
        );

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
        if non_default_members.len() + non_default_base_members.len()
            < members.len() + base_members.len()
        {
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
        let mut decode_constructor =
            FunctionBuilder::new(&access, "", &class_name, FunctionType::BlockBody);

        if !has_base_class {
            decode_constructor.add_attribute(
                r#"global::System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Microsoft.Performance",
    "CA1801: Review unused parameters",
    Justification="Special constructor used for Ice decoding")"#,
            );
        }

        decode_constructor.add_parameter("Ice11Decoder", "decoder", None, None);
        if has_base_class {
            decode_constructor.add_base_parameter("decoder");
        }
        decode_constructor
            .set_body(initialize_non_nullable_fields(&members, FieldType::Class))
            .add_never_editor_browsable_attribute();

        class_builder.add_block(decode_constructor.build());

        class_builder.add_block(encode_and_decode(class_def));

        self.generated_code
            .insert_scoped(class_def, class_builder.build().into());
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

    builder.add_base_parameters(
        &base_members
            .iter()
            .filter(|m| !m.is_default_initialized())
            .map(|m| m.parameter_name())
            .collect::<Vec<String>>(),
    );

    for member in members.iter().chain(base_members.iter()) {
        let parameter_type = member
            .data_type
            .to_type_string(namespace, TypeContext::DataMember);
        let parameter_name = member.parameter_name();

        builder.add_parameter(
            &parameter_type,
            &parameter_name,
            None,
            Some(&doc_comment_message(*member)),
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

    // TODO check preserve-slice metadata
    // const bool basePreserved = p->inheritsMetadata("preserve-slice");
    // const bool preserved = p->hasMetadata("preserve-slice");

    let is_base_preserved = false;
    let is_preserved = false;

    if is_preserved && !is_base_preserved {
        code.add_block("\
protected override global::System.Collections.Immutable.ImmutableList<IceRpc.Slice.SliceInfo> IceUnknownSlices { get; set; } =
    global::System.Collections.Immutable.ImmutableList<IceRpc.Slice.SliceInfo>.Empty;");
    }

    let encode_class = FunctionBuilder::new(
        "protected override",
        "void",
        "IceEncode",
        FunctionType::BlockBody,
    )
    .add_parameter("Ice11Encoder", "encoder", None, None)
    .set_body({
        let mut code = CodeBlock::new();

        let mut start_slice_args = vec!["IceTypeId"];

        if class_def.compact_id.is_some() {
            start_slice_args.push("_compactTypeId");
        }

        writeln!(
            code,
            "encoder.IceStartSlice({});",
            start_slice_args.join(", ")
        );

        code.writeln(&encode_data_members(&members, namespace, FieldType::Class));

        if has_base_class {
            code.writeln("encoder.IceEndSlice(false);");
            code.writeln("base.IceEncode(encoder);");
        } else {
            code.writeln("encoder.IceEndSlice(true);"); // last slice
        }

        code
    })
    .build();

    let decode_class = FunctionBuilder::new(
        "protected override",
        "void",
        "IceDecode",
        FunctionType::BlockBody,
    )
    .add_parameter("Ice11Decoder", "decoder", None, None)
    .set_body({
        let mut code = CodeBlock::new();
        code.writeln("decoder.IceStartSlice();");
        code.writeln(&decode_data_members(&members, namespace, FieldType::Class));
        code.writeln("decoder.IceEndSlice();");
        if has_base_class {
            code.writeln("base.IceDecode(decoder);");
        }
        code
    })
    .build();

    code.add_block(&encode_class);
    code.add_block(&decode_class);

    code
}
