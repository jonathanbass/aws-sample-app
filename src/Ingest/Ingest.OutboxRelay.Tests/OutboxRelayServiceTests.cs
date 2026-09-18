using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using AwesomeAssertions;
using Contracts;
using Ingest.OutboxRelay;
using NSubstitute;

namespace Ingest.OutboxRelay.Tests;

public class OutboxRelayServiceTests
{
    private const string QueueUrl = "https://sqs.eu-west-1.amazonaws.com/000000000000/test.fifo";

    private static readonly DateTimeOffset SubmittedAt =
        new(2026, 9, 17, 14, 30, 0, TimeSpan.Zero);

    private static readonly Guid MessageId =
        Guid.Parse("0199c0de-1111-4222-8333-444455556666");

    private readonly IAmazonSQS _sqs = Substitute.For<IAmazonSQS>();
    private readonly OutboxRelayService _service;

    public OutboxRelayServiceTests() => _service = new OutboxRelayService(_sqs, QueueUrl);

    private async Task<SendMessageRequest> RelayAndCaptureRequestAsync(TextSubmitted submitted)
    {
        await _service.RelayAsync(submitted, CancellationToken.None);

        return (SendMessageRequest)_sqs.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IAmazonSQS.SendMessageAsync))
            .GetArguments()[0]!;
    }

    [Fact]
    public async Task RelayAsync_deduplicates_on_the_message_id()
    {
        // The stream delivers at least once, so the same outbox item can arrive
        // twice. A deduplication id equal to the message id makes SQS collapse
        // the repeat, inside its five minute window.
        SendMessageRequest request = await RelayAndCaptureRequestAsync(
            new TextSubmitted(MessageId, "hello world", SubmittedAt));

        request.MessageDeduplicationId.Should().Be(MessageId.ToString());
    }

    [Fact]
    public async Task RelayAsync_puts_every_message_in_one_group_so_order_is_kept()
    {
        // SQS FIFO keeps order WITHIN a message group, not across groups. The
        // SPA appends to one list, so every message must share one group.
        // A group per message would give parallelism and lose the order.
        SendMessageRequest first = await RelayAndCaptureRequestAsync(
            new TextSubmitted(Guid.NewGuid(), "first", SubmittedAt));

        _sqs.ClearReceivedCalls();

        SendMessageRequest second = await RelayAndCaptureRequestAsync(
            new TextSubmitted(Guid.NewGuid(), "second", SubmittedAt));

        first.MessageGroupId.Should().NotBeNullOrWhiteSpace();
        second.MessageGroupId.Should().Be(first.MessageGroupId);
    }
}
