using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Notifier.Connections;

namespace Notifier.Connections.Tests;

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

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        await _container.DisposeAsync();
    }
}
