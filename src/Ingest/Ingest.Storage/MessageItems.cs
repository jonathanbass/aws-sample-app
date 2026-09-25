using Amazon.DynamoDBv2.Model;

namespace Ingest.Storage;

public sealed record MessageItems(
    Dictionary<string, AttributeValue> DomainItem,
    Dictionary<string, AttributeValue> OutboxItem)
{
    /// <summary>Both items, in the order they are written.</summary>
    public IReadOnlyList<Dictionary<string, AttributeValue>> Both => [DomainItem, OutboxItem];

    public const string PartitionKey = "pk";
    public const string SortKey = "sk";

    public const string DomainSortKey = "MESSAGE";
    public const string OutboxSortKey = "OUTBOX";

    public const string MessageIdAttribute = "messageId";
    public const string TextAttribute = "text";
    public const string SubmittedAtAttribute = "submittedAt";
    public const string ExpiresAtAttribute = "expiresAt";

    /// <summary>
    /// How long a spent outbox item survives before DynamoDB deletes it.
    /// Long enough to inspect during a failure, short enough to bound growth.
    /// </summary>
    public static readonly TimeSpan OutboxRetention = TimeSpan.FromHours(1);

    public static MessageItems Create(Guid messageId, string text, DateTimeOffset submittedAt)
    {
        string partitionKeyValue = $"MESSAGE#{messageId}";

        Dictionary<string, AttributeValue> ItemWithSortKey(string sortKeyValue) => new()
        {
            [PartitionKey] = new AttributeValue(partitionKeyValue),
            [SortKey] = new AttributeValue(sortKeyValue),
            [MessageIdAttribute] = new AttributeValue(messageId.ToString()),
            [TextAttribute] = new AttributeValue(text),
            [SubmittedAtAttribute] = new AttributeValue(submittedAt.ToString("O")),
        };

        Dictionary<string, AttributeValue> outboxItem = ItemWithSortKey(OutboxSortKey);

        // The outbox item is dead once the relay has published it. DynamoDB
        // deletes it, so the table does not hold two copies of every message
        // for ever. Only the outbox item expires - the domain item is the
        // message and must persist.
        outboxItem[ExpiresAtAttribute] = new AttributeValue
        {
            N = submittedAt.Add(OutboxRetention).ToUnixTimeSeconds().ToString(),
        };

        return new MessageItems(
            DomainItem: ItemWithSortKey(DomainSortKey),
            OutboxItem: outboxItem);
    }
}
