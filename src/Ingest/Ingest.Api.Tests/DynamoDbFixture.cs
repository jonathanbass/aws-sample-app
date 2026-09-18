using Ingest.Storage;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Ingest.Api.Tests;

/// <summary>
/// Runs DynamoDB Local in Docker so handler contract tests exercise real
/// persistence rather than a substitute. Per dotnet-testing.md, thin
/// orchestration over persistence is covered by integration tests - mocking
/// IAmazonDynamoDB here would test the mock wiring, not the behaviour.
/// </summary>
public sealed class DynamoDbFixture : IAsyncLifetime
{
    private const int DynamoDbLocalPort = 8000;

    public const string TableName = "messages-test";

    private readonly IContainer _container = new ContainerBuilder("amazon/dynamodb-local:latest")
        .WithPortBinding(DynamoDbLocalPort, assignRandomHostPort: true)
        .WithWaitStrategy(
            Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(DynamoDbLocalPort))
        .Build();

    public IAmazonDynamoDB Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        AmazonDynamoDBConfig config = new()
        {
            ServiceURL = $"http://localhost:{_container.GetMappedPublicPort(DynamoDbLocalPort)}",
            AuthenticationRegion = RegionEndpoint.EUWest1.SystemName,
        };

        // DynamoDB Local ignores credentials but the SDK requires them to sign.
        Client = new AmazonDynamoDBClient(new BasicAWSCredentials("local", "local"), config);

        await CreateMessagesTableAsync();
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        await _container.DisposeAsync();
    }

    private async Task CreateMessagesTableAsync() =>
        await Client.CreateTableAsync(new CreateTableRequest
        {
            TableName = TableName,
            BillingMode = BillingMode.PAY_PER_REQUEST,
            KeySchema =
            [
                new KeySchemaElement(MessageItems.PartitionKey, KeyType.HASH),
                new KeySchemaElement(MessageItems.SortKey, KeyType.RANGE),
            ],
            AttributeDefinitions =
            [
                new AttributeDefinition(MessageItems.PartitionKey, ScalarAttributeType.S),
                new AttributeDefinition(MessageItems.SortKey, ScalarAttributeType.S),
            ],
        });
}
