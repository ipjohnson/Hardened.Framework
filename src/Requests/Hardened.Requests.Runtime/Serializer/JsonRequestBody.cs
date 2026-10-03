using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Errors;
using Hardened.Requests.Runtime.Validation;

namespace Hardened.Requests.Runtime.Serializer;

/// <summary>
/// How both JSON request deserializers read a body.
/// </summary>
/// <remarks>
/// <para>
/// Into a pooled buffer and then from it, rather than streamed, because a body holding a value its
/// enum does not declare is read more than once. See <see cref="UndeclaredValue"/>. The whole body
/// is in memory while it is read. A streamed read held its 16 KB buffer, and most bodies fit in
/// that, so for them this holds no more than before.
/// </para>
/// <para>
/// A body with no undeclared value is read once, and answers or throws exactly as it did streamed.
/// </para>
/// </remarks>
internal static class JsonRequestBody
{
    /// <summary>
    /// The most undeclared values reported from one body. Every one costs a read of the whole body,
    /// so this bounds what a body full of them costs to answer.
    /// </summary>
    internal const int MaxUndeclaredValues = 16;

    private const int InitialBufferSize = 4096;

    public static async ValueTask<T?> Read<T>(IExecutionContext context, JsonTypeInfo<T> typeInfo)
    {
        var body = context.Request.Body;
        var buffer = ArrayPool<byte>.Shared.Rent(InitialBufferSize);
        var length = 0;

        try
        {
            int read;

            while ((read = await body.ReadAsync(buffer.AsMemory(length))) > 0)
            {
                length += read;

                if (length == buffer.Length)
                {
                    var larger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);

                    buffer.AsSpan(0, length).CopyTo(larger);
                    Return(buffer, length);
                    buffer = larger;
                }
            }

            return Deserialize(context, buffer, length, typeInfo);
        }
        finally
        {
            Return(buffer, length);
        }
    }

    /// <summary>
    /// Cleared before it goes back, as System.Text.Json clears its own, because a body can carry a
    /// password.
    /// </summary>
    private static void Return(byte[] buffer, int length)
    {
        buffer.AsSpan(0, length).Clear();
        ArrayPool<byte>.Shared.Return(buffer);
    }

    private static T? Deserialize<T>(
        IExecutionContext context,
        byte[] buffer,
        int length,
        JsonTypeInfo<T> typeInfo
    )
    {
        var json = new ReadOnlySpan<byte>(buffer, 0, length);

        // Skipped by the stream reader and refused by the span reader.
        if (json.StartsWith("﻿"u8))
        {
            json = json.Slice(3);
        }

        try
        {
            return JsonSerializer.Deserialize(json, typeInfo);
        }
        catch (UndeclaredValueException undeclared)
        {
            throw ReadPast(context, json, typeInfo, undeclared);
        }
        catch (JsonException exception)
        {
            var rewritten = MemberTypeMessage.Rewrite(exception, typeInfo);

            if (ReferenceEquals(rewritten, exception))
            {
                throw;
            }

            throw rewritten;
        }
    }

    /// <summary>
    /// Reads the body again, tolerating one more undeclared value each time, until a read reaches
    /// the end or fails for another reason.
    /// </summary>
    private static BodyBindingException ReadPast<T>(
        IExecutionContext context,
        ReadOnlySpan<byte> json,
        JsonTypeInfo<T> typeInfo,
        UndeclaredValueException first
    )
    {
        var failures = new List<JsonException> { first };
        object? body = null;

        while (failures.Count < MaxUndeclaredValues)
        {
            using var tolerance = UndeclaredValue.Tolerate(failures.Count);

            try
            {
                body = JsonSerializer.Deserialize(json, typeInfo);

                break;
            }
            catch (UndeclaredValueException next)
            {
                failures.Add(next);
            }
            catch (JsonException other)
            {
                failures.Add(MemberTypeMessage.Rewrite(other, typeInfo));

                break;
            }
        }

        var field = ExceptionToModelConverter.BodyField(context);

        var errors = failures
            .SelectMany(failure => ExceptionToModelConverter.BodyReadErrors(failure, field))
            .Select(error => new ValidationModules.ValidationError(
                error.Field,
                error.Code,
                error.Message
            ))
            .ToArray();

        return new BodyBindingException(
            ValidationModules.ValidationResult.FromErrors(errors),
            field,
            body,
            first
        );
    }
}
