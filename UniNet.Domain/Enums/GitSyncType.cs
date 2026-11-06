namespace UniNet.Domain.Enums;

public enum GitSyncType : short
{
    Initial = 0,
    Webhook = 1,
    Scheduled = 2,
    Manual = 3,
    Reconciliation = 4
}
