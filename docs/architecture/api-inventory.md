# Danh mục API từ mã nguồn

Cập nhật từ controller và `Program.cs` ngày **18/11/2026**. Xem [quy tắc biểu mẫu](form-rules-implementation.md) cho cấu hình `formRules` qua `POST /api/tables`, [đợt 1](priority-features-implementation.md) cho query export và chat cursor, và [hướng dẫn vận hành](../../docs/collaboration/operations-and-backup.md) cho `/health/ready`, `/metrics`. DTO nằm trong [`PlatformModels.cs`](../../backend-dotnet/Models/PlatformModels.cs); khi API chạy xem OpenAPI hệ thống tại `/openapi/v1.json` hoặc từng App tại `/api/apps/{appCode}/openapi.json`.

## Endpoint ngoài controller

| Method | Path | Nguồn |
|---|---|---|
| GET | `/` | [`Program.cs`](../../backend-dotnet/Program.cs), dòng 145; metadata cổng API |
| GET | `/health` | [`Program.cs`](../../backend-dotnet/Program.cs), dòng 112; liveness tĩnh |
| GET | `/health/ready` | [`Program.cs`](../../backend-dotnet/Program.cs), dòng 119; readiness kiểm tra kết nối PostgreSQL |
| GET | `/metrics` | [`Program.cs`](../../backend-dotnet/Program.cs), dòng 135; JSON hoặc Prometheus text (`?format=prometheus` / `Accept: text/plain`) |
| GET | `/openapi/v1.json` | ASP.NET Core OpenAPI (`MapOpenApi`) |

## AppEcosystemController (2)

| Method | Path | Action | Nguồn |
|---|---|---|---|
| GET | `/api/apps/{appCode}/openapi.json` | `GetAppOpenApi` | [AppEcosystemController.cs](../../backend-dotnet/Controllers/AppEcosystemController.cs), dòng 79 |
| POST | `/api/mcp/{appCode}` | `HandleMcpRpc` | [AppEcosystemController.cs](../../backend-dotnet/Controllers/AppEcosystemController.cs), dòng 91 |

## AppStoreController (14)

| Method | Path | Action | Nguồn |
|---|---|---|---|
| GET | `/api/store/manifest.json` | `GetPublicManifest` | [AppStoreController.cs](../../backend-dotnet/Controllers/AppStoreController.cs), dòng 14 |
| GET | `/api/store/apps` | `ListApps` | [AppStoreController.cs](../../backend-dotnet/Controllers/AppStoreController.cs), dòng 22 |
| GET | `/api/store/apps/{code}` | `GetAppDetail` | [AppStoreController.cs](../../backend-dotnet/Controllers/AppStoreController.cs), dòng 35 |
| GET | `/api/store/apps/{code}/download` | `DownloadAppPackage` | [AppStoreController.cs](../../backend-dotnet/Controllers/AppStoreController.cs), dòng 47 |
| GET | `/api/store/export-package/{codeOrId}` | `ExportInstalledPackage` | [AppStoreController.cs](../../backend-dotnet/Controllers/AppStoreController.cs), dòng 58 |
| POST | `/api/store/apps/{code}/install` | `InstallFromStore` | [AppStoreController.cs](../../backend-dotnet/Controllers/AppStoreController.cs), dòng 79 |
| POST | `/api/store/install-package` | `InstallUploadedPackage` | [AppStoreController.cs](../../backend-dotnet/Controllers/AppStoreController.cs), dòng 106 |
| POST | `/api/store/install-from-url` | `InstallFromUrl` | [AppStoreController.cs](../../backend-dotnet/Controllers/AppStoreController.cs), dòng 125 |
| GET | `/api/store/registries` | `ListRegistries` | [AppStoreController.cs](../../backend-dotnet/Controllers/AppStoreController.cs), dòng 148 |
| POST | `/api/store/registries` | `UpsertRegistry` | [AppStoreController.cs](../../backend-dotnet/Controllers/AppStoreController.cs), dòng 158 |
| POST | `/api/store/registries/{id}/sync` | `SyncRegistry` | [AppStoreController.cs](../../backend-dotnet/Controllers/AppStoreController.cs), dòng 177 |
| PATCH | `/api/store/registries/{id}/toggle` | `ToggleRegistry` | [AppStoreController.cs](../../backend-dotnet/Controllers/AppStoreController.cs), dòng 198 |
| GET | `/api/store/history` | `GetHistory` | [AppStoreController.cs](../../backend-dotnet/Controllers/AppStoreController.cs), dòng 212 |
| DELETE | `/api/store/registries/{id}` | `DeleteRegistry` | [AppStoreController.cs](../../backend-dotnet/Controllers/AppStoreController.cs), dòng 222 |

## AppsController (20)

| Method | Path | Action | Nguồn |
|---|---|---|---|
| GET | `/api/apps` | `ListApps` | [AppsController.cs](../../backend-dotnet/Controllers/AppsController.cs), dòng 24 |
| GET | `/api/apps/templates` | `ListTemplates` | [AppsController.cs](../../backend-dotnet/Controllers/AppsController.cs), dòng 35 |
| GET | `/api/apps/upgrade-v2/preview` | `PreviewUpgradeV2` | [AppsController.cs](../../backend-dotnet/Controllers/AppsController.cs), dòng 44 |
| POST | `/api/apps/upgrade-v2` | `UpgradeAllV2` | [AppsController.cs](../../backend-dotnet/Controllers/AppsController.cs), dòng 56 |
| POST | `/api/apps/{codeOrId}/upgrade-v2` | `UpgradeSingleV2` | [AppsController.cs](../../backend-dotnet/Controllers/AppsController.cs), dòng 68 |
| GET | `/api/apps/{codeOrId}` | `GetAppRuntime` | [AppsController.cs](../../backend-dotnet/Controllers/AppsController.cs), dòng 87 |
| POST | `/api/apps` | `UpsertApp` | [AppsController.cs](../../backend-dotnet/Controllers/AppsController.cs), dòng 121 |
| POST | `/api/apps/{appId}/validate-publish` | `ValidatePublish` | [AppsController.cs](../../backend-dotnet/Controllers/AppsController.cs), dòng 136 |
| POST | `/api/apps/{appId}/publish` | `PublishApp` | [AppsController.cs](../../backend-dotnet/Controllers/AppsController.cs), dòng 148 |
| GET | `/api/apps/{appId}/releases` | `ListReleases` | [AppsController.cs](../../backend-dotnet/Controllers/AppsController.cs), dòng 167 |
| GET | `/api/apps/{appId}/releases/diff` | `GetReleaseDiff` | [AppsController.cs](../../backend-dotnet/Controllers/AppsController.cs), dòng 179 |
| POST | `/api/apps/{appId}/releases/{versionNumber:int}/rollback` | `RollbackRelease` | [AppsController.cs](../../backend-dotnet/Controllers/AppsController.cs), dòng 191 |
| POST | `/api/apps/from-excel` | `CreateFromExcel` | [AppsController.cs](../../backend-dotnet/Controllers/AppsController.cs), dòng 203 |
| POST | `/api/apps/from-template` | `CreateFromTemplate` | [AppsController.cs](../../backend-dotnet/Controllers/AppsController.cs), dòng 218 |
| GET | `/api/apps/{codeOrId}/export` | `ExportBlueprint` | [AppsController.cs](../../backend-dotnet/Controllers/AppsController.cs), dòng 240 |
| POST | `/api/apps/import-blueprint` | `ImportBlueprint` | [AppsController.cs](../../backend-dotnet/Controllers/AppsController.cs), dòng 253 |
| POST | `/api/apps/{codeOrId}/upgrade-blueprint` | `UpgradeBlueprint` | [AppsController.cs](../../backend-dotnet/Controllers/AppsController.cs), dòng 265 |
| POST | `/api/apps/ai-generate` | `GenerateFromAiPrompt` | [AppsController.cs](../../backend-dotnet/Controllers/AppsController.cs), dòng 278 |
| DELETE | `/api/apps/{id}` | `DeleteApp` | [AppsController.cs](../../backend-dotnet/Controllers/AppsController.cs), dòng 300 |
| GET | `/api/apps/engine-status` | `EngineStatus` | [AppsController.cs](../../backend-dotnet/Controllers/AppsController.cs), dòng 314 |

## AttachmentsController (8)

| Method | Path | Action | Nguồn |
|---|---|---|---|
| POST | `/api/attachments` | `Upload` | [AttachmentsController.cs](../../backend-dotnet/Controllers/AttachmentsController.cs), dòng 53 |
| GET | `/api/attachments/list` | `List` | [AttachmentsController.cs](../../backend-dotnet/Controllers/AttachmentsController.cs), dòng 117 |
| GET | `/api/attachments/stats` | `GetStats` | [AttachmentsController.cs](../../backend-dotnet/Controllers/AttachmentsController.cs), dòng 129 |
| POST | `/api/attachments/quotas` | `UpsertQuota` | [AttachmentsController.cs](../../backend-dotnet/Controllers/AttachmentsController.cs), dòng 141 |
| POST | `/api/attachments/cleanup-orphans` | `CleanupOrphans` | [AttachmentsController.cs](../../backend-dotnet/Controllers/AttachmentsController.cs), dòng 153 |
| POST | `/api/attachments/{id}/share` | `CreateShare` | [AttachmentsController.cs](../../backend-dotnet/Controllers/AttachmentsController.cs), dòng 165 |
| GET | `/api/attachments/shared/{token}` | `DownloadShared` | [AttachmentsController.cs](../../backend-dotnet/Controllers/AttachmentsController.cs), dòng 175 |
| GET | `/api/attachments/{id}` | `Download` | [AttachmentsController.cs](../../backend-dotnet/Controllers/AttachmentsController.cs), dòng 191 |

## AuthController (11)

| Method | Path | Action | Nguồn |
|---|---|---|---|
| POST | `/api/auth/login` | `Login` | [AuthController.cs](../../backend-dotnet/Controllers/AuthController.cs), dòng 25 |
| POST | `/api/auth/demo-switch` | `DemoSwitch` | [AuthController.cs](../../backend-dotnet/Controllers/AuthController.cs), dòng 47 |
| POST | `/api/auth/logout` | `Logout` | [AuthController.cs](../../backend-dotnet/Controllers/AuthController.cs), dòng 69 |
| POST | `/api/auth/change-password` | `ChangePassword` | [AuthController.cs](../../backend-dotnet/Controllers/AuthController.cs), dòng 79 |
| GET | `/api/auth/me` | `Me` | [AuthController.cs](../../backend-dotnet/Controllers/AuthController.cs), dòng 90 |
| GET | `/api/auth/demo-accounts` | `DemoAccounts` | [AuthController.cs](../../backend-dotnet/Controllers/AuthController.cs), dòng 99 |
| GET | `/api/auth/users` | `ListUsers` | [AuthController.cs](../../backend-dotnet/Controllers/AuthController.cs), dòng 122 |
| POST | `/api/auth/users` | `UpsertUser` | [AuthController.cs](../../backend-dotnet/Controllers/AuthController.cs), dòng 134 |
| POST | `/api/auth/users/{id}/reset-password` | `ResetPassword` | [AuthController.cs](../../backend-dotnet/Controllers/AuthController.cs), dòng 149 |
| PATCH | `/api/auth/users/{id}/status` | `SetUserStatus` | [AuthController.cs](../../backend-dotnet/Controllers/AuthController.cs), dòng 162 |
| DELETE | `/api/auth/users/{id}` | `DeleteUser` | [AuthController.cs](../../backend-dotnet/Controllers/AuthController.cs), dòng 176 |

## AutomationsController (9)

| Method | Path | Action | Nguồn |
|---|---|---|---|
| GET | `/api/automations` | `List` | [AutomationsController.cs](../../backend-dotnet/Controllers/AutomationsController.cs), dòng 23 |
| POST | `/api/automations` | `Upsert` | [AutomationsController.cs](../../backend-dotnet/Controllers/AutomationsController.cs), dòng 32 |
| PATCH | `/api/automations/{id}/toggle` | `Toggle` | [AutomationsController.cs](../../backend-dotnet/Controllers/AutomationsController.cs), dòng 49 |
| DELETE | `/api/automations/{id}` | `Delete` | [AutomationsController.cs](../../backend-dotnet/Controllers/AutomationsController.cs), dòng 62 |
| GET | `/api/automations/logs` | `Logs` | [AutomationsController.cs](../../backend-dotnet/Controllers/AutomationsController.cs), dòng 74 |
| POST | `/api/automations/{id}/test` | `DryRun` | [AutomationsController.cs](../../backend-dotnet/Controllers/AutomationsController.cs), dòng 83 |
| GET | `/api/automations/outbox` | `ListOutbox` | [AutomationsController.cs](../../backend-dotnet/Controllers/AutomationsController.cs), dòng 95 |
| POST | `/api/automations/outbox/{id}/retry` | `RetryOutbox` | [AutomationsController.cs](../../backend-dotnet/Controllers/AutomationsController.cs), dòng 107 |
| POST | `/api/automations/outbox/retry-dead-letters` | `RetryAllDeadLetters` | [AutomationsController.cs](../../backend-dotnet/Controllers/AutomationsController.cs), dòng 119 |

## BackupsController (5)

| Method | Path | Action | Nguồn |
|---|---|---|---|
| GET | `/api/backups/config` | `GetConfig` | [BackupsController.cs](../../backend-dotnet/Controllers/BackupsController.cs), dòng 29 |
| POST | `/api/backups/config` | `UpsertConfig` | [BackupsController.cs](../../backend-dotnet/Controllers/BackupsController.cs), dòng 37 |
| GET | `/api/backups/runs` | `ListRuns` | [BackupsController.cs](../../backend-dotnet/Controllers/BackupsController.cs), dòng 46 |
| POST | `/api/backups/run` | `RunNow` | [BackupsController.cs](../../backend-dotnet/Controllers/BackupsController.cs), dòng 54 |
| GET | `/api/backups/runs/{id}/download` | `Download` | [BackupsController.cs](../../backend-dotnet/Controllers/BackupsController.cs), dòng 63 |

## CategoriesController (7)

| Method | Path | Action | Nguồn |
|---|---|---|---|
| GET | `/api/categories` | `GetCategories` | [CategoriesController.cs](../../backend-dotnet/Controllers/CategoriesController.cs), dòng 18 |
| POST | `/api/categories` | `UpsertCategory` | [CategoriesController.cs](../../backend-dotnet/Controllers/CategoriesController.cs), dòng 25 |
| GET | `/api/categories/items` | `GetItems` | [CategoriesController.cs](../../backend-dotnet/Controllers/CategoriesController.cs), dòng 35 |
| POST | `/api/categories/items` | `UpsertItem` | [CategoriesController.cs](../../backend-dotnet/Controllers/CategoriesController.cs), dòng 42 |
| POST | `/api/categories/items/bulk` | `BulkUpsertItems` | [CategoriesController.cs](../../backend-dotnet/Controllers/CategoriesController.cs), dòng 58 |
| DELETE | `/api/categories/{categoryCode}` | `DeleteCategory` | [CategoriesController.cs](../../backend-dotnet/Controllers/CategoriesController.cs), dòng 76 |
| DELETE | `/api/categories/items/{id}` | `DeleteItem` | [CategoriesController.cs](../../backend-dotnet/Controllers/CategoriesController.cs), dòng 83 |

## ChatController (15)

| Method | Path | Action | Nguồn |
|---|---|---|---|
| GET | `/api/chat/users` | `ListUsers` | [ChatController.cs](../../backend-dotnet/Controllers/ChatController.cs), dòng 24 |
| GET | `/api/chat/unread-summary` | `UnreadSummary` | [ChatController.cs](../../backend-dotnet/Controllers/ChatController.cs), dòng 33 |
| GET | `/api/chat/channels` | `ListChannels` | [ChatController.cs](../../backend-dotnet/Controllers/ChatController.cs), dòng 46 |
| POST | `/api/chat/channels` | `CreateChannel` | [ChatController.cs](../../backend-dotnet/Controllers/ChatController.cs), dòng 55 |
| POST | `/api/chat/channels/direct` | `EnsureDirect` | [ChatController.cs](../../backend-dotnet/Controllers/ChatController.cs), dòng 67 |
| POST | `/api/chat/channels/{id}/members` | `AddMember` | [ChatController.cs](../../backend-dotnet/Controllers/ChatController.cs), dòng 79 |
| GET | `/api/chat/channels/{id}/messages` | `ListMessages` | [ChatController.cs](../../backend-dotnet/Controllers/ChatController.cs), dòng 91 |
| POST | `/api/chat/channels/{id}/messages` | `SendMessage` | [ChatController.cs](../../backend-dotnet/Controllers/ChatController.cs), dòng 112 |
| POST | `/api/chat/channels/{id}/tasks` | `AssignTaskInChannel` | [ChatController.cs](../../backend-dotnet/Controllers/ChatController.cs), dòng 124 |
| POST | `/api/chat/channels/{id}/read` | `MarkChannelRead` | [ChatController.cs](../../backend-dotnet/Controllers/ChatController.cs), dòng 138 |
| POST | `/api/chat/messages/{messageId}/convert-to-task` | `ConvertToTask` | [ChatController.cs](../../backend-dotnet/Controllers/ChatController.cs), dòng 149 |
| POST | `/api/chat/channels/{id}/ai-copilot` | `AiCopilot` | [ChatController.cs](../../backend-dotnet/Controllers/ChatController.cs), dòng 160 |
| POST | `/api/chat/messages/{messageId}/reactions` | `ToggleReaction` | [ChatController.cs](../../backend-dotnet/Controllers/ChatController.cs), dòng 180 |
| PATCH | `/api/chat/messages/{messageId}/pin` | `TogglePin` | [ChatController.cs](../../backend-dotnet/Controllers/ChatController.cs), dòng 192 |
| DELETE | `/api/chat/messages/{messageId}` | `DeleteMessage` | [ChatController.cs](../../backend-dotnet/Controllers/ChatController.cs), dòng 201 |

## IntegrationsController (22)

| Method | Path | Action | Nguồn |
|---|---|---|---|
| GET | `/api/integrations/keys` | `ListKeys` | [IntegrationsController.cs](../../backend-dotnet/Controllers/IntegrationsController.cs), dòng 44 |
| POST | `/api/integrations/keys` | `CreateKey` | [IntegrationsController.cs](../../backend-dotnet/Controllers/IntegrationsController.cs), dòng 54 |
| POST | `/api/integrations/keys/{id}/rotate` | `RotateKey` | [IntegrationsController.cs](../../backend-dotnet/Controllers/IntegrationsController.cs), dòng 67 |
| PATCH | `/api/integrations/keys/{id}/toggle` | `ToggleKey` | [IntegrationsController.cs](../../backend-dotnet/Controllers/IntegrationsController.cs), dòng 79 |
| DELETE | `/api/integrations/keys/{id}` | `DeleteKey` | [IntegrationsController.cs](../../backend-dotnet/Controllers/IntegrationsController.cs), dòng 90 |
| GET | `/api/integrations/plugins/catalog` | `GetPluginCatalog` | [IntegrationsController.cs](../../backend-dotnet/Controllers/IntegrationsController.cs), dòng 102 |
| GET | `/api/integrations/plugins` | `ListPlugins` | [IntegrationsController.cs](../../backend-dotnet/Controllers/IntegrationsController.cs), dòng 110 |
| GET | `/api/integrations/apps/{appId}/plugins` | `ListAppPlugins` | [IntegrationsController.cs](../../backend-dotnet/Controllers/IntegrationsController.cs), dòng 120 |
| PUT | `/api/integrations/apps/{appId}/plugins/{code}` | `UpsertAppPluginBinding` | [IntegrationsController.cs](../../backend-dotnet/Controllers/IntegrationsController.cs), dòng 146 |
| POST | `/api/integrations/plugins` | `UpsertPlugin` | [IntegrationsController.cs](../../backend-dotnet/Controllers/IntegrationsController.cs), dòng 159 |
| PATCH | `/api/integrations/plugins/{id}/toggle` | `TogglePlugin` | [IntegrationsController.cs](../../backend-dotnet/Controllers/IntegrationsController.cs), dòng 171 |
| POST | `/api/integrations/plugins/{id}/test` | `TestPlugin` | [IntegrationsController.cs](../../backend-dotnet/Controllers/IntegrationsController.cs), dòng 182 |
| POST | `/api/integrations/einvoice/parse-xml` | `ParseEInvoiceXml` | [IntegrationsController.cs](../../backend-dotnet/Controllers/IntegrationsController.cs), dòng 192 |
| POST | `/api/integrations/plugins/{code}/execute` | `ExecutePlugin` | [IntegrationsController.cs](../../backend-dotnet/Controllers/IntegrationsController.cs), dòng 218 |
| POST | `/api/integrations/payments/{providerCode}/ipn` | `ReceivePaymentIpn` | [IntegrationsController.cs](../../backend-dotnet/Controllers/IntegrationsController.cs), dòng 309 |
| DELETE | `/api/integrations/plugins/{id}` | `DeletePlugin` | [IntegrationsController.cs](../../backend-dotnet/Controllers/IntegrationsController.cs), dòng 336 |
| GET | `/api/integrations/logs` | `ListLogs` | [IntegrationsController.cs](../../backend-dotnet/Controllers/IntegrationsController.cs), dòng 346 |
| POST | `/api/integrations/inbound/{pluginCode}/tables/{tableId}` | `InboundPluginWebhook` | [IntegrationsController.cs](../../backend-dotnet/Controllers/IntegrationsController.cs), dòng 356 |
| GET | `/api/external/tables/{tableId}/records` | `ExternalQueryRecords` | [IntegrationsController.cs](../../backend-dotnet/Controllers/IntegrationsController.cs), dòng 436 |
| GET | `/api/external/tables/{tableId}/changes` | `ExternalDeltaChanges` | [IntegrationsController.cs](../../backend-dotnet/Controllers/IntegrationsController.cs), dòng 446 |
| POST | `/api/external/tables/{tableId}/records` | `ExternalIngestRecord` | [IntegrationsController.cs](../../backend-dotnet/Controllers/IntegrationsController.cs), dòng 457 |
| PUT | `/api/external/tables/{tableId}/records/{keyOrId}` | `ExternalUpsertByKeyOrId` | [IntegrationsController.cs](../../backend-dotnet/Controllers/IntegrationsController.cs), dòng 482 |

## MetadataController (13)

| Method | Path | Action | Nguồn |
|---|---|---|---|
| GET | `/api/modules` | `GetModules` | [MetadataController.cs](../../backend-dotnet/Controllers/MetadataController.cs), dòng 23 |
| POST | `/api/modules` | `UpsertModule` | [MetadataController.cs](../../backend-dotnet/Controllers/MetadataController.cs), dòng 32 |
| DELETE | `/api/modules/{id}` | `DeleteModule` | [MetadataController.cs](../../backend-dotnet/Controllers/MetadataController.cs), dòng 47 |
| GET | `/api/tables` | `GetTables` | [MetadataController.cs](../../backend-dotnet/Controllers/MetadataController.cs), dòng 59 |
| POST | `/api/tables` | `UpsertTable` | [MetadataController.cs](../../backend-dotnet/Controllers/MetadataController.cs), dòng 68 |
| DELETE | `/api/tables/{id}` | `DeleteTable` | [MetadataController.cs](../../backend-dotnet/Controllers/MetadataController.cs), dòng 84 |
| GET | `/api/tables/{tableId}/print-templates` | `GetPrintTemplates` | [MetadataController.cs](../../backend-dotnet/Controllers/MetadataController.cs), dòng 96 |
| POST | `/api/print-templates` | `UpsertPrintTemplate` | [MetadataController.cs](../../backend-dotnet/Controllers/MetadataController.cs), dòng 106 |
| DELETE | `/api/print-templates/{id}` | `DeletePrintTemplate` | [MetadataController.cs](../../backend-dotnet/Controllers/MetadataController.cs), dòng 121 |
| GET | `/api/tables/{tableId}/compile` | `CompileTableSchema` | [MetadataController.cs](../../backend-dotnet/Controllers/MetadataController.cs), dòng 133 |
| GET | `/api/columns` | `GetColumns` | [MetadataController.cs](../../backend-dotnet/Controllers/MetadataController.cs), dòng 150 |
| POST | `/api/columns` | `UpsertColumn` | [MetadataController.cs](../../backend-dotnet/Controllers/MetadataController.cs), dòng 169 |
| DELETE | `/api/columns/{id}` | `DeleteColumn` | [MetadataController.cs](../../backend-dotnet/Controllers/MetadataController.cs), dòng 191 |

## NotificationsController (9)

| Method | Path | Action | Nguồn |
|---|---|---|---|
| GET | `/api/notifications` | `List` | [NotificationsController.cs](../../backend-dotnet/Controllers/NotificationsController.cs), dòng 25 |
| GET | `/api/notifications/stream` | `Stream` | [NotificationsController.cs](../../backend-dotnet/Controllers/NotificationsController.cs), dòng 38 |
| PATCH | `/api/notifications/{id}/read` | `MarkRead` | [NotificationsController.cs](../../backend-dotnet/Controllers/NotificationsController.cs), dòng 98 |
| POST | `/api/notifications/read-all` | `MarkAllRead` | [NotificationsController.cs](../../backend-dotnet/Controllers/NotificationsController.cs), dòng 107 |
| DELETE | `/api/notifications/{id}` | `Delete` | [NotificationsController.cs](../../backend-dotnet/Controllers/NotificationsController.cs), dòng 116 |
| POST | `/api/notifications/clear-read` | `ClearRead` | [NotificationsController.cs](../../backend-dotnet/Controllers/NotificationsController.cs), dòng 126 |
| POST | `/api/notifications/broadcast` | `Broadcast` | [NotificationsController.cs](../../backend-dotnet/Controllers/NotificationsController.cs), dòng 135 |
| GET | `/api/notifications/online-users` | `OnlineUsers` | [NotificationsController.cs](../../backend-dotnet/Controllers/NotificationsController.cs), dòng 160 |
| POST | `/api/notifications/presence` | `TouchPresence` | [NotificationsController.cs](../../backend-dotnet/Controllers/NotificationsController.cs), dòng 185 |

## OrgUnitsController (4)

| Method | Path | Action | Nguồn |
|---|---|---|---|
| GET | `/api/org-units` | `List` | [OrgUnitsController.cs](../../backend-dotnet/Controllers/OrgUnitsController.cs), dòng 20 |
| POST | `/api/org-units` | `Upsert` | [OrgUnitsController.cs](../../backend-dotnet/Controllers/OrgUnitsController.cs), dòng 29 |
| DELETE | `/api/org-units/{id}` | `Delete` | [OrgUnitsController.cs](../../backend-dotnet/Controllers/OrgUnitsController.cs), dòng 51 |
| GET | `/api/org-units/{id}/subtree-users` | `GetSubtreeUsers` | [OrgUnitsController.cs](../../backend-dotnet/Controllers/OrgUnitsController.cs), dòng 63 |

## PublicFormsController (5)

| Method | Path | Action | Nguồn |
|---|---|---|---|
| GET | `/api/public/forms/{tableId}` | `GetFormDefinition` | [PublicFormsController.cs](../../backend-dotnet/Controllers/PublicFormsController.cs), dòng 81 |
| GET | `/api/public/forms/{tableId}/lookup-options` | `GetPublicLookupOptions` | [PublicFormsController.cs](../../backend-dotnet/Controllers/PublicFormsController.cs), dòng 122 |
| GET | `/api/public/forms/{tableId}/track/{recordKey}` | `TrackSubmission` | [PublicFormsController.cs](../../backend-dotnet/Controllers/PublicFormsController.cs), dòng 156 |
| POST | `/api/public/forms/{tableId}/upload` | `UploadPublicAttachment` | [PublicFormsController.cs](../../backend-dotnet/Controllers/PublicFormsController.cs), dòng 200 |
| POST | `/api/public/forms/{tableId}/submit` | `SubmitPublicForm` | [PublicFormsController.cs](../../backend-dotnet/Controllers/PublicFormsController.cs), dòng 261 |

## RecordsController (37)

| Method | Path | Action | Nguồn |
|---|---|---|---|
| GET | `/api/records` | `Query` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 45 |
| GET | `/api/records/export` | `Export` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 61 |
| GET | `/api/records/views` | `ListSavedViews` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 175 |
| POST | `/api/records/views` | `SaveView` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 187 |
| DELETE | `/api/records/views/{id}` | `DeleteView` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 199 |
| GET | `/api/records/lookup-options` | `LookupOptions` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 208 |
| GET | `/api/records/global-search` | `GlobalSearch` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 226 |
| POST | `/api/records/{id}/restore-fields` | `RestoreRecordFields` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 238 |
| GET | `/api/records/{id}` | `GetById` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 307 |
| POST | `/api/records/{id}/ai-summary` | `AiSummary` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 338 |
| POST | `/api/records/ai-import-map` | `AiImportMap` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 369 |
| POST | `/api/records/ai-doc-ocr` | `AiDocOcr` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 387 |
| POST | `/api/records/{id}/ai-triage` | `AiWorkflowTriage` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 407 |
| POST | `/api/records/ai-insights` | `AiDashboardInsights` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 435 |
| GET | `/api/records/{id}/relations` | `GetCrossAppRelations` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 539 |
| GET | `/api/records/table-relations` | `GetTableRelations` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 549 |
| GET | `/api/records/{id}/audit` | `GetAuditHistory` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 561 |
| GET | `/api/records/analytics` | `GetTableAnalytics` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 572 |
| POST | `/api/records` | `Upsert` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 584 |
| POST | `/api/records/with-relations` | `UpsertWithRelations` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 683 |
| PATCH | `/api/records/{id}/cell` | `PatchCell` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 830 |
| POST | `/api/records/{id}/submit-approval` | `SubmitApproval` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 888 |
| POST | `/api/records/{id}/recall-approval` | `RecallApproval` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 912 |
| POST | `/api/records/{id}/cancel-approval` | `CancelApproval` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 932 |
| POST | `/api/records/{id}/approve` | `ProcessApproval` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 952 |
| GET | `/api/records/pending-approvals` | `PendingApprovals` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 970 |
| GET | `/api/records/{id}/comments` | `GetComments` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 979 |
| POST | `/api/records/{id}/comments` | `AddComment` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 988 |
| GET | `/api/records/trash` | `GetTrash` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 1000 |
| POST | `/api/records/{id}/restore` | `Restore` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 1013 |
| DELETE | `/api/records/{id}/permanent` | `PermanentDelete` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 1033 |
| GET | `/api/records/audit-logs` | `GlobalAuditLogs` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 1045 |
| POST | `/api/records/batch-delete` | `BatchDelete` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 1140 |
| DELETE | `/api/records/{id}` | `DeleteSingle` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 1166 |
| POST | `/api/records/validate` | `ValidateTable` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 1198 |
| POST | `/api/records/bulk-import` | `BulkImport` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 1218 |
| POST | `/api/records/bulk-import/preview` | `PreviewImport` | [RecordsController.cs](../../backend-dotnet/Controllers/RecordsController.cs), dòng 1222 |

## RolesController (8)

| Method | Path | Action | Nguồn |
|---|---|---|---|
| GET | `/api/roles/catalog` | `Catalog` | [RolesController.cs](../../backend-dotnet/Controllers/RolesController.cs), dòng 32 |
| GET | `/api/roles` | `List` | [RolesController.cs](../../backend-dotnet/Controllers/RolesController.cs), dòng 40 |
| POST | `/api/roles` | `Upsert` | [RolesController.cs](../../backend-dotnet/Controllers/RolesController.cs), dòng 49 |
| DELETE | `/api/roles/{id}` | `Delete` | [RolesController.cs](../../backend-dotnet/Controllers/RolesController.cs), dòng 65 |
| GET | `/api/roles/by-app/{appIdOrCode}` | `GetByApp` | [RolesController.cs](../../backend-dotnet/Controllers/RolesController.cs), dòng 77 |
| PUT | `/api/roles/by-app/{appIdOrCode}` | `SaveByApp` | [RolesController.cs](../../backend-dotnet/Controllers/RolesController.cs), dòng 90 |
| GET | `/api/roles/by-app/{appIdOrCode}/tables` | `GetTableMatrixByApp` | [RolesController.cs](../../backend-dotnet/Controllers/RolesController.cs), dòng 102 |
| PUT | `/api/roles/by-app/{appIdOrCode}/tables` | `SaveTableMatrixByApp` | [RolesController.cs](../../backend-dotnet/Controllers/RolesController.cs), dòng 115 |

## SettingsController (18)

| Method | Path | Action | Nguồn |
|---|---|---|---|
| GET | `/api/settings/me` | `GetMySettings` | [SettingsController.cs](../../backend-dotnet/Controllers/SettingsController.cs), dòng 28 |
| PUT | `/api/settings/me` | `UpdateMySettings` | [SettingsController.cs](../../backend-dotnet/Controllers/SettingsController.cs), dòng 38 |
| PUT | `/api/settings/profile` | `UpdateProfile` | [SettingsController.cs](../../backend-dotnet/Controllers/SettingsController.cs), dòng 48 |
| GET | `/api/settings/system` | `GetSystemSettings` | [SettingsController.cs](../../backend-dotnet/Controllers/SettingsController.cs), dòng 60 |
| PUT | `/api/settings/system` | `UpdateSystemSettings` | [SettingsController.cs](../../backend-dotnet/Controllers/SettingsController.cs), dòng 70 |
| GET | `/api/settings/notification-channels` | `ListNotificationChannels` | [SettingsController.cs](../../backend-dotnet/Controllers/SettingsController.cs), dòng 82 |
| POST | `/api/settings/notification-channels` | `UpsertNotificationChannel` | [SettingsController.cs](../../backend-dotnet/Controllers/SettingsController.cs), dòng 93 |
| DELETE | `/api/settings/notification-channels/{id}` | `DeleteNotificationChannel` | [SettingsController.cs](../../backend-dotnet/Controllers/SettingsController.cs), dòng 105 |
| POST | `/api/settings/notification-channels/{id}/test` | `TestNotificationChannel` | [SettingsController.cs](../../backend-dotnet/Controllers/SettingsController.cs), dòng 116 |
| GET | `/api/settings/notification-channels/logs` | `ListDeliveryLogs` | [SettingsController.cs](../../backend-dotnet/Controllers/SettingsController.cs), dòng 129 |
| GET | `/api/settings/ai/status` | `GetAiStatus` | [SettingsController.cs](../../backend-dotnet/Controllers/SettingsController.cs), dòng 139 |
| GET | `/api/settings/ai` | `GetAiConfig` | [SettingsController.cs](../../backend-dotnet/Controllers/SettingsController.cs), dòng 150 |
| PUT | `/api/settings/ai` | `UpdateAiSystemSettings` | [SettingsController.cs](../../backend-dotnet/Controllers/SettingsController.cs), dòng 165 |
| POST | `/api/settings/ai/providers` | `UpsertAiProvider` | [SettingsController.cs](../../backend-dotnet/Controllers/SettingsController.cs), dòng 177 |
| POST | `/api/settings/ai/providers/{id}/default` | `SetDefaultAiProvider` | [SettingsController.cs](../../backend-dotnet/Controllers/SettingsController.cs), dòng 190 |
| DELETE | `/api/settings/ai/providers/{id}` | `DeleteAiProvider` | [SettingsController.cs](../../backend-dotnet/Controllers/SettingsController.cs), dòng 202 |
| POST | `/api/settings/ai/providers/{id}/test` | `TestAiProvider` | [SettingsController.cs](../../backend-dotnet/Controllers/SettingsController.cs), dòng 214 |
| GET | `/api/settings/ai/logs` | `ListAiLogs` | [SettingsController.cs](../../backend-dotnet/Controllers/SettingsController.cs), dòng 228 |

## SsoController (7)

| Method | Path | Action | Nguồn |
|---|---|---|---|
| GET | `/api/auth/sso/providers` | `ListPublicProviders` | [SsoController.cs](../../backend-dotnet/Controllers/SsoController.cs), dòng 20 |
| GET | `/api/auth/sso/admin/providers` | `ListAdminProviders` | [SsoController.cs](../../backend-dotnet/Controllers/SsoController.cs), dòng 27 |
| POST | `/api/auth/sso/admin/providers` | `UpsertProvider` | [SsoController.cs](../../backend-dotnet/Controllers/SsoController.cs), dòng 39 |
| DELETE | `/api/auth/sso/admin/providers/{id}` | `DeleteProvider` | [SsoController.cs](../../backend-dotnet/Controllers/SsoController.cs), dòng 52 |
| POST | `/api/auth/sso/admin/discover` | `DiscoverOidc` | [SsoController.cs](../../backend-dotnet/Controllers/SsoController.cs), dòng 64 |
| GET | `/api/auth/sso/start/{code}` | `Start` | [SsoController.cs](../../backend-dotnet/Controllers/SsoController.cs), dòng 77 |
| GET | `/api/auth/sso/callback/{code}` | `Callback` | [SsoController.cs](../../backend-dotnet/Controllers/SsoController.cs), dòng 94 |

## TasksController (5)

| Method | Path | Action | Nguồn |
|---|---|---|---|
| GET | `/api/tasks` | `List` | [TasksController.cs](../../backend-dotnet/Controllers/TasksController.cs), dòng 28 |
| GET | `/api/tasks/{id}` | `GetById` | [TasksController.cs](../../backend-dotnet/Controllers/TasksController.cs), dòng 47 |
| POST | `/api/tasks` | `Create` | [TasksController.cs](../../backend-dotnet/Controllers/TasksController.cs), dòng 65 |
| PATCH | `/api/tasks/{id}` | `Update` | [TasksController.cs](../../backend-dotnet/Controllers/TasksController.cs), dòng 79 |
| DELETE | `/api/tasks/{id}` | `Delete` | [TasksController.cs](../../backend-dotnet/Controllers/TasksController.cs), dòng 99 |

## WidgetsController (5)

| Method | Path | Action | Nguồn |
|---|---|---|---|
| GET | `/api/widgets` | `List` | [WidgetsController.cs](../../backend-dotnet/Controllers/WidgetsController.cs), dòng 22 |
| GET | `/api/widgets/portal` | `PortalSummary` | [WidgetsController.cs](../../backend-dotnet/Controllers/WidgetsController.cs), dòng 43 |
| POST | `/api/widgets` | `Upsert` | [WidgetsController.cs](../../backend-dotnet/Controllers/WidgetsController.cs), dòng 53 |
| POST | `/api/widgets/layout` | `SaveLayout` | [WidgetsController.cs](../../backend-dotnet/Controllers/WidgetsController.cs), dòng 73 |
| DELETE | `/api/widgets/{id}` | `Delete` | [WidgetsController.cs](../../backend-dotnet/Controllers/WidgetsController.cs), dòng 87 |

## WorkflowsController (10)

| Method | Path | Action | Nguồn |
|---|---|---|---|
| GET | `/api/workflows` | `List` | [WorkflowsController.cs](../../backend-dotnet/Controllers/WorkflowsController.cs), dòng 22 |
| POST | `/api/workflows` | `Upsert` | [WorkflowsController.cs](../../backend-dotnet/Controllers/WorkflowsController.cs), dòng 31 |
| PATCH | `/api/workflows/{id}/toggle` | `Toggle` | [WorkflowsController.cs](../../backend-dotnet/Controllers/WorkflowsController.cs), dòng 48 |
| DELETE | `/api/workflows/{id}` | `Delete` | [WorkflowsController.cs](../../backend-dotnet/Controllers/WorkflowsController.cs), dòng 61 |
| GET | `/api/workflows/delegations` | `ListDelegations` | [WorkflowsController.cs](../../backend-dotnet/Controllers/WorkflowsController.cs), dòng 73 |
| POST | `/api/workflows/delegations` | `UpsertDelegation` | [WorkflowsController.cs](../../backend-dotnet/Controllers/WorkflowsController.cs), dòng 83 |
| DELETE | `/api/workflows/delegations/{id}` | `DeleteDelegation` | [WorkflowsController.cs](../../backend-dotnet/Controllers/WorkflowsController.cs), dòng 94 |
| GET | `/api/workflows/holidays` | `ListHolidays` | [WorkflowsController.cs](../../backend-dotnet/Controllers/WorkflowsController.cs), dòng 104 |
| POST | `/api/workflows/holidays` | `UpsertHoliday` | [WorkflowsController.cs](../../backend-dotnet/Controllers/WorkflowsController.cs), dòng 113 |
| DELETE | `/api/workflows/holidays/{id}` | `DeleteHoliday` | [WorkflowsController.cs](../../backend-dotnet/Controllers/WorkflowsController.cs), dòng 125 |

Tổng: **234 route action** trong 21 controller, ngoài 5 endpoint cấp nền tảng ở `Program.cs` / OpenAPI (`/`, `/health`, `/health/ready`, `/metrics`, `/openapi/v1.json`). Luồng `bulk-import` yêu cầu preview preflight như mô tả ở [hướng dẫn nhập Excel](excel-import-implementation.md); toàn bộ đường ghi `POST /api/records`, `PATCH /api/records/{id}/cell`, `POST /api/external/...` hỗ trợ `expectedVersion` và `Idempotency-Key`.
