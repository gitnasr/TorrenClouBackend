namespace TorreClou.Core.Exceptions;

public class ConflictException(string code, string message) : DomainException(code, message);
