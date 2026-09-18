using Amazon.Lambda.DynamoDBEvents;
using Contracts;
using Ingest.Storage;

namespace Ingest.OutboxRelay;

/// <summary>
/// Turns one DynamoDB stream record into the event to publish.
/// </summary>
/// <remarks>
/// Pure. It makes no call to AWS, so every filter rule below is a plain unit
/// test. The stream carries BOTH item types and every TTL deletion, so the
/// filtering here is the whole correctness of the relay.
/// </remarks>
public static class OutboxEventReader
{
    public static TextSubmitted? TryRead(DynamoDBEvent.DynamodbStreamRecord record)
    {
        // A REMOVE record carries OldImage, not NewImage. TTL deletions arrive
        // this way, so without this guard every expired outbox item would
        // republish one hour after it was sent.
        Dictionary<string, DynamoDBEvent.AttributeValue>? image = record.Dynamodb?.NewImage;

        if (image is null)
        {
            return null;
        }

        if (image[MessageItems.SortKey].S != MessageItems.OutboxSortKey)
        {
            return null;
        }

        return new TextSubmitted(
            MessageId: Guid.Parse(image[MessageItems.MessageIdAttribute].S),
            Text: image[MessageItems.TextAttribute].S,
            SubmittedAt: DateTimeOffset.Parse(image[MessageItems.SubmittedAtAttribute].S));
    }
}
