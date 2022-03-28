// Copyright (c) ZeroC, Inc. All rights reserved.

use crate::builders::{
    AttributeBuilder, CommentBuilder, ContainerBuilder, FunctionBuilder, FunctionType,
};
use crate::code_block::CodeBlock;
use crate::comments::operation_parameter_doc_comment;
use crate::comments::*;
use crate::decoding::*;
use crate::encoding::*;
use crate::generated_code::GeneratedCode;
use crate::member_util::*;
use crate::slicec_ext::*;
use slice::code_gen_util::*;
use slice::grammar::*;
use slice::visitor::Visitor;

pub struct ProxyVisitor<'a> {
    pub generated_code: &'a mut GeneratedCode,
}

impl<'a> Visitor for ProxyVisitor<'_> {
    fn visit_interface_start(&mut self, interface_def: &Interface) {
        let namespace = interface_def.namespace();
        let prx_interface = interface_def.proxy_name(); // IFooPrx
        let prx_impl: String = interface_def.proxy_implementation_name(); // FooPrx
        let access = interface_def.access_modifier();
        let all_bases: Vec<&Interface> = interface_def.all_base_interfaces();
        let bases: Vec<&Interface> = interface_def.base_interfaces();

        let prx_impl_bases: Vec<String> = vec![prx_interface.clone(), "IPrx".to_owned()];

        let all_base_impl: Vec<String> = all_bases
            .iter()
            .map(|b| b.scoped_proxy_implementation_name(&namespace))
            .collect();

        // prx bases
        let prx_bases: Vec<String> = bases
            .into_iter()
            .map(|b| b.scoped_proxy_name(&namespace))
            .collect();

        let summary_message = format!(
            r#"The client-side interface for Slice interface {}. <seealso cref="{}"/>.
{}"#,
            interface_def.identifier(),
            interface_def.interface_name(),
            doc_comment_message(interface_def)
        );

        let mut code = CodeBlock::new();
        code.add_block(
            &ContainerBuilder::new(&format!("{} partial interface", access), &prx_interface)
                .add_comment("summary", &summary_message)
                .add_type_id_attribute(interface_def)
                .add_container_attributes(interface_def)
                .add_bases(&prx_bases)
                .add_block(proxy_interface_operations(interface_def))
                .build(),
        );

        let mut proxy_impl_builder = ContainerBuilder::new(
            &format!("{} readonly partial record struct", access),
            &prx_impl,
        );

        proxy_impl_builder.add_bases(&prx_impl_bases)
            .add_comment("summary", &format!(r#"Typed proxy record struct. It implements <see cref="{}"/> by sending requests to a remote IceRPC service."#, prx_interface))
            .add_type_id_attribute(interface_def)
            .add_container_attributes(interface_def)
            .add_block(request_class(interface_def))
            .add_block(response_class(interface_def))
            .add_block(format!(r#"
/// <summary>The default path for services that implement Slice interface <c>{interface_name}</c>.</summary>
{access} static readonly string DefaultPath = typeof({prx_impl}).GetDefaultPath();

private static readonly IActivator _defaultActivator =
    SliceDecoder.GetActivator(typeof({prx_impl}).Assembly);

/// <summary>The proxy to the remote service.</summary>
{access} IceRpc.Proxy Proxy {{ get; init; }}"#,
                               access = access,
                               interface_name = interface_def.identifier(),
                               prx_impl = prx_impl
            ).into());

        for base_impl in all_base_impl {
            proxy_impl_builder.add_block(
                format!(
                    r#"
/// <summary>Implicit conversion to <see cref="{base_impl}"/>.</summary>
{access} static implicit operator {base_impl}({prx_impl} prx) => new(prx.Proxy);"#,
                    access = access,
                    base_impl = base_impl,
                    prx_impl = prx_impl
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

        self.generated_code.insert_scoped(interface_def, code)
    }
}

fn proxy_impl_static_methods(interface_def: &Interface) -> CodeBlock {
    let access = interface_def.access_modifier();
    format!(
        r#"/// <summary>Creates a new <see cref="{prx_impl}"/> from the give connection and path.</summary>
/// <param name="connection">The connection. If it's an outgoing connection, the endpoint of the new proxy is
/// <see cref="Connection.RemoteEndpoint"/>; otherwise, the new proxy has no endpoint.</param>
/// <param name="path">The path of the proxy. If null, the path is set to <see cref="DefaultPath"/>.</param>
/// <param name="invoker">The invoker. If null and connection is an incoming connection, the invoker is set to
/// the server's invoker.</param>
/// <returns>The new proxy.</returns>
{access} static {prx_impl} FromConnection(
    IceRpc.Connection connection,
    string? path = null,
    IceRpc.IInvoker? invoker = null) =>
    new(IceRpc.Proxy.FromConnection(connection, path ?? DefaultPath, invoker));

/// <summary>Creates a new relative proxy with the given path.</summary>
/// <param name="path">The path.</param>
/// <returns>The new relative proxy.</returns>
{access} static {prx_impl} FromPath(string path) => new(IceRpc.Proxy.FromPath(path));

/// <summary>Creates a new <see cref="{prx_impl}"/> from a string and invoker.</summary>
/// <param name="s">The string representation of the proxy.</param>
/// <param name="invoker">The invoker of the new proxy.</param>
/// <param name="format">The proxy format to use for this parsing. Use <c>null</c> to select the default URI format.
/// </param>
/// <returns>The new proxy.</returns>
/// <exception cref="global::System.FormatException"><c>s</c> does not contain a valid string representation
/// of a proxy.</exception>
{access} static {prx_impl} Parse(string s, IceRpc.IInvoker? invoker = null, IceRpc.IProxyFormat? format = null) =>
    new((format ?? IceRpc.UriProxyFormat.Instance).Parse(s, invoker));

/// <summary>Creates a new <see cref="{prx_impl}"/> from a string and invoker.</summary>
/// <param name="s">The proxy string representation.</param>
/// <param name="invoker">The invoker of the new proxy.</param>
/// <param name="format">The proxy format to use for this parsing. Use <c>null</c> to select the default URI format.
/// </param>
/// <param name="prx">The new proxy.</param>
/// <returns><c>true</c> if the s parameter was parsed successfully; otherwise, <c>false</c>.</returns>
{access} static bool TryParse(string s, IceRpc.IInvoker? invoker, IceRpc.IProxyFormat? format, out {prx_impl} prx)
{{
    if ((format ?? IceRpc.UriProxyFormat.Instance).TryParse(s, invoker, out IceRpc.Proxy? proxy))
    {{
        prx = new(proxy);
        return true;
    }}
    else
    {{
        prx = default;
        return false;
    }}
}}

/// <summary>Constructs an instance of <see cref="{prx_impl}"/>.</summary>
/// <param name="proxy">The proxy to the remote service.</param>
{access} {prx_impl}(IceRpc.Proxy proxy) => Proxy = proxy;

/// <inheritdoc/>
{access} override string ToString() => Proxy.ToString();"#,
        prx_impl = interface_def.proxy_implementation_name(),
        access = access
    ).into()
}

/// The actual implementation of the proxy operation.
fn proxy_operation_impl(operation: &Operation) -> CodeBlock {
    let namespace = &operation.namespace();
    let operation_name = operation.escape_identifier();
    let async_operation_name = operation.escape_identifier_with_suffix("Async");
    let return_task = operation.return_task(false);
    let is_oneway = operation.has_attribute("oneway", false);

    let parameters = operation.nonstreamed_parameters();
    let stream_return = operation.streamed_return_member();

    let invocation_parameter = escape_parameter_name(&operation.parameters(), "invocation");
    let cancel_parameter = escape_parameter_name(&operation.parameters(), "cancel");

    let void_return = operation.return_type.is_empty();
    let access = operation.parent().unwrap().access_modifier();

    let encoding = operation.encoding.to_cs_encoding();

    let mut builder = FunctionBuilder::new(
        &access,
        &return_task,
        &async_operation_name,
        FunctionType::BlockBody,
    );
    builder.set_inherit_doc(true);
    builder.add_operation_parameters(operation, TypeContext::Encode);

    let mut body = CodeBlock::new();

    if operation.compress_arguments() {
        body.writeln(&format!(
            "\
if ({invocation}?.Features.Get<IceRpc.Features.CompressPayload>() == null)
{{
    {invocation} ??= new IceRpc.Slice.Invocation();
    {invocation}.Features = {invocation}.Features.With(IceRpc.Features.CompressPayload.Yes);
}}
",
            invocation = invocation_parameter
        ));
    }

    let mut invoke_args = vec![
        format!(r#""{}""#, operation.identifier()),
    ];

    if void_return {
        invoke_args.push(encoding.to_owned());
    }

    // The payload argument
    if parameters.is_empty() {
        invoke_args.push(format!(
            "{encoding}.CreateEmptyPayload(hasStream: {has_stream})",
            encoding = encoding,
            has_stream = if operation.parameters.is_empty() {
                "false"
            } else {
                "true"
            }
        ));
    } else {
        invoke_args.push(format!(
            "Request.{}({})",
            operation_name,
            parameters
            .iter()
            .map(|p| p.parameter_name())
            .collect::<Vec<_>>().join(", ")
        ));
    }

    // Stream parameter (if any)
    if let Some(stream_parameter) = operation.streamed_parameter() {
        let stream_parameter_name = stream_parameter.parameter_name();
        let stream_type = stream_parameter.data_type();
        match stream_type.concrete_type() {
            Types::Primitive(b) if matches!(b, Primitive::Byte) => {
                invoke_args.push(stream_parameter_name)
            }
            _ => invoke_args.push(format!(
                "\
{encoding}.CreatePayloadSourceStream<{stream_type}>(
    {stream_parameter},
    {encode_action})",
                stream_type = stream_type.to_type_string(namespace, TypeContext::Encode, false),
                stream_parameter = stream_parameter_name,
                encoding = encoding,
                encode_action = encode_action(stream_type, TypeContext::Encode, namespace).indent()
            )),
        }
    } else {
        invoke_args.push("payloadSourceStream: null".to_owned());
    }

    if void_return && stream_return.is_none() {
        invoke_args.push("_defaultActivator".to_owned());
    }

    if !void_return {
        invoke_args.push("Response.".to_owned() + &async_operation_name);
    }

    invoke_args.push("invocation".to_owned());

    if operation.is_idempotent {
        invoke_args.push("idempotent: true".to_owned());
    }

    if void_return && is_oneway {
        invoke_args.push("oneway: true".to_owned());
    }

    invoke_args.push(format!("cancel: {}", cancel_parameter));

    writeln!(
        body,
        "\
return Proxy.InvokeAsync(
    {});",
        CodeBlock::from(invoke_args.join(",\n")).indent()
    );

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

    operation_params.push(escape_parameter_name(&operation.parameters(), "invocation"));
    operation_params.push(escape_parameter_name(&operation.parameters(), "cancel"));

    let mut builder = FunctionBuilder::new(
        &operation.parent().unwrap().access_modifier(),
        &return_task,
        &async_name,
        FunctionType::ExpressionBody,
    );
    builder.set_inherit_doc(true);
    builder.add_operation_parameters(operation, TypeContext::Encode);

    builder.set_body(
        format!(
            "new {base_prx_impl}(Proxy).{async_name}({operation_params})",
            base_prx_impl = operation.parent().unwrap().proxy_implementation_name(),
            async_name = async_name,
            operation_params = operation_params.join(", ")
        )
        .into(),
    );

    builder.build()
}

fn proxy_interface_operations(interface_def: &Interface) -> CodeBlock {
    let mut code = CodeBlock::new();
    let operations = interface_def.operations();

    for operation in operations {
        // TODO: add returns doc comments (see C++)
        code.add_block(
            &FunctionBuilder::new(
                "",
                &operation.return_task(false),
                &operation.escape_identifier_with_suffix("Async"),
                FunctionType::Declaration,
            )
            .add_container_attributes(operation)
            .add_comment("summary", &doc_comment_message(operation))
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
        .filter(|o| o.has_nonstreamed_parameters())
        .cloned()
        .collect::<Vec<_>>();

    if operations.is_empty() {
        return "".into();
    }

    let access = interface_def.access_modifier();
    let mut class_builder = ContainerBuilder::new(&format!("{} static class", access), "Request");

    class_builder.add_comment(
        "summary",
        "Converts the arguments of each operation that takes arguments into a request payload.",
    );

    for operation in operations {
        let params: Vec<&Parameter> = operation.nonstreamed_parameters();

        assert!(!params.is_empty());

        let mut builder = FunctionBuilder::new(
            &format!("{} static", access),
            "global::System.IO.Pipelines.PipeReader",
            &operation.escape_identifier(),
            FunctionType::BlockBody,
        );

        builder.add_comment(
            "summary",
            &format!(
                "Creates the request payload for operation {}.",
                operation.identifier()
            ),
        );

        for param in &params {
            builder.add_parameter(
                &param.to_type_string(namespace, TypeContext::Encode, false),
                &param.parameter_name(),
                None,
                operation_parameter_doc_comment(operation, param.identifier()),
            );
        }

        builder.add_comment("returns", &format!("The payload encoded with <see cref=\"{}\"/>.", operation.encoding.to_cs_encoding()));

        builder.set_body(encode_operation(operation, false, "return"));

        class_builder.add_block(builder.build());
    }

    class_builder.build().into()
}

fn response_class(interface_def: &Interface) -> CodeBlock {
    let namespace = &interface_def.namespace();
    let operations = interface_def
        .operations()
        .iter()
        .filter(|o| !o.return_type.is_empty())
        .cloned()
        .collect::<Vec<_>>();

    if operations.is_empty() {
        return "".into();
    }

    let access = interface_def.access_modifier();

    let mut class_builder = ContainerBuilder::new(&format!("{} static class", access), "Response");

    class_builder.add_comment(
        "summary",
        &format!(
            r#"Holds a <see cref="IceRpc.Slice.ResponseDecodeFunc{{T}}"/> for each non-void remote operation defined in <see cref="{}Prx"/>."#,
            interface_def.interface_name()));

    for operation in operations {
        let members = operation.return_members();
        assert!(!members.is_empty());

        let function_type = if operation.streamed_return_member().is_some() {
            FunctionType::BlockBody
        } else {
            FunctionType::ExpressionBody
        };

        let mut builder = FunctionBuilder::new(
            &format!("{} static async", access),
            &format!(
                "global::System.Threading.Tasks.ValueTask<{}>",
                members.to_tuple_type(namespace, TypeContext::Decode, false)
            ),
            &operation.escape_identifier_with_suffix("Async"),
            function_type,
        );

        builder.add_comment("summary", &format!(r#"The <see cref="ResponseDecodeFunc{{T}}"/> for the return value type of operation {}."#, operation.identifier()));
        builder.add_parameter("IceRpc.IncomingResponse", "response", None, None);
        builder.add_parameter(
            "global::System.Threading.CancellationToken",
            "cancel",
            None,
            None,
        );

        builder.set_body(response_operation_body(operation));

        class_builder.add_block(builder.build());
    }
    class_builder.build().into()
}

fn response_operation_body(operation: &Operation) -> CodeBlock {
    let mut code = CodeBlock::new();
    let namespace = &operation.namespace();
    let encoding = operation.encoding.to_cs_encoding();

    if let Some(stream_member) = operation.streamed_return_member() {
        let non_streamed_members = operation.nonstreamed_return_members();

        if non_streamed_members.is_empty() {
            writeln!(
                code,
                "\
await response.CheckVoidReturnValueAsync(
    {encoding},
    _defaultActivator,
    hasStream: true,
    cancel).ConfigureAwait(false);

return {decode_operation_stream}
",
    encoding = encoding,
    decode_operation_stream = decode_operation_stream(stream_member, namespace, encoding, false, false)
            );
        } else {
            writeln!(
                code,
                "\
var {return_value} = await response.ToReturnValueAsync(
    {encoding},
    _defaultActivator,
    {response_decode_func},
    hasStream: true,
    cancel).ConfigureAwait(false);

{decode_response_stream}

return {return_value_and_stream};
",
                encoding = encoding,
                return_value = non_streamed_members.to_argument_tuple("sliceP_"),
                response_decode_func = response_decode_func(operation).indent(),
                decode_response_stream =
                    decode_operation_stream(stream_member, namespace, encoding, false, true),
                return_value_and_stream = operation.return_members().to_argument_tuple("sliceP_")
            );
        }
    } else {
        writeln!(
            code,
            "\
await response.ToReturnValueAsync(
    {encoding},
    _defaultActivator,
    {response_decode_func},
    hasStream: false,
    cancel).ConfigureAwait(false)",
            encoding = encoding,
            response_decode_func = response_decode_func(operation).indent()
        );
    }

    code
}

fn response_decode_func(operation: &Operation) -> CodeBlock {
    let namespace = &operation.namespace();
    // vec of members
    let members = operation.nonstreamed_return_members();
    assert!(!members.is_empty());

    if members.len() == 1
        && get_bit_sequence_size(&members) == 0
        && members.first().unwrap().tag.is_none()
    {
        decode_func(members.first().unwrap().data_type(), namespace)
    } else {
        format!(
            "\
(ref SliceDecoder decoder) =>
{{
    {decode}
}}",
            decode = decode_operation(operation, false).indent()
        )
        .into()
    }
}
