using System.Text.Json;

namespace SmartApps.Api.Models;

public class SysAppDto
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Department { get; set; } = "Toàn công ty";
    public string Icon { get; set; } = "Apps";
    public string Color { get; set; } = "#1a73e8";
    public string BgColor { get; set; } = "#e8f0fe";
    public string Version { get; set; } = "1.0.0";
    public int OrderIndex { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public JsonElement Settings { get; set; }
    public int ModuleCount { get; set; }
    public int TableCount { get; set; }
    public int RecordCount { get; set; }
    public int PublishedVersion { get; set; } = 1;
    public string PublishStatus { get; set; } = "PUBLISHED";
    public DateTime? PublishedAt { get; set; }
    public string? PublishedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class AppReleaseDto
{
    public string Id { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public int VersionNumber { get; set; }
    public string? Notes { get; set; }
    public string PublishedBy { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; }
    public int TableCount { get; set; }
    public int ColumnCount { get; set; }
}

public class AppValidationResultDto
{
    public bool CanPublish { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
}

public class PublishAppRequest
{
    public string? Notes { get; set; }
}

public class AppSchemaDiffDto
{
    public bool HasPublishedRelease { get; set; }
    public int? PublishedVersion { get; set; }
    public string PublishStatus { get; set; } = "DRAFT";
    public List<string> AddedTables { get; set; } = new();
    public List<string> RemovedTables { get; set; } = new();
    public List<string> AddedColumns { get; set; } = new();
    public List<string> RemovedColumns { get; set; } = new();
    public List<string> ModifiedColumns { get; set; } = new();
}

public class UpsertAppRequest
{
    public string? Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Department { get; set; } = "Toàn công ty";
    public string Icon { get; set; } = "Apps";
    public string Color { get; set; } = "#1a73e8";
    public string BgColor { get; set; } = "#e8f0fe";
    public string Version { get; set; } = "1.0.0";
    public int OrderIndex { get; set; } = 1;
    public bool IsActive { get; set; } = true;
}

public class SysModuleDto
{
    public string Id { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "Layers";
    public int OrderIndex { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
    public int TableCount { get; set; }
    public int RecordCount { get; set; }
}

public class UpsertModuleRequest
{
    public string? Id { get; set; }
    public string AppId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "Layers";
    public int OrderIndex { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
}

public class SysTableDto
{
    public string Id { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string ModuleId { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string PrimaryView { get; set; } = "table";
    public string RowScope { get; set; } = "ALL";
    public bool ApprovalEnabled { get; set; }
    public string? ApprovalField { get; set; } = "trang_thai_duyet";
    public List<string> ApproverRoles { get; set; } = new() { "ADMIN", "HR_MANAGER", "ASSET_LEAD" };
    public bool PublicFormEnabled { get; set; }
    public DateTime? PublicFormExpiresAt { get; set; }
    public int? PublicFormMaxSubmissions { get; set; }
    public int OrderIndex { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public JsonElement UiConfig { get; set; }
    public List<FormRuleDto> FormRules { get; set; } = new();
    public FormLayoutConfigDto? FormLayout { get; set; }
    public int ColumnCount { get; set; }
    public int RecordCount { get; set; }
}

public class UpsertTableRequest
{
    public string? Id { get; set; }
    public string AppId { get; set; } = string.Empty;
    public string ModuleId { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string PrimaryView { get; set; } = "table";
    public string RowScope { get; set; } = "ALL";
    public bool ApprovalEnabled { get; set; }
    public string? ApprovalField { get; set; } = "trang_thai_duyet";
    public List<string> ApproverRoles { get; set; } = new() { "ADMIN", "HR_MANAGER", "ASSET_LEAD" };
    public bool PublicFormEnabled { get; set; }
    public DateTime? PublicFormExpiresAt { get; set; }
    public int? PublicFormMaxSubmissions { get; set; }
    public int OrderIndex { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public List<FormRuleDto>? FormRules { get; set; }
    public FormLayoutConfigDto? FormLayout { get; set; }
}

public class FormLayoutFieldItemDto
{
    public string ColumnCode { get; set; } = string.Empty;
    public int ColSpan { get; set; } = 1;
}

public class FormLayoutSectionDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Columns { get; set; } = 2;
    public bool Collapsible { get; set; }
    public string? CalloutText { get; set; }
    public string? CalloutTone { get; set; }
    public List<FormLayoutFieldItemDto> Fields { get; set; } = new();
}

public class FormLayoutConfigDto
{
    public string Mode { get; set; } = "sections";
    public List<FormLayoutSectionDto> Sections { get; set; } = new();
}

public class FormConditionDto
{
    public string Field { get; set; } = "";
    public string Operator { get; set; } = "EQUALS";
    public string? Value { get; set; }
}

public class FormRuleDto
{
    public string Id { get; set; } = "";
    public string TargetField { get; set; } = "";
    public string Property { get; set; } = "visible";
    public bool Value { get; set; } = true;
    public bool? ElseValue { get; set; }
    public string Match { get; set; } = "ALL";
    public List<FormConditionDto> Conditions { get; set; } = new();
}

public class SysColumnDto
{
    public string Id { get; set; } = string.Empty;
    public string TableId { get; set; } = string.Empty;
    public string ColumnCode { get; set; } = string.Empty;
    public string ColumnName { get; set; } = string.Empty;
    public string ExcelHeader { get; set; } = string.Empty;
    public string DataType { get; set; } = "STRING";
    public bool IsRequired { get; set; }
    public bool IsIdentityKey { get; set; }
    public bool ShowInTable { get; set; } = true;
    public bool ShowInForm { get; set; } = true;
    public bool ShowInParentForm { get; set; }
    public int Width { get; set; } = 170;
    public string? ValidationRegex { get; set; }
    public string? RefCategoryCode { get; set; }
    public bool AllowQuickCreate { get; set; }
    public string? RefAppId { get; set; }
    public string? RefTableId { get; set; }
    public string? RefValueField { get; set; }
    public string? RefDisplayField { get; set; }
    public string? LookupFilterField { get; set; }
    public string? LookupSourceField { get; set; }
    public bool IsIndexed { get; set; }
    public string? Formula { get; set; }
    public string? DefaultValue { get; set; }
    public List<string> HiddenForRoles { get; set; } = new();
    public List<string> ReadOnlyForRoles { get; set; } = new();
    public int OrderIndex { get; set; } = 1;
    public bool IsActive { get; set; } = true;
}

public class UpsertColumnRequest
{
    public string? Id { get; set; }
    public string TableId { get; set; } = string.Empty;
    public string ColumnCode { get; set; } = string.Empty;
    public string ColumnName { get; set; } = string.Empty;
    public string? ExcelHeader { get; set; }
    public string DataType { get; set; } = "STRING";
    public bool IsRequired { get; set; }
    public bool IsIdentityKey { get; set; }
    public bool ShowInTable { get; set; } = true;
    public bool ShowInForm { get; set; } = true;
    public bool ShowInParentForm { get; set; }
    public int Width { get; set; } = 170;
    public string? ValidationRegex { get; set; }
    public string? RefCategoryCode { get; set; }
    public bool AllowQuickCreate { get; set; }
    public string? RefAppId { get; set; }
    public string? RefTableId { get; set; }
    public string? RefValueField { get; set; }
    public string? RefDisplayField { get; set; }
    public string? LookupFilterField { get; set; }
    public string? LookupSourceField { get; set; }
    public bool IsIndexed { get; set; }
    public string? Formula { get; set; }
    public string? DefaultValue { get; set; }
    public List<string> HiddenForRoles { get; set; } = new();
    public List<string> ReadOnlyForRoles { get; set; } = new();
    public int OrderIndex { get; set; } = 1;
}

public class SysCategoryDto
{
    public string CategoryCode { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string? AppId { get; set; }
    public string? Description { get; set; }
    public int ItemCount { get; set; }
}

public class UpsertCategoryRequest
{
    public string CategoryCode { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string? AppId { get; set; }
    public string? Description { get; set; }
}

public class SysCategoryItemDto
{
    public string Id { get; set; } = string.Empty;
    public string CategoryCode { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Color { get; set; }
    public int OrderIndex { get; set; } = 1;
}

public class UpsertCategoryItemRequest
{
    public string? Id { get; set; }
    public string CategoryCode { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Color { get; set; }
    public int OrderIndex { get; set; } = 1;
}

public class ValidationErrorItem
{
    public string Severity { get; set; } = "ERROR";
    public string Field { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class WorkflowHistoryEntryDto
{
    public int StepOrder { get; set; }
    public string StepName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty; // SUBMIT | APPROVE | REJECT
    public string Actor { get; set; } = string.Empty;
    public string? ActorRole { get; set; }
    public string? Note { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
}

public class AppRecordDto
{
    public string Id { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string ModuleId { get; set; } = string.Empty;
    public string TableId { get; set; } = string.Empty;
    public string? RecordKey { get; set; }
    public Dictionary<string, object?> Data { get; set; } = new();
    public string Status { get; set; } = "ACTIVE";
    public int Version { get; set; } = 1;
    public string ApprovalStatus { get; set; } = "DRAFT";
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? RejectionReason { get; set; }
    public string? WorkflowId { get; set; }
    public int CurrentStepOrder { get; set; }
    public string? CurrentStepName { get; set; }
    public int TotalWorkflowSteps { get; set; }
    public List<string> CurrentStepApproverRoles { get; set; } = new();
    public List<WorkflowHistoryEntryDto> WorkflowHistory { get; set; } = new();
    public List<WorkflowStepDto>? WorkflowSnapshot { get; set; }
    public List<WorkflowStepApprovalVoteDto> StepApprovals { get; set; } = new();
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
    public List<ValidationErrorItem> ValidationErrors { get; set; } = new();
    public string CreatedBy { get; set; } = "system";
    public string UpdatedBy { get; set; } = "system";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class UpsertRecordRequest
{
    public string? Id { get; set; }
    public string AppId { get; set; } = string.Empty;
    public string ModuleId { get; set; } = string.Empty;
    public string TableId { get; set; } = string.Empty;
    public string? RecordKey { get; set; }
    public Dictionary<string, object?> Data { get; set; } = new();
    public string? Status { get; set; }
    public int? ExpectedVersion { get; set; }
}

public class ChildRelationMutationDto
{
    public string AppId { get; set; } = string.Empty;
    public string ModuleId { get; set; } = string.Empty;
    public string TableId { get; set; } = string.Empty;
    public string ViaColumnCode { get; set; } = string.Empty;
    public string? RefValueField { get; set; }
    public List<UpsertRecordRequest> Upserts { get; set; } = new();
    public List<string> DeleteIds { get; set; } = new();
}

public class UpsertRecordWithRelationsRequest : UpsertRecordRequest
{
    public List<ChildRelationMutationDto> ChildMutations { get; set; } = new();
}

public class PatchCellRequest
{
    public string Field { get; set; } = string.Empty;
    public object? Value { get; set; }
    public int? ExpectedVersion { get; set; }
}

public class BatchDeleteRequest
{
    public List<string> Ids { get; set; } = new();
}

public class BatchPatchRecordsRequest
{
    public string TableId { get; set; } = string.Empty;
    public List<string> RecordIds { get; set; } = new();
    public string ColumnCode { get; set; } = string.Empty;
    public object? Value { get; set; }
}

public class BatchApproveRecordsRequest
{
    public List<string> RecordIds { get; set; } = new();
    public bool Approve { get; set; } = true;
    public string? Reason { get; set; }
}

public class BatchOperationItemError
{
    public string RecordId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class BatchOperationResultDto
{
    public int Requested { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public List<string> SucceededIds { get; set; } = new();
    public List<BatchOperationItemError> Errors { get; set; } = new();
}

public class BulkImportRequest
{
    public string AppId { get; set; } = string.Empty;
    public string ModuleId { get; set; } = string.Empty;
    public string TableId { get; set; } = string.Empty;
    public bool UpsertByIdentityKey { get; set; } = true;
    public List<Dictionary<string, object?>> Rows { get; set; } = new();
    public List<int> RowNumbers { get; set; } = new();
    public string NumberFormat { get; set; } = "invariant";
    public string DateFormat { get; set; } = "yyyy-MM-dd";
    public string? PreviewFingerprint { get; set; }
}

public class ImportRowPreviewDto
{
    public int SourceRow { get; set; }
    public string Operation { get; set; } = "CREATE";
    public string? RecordKey { get; set; }
    public Dictionary<string, object?> Data { get; set; } = new();
    public List<ValidationErrorItem> Errors { get; set; } = new();
}

public class ImportPreviewDto
{
    public List<ImportRowPreviewDto> Rows { get; set; } = new();
    public int TotalRows => Rows.Count;
    public int ErrorRows => Rows.Count(r => r.Errors.Any(e => e.Severity == "ERROR"));
    public int ValidRows => TotalRows - ErrorRows;
    public int Inserted => Rows.Count(r => r.Operation == "CREATE");
    public int Updated => Rows.Count(r => r.Operation == "UPDATE");
    public bool CanImport => TotalRows > 0 && ErrorRows == 0;
    public string Fingerprint { get; set; } = "";
}

public class ApprovalActionRequest
{
    public bool Approve { get; set; }
    public string? Action { get; set; } // APPROVE | REJECT | RETURN
    public string? Reason { get; set; }
    public int? ExpectedVersion { get; set; }
    public int? ExpectedStepOrder { get; set; }
}

public class ApprovalLifecycleRequest
{
    public string? Reason { get; set; }
    public int? ExpectedVersion { get; set; }
}

public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class ValidateTableRequest
{
    public string TableId { get; set; } = string.Empty;
}

public class AppRuntimeTreeDto
{
    public SysAppDto App { get; set; } = new();
    public List<ModuleWithTablesDto> Modules { get; set; } = new();
}

public class ModuleWithTablesDto
{
    public SysModuleDto Module { get; set; } = new();
    public List<TableWithColumnsDto> Tables { get; set; } = new();
}

public class TableWithColumnsDto
{
    public SysTableDto Table { get; set; } = new();
    public List<SysColumnDto> Columns { get; set; } = new();
}

public class LookupSummaryFieldDto
{
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class LookupOptionDto
{
    public string RecordId { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? SubLabel { get; set; }
    public string? SearchText { get; set; }
    public Dictionary<string, object?>? Data { get; set; }
    public List<LookupSummaryFieldDto> SummaryFields { get; set; } = new();
}

public class RelatedTableGroupDto
{
    public string AppId { get; set; } = string.Empty;
    public string AppCode { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string AppColor { get; set; } = "#1a73e8";
    public string ModuleId { get; set; } = string.Empty;
    public string TableId { get; set; } = string.Empty;
    public string TableDisplayName { get; set; } = string.Empty;
    public string ViaColumnCode { get; set; } = string.Empty;
    public string ViaColumnName { get; set; } = string.Empty;
    public string RefValueField { get; set; } = string.Empty;
    public string TargetMatchValue { get; set; } = string.Empty;
    public bool CanCreate { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
    public List<SysColumnDto> Columns { get; set; } = new();
    public List<AppRecordDto> Records { get; set; } = new();
}

public class CreateAppFromExcelSheet
{
    public string SheetName { get; set; } = string.Empty;
    public List<Dictionary<string, object?>> Rows { get; set; } = new();
}

public class CreateAppFromExcelRequest
{
    public string AppCode { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? Icon { get; set; }
    public string? Color { get; set; }
    public string? BgColor { get; set; }
    public List<CreateAppFromExcelSheet> Sheets { get; set; } = new();
}

public class CreateAppFromTemplateRequest
{
    public string TemplateKey { get; set; } = string.Empty;
    public string? CustomCode { get; set; }
    public string? CustomName { get; set; }
    public bool IncludeSampleData { get; set; } = true;
}

public class AppTemplateSummaryDto
{
    public string TemplateKey { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Icon { get; set; } = "Apps";
    public string Color { get; set; } = "#1a73e8";
    public string BgColor { get; set; } = "#e8f0fe";
    public string Version { get; set; } = "2.0.0";
    public List<string> Highlights { get; set; } = new();
    public List<string> Features { get; set; } = new();
    public int ModuleCount { get; set; }
    public int TableCount { get; set; }
    public int SampleRecordCount { get; set; }
    public int WidgetCount { get; set; }
    public int FormRuleCount { get; set; }
    public int WorkflowCount { get; set; }
    public int AutomationCount { get; set; }
    public int PrintTemplateCount { get; set; }
    public int SavedViewCount { get; set; }
    public int PluginBindingCount { get; set; }
}

public class AppAutomationDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string SourceAppId { get; set; } = string.Empty;
    public string SourceTableId { get; set; } = string.Empty;
    public string TriggerEvent { get; set; } = "RECORD_CREATED";
    public string? ConditionField { get; set; }
    public string ConditionOperator { get; set; } = "EQUALS";
    public string? ConditionValue { get; set; }
    public string? TargetAppId { get; set; }
    public string? TargetModuleId { get; set; }
    public string? TargetTableId { get; set; }
    public string ActionType { get; set; } = "CREATE_RECORD";
    public string? MatchSourceField { get; set; }
    public string? MatchTargetField { get; set; }
    public string? WebhookUrl { get; set; }
    public string? WebhookSecret { get; set; }
    public string? NotifyRecipient { get; set; }
    public string? NotifyTemplate { get; set; }
    public Dictionary<string, string> FieldMappings { get; set; } = new();
    public List<AutomationStepDto> ChainSteps { get; set; } = new();
    public bool IsActive { get; set; } = true;
    public int RunCount { get; set; }
    public DateTime? LastRunAt { get; set; }
}

public class UpsertAutomationRequest
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string SourceAppId { get; set; } = string.Empty;
    public string SourceTableId { get; set; } = string.Empty;
    public string TriggerEvent { get; set; } = "RECORD_CREATED";
    public string? ConditionField { get; set; }
    public string ConditionOperator { get; set; } = "EQUALS";
    public string? ConditionValue { get; set; }
    public string? TargetAppId { get; set; }
    public string? TargetModuleId { get; set; }
    public string? TargetTableId { get; set; }
    public string ActionType { get; set; } = "CREATE_RECORD";
    public string? MatchSourceField { get; set; }
    public string? MatchTargetField { get; set; }
    public string? WebhookUrl { get; set; }
    public string? WebhookSecret { get; set; }
    public string? NotifyRecipient { get; set; }
    public string? NotifyTemplate { get; set; }
    public Dictionary<string, string> FieldMappings { get; set; } = new();
    public List<AutomationStepDto> ChainSteps { get; set; } = new();
    public bool IsActive { get; set; } = true;
}

public class AppAutomationLogDto
{
    public string Id { get; set; } = string.Empty;
    public string AutomationId { get; set; } = string.Empty;
    public string? AutomationName { get; set; }
    public string? SourceRecordId { get; set; }
    public string? TargetRecordId { get; set; }
    public string Status { get; set; } = "SUCCESS";
    public string? Message { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class PermissionCatalogItemDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Scope { get; set; } = "APP"; // SYSTEM | APP
}

public class PermissionCatalogDto
{
    public List<PermissionCatalogItemDto> SystemPermissions { get; set; } = new();
    public List<PermissionCatalogItemDto> AppPermissions { get; set; } = new();
}

public class RoleAppPermissionDto
{
    public string AppId { get; set; } = string.Empty;
    public string AppCode { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string? AppColor { get; set; }
    public List<string> Permissions { get; set; } = new();
    public string RowScope { get; set; } = "INHERIT"; // INHERIT | ALL | OWNER_ONLY
}

public class SysRoleDto
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Color { get; set; } = "#1a73e8";
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public bool AllAppsAccess { get; set; }
    public List<string> DefaultAppPermissions { get; set; } = new();
    public List<string> SystemPermissions { get; set; } = new();
    public List<RoleAppPermissionDto> AppPermissions { get; set; } = new();
    public int UserCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class RoleAppPermissionRequest
{
    public string AppId { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = new();
    public string RowScope { get; set; } = "INHERIT";
}

public class UpsertRoleRequest
{
    public string? Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Color { get; set; } = "#1a73e8";
    public bool IsActive { get; set; } = true;
    public bool AllAppsAccess { get; set; }
    public List<string> DefaultAppPermissions { get; set; } = new();
    public List<string> SystemPermissions { get; set; } = new();
    public List<RoleAppPermissionRequest> AppPermissions { get; set; } = new();
}

public class AppRoleMatrixItemDto
{
    public string RoleId { get; set; } = string.Empty;
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string RoleColor { get; set; } = "#1a73e8";
    public bool IsSystem { get; set; }
    public bool AllAppsAccess { get; set; }
    public bool HasAccess { get; set; }
    public List<string> Permissions { get; set; } = new();
    public string RowScope { get; set; } = "INHERIT";
}

public class AppRoleMatrixUpdateItem
{
    public string RoleId { get; set; } = string.Empty;
    public bool HasAccess { get; set; }
    public List<string> Permissions { get; set; } = new();
    public string RowScope { get; set; } = "INHERIT";
}

public class UpdateAppRoleMatrixRequest
{
    public List<AppRoleMatrixUpdateItem> Assignments { get; set; } = new();
}

public class EffectiveTablePermissionDto
{
    public bool CanView { get; set; } = true;
    public bool CanCreate { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
    public bool CanExport { get; set; }
    public bool CanApprove { get; set; }
    public string RowScope { get; set; } = "INHERIT"; // INHERIT | ALL | OWNER_ONLY
}

public class RoleTablePermissionItemDto
{
    public string RoleId { get; set; } = string.Empty;
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string RoleColor { get; set; } = "#1a73e8";
    public bool IsSystem { get; set; }
    public bool AllAppsAccess { get; set; }
    public string AppId { get; set; } = string.Empty;
    public string TableId { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string TableDisplayName { get; set; } = string.Empty;
    public bool HasExplicitRule { get; set; }
    public bool CanView { get; set; } = true;
    public bool CanCreate { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
    public bool CanExport { get; set; }
    public bool CanApprove { get; set; }
    public string RowScope { get; set; } = "INHERIT";
}

public class UpdateRoleTableRuleItem
{
    public string RoleId { get; set; } = string.Empty;
    public string TableId { get; set; } = string.Empty;
    public bool UseAppDefault { get; set; }
    public bool CanView { get; set; } = true;
    public bool CanCreate { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
    public bool CanExport { get; set; }
    public bool CanApprove { get; set; }
    public string RowScope { get; set; } = "INHERIT";
}

public class UpdateAppTableRoleMatrixRequest
{
    public List<UpdateRoleTableRuleItem> Rules { get; set; } = new();
}

public class SysUserDto
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? EmployeeCode { get; set; }
    public string Department { get; set; } = "Toàn công ty";
    public string? OrgUnitId { get; set; }
    public string RoleCode { get; set; } = "STAFF";
    public List<string> RoleCodes { get; set; } = new();
    public List<string> RoleNames { get; set; } = new();
    public bool IsAdmin { get; set; }
    public List<string> AllowedApps { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
    public List<string> SystemPermissions { get; set; } = new();
    public Dictionary<string, List<string>> AppPermissions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> AppRowScopes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, EffectiveTablePermissionDto> TablePermissions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? SsoProvider { get; set; }
    public string? SsoSubject { get; set; }
    public bool IsActive { get; set; } = true;
}

public class SsoProviderPublicDto
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string ProviderType { get; set; } = "CUSTOM_OIDC";
    public string DisplayName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool IsConfigured { get; set; }
    public string ButtonColor { get; set; } = "#1a73e8";
    public int OrderIndex { get; set; } = 1;
}

public class SsoProviderAdminDto : SsoProviderPublicDto
{
    public string? ClientId { get; set; }
    public bool HasClientSecret { get; set; }
    public string? TenantId { get; set; }
    public string? IssuerUrl { get; set; }
    public string? AuthorizationUrl { get; set; }
    public string? TokenUrl { get; set; }
    public string? UserinfoUrl { get; set; }
    public string Scopes { get; set; } = "openid email profile";
    public List<string> AllowedDomains { get; set; } = new();
    public bool AutoProvision { get; set; } = true;
    public string DefaultRoleCode { get; set; } = "STAFF";
}

public class UpsertSsoProviderRequest
{
    public string? Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string ProviderType { get; set; } = "CUSTOM_OIDC";
    public string DisplayName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? TenantId { get; set; }
    public string? IssuerUrl { get; set; }
    public string? AuthorizationUrl { get; set; }
    public string? TokenUrl { get; set; }
    public string? UserinfoUrl { get; set; }
    public string Scopes { get; set; } = "openid email profile";
    public List<string>? AllowedDomains { get; set; }
    public bool AutoProvision { get; set; } = true;
    public string DefaultRoleCode { get; set; } = "STAFF";
    public string? ButtonColor { get; set; }
    public int OrderIndex { get; set; } = 1;
}

public class OidcDiscoveryRequest
{
    public string IssuerUrl { get; set; } = string.Empty;
}

public class OidcDiscoveryResultDto
{
    public string Issuer { get; set; } = string.Empty;
    public string AuthorizationUrl { get; set; } = string.Empty;
    public string TokenUrl { get; set; } = string.Empty;
    public string? UserinfoUrl { get; set; }
    public string? JwksUri { get; set; }
}

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class UpsertUserRequest
{
    public string? Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? EmployeeCode { get; set; }
    public string Department { get; set; } = "Toàn công ty";
    public string? OrgUnitId { get; set; }
    public string RoleCode { get; set; } = "STAFF";
    public List<string>? RoleCodes { get; set; }
    public string? Password { get; set; }
    public List<string> AllowedApps { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
    public bool IsActive { get; set; } = true;
}

public class AppAuditLogDto
{
    public string Id { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string? AppCode { get; set; }
    public string TableId { get; set; } = string.Empty;
    public string RecordId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Actor { get; set; } = "system";
    public Dictionary<string, object?>? OldData { get; set; }
    public Dictionary<string, object?>? NewData { get; set; }
    public string? Summary { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CategoryDistributionItem
{
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Color { get; set; } = "#1a73e8";
    public int Count { get; set; }
}

public class CategoryGroupStatsDto
{
    public string ColumnCode { get; set; } = string.Empty;
    public string ColumnName { get; set; } = string.Empty;
    public List<CategoryDistributionItem> Items { get; set; } = new();
}

public class NumericFieldStatsDto
{
    public string ColumnCode { get; set; } = string.Empty;
    public string ColumnName { get; set; } = string.Empty;
    public string DataType { get; set; } = "NUMBER";
    public decimal Sum { get; set; }
    public decimal Avg { get; set; }
    public decimal Min { get; set; }
    public decimal Max { get; set; }
}

public class TableAnalyticsDto
{
    public string TableId { get; set; } = string.Empty;
    public int TotalRecords { get; set; }
    public int ValidRecords { get; set; }
    public int ErrorRecords { get; set; }
    public Dictionary<string, int> ApprovalBreakdown { get; set; } = new();
    public List<CategoryGroupStatsDto> Distributions { get; set; } = new();
    public List<NumericFieldStatsDto> NumericSummaries { get; set; } = new();
}

public class SysSavedViewDto
{
    public string Id { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string TableId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public JsonElement Filters { get; set; }
    public string? SortField { get; set; }
    public string SortDir { get; set; } = "DESC";
    public bool IsShared { get; set; } = true;
    public string CreatedBy { get; set; } = "admin";
}

public class UpsertSavedViewRequest
{
    public string? Id { get; set; }
    public string AppId { get; set; } = string.Empty;
    public string TableId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public JsonElement? Filters { get; set; }
    public string? SortField { get; set; }
    public string SortDir { get; set; } = "DESC";
    public bool IsShared { get; set; } = true;
}

public class AppWidgetDto
{
    public string Id { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string? AppCode { get; set; }
    public string TableId { get; set; } = string.Empty;
    public string? TableDisplayName { get; set; }
    public string Title { get; set; } = string.Empty;
    public string WidgetType { get; set; } = "KPI_COUNT";
    public string? MetricField { get; set; }
    public string? GroupField { get; set; }
    public string? FilterField { get; set; }
    public string? FilterValue { get; set; }
    public int ColSpan { get; set; } = 1;
    public string Color { get; set; } = "#1a73e8";
    public int OrderIndex { get; set; } = 1;
    public bool IsVisible { get; set; } = true;

    // Computed runtime payload
    public decimal NumericValue { get; set; }
    public string FormattedValue { get; set; } = "0";
    public string? SubLabel { get; set; }
    public List<CategoryDistributionItem> ChartItems { get; set; } = new();
    public List<AppRecordDto> RecentRows { get; set; } = new();
}

public class UpsertWidgetRequest
{
    public string? Id { get; set; }
    public string AppId { get; set; } = string.Empty;
    public string TableId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string WidgetType { get; set; } = "KPI_COUNT";
    public string? MetricField { get; set; }
    public string? GroupField { get; set; }
    public string? FilterField { get; set; }
    public string? FilterValue { get; set; }
    public int ColSpan { get; set; } = 1;
    public string Color { get; set; } = "#1a73e8";
    public int OrderIndex { get; set; } = 1;
    public bool IsVisible { get; set; } = true;
}

public class WidgetLayoutOrderItem
{
    public string Id { get; set; } = string.Empty;
    public int OrderIndex { get; set; }
    public int ColSpan { get; set; }
    public bool IsVisible { get; set; } = true;
}

public class SysNotificationDto
{
    public string Id { get; set; } = string.Empty;
    public string RecipientUsername { get; set; } = "*";
    public string Title { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? AppCode { get; set; }
    public string? RecordId { get; set; }
    public string Category { get; set; } = "SYSTEM";
    public string? LinkUrl { get; set; }
    public string? SenderUsername { get; set; }
    public string Priority { get; set; } = "NORMAL";
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class BroadcastNotificationRequest
{
    public string RecipientUsername { get; set; } = "*";
    public string Title { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string Category { get; set; } = "SYSTEM";
    public string Priority { get; set; } = "NORMAL";
    public string? LinkUrl { get; set; }
}

public class UserSettingsDto
{
    public string UserId { get; set; } = string.Empty;
    public string ThemeMode { get; set; } = "light";
    public string AccentColor { get; set; } = "#1a73e8";
    public bool CompactMode { get; set; }
    public string DensityMode { get; set; } = "comfortable";
    public List<string> PinnedApps { get; set; } = new();
    public string Language { get; set; } = "vi";
    public bool NotifyInApp { get; set; } = true;
    public bool NotifySound { get; set; } = true;
    public bool NotifyDesktop { get; set; }
    public bool NotifyChat { get; set; } = true;
    public bool NotifyTaskAssigned { get; set; } = true;
    public bool NotifyApproval { get; set; } = true;
    public bool NotifyMentions { get; set; } = true;
    public string EmailDigest { get; set; } = "INSTANT";
    public bool ChatEnterToSend { get; set; } = true;
    public bool NotifyEmail { get; set; }
    public bool NotifyTelegram { get; set; }
    public string? TelegramChatId { get; set; }
    public bool NotifyZalo { get; set; }
    public string? ZaloUserId { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class UpdateUserSettingsRequest
{
    public string ThemeMode { get; set; } = "light";
    public string AccentColor { get; set; } = "#1a73e8";
    public bool CompactMode { get; set; }
    public string? DensityMode { get; set; }
    public List<string>? PinnedApps { get; set; }
    public string Language { get; set; } = "vi";
    public bool NotifyInApp { get; set; } = true;
    public bool NotifySound { get; set; } = true;
    public bool NotifyDesktop { get; set; }
    public bool NotifyChat { get; set; } = true;
    public bool NotifyTaskAssigned { get; set; } = true;
    public bool NotifyApproval { get; set; } = true;
    public bool NotifyMentions { get; set; } = true;
    public string EmailDigest { get; set; } = "INSTANT";
    public bool ChatEnterToSend { get; set; } = true;
    public bool NotifyEmail { get; set; }
    public bool NotifyTelegram { get; set; }
    public string? TelegramChatId { get; set; }
    public bool NotifyZalo { get; set; }
    public string? ZaloUserId { get; set; }
}

public class NotificationChannelDto
{
    public string Id { get; set; } = string.Empty;
    public string OwnerScope { get; set; } = "SYSTEM";
    public string? OwnerUserId { get; set; }
    public string ChannelType { get; set; } = "TELEGRAM";
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public List<string> EventCategories { get; set; } = new() { "ALL" };
    public Dictionary<string, object?> Config { get; set; } = new();
    public bool HasSecret { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class UpsertNotificationChannelRequest
{
    public string? Id { get; set; }
    public string OwnerScope { get; set; } = "SYSTEM";
    public string ChannelType { get; set; } = "TELEGRAM";
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public List<string>? EventCategories { get; set; }
    public Dictionary<string, object?> Config { get; set; } = new();
}

public class NotificationDeliveryLogDto
{
    public string Id { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public string ChannelType { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = "SUCCESS";
    public string? Detail { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UpdateProfileRequest
{
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Department { get; set; } = "Toàn công ty";
}

public class SystemSettingsDto
{
    public string CompanyName { get; set; } = "SmartApps Enterprise";
    public string WorkspaceTitle { get; set; } = "SmartApps Enterprise — Không gian làm việc Đa ứng dụng";
    public string BrandColor { get; set; } = "#1a73e8";
    public string LogoUrl { get; set; } = string.Empty;
    public string BrandIcon { get; set; } = string.Empty;
    public string FaviconUrl { get; set; } = string.Empty;
    public string AnnouncementBanner { get; set; } = string.Empty;
    public bool AllowChatFileUpload { get; set; } = true;
    public bool AllowTaskFromChat { get; set; } = true;
    public string DefaultTaskAppCode { get; set; } = "OPS";
    public int SessionTimeoutDays { get; set; } = 7;
}

public class ChatUserSummaryDto
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string RoleCode { get; set; } = "STAFF";
}

public class ChecklistItemDto
{
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public bool Done { get; set; }
}

public class AppTaskDto
{
    public string Id { get; set; } = string.Empty;
    public string TaskCode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = "TODO";
    public string Priority { get; set; } = "MEDIUM";
    public string AssigneeUsername { get; set; } = string.Empty;
    public string? AssigneeFullName { get; set; }
    public string AssignerUsername { get; set; } = string.Empty;
    public string? AssignerFullName { get; set; }
    public List<string> Watchers { get; set; } = new();
    public string? DueDate { get; set; }
    public int Progress { get; set; }
    public List<ChecklistItemDto> Checklist { get; set; } = new();
    public string? ChannelId { get; set; }
    public string? ChannelName { get; set; }
    public string? MessageId { get; set; }
    public string? LinkedAppCode { get; set; }
    public string? LinkedRecordId { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateTaskRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Priority { get; set; } = "MEDIUM";
    public string AssigneeUsername { get; set; } = string.Empty;
    public List<string>? Watchers { get; set; }
    public string? DueDate { get; set; }
    public List<ChecklistItemDto>? Checklist { get; set; }
    public string? ChannelId { get; set; }
    public string? LinkedAppCode { get; set; }
    public string? LinkedRecordId { get; set; }
    public bool PostToChat { get; set; } = true;
}

public class UpdateTaskRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Status { get; set; }
    public string? Priority { get; set; }
    public string? AssigneeUsername { get; set; }
    public string? DueDate { get; set; }
    public int? Progress { get; set; }
    public List<ChecklistItemDto>? Checklist { get; set; }
}

public class ChatAttachmentDto
{
    public string Id { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public string Url { get; set; } = string.Empty;
}

public class ChatChannelDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ChannelType { get; set; } = "GROUP";
    public string AvatarColor { get; set; } = "#1a73e8";
    public string? LinkedAppCode { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime LastMessageAt { get; set; }
    public string? LastMessagePreview { get; set; }
    public int MemberCount { get; set; }
    public int UnreadCount { get; set; }
    public bool IsMuted { get; set; }
    public List<ChatUserSummaryDto> Members { get; set; } = new();
}

public class CreateChannelRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ChannelType { get; set; } = "GROUP";
    public string? AvatarColor { get; set; }
    public string? LinkedAppCode { get; set; }
    public List<string> MemberUsernames { get; set; } = new();
}

public class CreateDirectChannelRequest
{
    public string TargetUsername { get; set; } = string.Empty;
}

public class ChatMessageDto
{
    public string Id { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public string SenderUsername { get; set; } = string.Empty;
    public string? SenderFullName { get; set; }
    public string? SenderRoleCode { get; set; }
    public string MessageType { get; set; } = "TEXT";
    public string Content { get; set; } = string.Empty;
    public string? ReplyToId { get; set; }
    public string? ReplyToPreview { get; set; }
    public string? ReplyToSender { get; set; }
    public string? TaskId { get; set; }
    public AppTaskDto? Task { get; set; }
    public List<ChatAttachmentDto> Attachments { get; set; } = new();
    public List<string> Mentions { get; set; } = new();
    public Dictionary<string, List<string>> Reactions { get; set; } = new();
    public bool IsPinned { get; set; }
    public bool IsEdited { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SendChatMessageRequest
{
    public string Content { get; set; } = string.Empty;
    public string MessageType { get; set; } = "TEXT";
    public string? ReplyToId { get; set; }
    public List<ChatAttachmentDto>? Attachments { get; set; }
}

public class ConvertMessageToTaskRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string AssigneeUsername { get; set; } = string.Empty;
    public string Priority { get; set; } = "MEDIUM";
    public string? DueDate { get; set; }
}

public class ToggleReactionRequest
{
    public string Emoji { get; set; } = "👍";
}

public class AppBlueprintDto
{
    public string BlueprintVersion { get; set; } = "1.0";
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
    public SysAppDto App { get; set; } = new();
    public List<ModuleWithTablesDto> Modules { get; set; } = new();
    public List<AppWidgetDto> Widgets { get; set; } = new();
    public List<AppAutomationDto> Automations { get; set; } = new();
}

public class RecordCommentDto
{
    public string Id { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string TableId { get; set; } = string.Empty;
    public string RecordId { get; set; } = string.Empty;
    public string Author { get; set; } = "system";
    public string Content { get; set; } = string.Empty;
    public List<string> Mentions { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public class AddCommentRequest
{
    public string Content { get; set; } = string.Empty;
}

public class PendingApprovalItemDto
{
    public string RecordId { get; set; } = string.Empty;
    public string? RecordKey { get; set; }
    public string AppId { get; set; } = string.Empty;
    public string AppCode { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string AppColor { get; set; } = "#1a73e8";
    public string TableId { get; set; } = string.Empty;
    public string TableDisplayName { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = "system";
    public DateTime CreatedAt { get; set; }
    public string SummaryText { get; set; } = string.Empty;
    public int CurrentStepOrder { get; set; }
    public string? CurrentStepName { get; set; }
    public int TotalSteps { get; set; } = 1;
}

public class SysApiKeyDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string? AppCode { get; set; }
    public string? TableId { get; set; }
    public List<string> Scopes { get; set; } = new();
    public bool IsActive { get; set; } = true;
    public DateTime? LastUsedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? RotatedAt { get; set; }
    public string CreatedBy { get; set; } = "admin";
    public DateTime CreatedAt { get; set; }
}

public class CreateApiKeyRequest
{
    public string Name { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string? TableId { get; set; }
    public List<string> Scopes { get; set; } = new() { "records:read", "records:write" };
    public int? ExpiresInDays { get; set; }
}

public class SysPluginDto
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "CUSTOM";
    public string Version { get; set; } = "1.0.0";
    public string? Description { get; set; }
    public Dictionary<string, object?> Manifest { get; set; } = new();
    public Dictionary<string, object?> Config { get; set; } = new();
    public bool HasSecret { get; set; }
    public string? AppId { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime? LastTestedAt { get; set; }
    public string? LastTestStatus { get; set; }
    public string? LastTestMessage { get; set; }
    public string CreatedBy { get; set; } = "admin";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class UpsertPluginRequest
{
    public string? Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "CUSTOM";
    public string Version { get; set; } = "1.0.0";
    public string? Description { get; set; }
    public Dictionary<string, object?>? Manifest { get; set; }
    public Dictionary<string, object?>? Config { get; set; }
    public string? SecretValue { get; set; }
    public string? AppId { get; set; }
    public bool IsEnabled { get; set; }
}

public class AppPluginBindingDto
{
    public string Id { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string PluginCode { get; set; } = string.Empty;
    public string PluginName { get; set; } = string.Empty;
    public string Category { get; set; } = "CUSTOM";
    public string? Description { get; set; }
    public bool IsSystemEnabled { get; set; }
    public bool HasSecret { get; set; }
    public string? LastTestStatus { get; set; }
    public bool IsAppEnabled { get; set; }
    public string? TableId { get; set; }
    public Dictionary<string, string> FieldMapping { get; set; } = new();
    public DateTime? UpdatedAt { get; set; }
}

public class UpsertAppPluginBindingRequest
{
    public bool IsEnabled { get; set; }
    public string? TableId { get; set; }
    public Dictionary<string, string>? FieldMapping { get; set; }
}

public class PluginCatalogItemDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string Description { get; set; } = string.Empty;
    public List<string> RequiredConfigKeys { get; set; } = new();
    public List<string> SupportedOperations { get; set; } = new();
    public bool SupportsSandbox { get; set; } = true;
}

public class IntegrationLogDto
{
    public string Id { get; set; } = string.Empty;
    public string Direction { get; set; } = "INBOUND";
    public string SourceType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string? AppId { get; set; }
    public string? TableId { get; set; }
    public int StatusCode { get; set; }
    public bool Success { get; set; }
    public string? Summary { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ExternalDeltaRecordDto
{
    public string Id { get; set; } = string.Empty;
    public string? RecordKey { get; set; }
    public Dictionary<string, object?> Data { get; set; } = new();
    public string Status { get; set; } = "ACTIVE";
    public string ApprovalStatus { get; set; } = "DRAFT";
    public int Version { get; set; } = 1;
    public bool IsDeleted { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class SysPrintTemplateDto
{
    public string Id { get; set; } = string.Empty;
    public string TableId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PaperSize { get; set; } = "A4";
    public string Orientation { get; set; } = "portrait";
    public string HtmlTemplate { get; set; } = string.Empty;
    public string? CssStyles { get; set; }
    public bool IsDefault { get; set; }
    public bool WatermarkDraft { get; set; } = true;
    public bool RequireApproved { get; set; }
    public string CreatedBy { get; set; } = "admin";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class UpsertPrintTemplateRequest
{
    public string? Id { get; set; }
    public string TableId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PaperSize { get; set; } = "A4";
    public string Orientation { get; set; } = "portrait";
    public string HtmlTemplate { get; set; } = string.Empty;
    public string? CssStyles { get; set; }
    public bool IsDefault { get; set; }
    public bool WatermarkDraft { get; set; } = true;
    public bool RequireApproved { get; set; }
}

public class SysOrgUnitDto
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ParentId { get; set; }
    public string? ManagerUsername { get; set; }
    public int OrderIndex { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class UpsertOrgUnitRequest
{
    public string? Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ParentId { get; set; }
    public string? ManagerUsername { get; set; }
    public int OrderIndex { get; set; } = 1;
    public bool IsActive { get; set; } = true;
}

public class AiGenerateAppRequest
{
    public string Prompt { get; set; } = string.Empty;
    public string? AppCode { get; set; }
    public string? Department { get; set; }
}

public class BlueprintUpgradeResultDto
{
    public string AppId { get; set; } = string.Empty;
    public string AppCode { get; set; } = string.Empty;
    public int ModulesUpserted { get; set; }
    public int TablesUpserted { get; set; }
    public int ColumnsUpserted { get; set; }
    public int WidgetsAdded { get; set; }
}

public class WorkflowStepDto
{
    public int StepOrder { get; set; } = 1;
    public string Name { get; set; } = string.Empty;
    public List<string> ApproverRoles { get; set; } = new();
    public List<string> ApproverUsers { get; set; } = new();
    public int SlaHours { get; set; } = 24;
    public bool RequireCommentOnReject { get; set; }
    public string ApprovalMode { get; set; } = "ANY"; // ANY | ALL | QUORUM
    public int QuorumCount { get; set; } = 1;
    public bool AllowReturnToPrevious { get; set; }
    public bool UseOrgManager { get; set; }
    public string? ConditionField { get; set; }
    public string? ConditionOperator { get; set; }
    public string? ConditionValue { get; set; }
}

public class AutomationStepDto
{
    public string Id { get; set; } = string.Empty;
    public string ActionType { get; set; } = "SEND_NOTIFICATION";
    public string? TargetAppId { get; set; }
    public string? TargetModuleId { get; set; }
    public string? TargetTableId { get; set; }
    public string? MatchSourceField { get; set; }
    public string? MatchTargetField { get; set; }
    public string? WebhookUrl { get; set; }
    public string? WebhookSecret { get; set; }
    public string? NotifyRecipient { get; set; }
    public string? NotifyTemplate { get; set; }
    public Dictionary<string, string> FieldMappings { get; set; } = new();
    public string? ConditionField { get; set; }
    public string? ConditionOperator { get; set; }
    public string? ConditionValue { get; set; }
}

public class WorkflowStepApprovalVoteDto
{
    public int StepOrder { get; set; }
    public string Actor { get; set; } = string.Empty;
    public string? ActorRole { get; set; }
    public string? DelegatedFrom { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
}

public class SysWorkflowDelegationDto
{
    public string Id { get; set; } = string.Empty;
    public string DelegatorUsername { get; set; } = string.Empty;
    public string DelegateUsername { get; set; } = string.Empty;
    public string? AppId { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string? Reason { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

public class UpsertWorkflowDelegationRequest
{
    public string? Id { get; set; }
    public string? DelegatorUsername { get; set; }
    public string DelegateUsername { get; set; } = string.Empty;
    public string? AppId { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string? Reason { get; set; }
    public bool IsActive { get; set; } = true;
}

public class BusinessHolidayDto
{
    public string Id { get; set; } = string.Empty;
    public string HolidayDate { get; set; } = string.Empty; // yyyy-MM-dd
    public string Name { get; set; } = string.Empty;
    public bool IsRecurring { get; set; }
}

public class UpsertBusinessHolidayRequest
{
    public string? Id { get; set; }
    public string HolidayDate { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsRecurring { get; set; }
}

public class RestoreRecordFieldsRequest
{
    public string AuditId { get; set; } = string.Empty;
    public List<string> Fields { get; set; } = new();
    public int? ExpectedVersion { get; set; }
}

public class GlobalSearchHitDto
{
    public string Kind { get; set; } = string.Empty; // APP | RECORD | CHAT | ATTACHMENT
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Badge { get; set; }
    public DateTime? Timestamp { get; set; }
}

public class GlobalSearchResultDto
{
    public string Query { get; set; } = string.Empty;
    public List<GlobalSearchHitDto> Apps { get; set; } = new();
    public List<GlobalSearchHitDto> Records { get; set; } = new();
    public List<GlobalSearchHitDto> Chats { get; set; } = new();
    public List<GlobalSearchHitDto> Attachments { get; set; } = new();
}

public class AppWorkflowDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string AppId { get; set; } = string.Empty;
    public string? AppCode { get; set; }
    public string? AppName { get; set; }
    public string TableId { get; set; } = string.Empty;
    public string? TableDisplayName { get; set; }
    public string TriggerType { get; set; } = "MANUAL_SUBMIT";
    public string? ConditionField { get; set; }
    public string ConditionOperator { get; set; } = "EQUALS";
    public string? ConditionValue { get; set; }
    public List<WorkflowStepDto> Steps { get; set; } = new();
    public bool IsActive { get; set; } = true;
    public int PendingCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class UpsertWorkflowRequest
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string AppId { get; set; } = string.Empty;
    public string TableId { get; set; } = string.Empty;
    public string TriggerType { get; set; } = "MANUAL_SUBMIT";
    public string? ConditionField { get; set; }
    public string ConditionOperator { get; set; } = "EQUALS";
    public string? ConditionValue { get; set; }
    public List<WorkflowStepDto> Steps { get; set; } = new();
    public bool IsActive { get; set; } = true;
}

public class AiSystemSettingsDto
{
    public bool IsEnabled { get; set; } = true;
    public string? DefaultProviderId { get; set; } = "ai_gemini";
    public bool FallbackToRuleEngine { get; set; } = true;
    public bool MaskPiiBeforeCloudLlm { get; set; } = true;
    public int MonthlyTokenBudget { get; set; } = 0; // 0 = unlimited
    public bool PreferLocalForSensitive { get; set; } = false;
    public double Temperature { get; set; } = 0.2;
    public int MaxTokens { get; set; } = 2048;
    public int TimeoutSeconds { get; set; } = 45;
    public string SystemPromptPrefix { get; set; } =
        "Bạn là trợ lý AI doanh nghiệp của nền tảng SmartApps. Trả lời chính xác, súc tích, ưu tiên tiếng Việt chuẩn.";
    public List<string> EnabledFeatures { get; set; } = new()
    {
        "AI_APP_COPILOT",
        "AI_RECORD_ASSISTANT",
        "AI_CHAT_COPILOT",
        "AI_WORKFLOW_TRIAGE",
        "AI_DASHBOARD_INSIGHTS",
        "AI_IMPORT_MAPPER",
        "AI_DOC_OCR"
    };
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class AiProviderDto
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string ProviderType { get; set; } = "OPENAI"; // GEMINI | OPENAI | ANTHROPIC | OPENAI_COMPATIBLE
    public string DisplayName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool IsDefault { get; set; }
    public bool HasApiKey { get; set; }
    public string? ApiKeyPreview { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public List<string> SuggestedModels { get; set; } = new();
    public double? Temperature { get; set; }
    public int? MaxTokens { get; set; }
    public int TimeoutSeconds { get; set; } = 45;
    public string? OrganizationId { get; set; }
    public int OrderIndex { get; set; } = 10;
    public bool IsConfigured { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class UpsertAiProviderRequest
{
    public string? Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string ProviderType { get; set; } = "OPENAI";
    public string DisplayName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool IsDefault { get; set; }
    public string? ApiKey { get; set; }
    public bool ClearApiKey { get; set; }
    public string? BaseUrl { get; set; }
    public string Model { get; set; } = string.Empty;
    public double? Temperature { get; set; }
    public int? MaxTokens { get; set; }
    public int TimeoutSeconds { get; set; } = 45;
    public string? OrganizationId { get; set; }
    public int OrderIndex { get; set; } = 10;
}

public class AiTestProviderRequest
{
    public string? ApiKey { get; set; }
    public string? BaseUrl { get; set; }
    public string? Model { get; set; }
    public string? ProviderType { get; set; }
    public string? Prompt { get; set; }
}

public class AiTestResultDto
{
    public bool Ok { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public long LatencyMs { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ReplyPreview { get; set; }
}

public class AiUsageLogDto
{
    public string Id { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string FeatureCode { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Username { get; set; } = "system";
    public string Status { get; set; } = "SUCCESS";
    public long LatencyMs { get; set; }
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public string? Message { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AiCapabilityDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string Status { get; set; } = "PROPOSED"; // INTEGRATED | PROPOSED
    public string Description { get; set; } = string.Empty;
    public string Benefit { get; set; } = string.Empty;
    public string TargetRoute { get; set; } = string.Empty;
    public string RecommendedTier { get; set; } = "Fast / Balanced";
}

public class AiStatusDto
{
    public bool IsEnabled { get; set; }
    public bool IsConfigured { get; set; }
    public string? ActiveProviderId { get; set; }
    public string? ActiveProviderName { get; set; }
    public string? ActiveProviderType { get; set; }
    public string? ActiveModel { get; set; }
    public bool FallbackToRuleEngine { get; set; } = true;
    public bool MaskPiiBeforeCloudLlm { get; set; } = true;
    public int MonthlyTokenBudget { get; set; }
    public long MonthlyTokensUsed { get; set; }
    public bool PreferLocalForSensitive { get; set; }
    public List<string> EnabledFeatures { get; set; } = new();
}

// =========================================================================
// Phase 3: Enterprise AI Agents & Ecosystem DTOs
// =========================================================================

public class AiImportMapRequest
{
    public string TableId { get; set; } = string.Empty;
    public List<string> Headers { get; set; } = new();
    public List<List<string?>> SampleRows { get; set; } = new();
}

public class AiImportMappingSuggestion
{
    public string ExcelHeader { get; set; } = string.Empty;
    public string? ColumnCode { get; set; }
    public string? ColumnName { get; set; }
    public double Confidence { get; set; }
    public string TransformHint { get; set; } = "NONE"; // NONE | DATE_DMY | VND_INT | PHONE_VN | UPPERCASE | CATEGORY_FUZZY
    public string? Reason { get; set; }
}

public class AiImportMapResult
{
    public List<AiImportMappingSuggestion> Mappings { get; set; } = new();
    public string Engine { get; set; } = string.Empty;
}

public class AiDocOcrRequest
{
    public string TableId { get; set; } = string.Empty;
    public string? RawText { get; set; }
    public string? ImageBase64 { get; set; }
    public string? MimeType { get; set; }
    public string? FileName { get; set; }
}

public class AiDocOcrResult
{
    public Dictionary<string, object?> Fields { get; set; } = new();
    public Dictionary<string, List<Dictionary<string, object?>>> SubTables { get; set; } = new();
    public double Confidence { get; set; }
    public List<string> Warnings { get; set; } = new();
    public string Engine { get; set; } = string.Empty;
}

public class AiWorkflowTriageResult
{
    public string RiskLevel { get; set; } = "LOW"; // LOW | MEDIUM | HIGH
    public int RiskScore { get; set; }
    public string Recommendation { get; set; } = "APPROVE_FAST"; // APPROVE_FAST | REVIEW_CAREFULLY | REQUEST_CLARIFICATION
    public string RecommendationLabel { get; set; } = string.Empty;
    public List<string> SummaryBullets { get; set; } = new();
    public List<string> Anomalies { get; set; } = new();
    public int HistoricalSampleSize { get; set; }
    public string Engine { get; set; } = string.Empty;
}

public class AiChatActionItem
{
    public string Title { get; set; } = string.Empty;
    public string? AssigneeUsername { get; set; }
    public string? DueDate { get; set; }
    public string Priority { get; set; } = "MEDIUM";
    public string? SourceMessageId { get; set; }
}

public class AiChatCopilotResult
{
    public string Summary { get; set; } = string.Empty;
    public List<AiChatActionItem> ActionItems { get; set; } = new();
    public int AnalyzedMessages { get; set; }
    public string Engine { get; set; } = string.Empty;
}

public class AiNlFilterCondition
{
    public string Field { get; set; } = string.Empty;
    public string Op { get; set; } = "contains";
    public string Value { get; set; } = string.Empty;
    public string? Value2 { get; set; }
}

public class AiNlFilterSpec
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public string Logic { get; set; } = "AND";
    public List<AiNlFilterCondition> Conditions { get; set; } = new();
    public string AggregateMetric { get; set; } = "COUNT"; // COUNT | SUM | AVG
    public string? MetricField { get; set; }
    public string? GroupByField { get; set; }
    public string? SortField { get; set; }
    public string SortDir { get; set; } = "DESC";
}

public class AiDashboardInsightsRequest
{
    public string TableId { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
}

public class AiDashboardGroupStat
{
    public string Group { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal MetricValue { get; set; }
}

public class AiDashboardInsightsResult
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public AiNlFilterSpec AppliedPlan { get; set; } = new();
    public int MatchingRecords { get; set; }
    public decimal? AggregateValue { get; set; }
    public List<AiDashboardGroupStat> Breakdown { get; set; } = new();
    public List<AppRecordDto> SampleRecords { get; set; } = new();
    public string Engine { get; set; } = string.Empty;
}

public class AppUpgradeRequest
{
    public List<string>? AppIds { get; set; }
}

public class AppUpgradeCandidateDto
{
    public string AppId { get; set; } = string.Empty;
    public string AppCode { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string CurrentVersion { get; set; } = "1.0.0";
    public string TargetVersion { get; set; } = "2.0.0";
    public string? MatchedTemplateCode { get; set; }
    public bool NeedsUpgrade { get; set; }
    public List<string> MissingFeatures { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
}

public class AppUpgradeExecutionResultDto
{
    public string AppId { get; set; } = string.Empty;
    public string AppCode { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string PreviousVersion { get; set; } = "1.0.0";
    public string NewVersion { get; set; } = "2.0.0";
    public string? MatchedTemplateCode { get; set; }
    public int ModulesAdded { get; set; }
    public int TablesAdded { get; set; }
    public int ColumnsUpgraded { get; set; }
    public int FormRulesAttached { get; set; }
    public int WorkflowsAdded { get; set; }
    public int AutomationsAdded { get; set; }
    public int PrintTemplatesAdded { get; set; }
    public int SavedViewsAdded { get; set; }
    public int WidgetsAdded { get; set; }
    public int PluginBindingsAdded { get; set; }
    public int? PublishedReleaseVersion { get; set; }
}

// ─── Online App Store / Marketplace ───────────────────────────────────────────────

public class StoreRegistryDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RegistryUrl { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int AppCount { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UpsertStoreRegistryRequest
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RegistryUrl { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public class StoreAppSummaryDto
{
    public string PackageCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Department { get; set; } = "Doanh nghiệp";
    public string Category { get; set; } = "Kinh doanh & Vận hành";
    public string Version { get; set; } = "2.0.0";
    public string Publisher { get; set; } = "SmartApps Official";
    public string Icon { get; set; } = "Apps";
    public string Color { get; set; } = "#1a73e8";
    public string BgColor { get; set; } = "#e8f0fe";
    public List<string> Tags { get; set; } = new();
    public List<string> Highlights { get; set; } = new();
    public List<string> Features { get; set; } = new();
    public string SourceType { get; set; } = "OFFICIAL"; // OFFICIAL | REMOTE
    public string? RegistryId { get; set; }
    public string? RegistryName { get; set; }
    public string? DownloadUrl { get; set; }
    public string ChecksumSha256 { get; set; } = string.Empty;
    public long PackageSizeBytes { get; set; }
    public int DownloadCount { get; set; }
    public int InstallCount { get; set; }
    public bool IsInstalled { get; set; }
    public string? InstalledAppCode { get; set; }
    public string? InstalledAppId { get; set; }
    public string? InstalledVersion { get; set; }
    public bool HasUpdateAvailable { get; set; }
    public int ModuleCount { get; set; }
    public int TableCount { get; set; }
    public int SampleRecordCount { get; set; }
    public int WidgetCount { get; set; }
    public int FormRuleCount { get; set; }
    public int WorkflowCount { get; set; }
    public int AutomationCount { get; set; }
    public int PrintTemplateCount { get; set; }
    public int SavedViewCount { get; set; }
    public int PluginBindingCount { get; set; }
}

public class StorePackageCategoryDto
{
    public string CategoryCode { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<StorePackageCategoryItemDto> Items { get; set; } = new();
}

public class StorePackageCategoryItemDto
{
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Color { get; set; } = "#1a73e8";
    public int OrderIndex { get; set; }
}

public class StorePackageColumnDto
{
    public string ColumnCode { get; set; } = string.Empty;
    public string ColumnName { get; set; } = string.Empty;
    public string DataType { get; set; } = "STRING";
    public bool IsRequired { get; set; }
    public bool IsIdentityKey { get; set; }
    public bool ShowInTable { get; set; } = true;
    public bool ShowInForm { get; set; } = true;
    public bool ShowInParentForm { get; set; }
    public int Width { get; set; } = 170;
    public string? ValidationRegex { get; set; }
    public string? RefCategoryCode { get; set; }
    public bool AllowQuickCreate { get; set; }
    public string? RefAppId { get; set; }
    public string? RefTableId { get; set; }
    public string? InternalRefTableName { get; set; }
    public string? RefValueField { get; set; }
    public string? RefDisplayField { get; set; }
    public string? LookupFilterField { get; set; }
    public string? LookupSourceField { get; set; }
    public bool IsIndexed { get; set; }
    public string? Formula { get; set; }
    public string? DefaultValue { get; set; }
    public List<string> HiddenForRoles { get; set; } = new();
    public List<string> ReadOnlyForRoles { get; set; } = new();
    public int OrderIndex { get; set; } = 1;
}

public class StorePackageTableDto
{
    public string TableName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PrimaryView { get; set; } = "table";
    public string RowScope { get; set; } = "ALL";
    public bool ApprovalEnabled { get; set; }
    public string ApprovalField { get; set; } = "trang_thai_duyet";
    public List<string> ApproverRoles { get; set; } = new() { "ADMIN", "HR_MANAGER", "ASSET_LEAD" };
    public bool PublicFormEnabled { get; set; }
    public List<FormRuleDto> FormRules { get; set; } = new();
    public List<StorePackageColumnDto> Columns { get; set; } = new();
}

public class StorePackageModuleDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "Layers";
    public string Description { get; set; } = string.Empty;
    public List<StorePackageTableDto> Tables { get; set; } = new();
}

public class StorePackageWidgetDto
{
    public string TableName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string WidgetType { get; set; } = "KPI_COUNT";
    public string? MetricField { get; set; }
    public string? GroupField { get; set; }
    public string? FilterField { get; set; }
    public string? FilterValue { get; set; }
    public int ColSpan { get; set; } = 1;
    public string Color { get; set; } = "#1a73e8";
    public int OrderIndex { get; set; } = 1;
}

public class StorePackageWorkflowDto
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string TriggerType { get; set; } = "MANUAL_SUBMIT";
    public List<WorkflowStepDto> Steps { get; set; } = new();
}

public class StorePackageAutomationDto
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TriggerTableName { get; set; } = string.Empty;
    public string TriggerEvent { get; set; } = "RECORD_CREATED";
    public string? ConditionField { get; set; }
    public string ConditionOperator { get; set; } = "EQUALS";
    public string? ConditionValue { get; set; }
    public string? TargetTableName { get; set; }
    public string ActionType { get; set; } = "SEND_NOTIFICATION";
    public string? MatchSourceField { get; set; }
    public string? MatchTargetField { get; set; }
    public Dictionary<string, string> FieldMappings { get; set; } = new();
    public string? NotifyRecipient { get; set; }
    public string? NotifyTemplate { get; set; }
}

public class StorePackagePrintTemplateDto
{
    public string TableName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PaperSize { get; set; } = "A4";
    public string Orientation { get; set; } = "portrait";
    public bool IsDefault { get; set; }
    public bool WatermarkDraft { get; set; } = true;
    public bool RequireApproved { get; set; }
    public string HtmlTemplate { get; set; } = string.Empty;
    public string? CssStyles { get; set; }
}

public class StorePackageSavedViewDto
{
    public string TableName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsShared { get; set; } = true;
    public string FiltersJson { get; set; } = "{}";
    public string? SortField { get; set; }
    public string SortDir { get; set; } = "DESC";
}

public class StorePackagePluginBindingDto
{
    public string PluginCode { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public Dictionary<string, string> FieldMapping { get; set; } = new();
}

public class StorePackageRecordDto
{
    public string TableName { get; set; } = string.Empty;
    public Dictionary<string, object?> Data { get; set; } = new();
}

public class StoreAppPackageDto
{
    public string PackageFormat { get; set; } = "smartapps-package/2.0";
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
    public string ChecksumSha256 { get; set; } = string.Empty;
    public string PackageCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Department { get; set; } = "Doanh nghiệp";
    public string Category { get; set; } = "Kinh doanh & Vận hành";
    public string Version { get; set; } = "2.0.0";
    public string Publisher { get; set; } = "SmartApps Official";
    public string Icon { get; set; } = "Apps";
    public string Color { get; set; } = "#1a73e8";
    public string BgColor { get; set; } = "#e8f0fe";
    public List<string> Tags { get; set; } = new();
    public List<string> Highlights { get; set; } = new();
    public List<string> Features { get; set; } = new();
    public List<StorePackageCategoryDto> Categories { get; set; } = new();
    public List<StorePackageModuleDto> Modules { get; set; } = new();
    public List<StorePackageWidgetDto> Widgets { get; set; } = new();
    public List<StorePackageWorkflowDto> Workflows { get; set; } = new();
    public List<StorePackageAutomationDto> Automations { get; set; } = new();
    public List<StorePackagePrintTemplateDto> PrintTemplates { get; set; } = new();
    public List<StorePackageSavedViewDto> SavedViews { get; set; } = new();
    public List<StorePackagePluginBindingDto> PluginBindings { get; set; } = new();
    public List<StorePackageRecordDto> SampleRecords { get; set; } = new();
}

public class StoreAppDetailDto
{
    public StoreAppSummaryDto Summary { get; set; } = new();
    public List<StorePackageModuleDto> Modules { get; set; } = new();
    public List<StorePackageWorkflowDto> Workflows { get; set; } = new();
    public List<StorePackageAutomationDto> Automations { get; set; } = new();
    public List<StorePackagePrintTemplateDto> PrintTemplates { get; set; } = new();
    public List<StorePackageSavedViewDto> SavedViews { get; set; } = new();
    public List<StorePackagePluginBindingDto> PluginBindings { get; set; } = new();
}

public class InstallStoreAppRequest
{
    public string? RegistryId { get; set; }
    public string? CustomCode { get; set; }
    public string? CustomName { get; set; }
    public bool IncludeSampleData { get; set; } = true;
}

public class InstallStorePackageUrlRequest
{
    public string PackageUrl { get; set; } = string.Empty;
    public string? CustomCode { get; set; }
    public string? CustomName { get; set; }
    public bool IncludeSampleData { get; set; } = true;
}

public class InstallStorePackageBodyRequest
{
    public JsonElement Package { get; set; }
    public string? CustomCode { get; set; }
    public string? CustomName { get; set; }
    public bool IncludeSampleData { get; set; } = true;
}

public class StoreManifestDto
{
    public string StoreName { get; set; } = "SmartApps Online Store Hub";
    public string StoreVersion { get; set; } = "2.0.0";
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public List<StoreAppSummaryDto> Apps { get; set; } = new();
}

public class StoreInstallHistoryDto
{
    public string Id { get; set; } = string.Empty;
    public string PackageCode { get; set; } = string.Empty;
    public string? RegistryId { get; set; }
    public string? AppId { get; set; }
    public string InstalledVersion { get; set; } = "2.0.0";
    public string? PackageChecksum { get; set; }
    public string ActionType { get; set; } = "INSTALL";
    public string Actor { get; set; } = "admin";
    public DateTime CreatedAt { get; set; }
}

public class StoreSolutionSuiteAppItemDto
{
    public string PackageCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "Apps";
    public string Color { get; set; } = "#1a73e8";
    public bool IsInstalled { get; set; }
    public string? InstalledAppCode { get; set; }
}

public class StoreSolutionSuiteDto
{
    public string SuiteCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Tagline { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TargetAudience { get; set; } = string.Empty;
    public string Icon { get; set; } = "AutoAwesome";
    public string Color { get; set; } = "#1a73e8";
    public string BgColor { get; set; } = "#e8f0fe";
    public List<string> Highlights { get; set; } = new();
    public List<StoreSolutionSuiteAppItemDto> Apps { get; set; } = new();
    public int InstalledCount { get; set; }
    public int TotalCount { get; set; }
}

public class InstallSuiteAppOutcomeDto
{
    public string PackageCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "INSTALLED"; // INSTALLED | ALREADY_INSTALLED | FAILED
    public string? AppId { get; set; }
    public string? AppCode { get; set; }
    public string? Message { get; set; }
}

public class InstallSuiteResultDto
{
    public string SuiteCode { get; set; } = string.Empty;
    public string SuiteName { get; set; } = string.Empty;
    public int InstalledCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
    public List<InstallSuiteAppOutcomeDto> Outcomes { get; set; } = new();
}
