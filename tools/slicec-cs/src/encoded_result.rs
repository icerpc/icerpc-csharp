// Copyright (c) ZeroC, Inc. All rights reserved.

use slice::code_gen_util::TypeContext;
use slice::grammar::*;

use crate::builders::{CommentBuilder, ContainerBuilder, FunctionBuilder, FunctionType};
use crate::code_block::CodeBlock;
use crate::encoding::encode_operation;
use crate::member_util::escape_parameter_name;
use crate::slicec_ext::*;

pub fn encoded_result_struct(operation: &Operation) -> CodeBlock {
    assert!(operation.has_encoded_result());
    let operation_name = operation.escape_identifier();
    let struct_name = format!("{}EncodedResult", operation_name);
    let namespace = operation.namespace();
    let access = operation.access_modifier();
    let parameters = operation.return_members();
    let returns_classes = operation.returns_classes();
    let dispatch_parameter = escape_parameter_name(&parameters, "dispatch");

    let mut container_builder =
        ContainerBuilder::new(&format!("{} readonly record struct", access), &struct_name);

    container_builder.add_comment(
        "summary",
        &format!(
            "Helper record struct used to encode the return value of {operation_name} operation.",
            operation_name = operation_name
        ),
    );

    container_builder.add_block(
        format!(
            "\
/// <summary>Pipe reader to read the encoded return value of {operation_name} operation.</summary>
{access} global::System.IO.Pipelines.PipeReader Payload {{ get; }}",
            access = access,
            operation_name = operation_name
        )
        .into(),
    );

    let mut constructor_builder =
        FunctionBuilder::new(&access, "", &struct_name, FunctionType::BlockBody);

    constructor_builder.add_comment(
        "summary",
        &format!(
            r#"Constructs a new <see cref="{struct_name}"/> instance that
immediately encodes the return value of operation {operation_name}."#,
            struct_name = struct_name,
            operation_name = operation_name
        ),
    );

    match operation.return_members().as_slice() {
        [p] => {
            constructor_builder.add_parameter(
                &p.to_type_string(&namespace, TypeContext::Encode, false),
                "returnValue",
                None,
                None,
            );
        }
        _ => {
            for parameter in operation.return_members() {
                let parameter_type =
                    parameter.to_type_string(&namespace, TypeContext::Encode, false);
                let parameter_name = parameter.parameter_name();

                constructor_builder.add_parameter(&parameter_type, &parameter_name, None, None);
            }
        }
    }

    if !returns_classes {
        constructor_builder.add_parameter("IceRpc.Slice.Dispatch", &dispatch_parameter, None, None);
    }

    constructor_builder.set_body(encode_operation(operation, true, "Payload ="));

    container_builder.add_block(constructor_builder.build());
    container_builder.build().into()
}
