using System.Text.Json;
using Amazon.Lambda.SQSEvents;
using Contracts;

namespace Notifier.Consumer;

public sealed class ConsumerBatchProcessor
{
    private readonly BroadcastService _broadcast;

    public ConsumerBatchProcessor(BroadcastService broadcast) => _broadcast = broadcast;

    public async Task<SQSBatchResponse> ProcessAsync(
        SQSEvent batch,
        CancellationToken cancellationToken)
    {
        List<SQSBatchResponse.BatchItemFailure> failures = [];

        foreach (SQSEvent.SQSMessage message in batch.Records)
        {
            try
            {
                TextSubmitted? submitted =
                    JsonSerializer.Deserialize<TextSubmitted>(message.Body, EventJson.Options);

                ArgumentNullException.ThrowIfNull(submitted);

                await _broadcast.BroadcastAsync(submitted, cancellationToken);
            }
            catch (Exception)
            {
                // Report this message alone and carry on with the batch. A
                // throw would make SQS retry the WHOLE batch, so every good
                // message in it is delivered again and the browser shows it
                // three times before the batch dead-letters.
                failures.Add(new SQSBatchResponse.BatchItemFailure
                {
                    ItemIdentifier = message.MessageId,
                });
            }
        }

        return new SQSBatchResponse { BatchItemFailures = failures };
    }
}
