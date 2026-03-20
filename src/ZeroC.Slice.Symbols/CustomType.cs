// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice.Symbols;

/// <summary>Represents a custom type defined in Slice, where the user defines the target language mapped type and
/// provides the encode and decode methods.</summary>
public class CustomType : Entity, ISymbol, IType;
