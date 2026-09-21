namespace Notifier.Consumer;

/// <summary>
/// Names must match the environment variables set on the Lambda in
/// infra/main/consumer.tf. A mismatch fails at startup, not at invoke time.
/// </summary>
public static class ConsumerEnvironmentVariables
{
    public const string ConnectionsTableName = "CONNECTIONS_TABLE_NAME";

    /// <summary>
    /// The https:// management endpoint, NOT the wss:// URL the browser uses.
    /// </summary>
    public const string WebSocketManagementEndpoint = "WEBSOCKET_MANAGEMENT_ENDPOINT";
}
