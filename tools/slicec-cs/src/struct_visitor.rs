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
use crate::slicec_ext::{MemberExt, NamedSymbolExt, TypeRefExt};

use slice::ast::Ast;
use slice::grammar::*;
use slice::util::*;
use slice::visitor::Visitor;

#[derive(Debug)]
pub struct StructVisitor<'a> {
    pub generated_code: &'a mut GeneratedCode,
}

impl<'a> Visitor for StructVisitor<'a> {
    fn visit_struct_start(&mut self, struct_def: &Struct, _: usize, ast: &Ast) {
        let readonly = struct_def.has_attribute("cs:readonly");
        let escaped_identifier = struct_def.escape_identifier();
        let members = struct_def.members(ast);
        let namespace = struct_def.namespace();

        let mut builder = ContainerBuilder::new(
            &format!(
                "{access} partial record struct",
                access = if readonly { "public readonly" } else { "public" },
            ),
            &escaped_identifier,
        );

        builder
            .add_comment("summary", &doc_comment_message(struct_def))
            .add_container_attributes(struct_def);

        builder.add_block(
            members
                .iter()
                .map(|m| data_member_declaration(m, readonly, FieldType::NonMangled, ast))
                .collect::<Vec<_>>()
                .join("\n")
                .into(),
        );

        let mut main_constructor =
            FunctionBuilder::new("public", "", &escaped_identifier, FunctionType::BlockBody);
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
                    .data_type
                    .to_type_string(&namespace, ast, TypeContext::DataMember),
                member.identifier(),
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

        // Decode constructor
        builder.add_block(
            FunctionBuilder::new("public", "", &escaped_identifier, FunctionType::BlockBody)
                .add_comment(
                    "summary",
                    &format!(
                        r#"Constructs a new instance of <see cref="{}"/> from a decoder."#,
                        &escaped_identifier
                    ),
                )
                .add_parameter("IceDecoder", "decoder", None, Some("The decoder."))
                .set_body(decode_data_members(
                    &members,
                    &namespace,
                    FieldType::NonMangled,
                    ast,
                ))
                .build(),
        );

        // Encode method
        builder.add_block(
            FunctionBuilder::new("public readonly", "void", "Encode", FunctionType::BlockBody)
                .add_comment("summary", "Encodes the fields of this struct.")
                .add_parameter("IceEncoder", "encoder", None, Some("The encoder."))
                .set_body(encode_data_members(
                    &members,
                    &namespace,
                    FieldType::NonMangled,
                    ast,
                ))
                .build(),
        );

        self.generated_code
            .insert_scoped(struct_def, builder.build().into());
    }
}
