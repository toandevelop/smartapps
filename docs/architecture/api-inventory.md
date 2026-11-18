# Danh mục API từ mã nguồn

Cập nhật từ controller ngày **17/11/2026**, sau triển khai chức năng ưu tiên và form có điều kiện. Xem [quy tắc biểu mẫu](form-rules-implementation.md) cho cấu hình `formRules` qua `POST /api/tables` và lỗi kiểm tra dữ liệu. Xem [đợt 1](priority-features-implementation.md) cho query export và chat cursor. DTO nằm trong `backend-dotnet/Models/PlatformModels.cs`; khi API chạy xem `/openapi/v1.json`.

## Endpoint ngoài controller

| Method | Path | Nguồn |
|---|---|---|
| GET | `/` | `backend-dotnet/Program.cs` |
| GET | `/health` | `backend-dotnet/Program.cs`; trạng thái tĩnh |
| GET | `/openapi/v1.json` | ASP.NET Core OpenAPI |

## AppsController

| Method | Path | Action | Nguồn |
|---|---|---|---|
| GET | `/api/apps` | `ListApps` | [AppsController.cs](../../backend-dotnet/Controllers/AppsController.cs), dòng 22 |
| GET | `/api/apps/templates` | `ListTemplates` | [AppsController.cs](../../backend-dotnet/Controllers/AppsController.cs), dòng 33 |
| GET | `/api/apps/{codeOrId}` | `GetAppRuntime` | [AppsController.cs](../../backend-dotnet/Controllers/AppsController.cs), dòng 42 |
| POST | `/api/apps` | `UpsertApp` | [AppsController.cs](../../backend-dotnet/Controllers/AppsController.cs), dòng 75 |
| POST | `/api/apps/from-excel` | `CreateFromExcel` | [AppsController.cs](../../backend-dotnet/Controllers/AppsController.cs), dòng 90 |
| POST | `/api/apps/from-template` | `CreateFromTemplate` | [AppsController.cs](../../backend-dotnet/Controllers/AppsController.cs), dòng 105 |
| GET | `/api/apps/{codeOrId}/export` | `ExportBlueprint` | [AppsController.cs](../../backend-dotnet/Controllers/AppsController.cs), dòng 127 |
| POST | `/api/apps/import-blueprint` | `ImportBlueprint` | [AppsController.cs](../../backend-dotnet/Controllers/AppsController.cs), dòng 140 |
| POST | `/api/apps/{codeOrId}/upgrade-blueprint` | `UpgradeBlueprint` | [AppsController.cs](../../backend-dotnet/Controllers/AppsController.cs), dòng 152 |
| POST | `/api/apps/ai-generate` | `GenerateFromAiPrompt` | [AppsController.cs](../../backend-dotnet/Controllers/AppsController.cs), dòng 165 |
| DELETE | `/api/apps/{id}` | `DeleteApp` | [AppsController.cs](../../backend-dotnet/Controllers/AppsController.cs), dòng 187 |
| GET | `/api/apps/engine-status` | `EngineStatus` | [AppsController.cs](../../backend-dotnet/Controllers/AppsController.cs), dòng 201 |

## AttachmentsController

| Method | Path | Action | Nguồn |
|---|---|---|---|
| POST | `/api/attachments` | `Upload` | [AttachmentsController.cs](../../backend-dotnet/Controllers/AttachmentsController.cs), dòng 31 |
| GET | `/api/attachments/{id}` | `Download` | [AttachmentsController.cs](../../backend-dotnet/Controllers/AttachmentsController.cs), dòng 84 |

## AuthController

| Method | Path | Action | Nguồn |
|---|---|---|---|
| POST | `/api/auth/login` | `Login` | [AuthController.cs](../../backend-dotnet/Controllers/AuthController.cs), dòng 26 |
| POST | `/api/auth/demo-switch` | `DemoSwitch` | [AuthController.cs](../../backend-dotnet/Controllers/AuthController.cs), dòng 47 |
| POST | `/api/auth/logout` | `Logout` | [AuthController.cs](../../backend-dotnet/Controllers/AuthController.cs), dòng 68 |
| POST | `/api/auth/change-password` | `ChangePassword` | [AuthController.cs](../../backend-dotnet/Controllers/AuthController.cs), dòng 78 |
| GET | `/api/auth/me` | `Me` | [AuthController.cs](../../backend-dotnet/Controllers/AuthController.cs), dòng 89 |
| GET | `/api/auth/demo-accounts` | `DemoAccounts` | [AuthController.cs](../../backend-dotnet/Controllers/AuthController.cs), dòng 98 |
| GET | `/api/auth/users` | `ListUsers` | [AuthController.cs](../../backend-dotnet/Controllers/AuthController.cs), dòng 121 |
| POST | `/api/auth/users` | `UpsertUser` | [AuthController.cs](../../backend-dotnet/Controllers/AuthController.cs), dòng 133 |
| POST | `/api/auth/users/{id}/reset-password` | `ResetPassword` | [AuthController.cs](../../backend-dotnet/Controllers/AuthController.cs), dòng 148 |
| PATCH | `/api/auth/users/{id}/status` | `SetUserStatus` | [AuthController.cs](../../backend-dotnet/Controllers/AuthController.cs), dòng 161 |
| DELETE | `/api/auth/users/{id}` | `DeleteUser` | [AuthController.cs](../../backend-dotnet/Controllers/AuthController.cs), dòng 175 |

## AutomationsController

| Method | Path | Action | Nguồn |
|---|---|---|---|
| GET | `/api/automations` | `List` | [AutomationsController.cs](../../backend-dotnet/Controllers/AutomationsController.cs), dòng 22 |
| POST | `/api/automations` | `Upsert` | [AutomationsController.cs](../../backend-dotnet/Controllers/AutomationsController.cs), dòng 31 |
| PATCH | `/api/automations/{id}/toggle` | `Toggle` | [AutomationsController.cs](../../backend-dotnet/Controllers/AutomationsController.cs), dòng 48 |
| DELETE | `/api/automations/{id}` | `Delete` | [AutomationsController.cs](../../backend-dotnet/Controllers/AutomationsController.cs), dòng 61 |
| GET | `/api/automations/logs` | `Logs` | [AutomationsController.cs](../../backend-dotnet/Controllers/AutomationsController.cs), dòng 73 |
| POST | `/api/automations/{id}/test` | `DryRun` | [AutomationsController.cs](../../backend-dotnet/Controllers/AutomationsController.cs), dòng 82 |

## CategoriesController

| Method | Path | Action | Nguồn |
|---|---|---|---|
| GET | `/api/categories` | `GetCategories` | [CategoriesController.cs](../../backend-dotnet/Controllers/CategoriesController.cs), dòng 19 |
| POST | `/api/categories` | `UpsertCategory` | [CategoriesController.cs](../../backend-dotnet/Controllers/CategoriesController.cs), dòng 26 |
| GET | `/api/categories/items` | `GetItems` | [CategoriesController.cs](../../backend-dotnet/Controllers/CategoriesController.cs), dòng 36 |
| POST | `/api/categories/items` | `UpsertItem` | [CategoriesController.cs](../../backend-dotnet/Controllers/CategoriesController.cs), dòng 43 |

## ChatController

| Method | Path | Action | Nguồn |
|---|---|---|---|
| GET | `/api/chat/users` | `ListUsers` | [ChatController.cs](../../backend-dotnet/Controllers/ChatController.cs), dòng 23 |
| GET | `/api/chat/unread-summary` | `UnreadSummary` | [ChatController.cs](../../backend-dotnet/Controllers/ChatController.cs), dòng 32 |
| GET | `/api/chat/channels` | `ListChannels` | [ChatController.cs](../../backend-dotnet/Controllers/ChatController.cs), dòng 45 |
| POST | `/api/chat/channels` | `CreateChannel` | [ChatController.cs](../../backend-dotnet/Controllers/ChatController.cs), dòng 54 |
| POST | `/api/chat/channels/direct` | `EnsureDirect` | [ChatController.cs](../../backend-dotnet/Controllers/ChatController.cs), dòng 66 |
| POST | `/api/chat/channels/{id}/members` | `AddMember` | [ChatController.cs](../../backend-dotnet/Controllers/ChatController.cs), dòng 78 |
| GET | `/api/chat/channels/{id}/messages` | `ListMessages` | [ChatController.cs](../../backend-dotnet/Controllers/ChatController.cs), dòng 90 |
| POST | `/api/chat/channels/{id}/messages` | `SendMessage` | [ChatController.cs](../../backend-dotnet/Controllers/ChatController.cs), dòng 111 |
| POST | `/api/chat/channels/{id}/tasks` | `AssignTaskInChannel` | [ChatController.cs](../../backend-dotnet/Controllers/ChatController.cs), dòng 123 |
| POST | `/api/chat/channels/{id}/read` | `MarkChannelRead` | [ChatController.cs](../../backend-dotnet/Controllers/ChatController.cs), dòng 137 |
| POST | `/api/chat/messages/{messageId}/convert-to-task` | `ConvertToTask` | [ChatController.cs](../../backend-dotnet/Controllers/ChatController.cs), dòng 148 |
| POST | `/api/chat/messages/{messageId}/reactions` | `ToggleReaction` | [ChatController.cs](../../backend-dotnet/Controllers/ChatController.cs), dòng 159 |
| PATCH | `/api/chat/messages/{messageId}/pin` | `TogglePin` | [ChatController.cs](../../backend-dotnet/Controllers/ChatController.cs), dòng 171 |
| DELETE | `/api/chat/messages/{messageId}` | `DeleteMessage` | [ChatController.cs](../../backend-dotnet/Controllers/ChatController.cs), dòng 180 |

## IntegrationsController

| Method | Path | Action | Nguồn |
|---|---|---|---|
| GET | `/api/integrations/keys` | `ListKeys` | [IntegrationsController.cs](../../backend-dotnet/Controllers/IntegrationsController.cs), dòng 29 |
| POST | `/api/integrations/keys` | `CreateKey` | [IntegrationsController.cs](../../backend-dotnet/Controllers/IntegrationsController.cs), dòng 41 |
| PATCH | `/api/integrations/keys/{id}/toggle` | `ToggleKey` | [IntegrationsController.cs](../../backend-dotnet/Controllers/IntegrationsController.cs), dòng 56 |
| DELETE | `/api/integrations/keys/{id}` | `DeleteKey` | [IntegrationsController.cs](../../backend-dotnet/Controllers/IntegrationsController.cs), dòng 69 |
| GET | `/api/external/tables/{tableId}/records` | `ExternalQueryRecords` | [IntegrationsController.cs](../../backend-dotnet/Controllers/IntegrationsController.cs), dòng 102 |
| POST | `/api/external/tables/{tableId}/records` | `ExternalIngestRecord` | [IntegrationsController.cs](../../backend-dotnet/Controllers/IntegrationsController.cs), dòng 113 |

## MetadataController

| Method | Path | Action | Nguồn |
|---|---|---|---|
| GET | `/api/modules` | `GetModules` | [MetadataController.cs](../../backend-dotnet/Controllers/MetadataController.cs), dòng 24 |
| POST | `/api/modules` | `UpsertModule` | [MetadataController.cs](../../backend-dotnet/Controllers/MetadataController.cs), dòng 33 |
| DELETE | `/api/modules/{id}` | `DeleteModule` | [MetadataController.cs](../../backend-dotnet/Controllers/MetadataController.cs), dòng 48 |
| GET | `/api/tables` | `GetTables` | [MetadataController.cs](../../backend-dotnet/Controllers/MetadataController.cs), dòng 60 |
| POST | `/api/tables` | `UpsertTable` | [MetadataController.cs](../../backend-dotnet/Controllers/MetadataController.cs), dòng 69 |
| DELETE | `/api/tables/{id}` | `DeleteTable` | [MetadataController.cs](../../backend-dotnet/Controllers/MetadataController.cs), dòng 85 |
| GET | `/api/tables/{tableId}/compile` | `CompileTableSchema` | [MetadataController.cs](../../backend-dotnet/Controllers/MetadataController.cs), dòng 97 |
| GET | `/api/columns` | `GetColumns` | [MetadataController.cs](../../backend-dotnet/Controllers/MetadataController.cs), dòng 114 |
| POST | `/api/columns` | `UpsertColumn` | [MetadataController.cs](../../backend-dotnet/Controllers/MetadataController.cs), dòng 133 |
| DELETE | `/api/columns/{id}` | `DeleteColumn` | [MetadataController.cs](../../backend-dotnet/Controllers/MetadataController.cs), dòng 148 |

## NotificationsController

| Method | Path | Action | Nguồn |
|---|---|---|---|
| GET | `/api/notifications` | `List` | [NotificationsController.cs](../../backend-dotnet/Controllers/NotificationsController.cs), dòng 26 |
| GET | `/api/notifications/stream` | `Stream` | [NotificationsController.cs](../../backend-dotnet/Controllers/NotificationsController.cs), dòng 39 |
| PATCH | `/api/notifications/{id}/read` | `MarkRead` | [NotificationsController.cs](../../backend-dotnet/Controllers/NotificationsController.cs), dòng 99 |
| POST | `/api/notifications/read-all` | `MarkAllRead` | [NotificationsController.cs](../../backend-dotnet/Controllers/NotificationsController.cs), dòng 108 |
| DELETE | `/api/notifications/{id}` | `Delete` | [NotificationsController.cs](../../backend-dotnet/Controllers/NotificationsController.cs), dòng 117 |
| POST | `/api/notifications/clear-read` | `ClearRead` | [NotificationsController.cs](../../backend-dotnet/Controllers/NotificationsController.cs), dòng 127 |
| POST | `/api/notifications/broadcast` | `Broadcast` | [NotificationsController.cs](../../backend-dotnet/Controllers/NotificationsController.cs), dòng 136 |

## PublicFormsController

| Method | Path | Action | Nguồn |
|---|---|---|---|
| GET | `/api/public/forms/{tableId}` | `GetFormDefinition` | [PublicFormsController.cs](../../backend-dotnet/Controllers/PublicFormsController.cs), dòng 27 |
| POST | `/api/public/forms/{tableId}/submit` | `SubmitPublicForm` | [PublicFormsController.cs](../../backend-dotnet/Controllers/PublicFormsController.cs), dòng 61 |

## RecordsController

| Method | Path | Action | Nguồn |
|---|---|---|---|
| GET | `/api/records` | `Query` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 44 |
| GET | `/api/records/export` | `Export` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 60 |
| GET | `/api/records/views` | `ListSavedViews` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 172 |
| POST | `/api/records/views` | `SaveView` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 184 |
| DELETE | `/api/records/views/{id}` | `DeleteView` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 196 |
| GET | `/api/records/lookup-options` | `LookupOptions` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 205 |
| GET | `/api/records/{id}/relations` | `GetCrossAppRelations` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 221 |
| GET | `/api/records/{id}/audit` | `GetAuditHistory` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 231 |
| GET | `/api/records/analytics` | `GetTableAnalytics` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 242 |
| POST | `/api/records` | `Upsert` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 254 |
| PATCH | `/api/records/{id}/cell` | `PatchCell` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 346 |
| POST | `/api/records/{id}/submit-approval` | `SubmitApproval` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 397 |
| POST | `/api/records/{id}/approve` | `ProcessApproval` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 414 |
| GET | `/api/records/pending-approvals` | `PendingApprovals` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 425 |
| GET | `/api/records/{id}/comments` | `GetComments` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 434 |
| POST | `/api/records/{id}/comments` | `AddComment` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 443 |
| GET | `/api/records/trash` | `GetTrash` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 455 |
| POST | `/api/records/{id}/restore` | `Restore` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 468 |
| DELETE | `/api/records/{id}/permanent` | `PermanentDelete` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 488 |
| GET | `/api/records/audit-logs` | `GlobalAuditLogs` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 500 |
| POST | `/api/records/batch-delete` | `BatchDelete` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 595 |
| DELETE | `/api/records/{id}` | `DeleteSingle` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 621 |
| POST | `/api/records/validate` | `ValidateTable` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 653 |
| POST | `/api/records/bulk-import` | `BulkImport` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 674 |
| POST | `/api/records/bulk-import/preview` | `PreviewImport` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 678 |

## RolesController

| Method | Path | Action | Nguồn |
|---|---|---|---|
| GET | `/api/roles/catalog` | `Catalog` | [RolesController.cs](../../backend-dotnet/Controllers/RolesController.cs), dòng 33 |
| GET | `/api/roles` | `List` | [RolesController.cs](../../backend-dotnet/Controllers/RolesController.cs), dòng 41 |
| POST | `/api/roles` | `Upsert` | [RolesController.cs](../../backend-dotnet/Controllers/RolesController.cs), dòng 50 |
| DELETE | `/api/roles/{id}` | `Delete` | [RolesController.cs](../../backend-dotnet/Controllers/RolesController.cs), dòng 66 |
| GET | `/api/roles/by-app/{appIdOrCode}` | `GetByApp` | [RolesController.cs](../../backend-dotnet/Controllers/RolesController.cs), dòng 78 |
| PUT | `/api/roles/by-app/{appIdOrCode}` | `SaveByApp` | [RolesController.cs](../../backend-dotnet/Controllers/RolesController.cs), dòng 91 |
| GET | `/api/roles/by-app/{appIdOrCode}/tables` | `GetTableMatrixByApp` | [RolesController.cs](../../backend-dotnet/Controllers/RolesController.cs), dòng 103 |
| PUT | `/api/roles/by-app/{appIdOrCode}/tables` | `SaveTableMatrixByApp` | [RolesController.cs](../../backend-dotnet/Controllers/RolesController.cs), dòng 116 |

## SettingsController

| Method | Path | Action | Nguồn |
|---|---|---|---|
| GET | `/api/settings/me` | `GetMySettings` | [SettingsController.cs](../../backend-dotnet/Controllers/SettingsController.cs), dòng 29 |
| PUT | `/api/settings/me` | `UpdateMySettings` | [SettingsController.cs](../../backend-dotnet/Controllers/SettingsController.cs), dòng 39 |
| PUT | `/api/settings/profile` | `UpdateProfile` | [SettingsController.cs](../../backend-dotnet/Controllers/SettingsController.cs), dòng 49 |
| GET | `/api/settings/system` | `GetSystemSettings` | [SettingsController.cs](../../backend-dotnet/Controllers/SettingsController.cs), dòng 61 |
| PUT | `/api/settings/system` | `UpdateSystemSettings` | [SettingsController.cs](../../backend-dotnet/Controllers/SettingsController.cs), dòng 71 |
| GET | `/api/settings/notification-channels` | `ListNotificationChannels` | [SettingsController.cs](../../backend-dotnet/Controllers/SettingsController.cs), dòng 83 |
| POST | `/api/settings/notification-channels` | `UpsertNotificationChannel` | [SettingsController.cs](../../backend-dotnet/Controllers/SettingsController.cs), dòng 94 |
| DELETE | `/api/settings/notification-channels/{id}` | `DeleteNotificationChannel` | [SettingsController.cs](../../backend-dotnet/Controllers/SettingsController.cs), dòng 106 |
| POST | `/api/settings/notification-channels/{id}/test` | `TestNotificationChannel` | [SettingsController.cs](../../backend-dotnet/Controllers/SettingsController.cs), dòng 117 |
| GET | `/api/settings/notification-channels/logs` | `ListDeliveryLogs` | [SettingsController.cs](../../backend-dotnet/Controllers/SettingsController.cs), dòng 130 |

## SsoController

| Method | Path | Action | Nguồn |
|---|---|---|---|
| GET | `/api/auth/sso/providers` | `ListPublicProviders` | [SsoController.cs](../../backend-dotnet/Controllers/SsoController.cs), dòng 21 |
| GET | `/api/auth/sso/admin/providers` | `ListAdminProviders` | [SsoController.cs](../../backend-dotnet/Controllers/SsoController.cs), dòng 28 |
| POST | `/api/auth/sso/admin/providers` | `UpsertProvider` | [SsoController.cs](../../backend-dotnet/Controllers/SsoController.cs), dòng 40 |
| DELETE | `/api/auth/sso/admin/providers/{id}` | `DeleteProvider` | [SsoController.cs](../../backend-dotnet/Controllers/SsoController.cs), dòng 53 |
| POST | `/api/auth/sso/admin/discover` | `DiscoverOidc` | [SsoController.cs](../../backend-dotnet/Controllers/SsoController.cs), dòng 65 |
| GET | `/api/auth/sso/start/{code}` | `Start` | [SsoController.cs](../../backend-dotnet/Controllers/SsoController.cs), dòng 78 |
| GET | `/api/auth/sso/callback/{code}` | `Callback` | [SsoController.cs](../../backend-dotnet/Controllers/SsoController.cs), dòng 95 |

## TasksController

| Method | Path | Action | Nguồn |
|---|---|---|---|
| GET | `/api/tasks` | `List` | [TasksController.cs](../../backend-dotnet/Controllers/TasksController.cs), dòng 29 |
| GET | `/api/tasks/{id}` | `GetById` | [TasksController.cs](../../backend-dotnet/Controllers/TasksController.cs), dòng 48 |
| POST | `/api/tasks` | `Create` | [TasksController.cs](../../backend-dotnet/Controllers/TasksController.cs), dòng 66 |
| PATCH | `/api/tasks/{id}` | `Update` | [TasksController.cs](../../backend-dotnet/Controllers/TasksController.cs), dòng 80 |
| DELETE | `/api/tasks/{id}` | `Delete` | [TasksController.cs](../../backend-dotnet/Controllers/TasksController.cs), dòng 100 |

## WidgetsController

| Method | Path | Action | Nguồn |
|---|---|---|---|
| GET | `/api/widgets` | `List` | [WidgetsController.cs](../../backend-dotnet/Controllers/WidgetsController.cs), dòng 23 |
| GET | `/api/widgets/portal` | `PortalSummary` | [WidgetsController.cs](../../backend-dotnet/Controllers/WidgetsController.cs), dòng 44 |
| POST | `/api/widgets` | `Upsert` | [WidgetsController.cs](../../backend-dotnet/Controllers/WidgetsController.cs), dòng 54 |
| POST | `/api/widgets/layout` | `SaveLayout` | [WidgetsController.cs](../../backend-dotnet/Controllers/WidgetsController.cs), dòng 74 |
| DELETE | `/api/widgets/{id}` | `Delete` | [WidgetsController.cs](../../backend-dotnet/Controllers/WidgetsController.cs), dòng 88 |

## WorkflowsController

| Method | Path | Action | Nguồn |
|---|---|---|---|
| GET | `/api/workflows` | `List` | [WorkflowsController.cs](../../backend-dotnet/Controllers/WorkflowsController.cs), dòng 21 |
| POST | `/api/workflows` | `Upsert` | [WorkflowsController.cs](../../backend-dotnet/Controllers/WorkflowsController.cs), dòng 30 |
| PATCH | `/api/workflows/{id}/toggle` | `Toggle` | [WorkflowsController.cs](../../backend-dotnet/Controllers/WorkflowsController.cs), dòng 47 |
| DELETE | `/api/workflows/{id}` | `Delete` | [WorkflowsController.cs](../../backend-dotnet/Controllers/WorkflowsController.cs), dòng 60 |

Tổng: **138 route action** trong controller, ngoài Program/OpenAPI. Luồng bulk-import hiện yêu cầu preview như [hướng dẫn nhập Excel](excel-import-implementation.md).
