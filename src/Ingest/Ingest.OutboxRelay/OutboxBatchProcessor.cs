using Amazon.Lambda.DynamoDBEvents;

namespace Ingest.OutboxRelay;

public sealed class OutboxBatchProcessor
{
    private readonly OutboxRelayService _relay;

    public OutboxBatchProcessor(OutboxRelayService relay) => _relay = relay;

    public async Task<StreamsEventResponse> ProcessAsync(
        DynamoDBEvent batch,
        CancellationToken cancellationToken)
    {
        List<StreamsEventResponse.BatchItemFailure> failures = [];

        foreach (DynamoDBEvent.DynamodbStreamRecord record in batch.Records)
        {
            Contracts.TextSubmitted? submitted = OutboxEventReader.TryRead(record);

            if (submitted is null)
            {
                continue;
            }

            try
            {
                await _relay.RelayAsync(submitted, cancellationToken);
            }
            catch (Exception)
            {
                // Report the sequence number and STOP. A DynamoDB stream is
                // ordered, so Lambda retries from this checkpoint. To carry on
                // through the batch would re-send every later record on the
                // retry.
                failures.Add(new StreamsEventResponse.BatchItemFailure
                {
                    ItemIdentifier = record.Dynamodb.SequenceNumber,
                });

                break;
            }
        }

        return new StreamsEventResponse { BatchItemFailures = failures };
    }
}
