namespace Contracts;

public sealed record TextSubmitted(Guid MessageId, string Text, DateTimeOffset SubmittedAt);
