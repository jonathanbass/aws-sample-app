using Amazon.ApiGatewayManagementApi;
using Amazon.DynamoDBv2;
using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.SQSEvents;
using Amazon.Lambda.Serialization.SystemTextJson;
using Notifier.Consumer;
using Notifier.Storage;

// Composition root. All behaviour lives in ConsumerBatchProcessor and
// BroadcastService, which the tests exercise.

string tableName =
    Environment.GetEnvironmentVariable(ConsumerEnvironmentVariables.ConnectionsTableName)
    ?? throw new InvalidOperationException(
        $"{ConsumerEnvironmentVariables.ConnectionsTableName} is not configured.");

string managementEndpoint =
    Environment.GetEnvironmentVariable(ConsumerEnvironmentVariables.WebSocketManagementEndpoint)
    ?? throw new InvalidOperationException(
        $"{ConsumerEnvironmentVariables.WebSocketManagementEndpoint} is not configured.");

// The MANAGEMENT endpoint, which is https://, NOT the wss:// URL the browser
// connects to. Pointing this at the wss:// URL fails at runtime with an
// unhelpful error.
AmazonApiGatewayManagementApiClient socketApi = new(
    new AmazonApiGatewayManagementApiConfig { ServiceURL = managementEndpoint });

ConsumerBatchProcessor processor = new(
    new BroadcastService(
        socketApi,
        new ConnectionRegistry(new AmazonDynamoDBClient(), tableName, TimeProvider.System)));

await LambdaBootstrapBuilder
    .Create<SQSEvent, SQSBatchResponse>(
        (batch, _) => processor.ProcessAsync(batch, CancellationToken.None),
        new DefaultLambdaJsonSerializer())
    .Build()
    .RunAsync();
