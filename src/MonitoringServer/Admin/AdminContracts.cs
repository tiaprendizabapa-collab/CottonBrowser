namespace MonitoringServer.Admin;

// Contracts for the next service layer. The prototype exposes only policy drafts.
public enum AdminRole { Reader, PolicyEditor, Admin }
public sealed record AdminUser(Guid Id, Guid TenantId, string Issuer, string Subject,
    string DisplayName, AdminRole Role);
public sealed record Department(Guid Id, Guid TenantId, string Name, string? ExternalGroupId);

public sealed record BrowserPolicyVersion(Guid Id, Guid DraftId, int Version,
    BrowserPolicyDraft Snapshot, Guid PublishedBy, DateTimeOffset PublishedAt);

public sealed record ManagedDevice(Guid Id, Guid TenantId, Guid? DepartmentId,
    string BrowserVersion, DateTimeOffset? LastSeenAt, DateTimeOffset EnrolledAt);
public enum PolicyDeliveryState { Queued, Received, Applied, Failed }
public sealed record PolicyDelivery(Guid DeviceId, Guid PolicyVersionId,
    PolicyDeliveryState State, DateTimeOffset ReportedAt, string? ErrorCode);

public sealed record DeviceHealthSample(Guid DeviceId, DateTimeOffset RecordedAt,
    double CpuPercent, long BrowserWorkingSetBytes);
public sealed record SecurityBlockEvent(Guid Id, Guid TenantId, Guid DeviceId,
    string RuleId, string DestinationHost, DateTimeOffset OccurredAt);
public sealed record AdminAuditEvent(long Id, Guid TenantId, Guid? ActorId,
    string Action, Guid? EntityId, int? PreviousRevision, int? NewRevision,
    DateTimeOffset OccurredAt);
