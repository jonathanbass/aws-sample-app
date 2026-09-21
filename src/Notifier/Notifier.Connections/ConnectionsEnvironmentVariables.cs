namespace Notifier.Connections;

/// <summary>
/// Names must match the environment variables set on the Lambda in
/// infra/main/websocket.tf. A mismatch fails at startup, not at invoke time.
/// </summary>
public static class ConnectionsEnvironmentVariables
{
    public const string ConnectionsTableName = "CONNECTIONS_TABLE_NAME";
}
