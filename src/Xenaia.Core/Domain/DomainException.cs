namespace Xenaia.Core.Domain;

/// <summary>
/// Base for all domain-rule violations. Deriving from a single type lets
/// hosts and pipelines treat "the domain said no" uniformly.
/// </summary>
public abstract class DomainException(string message) : Exception(message);
