namespace TorreClou.Core.Exceptions;

public class NotFoundException(string code, string message) : DomainException(code, message);
