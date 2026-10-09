namespace ProjectMahjong.Client.Services;

public sealed record ApiCallResult<T>(T? Value, string? Error)
{
    public bool Succeeded => Value is not null;

    public static ApiCallResult<T> Success(T value) => new(value, null);

    public static ApiCallResult<T> Failure(string error) => new(default, error);
}

