using Amazon.Lambda.DynamoDBEvents;
using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Serialization.SystemTextJson;
using Amazon.SQS;
using Ingest.OutboxRelay;

// Composition root. All behaviour lives in OutboxBatchProcessor,
// OutboxEventReader and OutboxRelayService, which the tests exercise.

string queueUrl =
    Environment.GetEnvironmentVariable(RelayEnvironmentVariables.OutboxQueueUrl)
    ?? throw new InvalidOperationException(
        $"{RelayEnvironmentVariables.OutboxQueueUrl} is not configured.");

OutboxBatchProcessor processor = new(new OutboxRelayService(new AmazonSQSClient(), queueUrl));

await LambdaBootstrapBuilder
    .Create<DynamoDBEvent, StreamsEventResponse>(
        (batch, _) => processor.ProcessAsync(batch, CancellationToken.None),
        new DefaultLambdaJsonSerializer())
    .Build()
    .RunAsync();
