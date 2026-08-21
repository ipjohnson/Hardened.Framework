namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// A handler's declared response set, for a module in <see cref="ResponseModel.Response"/> mode.
/// </summary>
/// <remarks>
/// <para>
/// One type per arity from two cases to eight, in one file, because they are one type written eight
/// times rather than eight types. Splitting them apart would spread a single decision across eight
/// places without making any of them easier to read.
/// </para>
/// <para>
/// <b>Shipped precompiled rather than emitted per assembly.</b> A generated copy in each assembly
/// gives two libraries two distinct <c>Response&lt;Todo, NotFound&gt;</c> types that do not unify
/// across the boundary, so a handler in one cannot return what the other declared.
/// </para>
/// <para>
/// <b>It carries no <c>[Union]</c>, now or ever.</b> Adding that attribute to an existing type is
/// source-breaking: patterns applied to a union unwrap to <c>Value</c>, so
/// <c>result is { Value: Todo todo }</c> stops compiling. The danger is not the attribute but when
/// the break lands - a type that behaves like a struct on one compiler and a union on the next
/// changes the meaning of existing, untouched, compiling code because someone upgraded the build
/// image. A team that wants language-union semantics selects <see cref="ResponseModel.Union"/> and
/// gets the real keyword, which is better than a shim imitating one.
/// </para>
/// <para>
/// <b>The shape is the C# basic union pattern</b> - one public single-parameter constructor per
/// case, and a public <c>object? Value</c>. That is what Hardened matches structurally, so this,
/// a generated response union and a C# 15 <c>union</c> declaration all travel one code path and
/// the generators need no toolchain awareness. It is also the shape <c>OneOfEmitter</c> already
/// emits one layer down.
/// </para>
/// <para>
/// <b>A case type may not appear twice.</b> Two identical type arguments produce two identical
/// conversions and the compiler reports CS0457 at the point of use - which is what makes the
/// per-status wrapper types mandatory rather than stylistic, in this mode and under the keyword
/// alike.
/// </para>
/// <para>
/// <b>Naming.</b> Spelling the set out in three signatures is noise; the answer is a using-alias,
/// which costs nothing at run time because an alias is a name rather than a type.
/// <code>
/// using GetTodoResult = Hardened.Requests.Abstract.Responses.Response&lt;Todo, NotFound&gt;;
/// </code>
/// Naming by inheritance does not work - a derived type has no conversion to its base's operators
/// and fails CS0266 - which is the whole reason this is a struct.
/// </para>
/// </remarks>

/// <summary>A response set of 2 cases. See <see cref="Response{T1, T2}"/>.</summary>
public readonly struct Response<T1, T2> {

    public Response(T1 value) {
        Value = value;
    }

    public Response(T2 value) {
        Value = value;
    }

    /// <summary>
    /// The case this response holds.
    /// </summary>
    /// <remarks>
    /// Nullable because <c>default</c> bypasses every constructor and <c>return default;</c>
    /// compiles. Nothing here produces that, and a caller who writes it has declared no case -
    /// which the generated dispatch answers as a server fault rather than guessing a status.
    /// </remarks>
    public object? Value { get; }

    public static implicit operator Response<T1, T2>(T1 value) => new(value);
    public static implicit operator Response<T1, T2>(T2 value) => new(value);

    /// <summary>The case's own rendering, so logging one does not print the wrapper.</summary>
    public override string ToString() => Value?.ToString() ?? "";
}

/// <summary>A response set of 3 cases. See <see cref="Response{T1, T2}"/>.</summary>
public readonly struct Response<T1, T2, T3> {

    public Response(T1 value) {
        Value = value;
    }

    public Response(T2 value) {
        Value = value;
    }

    public Response(T3 value) {
        Value = value;
    }

    /// <summary>
    /// The case this response holds.
    /// </summary>
    /// <remarks>
    /// Nullable because <c>default</c> bypasses every constructor and <c>return default;</c>
    /// compiles. Nothing here produces that, and a caller who writes it has declared no case -
    /// which the generated dispatch answers as a server fault rather than guessing a status.
    /// </remarks>
    public object? Value { get; }

    public static implicit operator Response<T1, T2, T3>(T1 value) => new(value);
    public static implicit operator Response<T1, T2, T3>(T2 value) => new(value);
    public static implicit operator Response<T1, T2, T3>(T3 value) => new(value);

    /// <summary>The case's own rendering, so logging one does not print the wrapper.</summary>
    public override string ToString() => Value?.ToString() ?? "";
}

/// <summary>A response set of 4 cases. See <see cref="Response{T1, T2}"/>.</summary>
public readonly struct Response<T1, T2, T3, T4> {

    public Response(T1 value) {
        Value = value;
    }

    public Response(T2 value) {
        Value = value;
    }

    public Response(T3 value) {
        Value = value;
    }

    public Response(T4 value) {
        Value = value;
    }

    /// <summary>
    /// The case this response holds.
    /// </summary>
    /// <remarks>
    /// Nullable because <c>default</c> bypasses every constructor and <c>return default;</c>
    /// compiles. Nothing here produces that, and a caller who writes it has declared no case -
    /// which the generated dispatch answers as a server fault rather than guessing a status.
    /// </remarks>
    public object? Value { get; }

    public static implicit operator Response<T1, T2, T3, T4>(T1 value) => new(value);
    public static implicit operator Response<T1, T2, T3, T4>(T2 value) => new(value);
    public static implicit operator Response<T1, T2, T3, T4>(T3 value) => new(value);
    public static implicit operator Response<T1, T2, T3, T4>(T4 value) => new(value);

    /// <summary>The case's own rendering, so logging one does not print the wrapper.</summary>
    public override string ToString() => Value?.ToString() ?? "";
}

/// <summary>A response set of 5 cases. See <see cref="Response{T1, T2}"/>.</summary>
public readonly struct Response<T1, T2, T3, T4, T5> {

    public Response(T1 value) {
        Value = value;
    }

    public Response(T2 value) {
        Value = value;
    }

    public Response(T3 value) {
        Value = value;
    }

    public Response(T4 value) {
        Value = value;
    }

    public Response(T5 value) {
        Value = value;
    }

    /// <summary>
    /// The case this response holds.
    /// </summary>
    /// <remarks>
    /// Nullable because <c>default</c> bypasses every constructor and <c>return default;</c>
    /// compiles. Nothing here produces that, and a caller who writes it has declared no case -
    /// which the generated dispatch answers as a server fault rather than guessing a status.
    /// </remarks>
    public object? Value { get; }

    public static implicit operator Response<T1, T2, T3, T4, T5>(T1 value) => new(value);
    public static implicit operator Response<T1, T2, T3, T4, T5>(T2 value) => new(value);
    public static implicit operator Response<T1, T2, T3, T4, T5>(T3 value) => new(value);
    public static implicit operator Response<T1, T2, T3, T4, T5>(T4 value) => new(value);
    public static implicit operator Response<T1, T2, T3, T4, T5>(T5 value) => new(value);

    /// <summary>The case's own rendering, so logging one does not print the wrapper.</summary>
    public override string ToString() => Value?.ToString() ?? "";
}

/// <summary>A response set of 6 cases. See <see cref="Response{T1, T2}"/>.</summary>
public readonly struct Response<T1, T2, T3, T4, T5, T6> {

    public Response(T1 value) {
        Value = value;
    }

    public Response(T2 value) {
        Value = value;
    }

    public Response(T3 value) {
        Value = value;
    }

    public Response(T4 value) {
        Value = value;
    }

    public Response(T5 value) {
        Value = value;
    }

    public Response(T6 value) {
        Value = value;
    }

    /// <summary>
    /// The case this response holds.
    /// </summary>
    /// <remarks>
    /// Nullable because <c>default</c> bypasses every constructor and <c>return default;</c>
    /// compiles. Nothing here produces that, and a caller who writes it has declared no case -
    /// which the generated dispatch answers as a server fault rather than guessing a status.
    /// </remarks>
    public object? Value { get; }

    public static implicit operator Response<T1, T2, T3, T4, T5, T6>(T1 value) => new(value);
    public static implicit operator Response<T1, T2, T3, T4, T5, T6>(T2 value) => new(value);
    public static implicit operator Response<T1, T2, T3, T4, T5, T6>(T3 value) => new(value);
    public static implicit operator Response<T1, T2, T3, T4, T5, T6>(T4 value) => new(value);
    public static implicit operator Response<T1, T2, T3, T4, T5, T6>(T5 value) => new(value);
    public static implicit operator Response<T1, T2, T3, T4, T5, T6>(T6 value) => new(value);

    /// <summary>The case's own rendering, so logging one does not print the wrapper.</summary>
    public override string ToString() => Value?.ToString() ?? "";
}

/// <summary>A response set of 7 cases. See <see cref="Response{T1, T2}"/>.</summary>
public readonly struct Response<T1, T2, T3, T4, T5, T6, T7> {

    public Response(T1 value) {
        Value = value;
    }

    public Response(T2 value) {
        Value = value;
    }

    public Response(T3 value) {
        Value = value;
    }

    public Response(T4 value) {
        Value = value;
    }

    public Response(T5 value) {
        Value = value;
    }

    public Response(T6 value) {
        Value = value;
    }

    public Response(T7 value) {
        Value = value;
    }

    /// <summary>
    /// The case this response holds.
    /// </summary>
    /// <remarks>
    /// Nullable because <c>default</c> bypasses every constructor and <c>return default;</c>
    /// compiles. Nothing here produces that, and a caller who writes it has declared no case -
    /// which the generated dispatch answers as a server fault rather than guessing a status.
    /// </remarks>
    public object? Value { get; }

    public static implicit operator Response<T1, T2, T3, T4, T5, T6, T7>(T1 value) => new(value);
    public static implicit operator Response<T1, T2, T3, T4, T5, T6, T7>(T2 value) => new(value);
    public static implicit operator Response<T1, T2, T3, T4, T5, T6, T7>(T3 value) => new(value);
    public static implicit operator Response<T1, T2, T3, T4, T5, T6, T7>(T4 value) => new(value);
    public static implicit operator Response<T1, T2, T3, T4, T5, T6, T7>(T5 value) => new(value);
    public static implicit operator Response<T1, T2, T3, T4, T5, T6, T7>(T6 value) => new(value);
    public static implicit operator Response<T1, T2, T3, T4, T5, T6, T7>(T7 value) => new(value);

    /// <summary>The case's own rendering, so logging one does not print the wrapper.</summary>
    public override string ToString() => Value?.ToString() ?? "";
}

/// <summary>A response set of 8 cases. See <see cref="Response{T1, T2}"/>.</summary>
public readonly struct Response<T1, T2, T3, T4, T5, T6, T7, T8> {

    public Response(T1 value) {
        Value = value;
    }

    public Response(T2 value) {
        Value = value;
    }

    public Response(T3 value) {
        Value = value;
    }

    public Response(T4 value) {
        Value = value;
    }

    public Response(T5 value) {
        Value = value;
    }

    public Response(T6 value) {
        Value = value;
    }

    public Response(T7 value) {
        Value = value;
    }

    public Response(T8 value) {
        Value = value;
    }

    /// <summary>
    /// The case this response holds.
    /// </summary>
    /// <remarks>
    /// Nullable because <c>default</c> bypasses every constructor and <c>return default;</c>
    /// compiles. Nothing here produces that, and a caller who writes it has declared no case -
    /// which the generated dispatch answers as a server fault rather than guessing a status.
    /// </remarks>
    public object? Value { get; }

    public static implicit operator Response<T1, T2, T3, T4, T5, T6, T7, T8>(T1 value) => new(value);
    public static implicit operator Response<T1, T2, T3, T4, T5, T6, T7, T8>(T2 value) => new(value);
    public static implicit operator Response<T1, T2, T3, T4, T5, T6, T7, T8>(T3 value) => new(value);
    public static implicit operator Response<T1, T2, T3, T4, T5, T6, T7, T8>(T4 value) => new(value);
    public static implicit operator Response<T1, T2, T3, T4, T5, T6, T7, T8>(T5 value) => new(value);
    public static implicit operator Response<T1, T2, T3, T4, T5, T6, T7, T8>(T6 value) => new(value);
    public static implicit operator Response<T1, T2, T3, T4, T5, T6, T7, T8>(T7 value) => new(value);
    public static implicit operator Response<T1, T2, T3, T4, T5, T6, T7, T8>(T8 value) => new(value);

    /// <summary>The case's own rendering, so logging one does not print the wrapper.</summary>
    public override string ToString() => Value?.ToString() ?? "";
}
