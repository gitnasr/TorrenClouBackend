namespace TorreClou.Core.Exceptions;

public class BusinessRuleException(string code, string message) : DomainException(code, message);
