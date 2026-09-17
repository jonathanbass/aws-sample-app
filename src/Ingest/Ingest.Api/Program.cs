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

SubmitMessageFunction function = new(
    new AmazonDynamoDBClient(),
    tableName,
    TimeProvider.System);

await LambdaBootstrapBuilder
    .Create<APIGatewayHttpApiV2ProxyRequest, APIGatewayHttpApiV2ProxyResponse>(
        function.HandleAsync,
        new DefaultLambdaJsonSerializer())
    .Build()
    .RunAsync();
