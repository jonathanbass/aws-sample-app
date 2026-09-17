namespace Ingest.Api;

/// <summary>
/// Names must match the environment variables set on the Lambda in
/// infra/main/ingest.tf. A mismatch fails at startup, not at invoke time.
/// </summary>
public static class EnvironmentVariables
{
    public const string MessagesTableName = "MESSAGES_TABLE_NAME";
}
