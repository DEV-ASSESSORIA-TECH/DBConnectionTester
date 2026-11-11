namespace DBConnectionTester.Models;

public enum StorageScope
{
    LocalUser,
    SharedMachine,
    Portable,
    Custom
}

public sealed record StoreDescriptor(
    Guid StoreId,
    string DatabasePath,
    StorageScope Scope,
    Guid? ClonedFromStoreId,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastOpenedAt,
    int SchemaVersion);

public enum StoreInspectionStatus
{
    Missing,
    Compatible,
    FutureVersion,
    Invalid
}

public sealed record StoreInspection(
    string DatabasePath,
    StoreInspectionStatus Status,
    StoreDescriptor? Descriptor,
    string ErrorMessage)
{
    public bool IsCompatible => Status == StoreInspectionStatus.Compatible;
}
