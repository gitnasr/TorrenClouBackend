namespace TorreClou.Core.Exceptions;

public class ValidationException(string code, string message) : DomainException(code, message);
