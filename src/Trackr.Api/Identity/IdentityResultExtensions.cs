using Microsoft.AspNetCore.Identity;

namespace Trackr.Api.Identity;

public static class IdentityResultExtensions
{
    /// <summary>
    /// Turns a failed <see cref="IdentityResult"/> into a 400 with a validation-problem
    /// body, so the client renders Identity's own messages ("Passwords must be at least
    /// 12 characters", "Email is already taken") rather than a generic failure.
    /// </summary>
    /// <remarks>
    /// Identity codes are grouped onto the field they concern where that is obvious, and
    /// otherwise onto an empty key, which is how EditForm surfaces a form-level message.
    /// </remarks>
    public static IResult ToValidationProblem(this IdentityResult result)
    {
        var errors = result.Errors
            .GroupBy(FieldFor)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.Description).ToArray());

        return Results.ValidationProblem(errors);
    }

    private static string FieldFor(IdentityError error) => error.Code switch
    {
        "DuplicateEmail" or "InvalidEmail" => "email",
        "DuplicateUserName" or "InvalidUserName" => "email",
        var code when code.StartsWith("Password", StringComparison.Ordinal) => "password",
        _ => ""
    };
}
