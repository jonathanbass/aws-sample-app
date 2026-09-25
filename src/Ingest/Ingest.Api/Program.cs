using Amazon.DynamoDBv2;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Serialization.SystemTextJson;
using Ingest.Api;

// Composition root. Three lines of glue - all behaviour lives in
// SubmitMessageFunction, which is what the tests exercise.

string tableName =
    Environment.GetEnvironmentVariable(EnvironmentVariables.MessagesTableName)
    ?? throw new InvalidOperationException(
        $"{EnvironmentVariables.MessagesTableName} is not configured.");

AmazonDynamoDBClient dynamoDb = new();

// One Lambda serves every route on /messages. The router dispatches on the
// method API Gateway puts on the request context.
MessagesRouter router = new(
    new SubmitMessageFunction(dynamoDb, tableName, TimeProvider.System),
    new ListMessagesService(dynamoDb, tableName));

await LambdaBootstrapBuilder
    .Create<APIGatewayHttpApiV2ProxyRequest, APIGatewayHttpApiV2ProxyResponse>(
        router.HandleAsync,
        new DefaultLambdaJsonSerializer())
    .Build()
    .RunAsync();
