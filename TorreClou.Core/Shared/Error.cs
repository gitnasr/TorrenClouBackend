using TorreClou.Core.Enums;

namespace TorreClou.Core.Shared;

public sealed record Error(ErrorCode Code, string Message)
{
    public static readonly Error None = new(ErrorCode.None, string.Empty);
    public static readonly Error NullValue = new(ErrorCode.NullValue, "The specified result value is null.");
}
