using Amazon.Lambda.DynamoDBEvents;
using AwesomeAssertions;
using Contracts;
using Ingest.OutboxRelay;
using Ingest.Storage;

namespace Ingest.OutboxRelay.Tests;

public class OutboxEventReaderTests
{
    private static readonly DateTimeOffset SubmittedAt =
        new(2026, 9, 17, 14, 30, 0, TimeSpan.Zero);

    private static readonly Guid MessageId =
        Guid.Parse("0199c0de-1111-4222-8333-444455556666");

    /// <summary>
    /// Builds the stream record from the REAL writer. If the writer changes an
    /// attribute name, this test fails. A hand-written item would hide that.
    /// </summary>
    private static DynamoDBEvent.DynamodbStreamRecord StreamRecord(
        string eventName,
        Dictionary<string, Amazon.DynamoDBv2.Model.AttributeValue> writtenItem) =>
        new()
        {
            EventName = eventName,
            Dynamodb = new DynamoDBEvent.StreamRecord { NewImage = ToStreamImage(writtenItem) },
        };

    /// <summary>
    /// Amazon.Lambda.DynamoDBEvents defines its OWN AttributeValue type. It is
    /// not the SDK's Amazon.DynamoDBv2.Model.AttributeValue that the writer
    /// produces. The two have the same shape but do not convert implicitly.
    /// </summary>
    private static Dictionary<string, DynamoDBEvent.AttributeValue> ToStreamImage(
        Dictionary<string, Amazon.DynamoDBv2.Model.AttributeValue> writtenItem) =>
        writtenItem.ToDictionary(
            attribute => attribute.Key,
            attribute => new DynamoDBEvent.AttributeValue
            {
                S = attribute.Value.S,
                N = attribute.Value.N,
            });

    [Fact]
    public void TryRead_maps_an_inserted_outbox_item_to_the_event()
    {
        MessageItems items = MessageItems.Create(MessageId, "hello world", SubmittedAt);

        TextSubmitted? result =
            OutboxEventReader.TryRead(StreamRecord("INSERT", items.OutboxItem));

        result.Should().NotBeNull();
        result!.MessageId.Should().Be(MessageId);
        result.Text.Should().Be("hello world");
        result.SubmittedAt.Should().Be(SubmittedAt);
    }

    [Fact]
    public void TryRead_ignores_the_domain_item()
    {
        MessageItems items = MessageItems.Create(MessageId, "hello world", SubmittedAt);

        // One TransactWriteItems call writes BOTH items, so BOTH reach the
        // stream. The domain item carries the same attributes as the outbox
        // item. Without this filter every message publishes twice.
        TextSubmitted? result =
            OutboxEventReader.TryRead(StreamRecord("INSERT", items.DomainItem));

        result.Should().BeNull();
    }

    [Fact]
    public void TryRead_ignores_a_remove_record()
    {
        // The TTL on the outbox item (design D16) deletes it after one hour.
        // DynamoDB writes a REMOVE record to the same stream. A REMOVE record
        // carries OldImage, not NewImage. Without this filter every expired
        // message republishes an hour after it was sent.
        DynamoDBEvent.DynamodbStreamRecord remove = new()
        {
            EventName = "REMOVE",
            Dynamodb = new DynamoDBEvent.StreamRecord { NewImage = null },
        };

        TextSubmitted? result = OutboxEventReader.TryRead(remove);

        result.Should().BeNull();
    }
}
