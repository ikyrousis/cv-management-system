namespace CVManagement.Application.Results;

public enum FailureKind
{
    NotFound,
    Validation
}

public sealed record ServiceFailure(FailureKind Kind, string Message)
{
    public static ServiceFailure NotFound(string message) => new(FailureKind.NotFound, message);
    public static ServiceFailure Validation(string message) => new(FailureKind.Validation, message);
}

public sealed class Result
{
    private readonly ServiceFailure? _failure;

    private Result(ServiceFailure? failure) => _failure = failure;

    public bool IsSuccess => _failure is null;

    public ServiceFailure Failure =>
        _failure ?? throw new InvalidOperationException("A successful result carries no failure.");

    public static Result Success() => new(null);

    public static Result Fail(ServiceFailure failure) => new(failure);
}

public sealed class Result<T>
{
    private readonly ServiceFailure? _failure;

    private Result(T? value, ServiceFailure? failure)
    {
        Value = value;
        _failure = failure;
    }

    public bool IsSuccess => _failure is null;

    public T? Value { get; }

    public ServiceFailure Failure =>
        _failure ?? throw new InvalidOperationException("A successful result carries no failure.");

    public static Result<T> Ok(T value) => new(value, null);

    public static Result<T> Fail(ServiceFailure failure) => new(default, failure);
}
