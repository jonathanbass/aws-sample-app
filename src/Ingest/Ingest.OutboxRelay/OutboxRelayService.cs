using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Contracts;

namespace Ingest.OutboxRelay;

public sealed class OutboxRelayService
{
    /// <summary>
    /// One group for every message.
    /// </summary>
    /// <remarks>
    /// SQS FIFO keeps order WITHIN a group, never across groups. The SPA shows
    /// one list, so one group is what keeps that list in submission order. The
    /// cost is throughput: one group is limited to 300 messages a second. That
    /// is far above anything this project sends.
    /// </remarks>
    private const string SingleOrderedGroup = "messages";

    private readonly IAmazonSQS _sqs;
    private readonly string _queueUrl;

    public OutboxRelayService(IAmazonSQS sqs, string queueUrl)
    {
        _sqs = sqs;
        _queueUrl = queueUrl;
    }

    public Task RelayAsync(TextSubmitted submitted, CancellationToken cancellationToken) =>
        _sqs.SendMessageAsync(
            new SendMessageRequest
            {
                QueueUrl = _queueUrl,
                MessageBody = JsonSerializer.Serialize(submitted, EventJson.Options),

                // The stream delivers at least once. SQS FIFO collapses a
                // repeat that carries the same deduplication id, inside a five
                // minute window.
                MessageGroupId = SingleOrderedGroup,
                MessageDeduplicationId = submitted.MessageId.ToString(),
            },
            cancellationToken);
}
