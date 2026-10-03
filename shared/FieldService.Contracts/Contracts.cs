namespace FieldService.Contracts;
public record LoginRequest(string Email, string Password);
public record Session(string Token, Guid UserId, string Name, string Role, DateTimeOffset ExpiresAt);
public record SiteInput(string Name, string Address);
public record AssetInput(Guid SiteId, string Identifier, string Name, string Category, string Location, string Status, int ServiceIntervalDays);
public record ChecklistDefinition(Guid Id, string Label, bool Required = true);
public record ScheduleInput(Guid AssetId, string Name, int IntervalDays, DateTimeOffset NextDueAt, string Priority, Guid? TechnicianId, List<ChecklistDefinition> Checklist);
public record WorkOrderInput(Guid AssetId, string Title, DateTimeOffset DueAt, string Priority, Guid? TechnicianId, List<ChecklistDefinition> Checklist, Guid? FollowUpFaultId = null);
public record AssignmentInput(Guid? TechnicianId, long Version);
public record ReviewInput(long Version, string Notes);
public class Inspection
{
 public string Notes { get; set; } = "";
 public DateTimeOffset? StartedAt { get; set; }
 public DateTimeOffset? SubmittedAt { get; set; }
 public List<ChecklistAnswer> Answers { get; set; } = [];
 public List<FaultEntry> Faults { get; set; } = [];
 public List<PartUse> Parts { get; set; } = [];
 public List<Guid> AttachmentIds { get; set; } = [];
}
public record ChecklistAnswer(Guid ItemId, bool Done, string Notes);
public record FaultEntry(Guid Id, string Description, string Severity);
public record PartUse(Guid PartId, decimal Quantity);
public record SyncMutation(Guid OperationId, Guid WorkOrderId, long BaseVersion, string Action, Inspection Inspection);
public record SyncAck(Guid OperationId, WorkOrderDto WorkOrder);
public record ConflictResponse(string Message, WorkOrderDto Current);
public record WorkOrderDto(Guid Id, Guid AssetId, string AssetName, string AssetIdentifier, string SiteName, string Title, DateTimeOffset DueAt, string Priority, Guid? TechnicianId, string? TechnicianName, string Status, long Version, List<ChecklistDefinition> Checklist, Inspection Inspection, string ReviewNotes, DateTimeOffset? CompletedAt)
{
 public string AssetLocation { get; init; } = "";
 public string SiteAddress { get; init; } = "";
}
public record PartDto(Guid Id, string Name, string Sku, decimal Stock);
public record TechnicianDto(Guid Id, string Name, string Email);
public record DownloadBundle(List<WorkOrderDto> WorkOrders, List<PartDto> Parts, DateTimeOffset ServerTime);
public record AttachmentAck(Guid Id, string ContentType, long Size);
