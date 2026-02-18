namespace TorreClou.Core.Exceptions;

public class ForbiddenException(string code, string message) : DomainException(code, message);
