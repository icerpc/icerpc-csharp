// Copyright (c) ZeroC, Inc. All rights reserved.

use slice::grammar::*;
use slice::utils::code_gen_util::TypeContext;

use crate::builders::{Builder, CommentBuilder, ContainerBuilder, FunctionBuilder, FunctionType};
use crate::code_block::CodeBlock;
use crate::slicec_ext::*;

pub fn encoded_result_struct(operation: &Operation) -> CodeBlock {
    assert!(operation.has_encoded_result());
    let operation_name = operation.escape_identifier();
    let struct_name = format!("{}EncodedResult", operation_name);
    let namespace = operation.namespace();
    let access = operation.access_modifier();

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
        FunctionBuilder::new(&access, "", &struct_name, FunctionType::ExpressionBody);

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

            constructor_builder.add_parameter(
                "IceRpc.Features.IFeatureCollection",
                "features",
                None,
                None,
            );

            constructor_builder.set_body(
                format!(
                    "Payload = Response.{operation_name}(returnValue, features.Get<ISliceFeature>()?.EncodeOptions)",
                    operation_name = operation_name
                )
                .into(),
            );
        }
        parameters => {
            for parameter in parameters {
                let parameter_type =
                    parameter.to_type_string(&namespace, TypeContext::Encode, false);
                let parameter_name = parameter.parameter_name();

                constructor_builder.add_parameter(&parameter_type, &parameter_name, None, None);
            }

            constructor_builder.add_parameter(
                "IceRpc.Features.IFeatureCollection",
                "features",
                None,
                None,
            );

            constructor_builder.set_body(
                format!(
                    "Payload = Response.{operation_name}({args}, features.Get<ISliceFeature>()?.EncodeOptions)",
                    operation_name = operation_name,
                    args = parameters
                        .iter()
                        .map(|p| p.parameter_name())
                        .collect::<Vec<_>>()
                        .join(", ")
                )
                .into(),
            );
        }
    }

    container_builder.add_block(constructor_builder.build());
    container_builder.build()
}
