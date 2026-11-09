namespace NeoContext.Core.Results;

/// <summary>Why an operation failed. The UI maps each kind to a user-facing message and action.</summary>
public enum ErrorKind
{
    /// <summary>The key, file or package no longer exists.</summary>
    NotFound,

    /// <summary>Access was denied and running as administrator may fix it.</summary>
    RequiresElevation,

    /// <summary>Access was denied even though the process is elevated (e.g. a TrustedInstaller-owned key).</summary>
    AccessDenied,

    /// <summary>The entry is protected by a safety rule (e.g. the default "Open" verb).</summary>
    Protected,

    /// <summary>The input failed validation (empty name, name too long, ...).</summary>
    InvalidInput,

    /// <summary>The provider does not support this operation for this entry.</summary>
    NotSupported,

    /// <summary>A network request failed.</summary>
    Network,

    /// <summary>Downloaded content failed an integrity or signature check.</summary>
    Integrity,

    /// <summary>Anything else. The message carries the details.</summary>
    Unexpected,
}

/// <summary>A failure description. Messages are for logs; the UI localizes by <see cref="Kind"/>.</summary>
public sealed record Error(ErrorKind Kind, string Message)
{
    public override string ToString() => $"{Kind}: {Message}";
}

/// <summary>The outcome of an operation that returns no value.</summary>
public readonly record struct Result
{
    private Result(Error? error) => Error = error;

    public Error? Error { get; }

    public bool IsSuccess => Error is null;

    public static Result Success() => new(null);

    public static Result Failure(Error error) => new(error);

    public static Result Failure(ErrorKind kind, string message) => new(new Error(kind, message));

    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>The outcome of an operation that returns a value on success.</summary>
public readonly record struct Result<T>
{
    private readonly T? _value;

    private Result(T? value, Error? error)
    {
        _value = value;
        Error = error;
    }

    public Error? Error { get; }

    public bool IsSuccess => Error is null;

    /// <summary>The value. Throws when the result is a failure, so check <see cref="IsSuccess"/> first.</summary>
    public T Value => IsSuccess ? _value! : throw new InvalidOperationException($"Result has no value: {Error}");

    public static Result<T> Success(T value) => new(value, null);

    public static Result<T> Failure(Error error) => new(default, error);

    public static Result<T> Failure(ErrorKind kind, string message) => new(default, new Error(kind, message));

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Failure(error);

    /// <summary>Drops the value, keeping only success or failure.</summary>
    public Result ToResult() => Error is null ? Result.Success() : Result.Failure(Error);
}
