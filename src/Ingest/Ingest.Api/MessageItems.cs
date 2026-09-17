using Amazon.DynamoDBv2.Model;

namespace Ingest.Api;

public sealed record MessageItems(
    Dictionary<string, AttributeValue> DomainItem,
    Dictionary<string, AttributeValue> OutboxItem)
{
    public const string PartitionKey = "pk";
    public const string SortKey = "sk";

    public const string DomainSortKey = "MESSAGE";
    public const string OutboxSortKey = "OUTBOX";

    public const string MessageIdAttribute = "messageId";
    public const string TextAttribute = "text";
    public const string SubmittedAtAttribute = "submittedAt";

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

        return new MessageItems(
            DomainItem: ItemWithSortKey(DomainSortKey),
            OutboxItem: ItemWithSortKey(OutboxSortKey));
    }
}
