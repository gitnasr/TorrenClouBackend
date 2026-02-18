namespace TorreClou.Core.Exceptions;

public class ExternalServiceException(string code, string message) : DomainException(code, message);
