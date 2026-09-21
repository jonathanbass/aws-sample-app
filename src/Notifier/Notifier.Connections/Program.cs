using Amazon.DynamoDBv2;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Serialization.SystemTextJson;
using Notifier.Connections;

// Composition root. All behaviour lives in ConnectionFunction and
// ConnectionRegistry, which the tests exercise.

string tableName =
    Environment.GetEnvironmentVariable(ConnectionsEnvironmentVariables.ConnectionsTableName)
    ?? throw new InvalidOperationException(
        $"{ConnectionsEnvironmentVariables.ConnectionsTableName} is not configured.");

ConnectionFunction function = new(
    new ConnectionRegistry(new AmazonDynamoDBClient(), tableName, TimeProvider.System));

// A WebSocket route uses the version 1 proxy payload, not version 2. The
// connection id and the route key arrive on RequestContext.
await LambdaBootstrapBuilder
    .Create<APIGatewayProxyRequest, APIGatewayProxyResponse>(
        function.HandleAsync,
        new DefaultLambdaJsonSerializer())
    .Build()
    .RunAsync();
