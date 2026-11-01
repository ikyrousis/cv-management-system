using CVManagement.Results;
using Microsoft.AspNetCore.Mvc;

namespace CVManagement.Extensions;

/// Renders a service outcome as an HTTP result, so controllers never branch on failure kinds.
public static class ResultExtensions
{
    public static IActionResult AsNoContent(this ControllerBase controller, Result result)
        => result.IsSuccess ? controller.NoContent() : controller.AsError(result.Failure);

    public static ActionResult<T> AsResult<T>(this ControllerBase controller, Result<T> result)
        => result.IsSuccess ? controller.Ok(result.Value) : controller.AsError<T>(result.Failure);

    public static ActionResult<T> AsResult<T>(
        this ControllerBase controller,
        Result<T> result,
        Func<T, ActionResult<T>> onSuccess)
        => result.IsSuccess ? onSuccess(result.Value!) : controller.AsError<T>(result.Failure);

    public static IActionResult AsError(this ControllerBase controller, ServiceFailure failure)
        => failure.Kind switch
        {
            FailureKind.NotFound => controller.NotFound(),
            // A bare 401 with no body: every login failure must be byte-identical, so the response
            // cannot reveal whether the username or the password was wrong, or the account inactive.
            FailureKind.Unauthorized => controller.Unauthorized(),
            _ => controller.BadRequest(failure.Message)
        };

    public static ActionResult<T> AsError<T>(this ControllerBase controller, ServiceFailure failure)
        => failure.Kind switch
        {
            FailureKind.NotFound => controller.NotFound(),
            FailureKind.Unauthorized => controller.Unauthorized(),
            _ => controller.BadRequest(failure.Message)
        };
}
