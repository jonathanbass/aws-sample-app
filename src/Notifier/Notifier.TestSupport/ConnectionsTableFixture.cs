using Xunit;
using Notifier.Storage;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Notifier.TestSupport;

/// <summary>
/// Runs DynamoDB Local in Docker so the registry tests exercise real
/// persistence. Per dotnet-testing.md, thin orchestration over persistence is
/// covered by integration tests - a substituted IAmazonDynamoDB would assert
/// the substitute, not the behaviour.
/// </summary>
public sealed class ConnectionsTableFixture : IAsyncLifetime
{
    private const int DynamoDbLocalPort = 8000;

    public const string TableName = "connections-test";

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

        Client = new AmazonDynamoDBClient(new BasicAWSCredentials("local", "local"), config);

        await Client.CreateTableAsync(new CreateTableRequest
        {
            TableName = TableName,
            BillingMode = BillingMode.PAY_PER_REQUEST,
            KeySchema = [new KeySchemaElement(ConnectionItem.PartitionKey, KeyType.HASH)],
            AttributeDefinitions =
            [
                new AttributeDefinition(ConnectionItem.PartitionKey, ScalarAttributeType.S),
            ],
        });
    }

    /// <summary>
    /// Removes every row. Call this at the start of each test.
    /// </summary>
    /// <remarks>
    /// The fixture is shared by every test in a class, so rows written by one
    /// test are visible to the next. A broadcast test then pushes to the
    /// previous test's connections and counts the wrong number of messages.
    /// The failure depends on test order, so it looks intermittent.
    /// </remarks>
    public async Task ClearAsync()
    {
        ScanResponse rows = await Client.ScanAsync(new ScanRequest
        {
            TableName = TableName,
            ProjectionExpression = ConnectionItem.PartitionKey,
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
}
