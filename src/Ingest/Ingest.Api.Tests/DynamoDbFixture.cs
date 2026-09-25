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

    /// <summary>
    /// Removes every row. Call this at the start of each test that counts rows.
    /// </summary>
    /// <remarks>
    /// The fixture is shared by every test in a class, so rows written by one
    /// test are visible to the next. A scan then returns the earlier test's
    /// data and the counts are wrong, in a way that depends on test order.
    /// </remarks>
    public async Task ClearAsync()
    {
        ScanResponse rows = await Client.ScanAsync(new ScanRequest
        {
            TableName = TableName,
            ProjectionExpression = $"{MessageItems.PartitionKey}, {MessageItems.SortKey}",
        });

        foreach (Dictionary<string, AttributeValue> row in rows.Items)
        {
            await Client.DeleteItemAsync(TableName, row);
        }
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
