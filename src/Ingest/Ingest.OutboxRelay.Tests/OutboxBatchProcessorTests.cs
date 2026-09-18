using Amazon.Lambda.DynamoDBEvents;
using Amazon.SQS;
using Amazon.SQS.Model;
using AwesomeAssertions;
using Ingest.OutboxRelay;
using Ingest.Storage;
using NSubstitute;

namespace Ingest.OutboxRelay.Tests;

public class OutboxBatchProcessorTests
{
    private static readonly DateTimeOffset SubmittedAt =
        new(2026, 9, 17, 14, 30, 0, TimeSpan.Zero);

    private readonly IAmazonSQS _sqs = Substitute.For<IAmazonSQS>();
    private readonly OutboxBatchProcessor _processor;

    public OutboxBatchProcessorTests() =>
        _processor = new OutboxBatchProcessor(
            new OutboxRelayService(_sqs, "https://sqs.eu-west-1.amazonaws.com/0/test.fifo"));

    private static DynamoDBEvent.DynamodbStreamRecord Record(
        string sequenceNumber,
        Dictionary<string, Amazon.DynamoDBv2.Model.AttributeValue> writtenItem) =>
        new()
        {
            EventName = "INSERT",
            Dynamodb = new DynamoDBEvent.StreamRecord
            {
                SequenceNumber = sequenceNumber,
                NewImage = writtenItem.ToDictionary(
                    attribute => attribute.Key,
                    attribute => new DynamoDBEvent.AttributeValue
                    {
                        S = attribute.Value.S,
                        N = attribute.Value.N,
                    }),
            },
        };

    private int SendCount => _sqs.ReceivedCalls()
        .Count(call => call.GetMethodInfo().Name == nameof(IAmazonSQS.SendMessageAsync));

    [Fact]
    public async Task ProcessAsync_relays_the_outbox_item_only()
    {
        MessageItems items = MessageItems.Create(Guid.NewGuid(), "hello world", SubmittedAt);

        // One TransactWriteItems call puts BOTH items on the stream, so both
        // arrive in one batch. Only one of them is an event to publish.
        DynamoDBEvent batch = new()
        {
            Records =
            [
                Record("1", items.DomainItem),
                Record("2", items.OutboxItem),
            ],
        };

        await _processor.ProcessAsync(batch, CancellationToken.None);

        SendCount.Should().Be(1);
    }

    [Fact]
    public async Task ProcessAsync_reports_a_failed_record_so_Lambda_retries_it()
    {
        _sqs.SendMessageAsync(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<SendMessageResponse>>(_ => throw new AmazonSQSException("SQS is down"));

        MessageItems items = MessageItems.Create(Guid.NewGuid(), "hello world", SubmittedAt);

        DynamoDBEvent batch = new() { Records = [Record("42", items.OutboxItem)] };

        // Without this the exception escapes, Lambda retries the WHOLE batch,
        // or the record ages out of the stream and the event is lost. The
        // outbox guarantee depends on the failure being reported.
        StreamsEventResponse response = await _processor.ProcessAsync(batch, CancellationToken.None);

        response.BatchItemFailures.Should().ContainSingle()
            .Which.ItemIdentifier.Should().Be("42");
    }
}
