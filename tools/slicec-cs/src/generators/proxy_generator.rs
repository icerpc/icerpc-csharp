// Copyright (c) ZeroC, Inc.

use super::generated_code::GeneratedCode;
use crate::builders::{
    AttributeBuilder, Builder, CommentBuilder, ContainerBuilder, FunctionBuilder, FunctionCallBuilder, FunctionType,
};
use crate::decoding::*;
use crate::encoding::*;
use crate::member_util::*;
use crate::slicec_ext::*;
use slice::code_block::CodeBlock;

use slice::grammar::*;
use slice::utils::code_gen_util::*;

pub fn generate_proxy(interface_def: &Interface, generated_code: &mut GeneratedCode) {
    let namespace = interface_def.namespace();
    let interface = interface_def.interface_name(); // IFoo
    let slice_interface = interface_def.module_scoped_identifier();
    let proxy_impl: String = interface_def.proxy_name(); // FooProxy
    let access = interface_def.access_modifier();
    let all_bases: Vec<&Interface> = interface_def.all_base_interfaces();
    let bases: Vec<&Interface> = interface_def.base_interfaces();

    let proxy_impl_bases: Vec<String> = vec![interface.clone(), "IProxy".to_owned()];

    let all_base_impl: Vec<String> = all_bases.iter().map(|b| b.scoped_proxy_name(&namespace)).collect();

    // proxy bases
    let interface_bases: Vec<String> = bases.into_iter().map(|b| b.scoped_interface_name(&namespace)).collect();

    let mut code = CodeBlock::default();
    code.add_block(
        &ContainerBuilder::new(&format!("{access} partial interface"), &interface)
            .add_comments(interface_def.formatted_doc_comment())
            .add_generated_remark_with_note(
                "client-side interface",
                format!("It's implemented by <see cref=\"{proxy_impl}\" />."),
                interface_def,
            )
            .add_type_id_attribute(interface_def)
            .add_container_attributes(interface_def)
            .add_bases(&interface_bases)
            .add_block(proxy_interface_operations(interface_def))
            .build(),
    );

    let mut proxy_impl_builder =
        ContainerBuilder::new(&format!("{access} readonly partial record struct"), &proxy_impl);

    proxy_impl_builder
        .add_bases(&proxy_impl_bases)
        .add_comment(
            "summary",
            format!(
                r#"
Implements <see cref="{interface}" /> by making invocations on a remote IceRPC service.
This remote service must implement Slice interface {slice_interface}."#
            ),
        )
        .add_generated_remark("record struct", interface_def)
        .add_type_id_attribute(interface_def)
        .add_container_attributes(interface_def)
        .add_block(request_class(interface_def))
        .add_block(response_class(interface_def))
        .add_block(
            format!(
                r#"
/// <summary>Gets the default service address for services that implement Slice interface {slice_interface}.
/// Its protocol is <see cref="IceRpc.Protocol.IceRpc" /> and its path is computed from the name of the Slice interface.
/// </summary>
public static IceRpc.ServiceAddress DefaultServiceAddress {{ get; }} =
    new(IceRpc.Protocol.IceRpc) {{ Path = typeof({proxy_impl}).GetDefaultServicePath() }};

/// <inheritdoc/>
public SliceEncodeOptions? EncodeOptions {{ get; init; }} = null;

/// <inheritdoc/>
public IceRpc.IInvoker? Invoker {{ get; init; }} = null;

/// <inheritdoc/>
public IceRpc.ServiceAddress ServiceAddress {{ get; init; }} = DefaultServiceAddress;"#
            )
            .into(),
        );

    if interface_def.supported_encodings().supports(&Encoding::Slice1) {
        proxy_impl_builder.add_block(
            format!(
                "\
private static readonly IActivator _defaultActivator =
    IActivator.FromAssembly(typeof({proxy_impl}).Assembly);"
            )
            .into(),
        );
    }

    for base_impl in all_base_impl {
        proxy_impl_builder.add_block(
            format!(
                r#"
/// <summary>Provides an implicit conversion to <see cref="{base_impl}" />.</summary>
public static implicit operator {base_impl}({proxy_impl} proxy) =>
    new() {{ EncodeOptions = proxy.EncodeOptions, Invoker = proxy.Invoker, ServiceAddress = proxy.ServiceAddress }};"#
            )
            .into(),
        );
    }

    proxy_impl_builder.add_block(proxy_impl_static_methods(interface_def));

    for operation in interface_def.all_inherited_operations() {
        proxy_impl_builder.add_block(proxy_base_operation_impl(operation));
    }

    for operation in interface_def.operations() {
        proxy_impl_builder.add_block(proxy_operation_impl(operation));
    }

    code.add_block(&proxy_impl_builder.build());

    generated_code.insert_scoped(interface_def, code)
}

fn proxy_impl_static_methods(interface_def: &Interface) -> CodeBlock {
    format!(
        r#"/// <summary>Creates a relative proxy from a path.</summary>
/// <param name="path">The path.</param>
/// <returns>The new relative proxy.</returns>
public static {proxy_impl} FromPath(string path) => new() {{ ServiceAddress = new() {{ Path = path }} }};

/// <summary>Constructs a proxy from an invoker, a service address and encode options.</summary>
/// <param name="invoker">The invocation pipeline of the proxy.</param>
/// <param name="serviceAddress">The service address. <see langword="null" /> is equivalent to <see cref="DefaultServiceAddress" />.</param>
/// <param name="encodeOptions">The encode options, used to customize the encoding of request payloads.</param>
public {proxy_impl}(
    IceRpc.IInvoker invoker,
    IceRpc.ServiceAddress? serviceAddress = null,
    SliceEncodeOptions? encodeOptions = null)
{{
    Invoker = invoker;
    ServiceAddress = serviceAddress ?? DefaultServiceAddress;
    EncodeOptions = encodeOptions;
}}

/// <summary>Constructs a proxy from an invoker, a service address URI and encode options.</summary>
/// <param name="invoker">The invocation pipeline of the proxy.</param>
/// <param name="serviceAddressUri">A URI that represents a service address.</param>
/// <param name="encodeOptions">The encode options, used to customize the encoding of request payloads.</param>
public {proxy_impl}(IceRpc.IInvoker invoker, System.Uri serviceAddressUri, SliceEncodeOptions? encodeOptions = null)
    : this(invoker, new IceRpc.ServiceAddress(serviceAddressUri), encodeOptions)
{{
}}

/// <summary>Constructs a proxy with the default service address and a <see langword="null" /> invoker.</summary>
public {proxy_impl}()
{{
}}"#,
        proxy_impl = interface_def.proxy_name(),
    )
    .into()
}

/// The actual implementation of the proxy operation.
fn proxy_operation_impl(operation: &Operation) -> CodeBlock {
    let namespace = &operation.namespace();
    let operation_name = operation.escape_identifier();
    let async_operation_name = operation.escape_identifier_with_suffix("Async");
    let return_task = operation.return_task(false);

    let parameters = operation.non_streamed_parameters();

    let features_parameter = escape_parameter_name(&operation.parameters(), "features");
    let cancellation_token_parameter = escape_parameter_name(&operation.parameters(), "cancellationToken");

    let void_return = operation.return_type.is_empty();

    let encoding = operation.encoding.to_cs_encoding();

    let body_type = if operation.compress_arguments() {
        FunctionType::BlockBody
    } else {
        FunctionType::ExpressionBody
    };

    let mut builder = FunctionBuilder::new("public", &return_task, &async_operation_name, body_type);
    builder.set_inherit_doc(true);
    builder.add_operation_parameters(operation, TypeContext::Encode);

    let mut body = CodeBlock::default();

    if operation.compress_arguments() {
        body.writeln(&format!(
            "\
if ({features_parameter}?.Get<IceRpc.Features.ICompressFeature>() is null)
{{
    {features_parameter} ??= new IceRpc.Features.FeatureCollection();
    {features_parameter} = IceRpc.Features.FeatureCollectionExtensions.With(
        {features_parameter},
        IceRpc.Features.CompressFeature.Compress);
}}
"
        ));
    }

    let mut invocation_builder = FunctionCallBuilder::new("this.InvokeAsync");
    invocation_builder.use_semi_colon(false);
    invocation_builder.arguments_on_newline(true);

    // The operation to call
    invocation_builder.add_argument(format!(r#""{}""#, operation.identifier()));

    // The payload argument
    if operation.parameters.is_empty() {
        invocation_builder.add_argument("payload: null");
    } else if parameters.is_empty() {
        invocation_builder.add_argument(format!("{encoding}.CreateSizeZeroPayload()"));
    } else {
        invocation_builder.add_argument(format!(
            "Request.{operation_name}({}, encodeOptions: EncodeOptions)",
            parameters
                .iter()
                .map(|p| p.parameter_name())
                .collect::<Vec<_>>()
                .join(", "),
        ));
    }

    // Stream parameter (if any)
    if let Some(stream_parameter) = operation.streamed_parameter() {
        let stream_parameter_name = stream_parameter.parameter_name();
        let stream_type = stream_parameter.data_type();

        match stream_type.concrete_type() {
            Types::Primitive(Primitive::UInt8) if !stream_type.is_optional => {
                invocation_builder.add_argument(stream_parameter_name);
            }
            _ => {
                invocation_builder.add_argument(
                    FunctionCallBuilder::new(format!(
                        "{stream_parameter_name}.ToPipeReader<{}>",
                        stream_type.cs_type_string(namespace, TypeContext::Encode, false),
                    ))
                    .use_semi_colon(false)
                    .add_argument(
                        encode_stream_parameter(stream_type, TypeContext::Encode, namespace, operation.encoding)
                            .indent(),
                    )
                    .add_argument(stream_type.fixed_wire_size().is_none())
                    .add_argument(encoding)
                    .add_argument("this.EncodeOptions")
                    .build(),
                );
            }
        }
    } else {
        invocation_builder.add_argument("payloadContinuation: null");
    }

    // For Slice2 operations without return type, and without an exception specification, reuse the
    // IncomingResponseExtensions.DecodeVoidReturnValueAsync method, otherwise call the generated decode
    // method in the Response class.
    if operation.return_members().is_empty()
        && operation.encoding != Encoding::Slice1
        && matches!(operation.throws, Throws::None)
    {
        invocation_builder.add_argument("IceRpc.Slice.IncomingResponseExtensions.DecodeVoidReturnValueAsync");
    } else {
        invocation_builder.add_argument(format!("Response.{async_operation_name}"));
    }

    invocation_builder.add_argument(features_parameter);

    invocation_builder.add_argument_if(operation.is_idempotent, "idempotent: true");

    invocation_builder.add_argument_if(void_return && operation.is_oneway(), "oneway: true");

    invocation_builder.add_argument(format!("cancellationToken: {cancellation_token_parameter}"));

    let invocation = invocation_builder.build();

    match body_type {
        FunctionType::ExpressionBody => body.writeln(&invocation),
        FunctionType::BlockBody => writeln!(body, "return {invocation};"),
        _ => panic!("unexpected function type"),
    }

    builder.set_body(body);

    builder.build()
}

fn proxy_base_operation_impl(operation: &Operation) -> CodeBlock {
    let async_name = operation.escape_identifier_with_suffix("Async");
    let return_task = operation.return_task(false);
    let mut operation_params = operation
        .parameters()
        .iter()
        .map(|p| p.parameter_name())
        .collect::<Vec<_>>();

    operation_params.push(escape_parameter_name(&operation.parameters(), "features"));
    operation_params.push(escape_parameter_name(&operation.parameters(), "cancellationToken"));

    let mut builder = FunctionBuilder::new("public", &return_task, &async_name, FunctionType::ExpressionBody);
    builder.set_inherit_doc(true);
    builder.add_operation_parameters(operation, TypeContext::Encode);

    builder.set_body(
        format!(
            "(({base_proxy_impl})this).{async_name}({operation_params})",
            base_proxy_impl = operation.parent().unwrap().proxy_name(),
            operation_params = operation_params.join(", "),
        )
        .into(),
    );

    builder.build()
}

fn proxy_interface_operations(interface_def: &Interface) -> CodeBlock {
    let mut code = CodeBlock::default();
    let operations = interface_def.operations();

    for operation in operations {
        code.add_block(
            &FunctionBuilder::new(
                "",
                &operation.return_task(false),
                &operation.escape_identifier_with_suffix("Async"),
                FunctionType::Declaration,
            )
            .add_container_attributes(operation)
            .add_comments(operation.formatted_doc_comment())
            .add_operation_parameters(operation, TypeContext::Encode)
            .build(),
        );
    }

    code
}

fn request_class(interface_def: &Interface) -> CodeBlock {
    let namespace = &interface_def.namespace();
    let operations = interface_def
        .operations()
        .iter()
        .filter(|o| o.has_non_streamed_parameters())
        .cloned()
        .collect::<Vec<_>>();

    if operations.is_empty() {
        return "".into();
    }

    let mut class_builder = ContainerBuilder::new("public static class", "Request");

    class_builder
        .add_comment(
            "summary",
            "Provides static methods that encode operation arguments into request payloads.",
        )
        .add_generated_remark("static class", interface_def);

    for operation in operations {
        let params: Vec<&Parameter> = operation.non_streamed_parameters();

        assert!(!params.is_empty());

        let mut builder = FunctionBuilder::new(
            "public static",
            "global::System.IO.Pipelines.PipeReader",
            &operation.escape_identifier(),
            FunctionType::BlockBody,
        );

        builder.add_comment(
            "summary",
            format!(
                "Encodes the argument{s} of operation <c>{slice_operation}</c> into a request payload.",
                s = if params.len() == 1 { "" } else { "s" },
                slice_operation = operation.identifier(),
            ),
        );

        for param in &params {
            builder.add_parameter(
                &param.cs_type_string(namespace, TypeContext::Encode, false),
                &param.parameter_name(),
                None,
                param.formatted_parameter_doc_comment(),
            );
        }

        builder.add_parameter(
            "SliceEncodeOptions?",
            "encodeOptions",
            Some("null"),
            Some("The Slice encode options.".to_owned()),
        );

        builder.add_comment(
            "returns",
            format!(
                "The payload encoded with <see cref=\"{}\" />.",
                operation.encoding.to_cs_encoding(),
            ),
        );

        builder.set_body(encode_operation(operation, false));

        class_builder.add_block(builder.build());
    }

    class_builder.build()
}

fn response_class(interface_def: &Interface) -> CodeBlock {
    let namespace = &interface_def.namespace();
    let operations = interface_def
        .operations()
        .iter()
        .filter(|o| {
            // We need to generate a method to decode the responses of any operations with return members, Slice2
            // operations with an exception specification, or any Slice1 operations (to correctly setup the activator
            // used for decoding Slice1 exceptions). We don't have to check Throws::AnyException because it is only
            // valid in Slice1.
            !o.return_members().is_empty() || o.encoding == Encoding::Slice1 || matches!(&o.throws, Throws::Specific(_))
        })
        .cloned()
        .collect::<Vec<_>>();

    if operations.is_empty() {
        return "".into();
    }

    let mut class_builder = ContainerBuilder::new("public static class", "Response");

    class_builder.add_comment(
        "summary",
        format!(
            r#"Provides a <see cref="ResponseDecodeFunc{{T}}" /> for each operation defined in Slice interface {}."#,
            interface_def.module_scoped_identifier(),
        ),
    ).add_generated_remark("static class", interface_def);

    for operation in operations {
        let members = operation.return_members();

        let function_type = if operation.streamed_return_member().is_some() {
            FunctionType::BlockBody
        } else {
            FunctionType::ExpressionBody
        };

        let return_type = if members.is_empty() {
            "global::System.Threading.Tasks.ValueTask".to_owned()
        } else {
            format!(
                "global::System.Threading.Tasks.ValueTask<{}>",
                members.to_tuple_type(namespace, TypeContext::Decode, false),
            )
        };

        let mut builder = FunctionBuilder::new(
            if function_type == FunctionType::ExpressionBody {
                "public static"
            } else {
                "public static async"
            },
            &return_type,
            &operation.escape_identifier_with_suffix("Async"),
            function_type,
        );

        let comment_content = format!(
            r#"Decodes an incoming response for operation <c>{}</c>."#,
            operation.identifier(),
        );

        builder.add_comment("summary", comment_content);
        builder.add_parameter("IceRpc.IncomingResponse", "response", None, None);
        builder.add_parameter("IceRpc.OutgoingRequest", "request", None, None);
        builder.add_parameter("GenericProxy", "sender", None, None);
        builder.add_parameter(
            "global::System.Threading.CancellationToken",
            "cancellationToken",
            None,
            None,
        );

        builder.set_body(response_operation_body(operation));

        class_builder.add_block(builder.build());
    }
    class_builder.build()
}

fn response_operation_body(operation: &Operation) -> CodeBlock {
    let mut code = CodeBlock::default();
    let namespace = &operation.namespace();
    let non_streamed_members = operation.non_streamed_return_members();
    let return_void = operation.return_members().is_empty();

    if let Some(stream_member) = operation.streamed_return_member() {
        // async method with await
        if non_streamed_members.is_empty() {
            writeln!(
                code,
                "\
await response.DecodeVoidReturnValueAsync(
    request,
    {encoding},
    sender,
    decodeException: {exception_decode_func},
    defaultActivator: null,
    cancellationToken).ConfigureAwait(false);
",
                encoding = operation.encoding.to_cs_encoding(),
                exception_decode_func = exception_decode_func(operation),
            );
        } else {
            writeln!(
                code,
                "\
var {return_value} = await response.DecodeReturnValueAsync(
    request,
    {encoding},
    sender,
    {return_value_decode_func},
    decodeException: {exception_decode_func},
    defaultActivator: null,
    cancellationToken).ConfigureAwait(false);
",
                return_value = non_streamed_members.to_argument_tuple("sliceP_"),
                encoding = operation.encoding.to_cs_encoding(),
                return_value_decode_func = return_value_decode_func(operation).indent(),
                exception_decode_func = exception_decode_func(operation),
            );
        }

        let stream_type = stream_member.data_type();
        match stream_type.concrete_type() {
            Types::Primitive(Primitive::UInt8) if !stream_type.is_optional => {
                writeln!(
                    code,
                    "var {} = response.DetachPayload();",
                    stream_member.parameter_name_with_prefix("sliceP_"),
                )
            }
            _ => writeln!(
                code,
                "\
var payloadContinuation = response.DetachPayload();
var {stream_parameter_name} = {decode_operation_stream}
",
                stream_parameter_name = stream_member.parameter_name_with_prefix("sliceP_"),
                decode_operation_stream = decode_operation_stream(stream_member, namespace, operation.encoding, false),
            ),
        }

        writeln!(
            code,
            "return {};",
            operation.return_members().to_argument_tuple("sliceP_"),
        );
    } else if return_void {
        writeln!(
            code,
            "\
response.DecodeVoidReturnValueAsync(
    request,
    {encoding},
    sender,
    decodeException: {exception_decode_func},
    defaultActivator: {default_activator},
    cancellationToken)
",
            encoding = operation.encoding.to_cs_encoding(),
            exception_decode_func = exception_decode_func(operation),
            default_activator = default_activator(operation.encoding),
        );
    } else {
        writeln!(
            code,
            "\
response.DecodeReturnValueAsync(
    request,
    {encoding},
    sender,
    {return_value_decode_func},
    decodeException: {exception_decode_func},
    defaultActivator: {default_activator},
    cancellationToken)
",
            encoding = operation.encoding.to_cs_encoding(),
            return_value_decode_func = return_value_decode_func(operation).indent(),
            exception_decode_func = exception_decode_func(operation),
            default_activator = default_activator(operation.encoding),
        );
    }
    code
}

fn exception_decode_func(operation: &Operation) -> String {
    match &operation.throws {
        Throws::Specific(exception) if operation.encoding != Encoding::Slice1 => {
            format!(
                "(ref SliceDecoder decoder, string? message) => new {}(ref decoder, message)",
                exception.escape_scoped_identifier(&operation.namespace()),
            )
        }
        _ => "null".to_owned(),
    }
}

fn return_value_decode_func(operation: &Operation) -> CodeBlock {
    let namespace = &operation.namespace();
    // vec of members
    let members = operation.non_streamed_return_members();
    assert!(!members.is_empty());

    if members.len() == 1
        && get_bit_sequence_size(operation.encoding, &members) == 0
        && !members.first().unwrap().is_tagged()
    {
        decode_func(members.first().unwrap().data_type(), namespace, operation.encoding)
    } else {
        format!(
            "\
(ref SliceDecoder decoder) =>
{{
    {decode}
}}",
            decode = decode_operation(operation, false).indent(),
        )
        .into()
    }
}
