// Copyright (c) ZeroC, Inc.

use crate::builders::{Builder, CommentBuilder, ContainerBuilder, FunctionBuilder, FunctionType};
use crate::slicec_ext::*;
use slicec::code_block::CodeBlock;

use slicec::grammar::*;
use slicec::utils::code_gen_util::TypeContext;

pub fn encoded_result_struct(operation: &Operation) -> CodeBlock {
    assert!(operation.has_encoded_result());
    let operation_name = operation.escape_identifier();
    let struct_name = format!("{operation_name}EncodedResult");
    let namespace = operation.namespace();
    let access = operation.access_modifier();

    let mut container_builder = ContainerBuilder::new(&format!("{access} readonly record struct"), &struct_name);

    container_builder.add_comment(
        "summary",
        format!("Helper record struct used to encode the return value of {operation_name} operation."),
    );

    container_builder.add_block(
        format!(
            "\
/// <summary>Pipe reader to read the encoded return value of {operation_name} operation.</summary>
{access} global::System.IO.Pipelines.PipeReader Payload {{ get; }}"
        )
        .into(),
    );

    let mut constructor_builder = FunctionBuilder::new(&access, "", &struct_name, FunctionType::ExpressionBody);

    constructor_builder.add_comment(
        "summary",
        format!(
            r#"Constructs a new <see cref="{struct_name}" /> instance that
immediately encodes the return value of operation {operation_name}."#
        ),
    );

    match operation.non_streamed_return_members().as_slice() {
        [p] => {
            constructor_builder.add_parameter(
                &p.cs_type_string(&namespace, TypeContext::Encode, false),
                "returnValue",
                None,
                None,
            );

            constructor_builder.add_parameter("IceRpc.Features.IFeatureCollection", "features", None, None);

            constructor_builder.set_body(
                format!(
                    "Payload = Response.Encode{operation_name}(returnValue, features.Get<ISliceFeature>()?.EncodeOptions)"
                )
                .into(),
            );
        }
        parameters => {
            for parameter in parameters {
                let parameter_type = parameter.cs_type_string(&namespace, TypeContext::Encode, false);
                let parameter_name = parameter.parameter_name();

                constructor_builder.add_parameter(&parameter_type, &parameter_name, None, None);
            }

            constructor_builder.add_parameter("IceRpc.Features.IFeatureCollection", "features", None, None);

            constructor_builder.set_body(
                format!(
                    "Payload = Response.Encode{operation_name}({args}, features.Get<ISliceFeature>()?.EncodeOptions)",
                    args = parameters
                        .iter()
                        .map(|p| p.parameter_name())
                        .collect::<Vec<_>>()
                        .join(", "),
                )
                .into(),
            );
        }
    }

    container_builder.add_block(constructor_builder.build());
    container_builder.build()
}
