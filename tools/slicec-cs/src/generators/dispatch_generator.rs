// Copyright (c) ZeroC, Inc.

use crate::builders::{AttributeBuilder, Builder, CommentBuilder, ContainerBuilder, FunctionBuilder, FunctionType};
use crate::code_block::CodeBlock;
use crate::code_gen_util::TypeContext;
use crate::cs_attributes::CsEncodedReturn;
use crate::decoding::*;
use crate::encoding::*;
use crate::slicec_ext::*;
use slicec::grammar::*;

pub fn generate_dispatch(interface_def: &Interface) -> CodeBlock {
    let namespace = interface_def.namespace();
    let bases = interface_def.base_interfaces();
    let service_name = interface_def.service_name();
    let access = interface_def.access_modifier();
    let mut interface_builder = ContainerBuilder::new(&format!("{access} partial interface"), &service_name);

    if let Some(summary) = interface_def.formatted_doc_comment_summary() {
        interface_builder.add_comment("summary", summary);
    }

    interface_builder
        .add_generated_remark_with_note(
            "server-side interface",
            r#"Your service implementation must implement this interface."#,
            interface_def,
        )
        .add_comments(interface_def.formatted_doc_comment_seealso())
        .add_type_id_attribute(interface_def)
        .add_default_service_path_attribute(interface_def);

    interface_builder.add_bases(
        &bases
            .iter()
            .map(|b| b.scoped_service_name(&namespace))
            .collect::<Vec<_>>(),
    );

    interface_builder
        .add_block(request_class(interface_def))
        .add_block(response_class(interface_def));

    if interface_def.supported_encodings().supports(Encoding::Slice1) {
        interface_builder.add_block(
            format!(
                "\
private static readonly IActivator _defaultActivator =
    IActivator.FromAssembly(typeof({service_name}).Assembly);"
            )
            .into(),
        );
    }

    // TODO: add a Slice cs attribute to conditionally suppress the generation of these methods.
    if interface_def.module_scoped_identifier() != "Ice::Object" {
        for operation in interface_def.operations() {
            interface_builder.add_block(operation_declaration(operation));
        }
    }

    interface_builder.build()
}

fn request_class(interface_def: &Interface) -> CodeBlock {
    let operations = interface_def.operations();

    if operations.is_empty() {
        return "".into();
    }

    // Check if any of the base interfaces will already have a 'Request' class generated.
    // We generate a 'Request' class for any interface that has operations, even if they have no parameters.
    let mut class_builder = ContainerBuilder::new(
        if !interface_def.all_inherited_operations().is_empty() {
            "public static new class"
        } else {
            "public static class"
        },
        "Request",
    );

    class_builder
        .add_comment("summary", "Provides static methods that decode request payloads.")
        .add_generated_remark("static class", interface_def);

    for operation in operations {
        let parameters = operation.parameters();

        let namespace = &operation.namespace();

        let function_type = if operation.streamed_parameter().is_some() {
            FunctionType::BlockBody
        } else {
            FunctionType::ExpressionBody
        };

        let return_type = if parameters.is_empty() {
            "global::System.Threading.Tasks.ValueTask".to_owned()
        } else {
            format!(
                "global::System.Threading.Tasks.ValueTask<{}>",
                parameters.to_tuple_type(namespace, TypeContext::IncomingParam),
            )
        };

        let mut builder = FunctionBuilder::new(
            if function_type == FunctionType::BlockBody {
                "public static async"
            } else {
                "public static"
            },
            &return_type,
            &operation.escape_identifier_with_prefix_and_suffix("Decode", "Async"),
            function_type,
        );

        builder.add_comment(
            "summary",
            format!(
                r#"Decodes the request payload of operation <c>{operation_identifier}</c>."#,
                operation_identifier = operation.identifier(),
            ),
        );

        builder.add_parameter(
            "IceRpc.IncomingRequest",
            "request",
            None,
            Some("The incoming request.".to_owned()),
        );
        builder.add_parameter(
            "global::System.Threading.CancellationToken",
            "cancellationToken",
            None,
            Some("A cancellation token that receives the cancellation requests.".to_owned()),
        );
        builder.set_body(request_decode_body(operation));

        class_builder.add_block(builder.build());
    }

    class_builder.build()
}

fn response_class(interface_def: &Interface) -> CodeBlock {
    let operations = interface_def.operations();

    if operations.is_empty() {
        return "".into();
    }

    // Check if any of the base interfaces will already have a 'Response' class generated.
    // A 'Response' class is generated for any interface that defines at least one operation,
    // regardless of whether its operations have non-streamed return members.
    let mut class_builder = ContainerBuilder::new(
        if !interface_def.all_inherited_operations().is_empty() {
            "public static new class"
        } else {
            "public static class"
        },
        "Response",
    );

    class_builder
        .add_comment(
            "summary",
            "Provides static methods that encode return values into response payloads.",
        )
        .add_generated_remark("static class", interface_def);

    for operation in operations {
        let non_streamed_returns = operation.non_streamed_return_members();

        let namespace = &operation.namespace();
        let operation_name = &operation.escape_identifier();

        let mut builder = FunctionBuilder::new(
            "public static",
            "global::System.IO.Pipelines.PipeReader",
            format!("Encode{operation_name}").as_str(),
            if non_streamed_returns.is_empty() {
                FunctionType::ExpressionBody
            } else {
                FunctionType::BlockBody
            },
        );

        builder.add_comment(
            "summary",
            format!(
                "Encodes the return value of operation <c>{}</c> into a response payload.",
                operation.identifier(),
            ),
        );

        match non_streamed_returns.as_slice() {
            [param] => {
                builder.add_parameter(
                    &param.data_type().outgoing_parameter_type_string(namespace),
                    "returnValue",
                    None,
                    Some("The operation return value.".to_owned()),
                );
            }
            _ => {
                for param in &non_streamed_returns {
                    builder.add_parameter(
                        &param.data_type().outgoing_parameter_type_string(namespace),
                        &param.parameter_name(),
                        None,
                        param.formatted_param_doc_comment(),
                    );
                }
            }
        }

        builder.add_parameter(
            "SliceEncodeOptions?",
            "encodeOptions",
            Some("null"),
            Some("The Slice encode options.".to_owned()),
        );

        builder.add_comment("returns", "A new response payload.");

        builder.set_body(encode_operation(operation, true));

        class_builder.add_block(builder.build());

        // EncodeStreamOfXxx for the payload continuation if the operation has a streamed return member.
        if let Some(stream_return) = operation.streamed_return_member() {
            let mut builder = FunctionBuilder::new(
                "public static",
                "global::System.IO.Pipelines.PipeReader",
                format!("EncodeStreamOf{operation_name}").as_str(),
                FunctionType::ExpressionBody,
            );

            builder.add_comment(
                "summary",
                format!(
                    "Encodes the stream returned by operation <c>{}</c> into a response payload continuation.",
                    operation.identifier(),
                ),
            );

            let stream_arg = if non_streamed_returns.is_empty() {
                "returnValue".to_owned()
            } else {
                stream_return.parameter_name()
            };

            builder.add_parameter(
                &stream_return.cs_type_string(namespace, TypeContext::OutgoingParam),
                &stream_arg,
                None,
                Some("The stream returned by the operation.".to_owned()),
            );

            builder.add_parameter(
                "SliceEncodeOptions?",
                "encodeOptions",
                Some("null"),
                Some("The Slice encode options.".to_owned()),
            );

            builder.add_comment("returns", "A new response payload continuation.");

            builder.set_body(encode_operation_stream(operation));

            class_builder.add_block(builder.build());
        }
    }

    class_builder.build()
}

fn request_decode_body(operation: &Operation) -> CodeBlock {
    let mut code = CodeBlock::default();

    let non_streamed_parameters = operation.non_streamed_parameters();
    let namespace = &operation.namespace();
    let encoding = operation.encoding;

    if let Some(stream_member) = operation.streamed_parameter() {
        if non_streamed_parameters.is_empty() {
            writeln!(
                code,
                "await request.DecodeEmptyArgsAsync({encoding}, cancellationToken).ConfigureAwait(false);",
                encoding = encoding.to_cs_encoding(),
            );

            let stream_type = stream_member.data_type();
            match stream_type.concrete_type() {
                Types::Primitive(Primitive::UInt8) if !stream_type.is_optional => {
                    writeln!(code, "return IceRpc.IncomingFrameExtensions.DetachPayload(request);");
                }
                _ => {
                    writeln!(
                        code,
                        "\
var payloadContinuation = IceRpc.IncomingFrameExtensions.DetachPayload(request);
return {}",
                        decode_operation_stream(stream_member, namespace, encoding, true)
                    )
                }
            }
        } else {
            writeln!(
                code,
                "\
var {args} = await request.DecodeArgsAsync(
    {encoding},
    {decode_func},
    defaultActivator: null,
    cancellationToken).ConfigureAwait(false);",
                args = non_streamed_parameters.to_argument_tuple(),
                encoding = encoding.to_cs_encoding(),
                decode_func = decode_non_streamed_parameters_func(&non_streamed_parameters, encoding).indent(),
            );
            let stream_type = stream_member.data_type();
            match stream_type.concrete_type() {
                Types::Primitive(Primitive::UInt8) if !stream_type.is_optional => {
                    writeln!(
                        code,
                        "var {} = IceRpc.IncomingFrameExtensions.DetachPayload(request);",
                        stream_member.parameter_name_with_prefix(),
                    )
                }
                _ => writeln!(
                    code,
                    "\
var payloadContinuation = IceRpc.IncomingFrameExtensions.DetachPayload(request);
var {stream_parameter_name} = {decode_operation_stream}
",
                    stream_parameter_name = stream_member.parameter_name_with_prefix(),
                    decode_operation_stream = decode_operation_stream(stream_member, namespace, encoding, true),
                ),
            }
            writeln!(code, "return {};", operation.parameters().to_argument_tuple());
        }
    } else {
        if non_streamed_parameters.is_empty() {
            writeln!(
                code,
                "request.DecodeEmptyArgsAsync({encoding}, cancellationToken)",
                encoding = encoding.to_cs_encoding(),
            );
        } else {
            writeln!(
                code,
                "\
request.DecodeArgsAsync(
    {encoding},
    {decode_func},
    defaultActivator: {default_activator},
    cancellationToken)
",
                encoding = encoding.to_cs_encoding(),
                decode_func = decode_non_streamed_parameters_func(&non_streamed_parameters, encoding).indent(),
                default_activator = default_activator(encoding),
            );
        }
    }
    code
}

fn operation_declaration(operation: &Operation) -> CodeBlock {
    let mut builder = FunctionBuilder::new(
        "public",
        &operation.dispatch_return_task(),
        &operation.escape_identifier_with_suffix("Async"),
        FunctionType::Declaration,
    );
    if let Some(summary) = operation.formatted_doc_comment_summary() {
        builder.add_comment("summary", summary);
    }
    builder
        .add_operation_parameters(operation, TypeContext::IncomingParam)
        .add_comments(operation.formatted_doc_comment_seealso())
        .add_attribute(operation_attribute(operation))
        .build()
}

fn operation_attribute(operation: &Operation) -> String {
    let mut attribute = format!(r#"SliceServiceMethod("{}""#, operation.identifier());

    if operation.compress_return() {
        attribute += ", CompressReturn = true";
    }

    if operation.has_attribute::<CsEncodedReturn>() {
        attribute += ", EncodedReturn = true";
    }

    if operation.is_idempotent {
        attribute += ", Idempotent = true";
    }

    if !operation.exception_specification.is_empty() {
        let exceptions = operation
            .exception_specification
            .iter()
            .map(|ex| format!("typeof({})", ex.escape_scoped_identifier(&operation.namespace())))
            .collect::<Vec<_>>()
            .join(", ");

        attribute += &format!(", ExceptionSpecification = new System.Type[] {{ {exceptions} }}");
    }

    attribute += ")";
    attribute
}
