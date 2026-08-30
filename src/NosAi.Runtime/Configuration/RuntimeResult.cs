namespace NosAi.Runtime.Configuration;

public readonly record struct RuntimeError(string Code, string Message)
{
    public override string ToString() => $"{Code}: {Message}";
}

public readonly record struct RuntimeResult<T>(T? Value, RuntimeError? Error)
{
    public bool IsSuccess => Error is null;

    public static RuntimeResult<T> Success(T value) => new(value, null);

    public static RuntimeResult<T> Failure(string code, string message) =>
        new(default, new RuntimeError(code, message));
}
