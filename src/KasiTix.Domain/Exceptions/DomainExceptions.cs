namespace KasiTix.Domain.Exceptions;

// One base type, one subclass per HTTP outcome. ApiExceptionHandler maps each
// to a status code — nothing else in the app decides status codes for failures.
public abstract class DomainException(string message) : Exception(message);
public sealed class NotFoundException(string message) : DomainException(message);
public sealed class ConflictException(string message) : DomainException(message);
public sealed class UnprocessableEntityException(string message) : DomainException(message);