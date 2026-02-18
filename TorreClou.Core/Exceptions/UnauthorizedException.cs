namespace TorreClou.Core.Exceptions;

public class UnauthorizedException(string code, string message) : DomainException(code, message);
