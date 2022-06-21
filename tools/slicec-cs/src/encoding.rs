// Copyright (c) ZeroC, Inc. All rights reserved.

use slice::code_gen_util::*;
use slice::grammar::*;

use crate::builders::{Builder, FunctionCallBuilder};
use crate::code_block::CodeBlock;
use crate::cs_util::*;
use crate::slicec_ext::*;

pub fn encode_data_members(
    members: &[&DataMember],
    namespace: &str,
    field_type: FieldType,
    encoding: Encoding,
) -> CodeBlock {
    let mut code = CodeBlock::new();

    let (required_members, tagged_members) = get_sorted_members(members);

    let bit_sequence_size = if encoding == Encoding::Slice1 {
        0
    } else {
        get_bit_sequence_size(&required_members)
    };

    if bit_sequence_size > 0 {
        writeln!(
            code,
            "var bitSequenceWriter = encoder.GetBitSequenceWriter({});",
            bit_sequence_size
        );
    }

    for member in required_members {
        let param = format!("this.{}", member.field_name(field_type));
        code.writeln(&encode_type(
            member.data_type(),
            TypeContext::DataMember,
            namespace,
            &param,
            "encoder",
            encoding,
        ));
    }

    // Encode tagged
    for member in tagged_members {
        let param = format!("this.{}", member.field_name(field_type));
        code.writeln(&encode_tagged_type(
            member,
            namespace,
            &param,
            "encoder",
            TypeContext::DataMember,
            encoding,
        ));
    }

    code
}

fn encode_type(
    type_ref: &TypeRef,
    type_context: TypeContext,
    namespace: &str,
    param: &str,
    encoder_param: &str,
    encoding: Encoding,
) -> CodeBlock {
    match &type_ref.concrete_typeref() {
        TypeRefs::Interface(_) if type_ref.is_optional && encoding == Encoding::Slice1 => {
            format!(
                "{encoder_param}.EncodeNullableProxy({param}?.Proxy);",
                encoder_param = encoder_param,
                param = param)
        }
        _ if type_ref.is_class_type() => {
            assert!(encoding == Encoding::Slice1);
            if type_ref.is_optional {
                format!(
                    "{encoder_param}.EncodeNullableClass({param});",
                    encoder_param = encoder_param,
                    param = param
                )
            } else {
                format!(
                    "{encoder_param}.EncodeClass({param});",
                    encoder_param = encoder_param,
                    param = param
                )
            }
        }
        concrete_typeref => {
            let value = if type_ref.is_optional && type_ref.is_value_type() {
                format!("{}.Value", param)
            } else {
                param.to_owned()
            };
            let encode_type = match concrete_typeref {
                TypeRefs::Primitive(primitive_ref) => {
                    format!(
                        "{encoder_param}.Encode{type_suffix}({value});",
                        encoder_param = encoder_param,
                        type_suffix = primitive_ref.type_suffix(),
                        value = value
                    )
                }
                TypeRefs::Struct(_) => {
                    format!(
                        "{value}.Encode(ref {encoder_param});",
                        value = value,
                        encoder_param = encoder_param
                    )
                }
                TypeRefs::Exception(_) => format!(
                    "{param}.Encode(ref {encoder_param});",
                    param = param,
                    encoder_param = encoder_param
                ),
                TypeRefs::Trait(_) => format!(
                    "{param}.EncodeTrait(ref {encoder_param});",
                    param = param,
                    encoder_param = encoder_param
                ),
                TypeRefs::CustomType(custom_type_ref) => {
                    format!(
                        "{encoder_extensions_class}.Encode{identifier}(ref {encoder_param}, {value});",
                        encoder_extensions_class = custom_type_ref.escape_scoped_identifier_with_suffix(
                            "SliceEncoderExtensions",
                            namespace),
                        identifier = custom_type_ref.identifier(),
                        encoder_param = encoder_param,
                        value = value
                    )
                }
                TypeRefs::Sequence(sequence_ref) => format!(
                    "{};",
                    encode_sequence(sequence_ref, namespace, param, type_context, encoder_param, encoding),
                ),
                TypeRefs::Dictionary(dictionary_ref) => {
                    format!(
                        "{};",
                        encode_dictionary(dictionary_ref, namespace, param, encoder_param, encoding)
                    )
                }
                TypeRefs::Interface(_) => {
                    format!("{encoder_param}.EncodeProxy({value}.Proxy);", encoder_param = encoder_param, value = value)
                }
                TypeRefs::Enum(enum_ref) => format!(
                    "{encoder_extensions_class}.Encode{name}(ref {encoder_param}, {param});",
                    encoder_extensions_class = enum_ref
                        .escape_scoped_identifier_with_suffix(
                            "SliceEncoderExtensions",
                            namespace
                    ),
                    name = fix_case(enum_ref.identifier(), CaseStyle::Pascal),
                    param = value,
                    encoder_param = encoder_param
                ),
                _ => panic!("class and proxy types are handled in the outer match"),
            };

            if type_ref.is_optional {
                assert!(encoding == Encoding::Slice2);
                // A null T[]? or List<T>? is implicitly converted into a default aka null
                // ReadOnlyMemory<T> or ReadOnlySpan<T>. Furthermore, the span of a default
                // ReadOnlyMemory<T> is a default ReadOnlySpan<T>, which is distinct from
                // the span of an empty sequence. This is why the "value.Span != null" below
                // works correctly.
                format!(
                    "\
bitSequenceWriter.Write({param} != null);
if ({param} != null)
{{
    {encode_type}
}}
",
                    param = match concrete_typeref {
                        TypeRefs::Sequence(sequence_ref)
                            if sequence_ref.has_fixed_size_numeric_elements()
                                && !sequence_ref.has_attribute("cs::generic", false)
                                && type_context == TypeContext::Encode =>
                            format!("{}.Span", param),
                        _ => param.to_owned(),
                    },
                    encode_type = encode_type,
                )
            } else {
                encode_type
            }
        }
    }
    .into()
}

fn encode_tagged_type(
    member: &impl Member,
    namespace: &str,
    param: &str,
    encoder_param: &str,
    type_context: TypeContext,
    encoding: Encoding,
) -> CodeBlock {
    let mut code = CodeBlock::new();
    let data_type = member.data_type();

    assert!(data_type.is_optional);
    assert!(member.tag().is_some());

    let tag = member.tag().unwrap();

    let read_only_memory = match data_type.concrete_type() {
        Types::Sequence(sequence_def)
            if sequence_def.has_fixed_size_numeric_elements()
                && type_context == TypeContext::Encode
                && !data_type.has_attribute("cs::generic", false) =>
        {
            true
        }
        _ => false,
    };

    let value = if data_type.is_value_type() {
        format!("{}.Value", param)
    } else {
        param.to_owned()
    };

    // For types with a known size, we provide a size parameter with the size of the tagged
    // param/member:
    let (size_parameter, count_value) = match data_type.concrete_type() {
        Types::Primitive(primitive_def) if primitive_def.is_fixed_size() => {
            if encoding == Encoding::Slice1 {
                (None, None)
            } else {
                (Some(primitive_def.min_wire_size().to_string()), None)
            }
        }
        Types::Primitive(primitive_def) if !matches!(primitive_def, Primitive::String) => {
            if primitive_def.is_unsigned_numeric() {
                (
                    Some(format!("SliceEncoder.GetVarUInt62EncodedSize({})", value)),
                    None,
                )
            } else {
                (
                    Some(format!("SliceEncoder.GetVarInt62EncodedSize({})", value)),
                    None,
                )
            }
        }
        Types::Struct(struct_def) if struct_def.is_fixed_size() => {
            (Some(struct_def.min_wire_size().to_string()), None)
        }
        Types::Exception(exception_def) if exception_def.is_fixed_size() => {
            (Some(exception_def.min_wire_size().to_string()), None)
        }
        Types::Enum(enum_def) => {
            if let Some(underlying) = &enum_def.underlying {
                (Some(underlying.min_wire_size().to_string()), None)
            } else {
                (None, None)
            }
        }
        Types::Sequence(sequence_def) if sequence_def.element_type.is_fixed_size() => {
            if sequence_def.element_type.is_fixed_size()
                && sequence_def.element_type.min_wire_size() == 1
            {
                (None, None)
            } else if read_only_memory {
                (
                    Some(format!(
                        "{encoder_param}.GetSizeLength({value}.Length) + {min_wire_size} * {value}.Length",
                        encoder_param = encoder_param,
                        min_wire_size = sequence_def.element_type.min_wire_size(),
                        value = value,
                    )),
                    None,
                )
            } else {
                (
                    Some(format!(
                        "{encoder_param}.GetSizeLength(count_) + {min_wire_size} * count_",
                        encoder_param = encoder_param,
                        min_wire_size = sequence_def.element_type.min_wire_size()
                    )),
                    Some(value.clone()),
                )
            }
        }
        Types::Dictionary(dictionary_def)
            if dictionary_def.key_type.is_fixed_size()
                && dictionary_def.value_type.is_fixed_size() =>
        {
            (
                Some(format!(
                    "{encoder_param}.GetSizeLength(count_) + {min_wire_size} * count_",
                    encoder_param = encoder_param,
                    min_wire_size = dictionary_def.key_type.min_wire_size()
                        + dictionary_def.value_type.min_wire_size()
                )),
                Some(value.clone()),
            )
        }
        _ => (None, None),
    };

    let unwrapped_name = member.parameter_name() + "_";
    let null_check = if read_only_memory {
        format!("{}.Span != null", param)
    } else {
        format!(
            "{param} is {unwrapped_type} {unwrapped_name}",
            param = param,
            unwrapped_type = data_type.to_type_string(namespace, type_context, true),
            unwrapped_name = &unwrapped_name
        )
    };

    let encode_tagged_call = FunctionCallBuilder::new(&format!("{}.EncodeTagged", encoder_param))
        .add_argument(tag.to_string())
        .add_argument_if(
            encoding == Encoding::Slice1 && data_type.tag_format() != Some(TagFormat::VSize),
            || format!("IceRpc.Slice.TagFormat.{}", data_type.tag_format().unwrap()),
        )
        .add_argument_if(size_parameter.is_some(), || {
            format!("size: {}", size_parameter.unwrap())
        })
        .add_argument_if_else(read_only_memory, value, unwrapped_name)
        .add_argument(encode_action(
            &clone_as_non_optional(data_type),
            type_context,
            namespace,
            encoding,
        ))
        .build();

    writeln!(
        code,
        "\
if ({null_check})
{{
    {encode_tagged}
}}",
        null_check = null_check,
        encode_tagged = {
            let mut code = CodeBlock::new();
            if let Some(count) = count_value {
                code.writeln(&format!("int count_ = {}.Count();", count));
            }
            code.writeln(&encode_tagged_call);
            code
        }
    );

    code
}

fn encode_sequence(
    sequence_ref: &TypeRef<Sequence>,
    namespace: &str,
    value: &str,
    type_context: TypeContext,
    encoder_param: &str,
    encoding: Encoding,
) -> CodeBlock {
    let has_custom_type = sequence_ref.has_attribute("cs::generic", false);
    if sequence_ref.has_fixed_size_numeric_elements() {
        if type_context == TypeContext::Encode && !has_custom_type {
            format!(
                "{encoder_param}.EncodeSpan({value}.Span)",
                encoder_param = encoder_param,
                value = value
            )
        } else {
            format!(
                "{encoder_param}.EncodeSequence({value})",
                encoder_param = encoder_param,
                value = value
            )
        }
    } else {
        format!(
            "\
{encoder_param}.EncodeSequence{with_bit_sequence}(
    {param},
    {encode_action})",
            with_bit_sequence = if encoding != Encoding::Slice1
                && sequence_ref.element_type.is_bit_sequence_encodable()
            {
                "WithBitSequence"
            } else {
                ""
            },
            encoder_param = encoder_param,
            param = value,
            encode_action = encode_action(
                &sequence_ref.element_type,
                TypeContext::Nested,
                namespace,
                encoding
            )
            .indent()
        )
    }
    .into()
}

fn encode_dictionary(
    dictionary_def: &Dictionary,
    namespace: &str,
    param: &str,
    encoder_param: &str,
    encoding: Encoding,
) -> CodeBlock {
    format!(
        "\
{encoder_param}.{method}(
    {param},
    {encode_key},
    {encode_value})",
        method = if encoding != Encoding::Slice1
            && dictionary_def.value_type.is_bit_sequence_encodable()
        {
            "EncodeDictionaryWithBitSequence"
        } else {
            "EncodeDictionary"
        },
        encoder_param = encoder_param,
        param = param,
        encode_key = encode_action(
            &dictionary_def.key_type,
            TypeContext::Nested,
            namespace,
            encoding
        )
        .indent(),
        encode_value = encode_action(
            &dictionary_def.value_type,
            TypeContext::Nested,
            namespace,
            encoding
        )
        .indent()
    )
    .into()
}

pub fn encode_action(
    type_ref: &TypeRef,
    type_context: TypeContext,
    namespace: &str,
    encoding: Encoding,
) -> CodeBlock {
    let mut code = CodeBlock::new();
    let is_optional = type_ref.is_optional;

    let value = match (type_ref.is_optional, type_ref.is_value_type()) {
        (true, false) => "value!",
        (true, true) => "value!.Value",
        _ => "value",
    };
    let value_type = type_ref.to_type_string(namespace, type_context, false);

    match &type_ref.concrete_typeref() {
        TypeRefs::Interface(_) => {
            if is_optional && encoding == Encoding::Slice1 {
                write!(
                    code,
                    "(ref SliceEncoder encoder, {value_type} value) => encoder.EncodeNullableProxy(value?.Proxy)",
                    value_type = value_type,
                );
            } else {
                write!(
                    code,
                    "(ref SliceEncoder encoder, {value_type} value) => encoder.EncodeProxy({value}.Proxy)",
                    value_type = value_type,
                    value = value
                );
            }
        }
        TypeRefs::Class(_) => {
            assert!(encoding == Encoding::Slice1);
            if is_optional {
                write!(
                    code,
                    "(ref SliceEncoder encoder, {} value) => encoder.EncodeNullableClass(value)",
                    value_type
                );
            } else {
                write!(
                    code,
                    "(ref SliceEncoder encoder, {} value) => encoder.EncodeClass(value)",
                    value_type
                );
            }
        }
        TypeRefs::Primitive(primitive_ref) => {
            write!(
                code,
                "(ref SliceEncoder encoder, {value_type} value) => encoder.Encode{builtin_type}({value})",
                value_type = value_type,
                builtin_type = primitive_ref.type_suffix(),
                value = value
            )
        }
        TypeRefs::Enum(enum_ref) => {
            write!(
                code,
                "(ref SliceEncoder encoder, {value_type} value) => {encoder_extensions_class}.Encode{name}(ref encoder, {value})",
                value_type = value_type,
                encoder_extensions_class = enum_ref.escape_scoped_identifier_with_suffix(
                    "SliceEncoderExtensions",
                    namespace),
                name = fix_case(enum_ref.identifier(), CaseStyle::Pascal),
                value = value
            )
        }
        TypeRefs::Dictionary(dictionary_ref) => {
            write!(
                code,
                "(ref SliceEncoder encoder, {value_type} value) => {encode_dictionary}",
                value_type = value_type,
                encode_dictionary =
                    encode_dictionary(dictionary_ref, namespace, "value", "encoder", encoding)
            );
        }
        TypeRefs::Sequence(sequence_ref) => {
            // We generate the sequence encoder inline, so this function must not be called when
            // the top-level object is not cached.
            write!(
                code,
                "(ref SliceEncoder encoder, {value_type} value) => {encode_sequence}",
                value_type = value_type,
                encode_sequence = encode_sequence(
                    sequence_ref,
                    namespace,
                    "value",
                    type_context,
                    "encoder",
                    encoding
                )
            )
        }
        TypeRefs::Struct(_) => {
            write!(
                code,
                "(ref SliceEncoder encoder, {value_type} value) => {value}.Encode(ref encoder)",
                value_type = value_type,
                value = value
            )
        }
        TypeRefs::Exception(_) => {
            write!(
                code,
                "(ref SliceEncoder encoder, {value_type} value) => {value}.Encode(ref encoder)",
                value_type = value_type,
                value = value
            )
        }
        TypeRefs::Trait(_) => {
            write!(
                code,
                "(ref SliceEncoder encoder, {value_type} value) => value.EncodeTrait(ref encoder)",
                value_type = value_type,
            )
        }
        TypeRefs::CustomType(custom_type_ref) => {
            write!(
                code,
                "(ref SliceEncoder encoder, {value_type} value) => {encoder_extensions_class}.Encode{identifier}(ref encoder, value)",
                value_type = value_type,
                encoder_extensions_class = custom_type_ref.escape_scoped_identifier_with_suffix(
                    "SliceEncoderExtensions",
                    namespace),
                identifier = custom_type_ref.identifier()
            )
        }
    }

    code
}

fn encode_operation_parameters(
    operation: &Operation,
    return_type: bool,
    encoder_param: &str,
) -> CodeBlock {
    let mut code = CodeBlock::new();
    let namespace = &operation.namespace();

    let members = if return_type {
        operation.nonstreamed_return_members()
    } else {
        operation.nonstreamed_parameters()
    };

    let (required_members, tagged_members) = get_sorted_members(&members);

    let bit_sequence_size = if operation.encoding == Encoding::Slice1 {
        0
    } else {
        get_bit_sequence_size(&members)
    };

    if bit_sequence_size > 0 {
        writeln!(
            code,
            "var bitSequenceWriter = {encoder_param}.GetBitSequenceWriter({bit_sequence_size});",
            encoder_param = encoder_param,
            bit_sequence_size = bit_sequence_size
        );
    }

    let return_value_name = return_type && members.len() == 1;

    for member in required_members {
        let name = if return_value_name {
            "returnValue".to_owned()
        } else {
            member.parameter_name()
        };

        code.writeln(&encode_type(
            member.data_type(),
            TypeContext::Encode,
            namespace,
            name.as_str(),
            encoder_param,
            operation.encoding,
        ));
    }

    for member in tagged_members {
        let name = if return_value_name {
            "returnValue".to_owned()
        } else {
            member.parameter_name()
        };
        code.writeln(&encode_tagged_type(
            member,
            namespace,
            name.as_str(),
            encoder_param,
            TypeContext::Encode,
            operation.encoding,
        ));
    }

    code
}

pub fn encode_operation(
    operation: &Operation,
    return_type: bool,
    assign_pipe_reader: &str,
) -> CodeBlock {
    format!(
        "\
var pipe_ = new global::System.IO.Pipelines.Pipe(
    sliceEncodeFeature?.PipeOptions ?? SliceEncodeFeature.Default.PipeOptions);
var encoder_ = new SliceEncoder(pipe_.Writer, {encoding}, {class_format});

{size_placeholder_and_start_position}

{encode_returns}

{rewrite_size}

pipe_.Writer.Complete();  // flush to reader and sets Is[Writer]Completed to true.
{assign_pipe_reader} pipe_.Reader;",
        size_placeholder_and_start_position = match operation.encoding {
            Encoding::Slice1 => "",
            _ => "\
Span<byte> sizePlaceholder_ = encoder_.GetPlaceholderSpan(4);
int startPos_ = encoder_.EncodedByteCount;",
        },
        rewrite_size = match operation.encoding {
            Encoding::Slice1 => "",
            _ => "SliceEncoder.EncodeVarUInt62((ulong)(encoder_.EncodedByteCount - startPos_), sizePlaceholder_);",
        },
        encoding = operation.encoding.to_cs_encoding(),
        class_format = operation.format_type(),
        encode_returns = encode_operation_parameters(operation, return_type, "encoder_"),
        assign_pipe_reader = assign_pipe_reader
    )
    .into()
}
