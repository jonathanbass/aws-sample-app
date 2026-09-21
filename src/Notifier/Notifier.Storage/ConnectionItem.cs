namespace Notifier.Storage;

/// <summary>
/// The connections table schema. The consumer Lambda in Phase 5 reads the same
/// table, so both ends must take the attribute names from here.
/// </summary>
public static class ConnectionItem
{
    public const string PartitionKey = "connectionId";
    public const string ExpiresAtAttribute = "expiresAt";
}
