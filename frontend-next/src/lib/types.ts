export interface SysApp {
  id: string;
  code: string;
  name: string;
  description?: string | null;
  department: string;
  icon: string;
  color: string;
  bgColor: string;
  version: string;
  orderIndex: number;
  isActive: boolean;
  settings?: Record<string, unknown>;
  publishedVersion?: number;
  publishStatus?: string;
  moduleCount: number;
  tableCount: number;
  recordCount: number;
  createdAt?: string;
  updatedAt?: string;
}

export interface SysModule {
  id: string;
  appId: string;
  code: string;
  name: string;
  icon: string;
  orderIndex: number;
  isActive: boolean;
  description?: string | null;
  tableCount: number;
  recordCount: number;
}

export interface SysTable {
  id: string;
  appId: string;
  moduleId: string;
  tableName: string;
  displayName: string;
  description?: string | null;
  primaryView: string;
  rowScope?: string;
  approvalEnabled?: boolean;
  approvalField?: string | null;
  approverRoles?: string[];
  publicFormEnabled?: boolean;
  publicFormExpiresAt?: string | null;
  publicFormMaxSubmissions?: number | null;
  formRules?: FormRule[];
  orderIndex: number;
  isActive: boolean;
  columnCount: number;
  recordCount: number;
}

export interface FormCondition {
  field: string;
  operator: 'EQUALS' | 'NOT_EQUALS' | 'CONTAINS' | 'GREATER_THAN' | 'GREATER_OR_EQUAL' | 'LESS_THAN' | 'LESS_OR_EQUAL' | 'IS_EMPTY' | 'NOT_EMPTY';
  value?: string | null;
}
export interface FormRule {
  id: string;
  targetField: string;
  property: 'visible' | 'required' | 'readOnly';
  value: boolean;
  elseValue?: boolean | null;
  match: 'ALL' | 'ANY';
  conditions: FormCondition[];
}

export type DynamicDataType =
  | 'STRING'
  | 'NUMBER'
  | 'CURRENCY'
  | 'DATE'
  | 'BOOLEAN'
  | 'SELECT'
  | 'LOOKUP'
  | 'EMAIL'
  | 'TEL'
  | 'TEXTAREA'
  | 'FILE'
  | 'IMAGE'
  | 'SUB_TABLE'
  | 'AUTO_NUMBER';

export interface SysColumn {
  id: string;
  tableId: string;
  columnCode: string;
  columnName: string;
  excelHeader: string;
  dataType: DynamicDataType | string;
  isRequired: boolean;
  isIdentityKey: boolean;
  showInTable: boolean;
  showInForm: boolean;
  showInParentForm?: boolean;
  width: number;
  validationRegex?: string | null;
  refCategoryCode?: string | null;
  allowQuickCreate?: boolean;
  refAppId?: string | null;
  refTableId?: string | null;
  refValueField?: string | null;
  refDisplayField?: string | null;
  lookupFilterField?: string | null;
  lookupSourceField?: string | null;
  formula?: string | null;
  defaultValue?: string | null;
  hiddenForRoles?: string[];
  readOnlyForRoles?: string[];
  orderIndex: number;
  isActive?: boolean;
}

export interface SysCategory {
  categoryCode: string;
  categoryName: string;
  appId?: string | null;
  description?: string | null;
  itemCount: number;
}

export interface SysCategoryItem {
  id: string;
  categoryCode: string;
  itemCode: string;
  itemName: string;
  color?: string | null;
  orderIndex: number;
}

export interface ValidationErrorItem {
  severity: string;
  field: string;
  message: string;
}

export interface AppRecord {
  id: string;
  appId: string;
  moduleId: string;
  tableId: string;
  recordKey?: string | null;
  data: Record<string, unknown>;
  status: string;
  version?: number;
  approvalStatus?: string;
  approvedBy?: string | null;
  approvedAt?: string | null;
  rejectionReason?: string | null;
  workflowId?: string | null;
  currentStepOrder?: number;
  currentStepName?: string | null;
  totalWorkflowSteps?: number;
  currentStepApproverRoles?: string[];
  workflowHistory?: WorkflowHistoryEntry[];
  stepApprovals?: WorkflowStepApprovalVote[];
  deletedAt?: string | null;
  deletedBy?: string | null;
  validationErrors?: ValidationErrorItem[];
  createdBy?: string;
  updatedBy?: string;
  createdAt?: string;
  updatedAt?: string;
}

export interface AppRuntimeTree {
  app: SysApp;
  modules: Array<{
    module: SysModule;
    tables: Array<{
      table: SysTable;
      columns: SysColumn[];
    }>;
  }>;
}

export interface LookupSummaryField {
  code: string;
  label: string;
  value: string;
}

export interface LookupOption {
  recordId: string;
  value: string;
  label: string;
  subLabel?: string;
  searchText?: string;
  data?: Record<string, unknown>;
  summaryFields?: LookupSummaryField[];
}

export interface RelatedTableGroup {
  appId: string;
  appCode: string;
  appName: string;
  appColor: string;
  moduleId?: string;
  tableId: string;
  tableDisplayName: string;
  viaColumnCode?: string;
  viaColumnName: string;
  refValueField?: string;
  targetMatchValue?: string;
  canCreate?: boolean;
  canEdit?: boolean;
  canDelete?: boolean;
  columns: SysColumn[];
  records: AppRecord[];
}

export interface ChildRelationMutation {
  appId: string;
  moduleId: string;
  tableId: string;
  viaColumnCode: string;
  refValueField?: string;
  upserts: Array<{
    id?: string;
    expectedVersion?: number;
    data: Record<string, unknown>;
  }>;
  deleteIds: string[];
}

export interface AppAutomation {
  id: string;
  name: string;
  description?: string | null;
  sourceAppId: string;
  sourceTableId: string;
  triggerEvent: string;
  conditionField?: string | null;
  conditionOperator: string;
  conditionValue?: string | null;
  targetAppId?: string | null;
  targetModuleId?: string | null;
  targetTableId?: string | null;
  actionType: string;
  matchSourceField?: string | null;
  matchTargetField?: string | null;
  webhookUrl?: string | null;
  webhookSecret?: string | null;
  notifyRecipient?: string | null;
  notifyTemplate?: string | null;
  fieldMappings: Record<string, string>;
  isActive: boolean;
  runCount: number;
  lastRunAt?: string | null;
}

export interface AppAutomationLog {
  id: string;
  automationId: string;
  automationName?: string | null;
  sourceRecordId?: string | null;
  targetRecordId?: string | null;
  status: string;
  message?: string | null;
  createdAt: string;
}

export interface PermissionCatalogItem {
  code: string;
  name: string;
  description: string;
  scope: 'SYSTEM' | 'APP' | string;
}

export interface PermissionCatalog {
  systemPermissions: PermissionCatalogItem[];
  appPermissions: PermissionCatalogItem[];
}

export interface RoleAppPermission {
  appId: string;
  appCode: string;
  appName: string;
  appColor?: string | null;
  permissions: string[];
  rowScope: 'INHERIT' | 'ALL' | 'OWNER_ONLY' | string;
}

export interface SysRole {
  id: string;
  code: string;
  name: string;
  description?: string | null;
  color: string;
  isSystem: boolean;
  isActive: boolean;
  allAppsAccess: boolean;
  defaultAppPermissions: string[];
  systemPermissions: string[];
  appPermissions: RoleAppPermission[];
  userCount: number;
  createdAt?: string;
  updatedAt?: string;
}

export interface AppRoleMatrixItem {
  roleId: string;
  roleCode: string;
  roleName: string;
  roleColor: string;
  isSystem: boolean;
  allAppsAccess: boolean;
  hasAccess: boolean;
  permissions: string[];
  rowScope: 'INHERIT' | 'ALL' | 'OWNER_ONLY' | string;
}

export interface EffectiveTablePermission {
  canView: boolean;
  canCreate: boolean;
  canEdit: boolean;
  canDelete: boolean;
  canExport: boolean;
  canApprove: boolean;
  rowScope: 'INHERIT' | 'ALL' | 'OWNER_ONLY' | string;
}

export interface RoleTablePermissionItem {
  roleId: string;
  roleCode: string;
  roleName: string;
  roleColor: string;
  isSystem: boolean;
  allAppsAccess: boolean;
  appId: string;
  tableId: string;
  tableName: string;
  tableDisplayName: string;
  hasExplicitRule: boolean;
  canView: boolean;
  canCreate: boolean;
  canEdit: boolean;
  canDelete: boolean;
  canExport: boolean;
  canApprove: boolean;
  rowScope: 'INHERIT' | 'ALL' | 'OWNER_ONLY' | string;
}

export interface SysUser {
  id: string;
  username: string;
  fullName: string;
  email?: string | null;
  employeeCode?: string | null;
  department: string;
  orgUnitId?: string | null;
  roleCode: string;
  roleCodes?: string[];
  roleNames?: string[];
  isAdmin?: boolean;
  allowedApps: string[];
  permissions: string[];
  systemPermissions?: string[];
  appPermissions?: Record<string, string[]>;
  appRowScopes?: Record<string, string>;
  tablePermissions?: Record<string, EffectiveTablePermission>;
  ssoProvider?: string | null;
  ssoSubject?: string | null;
  isActive: boolean;
}

export interface SsoProviderPublic {
  id: string;
  code: string;
  providerType: 'GOOGLE' | 'MICROSOFT' | 'CUSTOM_OIDC' | string;
  displayName: string;
  isEnabled: boolean;
  isConfigured: boolean;
  buttonColor: string;
  orderIndex: number;
}

export interface SsoProviderAdmin extends SsoProviderPublic {
  clientId?: string | null;
  hasClientSecret: boolean;
  tenantId?: string | null;
  issuerUrl?: string | null;
  authorizationUrl?: string | null;
  tokenUrl?: string | null;
  userinfoUrl?: string | null;
  scopes: string;
  allowedDomains: string[];
  autoProvision: boolean;
  defaultRoleCode: string;
}

export interface DemoAccount {
  username: string;
  fullName: string;
  roleCode: string;
  roleCodes?: string[];
  roleNames?: string[];
  department: string;
  allowedApps: string[];
}

export interface SysNotification {
  id: string;
  recipientUsername: string;
  title: string;
  message?: string | null;
  appCode?: string | null;
  recordId?: string | null;
  category?: string;
  linkUrl?: string | null;
  senderUsername?: string | null;
  priority?: string;
  isRead: boolean;
  createdAt: string;
}

export interface UserSettings {
  userId: string;
  themeMode: 'light' | 'dark' | 'auto' | string;
  accentColor: string;
  compactMode: boolean;
  language: string;
  notifyInApp: boolean;
  notifySound: boolean;
  notifyDesktop: boolean;
  notifyChat: boolean;
  notifyTaskAssigned: boolean;
  notifyApproval: boolean;
  notifyMentions: boolean;
  emailDigest: 'INSTANT' | 'DAILY' | 'OFF' | string;
  chatEnterToSend: boolean;
  notifyEmail?: boolean;
  notifyTelegram?: boolean;
  telegramChatId?: string | null;
  notifyZalo?: boolean;
  zaloUserId?: string | null;
  updatedAt?: string;
}

export interface NotificationChannel {
  id: string;
  ownerScope: 'SYSTEM' | 'USER' | string;
  ownerUserId?: string | null;
  channelType: 'GMAIL_SMTP' | 'TELEGRAM' | 'ZALO' | 'TEAMS' | 'SLACK' | 'WEBHOOK' | string;
  name: string;
  isEnabled: boolean;
  eventCategories: string[];
  config: Record<string, unknown>;
  hasSecret: boolean;
  updatedAt?: string;
}

export interface NotificationDeliveryLog {
  id: string;
  channelId: string;
  channelType: string;
  channelName: string;
  title: string;
  status: 'SUCCESS' | 'FAILED' | string;
  detail?: string | null;
  createdAt: string;
}

export interface SystemSettings {
  companyName: string;
  workspaceTitle: string;
  logoUrl?: string;
  brandIcon?: string;
  faviconUrl?: string;
  announcementBanner: string;
  allowChatFileUpload: boolean;
  allowTaskFromChat: boolean;
  defaultTaskAppCode: string;
  sessionTimeoutDays: number;
}

export interface ChatUserSummary {
  id: string;
  username: string;
  fullName: string;
  department: string;
  roleCode: string;
}

export interface ChecklistItem {
  id: string;
  text: string;
  done: boolean;
}

export interface AppTask {
  id: string;
  taskCode: string;
  title: string;
  description?: string | null;
  status: 'TODO' | 'IN_PROGRESS' | 'REVIEW' | 'DONE' | 'CANCELLED' | string;
  priority: 'LOW' | 'MEDIUM' | 'HIGH' | 'URGENT' | string;
  assigneeUsername: string;
  assigneeFullName?: string | null;
  assignerUsername: string;
  assignerFullName?: string | null;
  watchers: string[];
  dueDate?: string | null;
  progress: number;
  checklist: ChecklistItem[];
  channelId?: string | null;
  channelName?: string | null;
  messageId?: string | null;
  linkedAppCode?: string | null;
  linkedRecordId?: string | null;
  completedAt?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface ChatAttachment {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  url: string;
}

export interface ChatChannel {
  id: string;
  name: string;
  description?: string | null;
  channelType: 'PUBLIC' | 'GROUP' | 'DIRECT' | 'DEPARTMENT' | string;
  avatarColor: string;
  linkedAppCode?: string | null;
  createdBy: string;
  lastMessageAt: string;
  lastMessagePreview?: string | null;
  memberCount: number;
  unreadCount: number;
  isMuted: boolean;
  members: ChatUserSummary[];
}

export interface ChatMessage {
  id: string;
  channelId: string;
  senderUsername: string;
  senderFullName?: string | null;
  senderRoleCode?: string | null;
  messageType: 'TEXT' | 'TASK_CARD' | 'FILE' | 'SYSTEM' | 'RECORD_REF' | string;
  content: string;
  replyToId?: string | null;
  replyToPreview?: string | null;
  replyToSender?: string | null;
  taskId?: string | null;
  task?: AppTask | null;
  attachments: ChatAttachment[];
  mentions: string[];
  reactions: Record<string, string[]>;
  isPinned: boolean;
  isEdited: boolean;
  createdAt: string;
}

export interface AppAuditLog {
  id: string;
  appId: string;
  tableId: string;
  recordId: string;
  action: string;
  actor: string;
  oldData?: Record<string, unknown> | null;
  newData?: Record<string, unknown> | null;
  summary?: string | null;
  createdAt: string;
}

export interface TableAnalytics {
  tableId: string;
  totalRecords: number;
  validRecords: number;
  errorRecords: number;
  approvalBreakdown?: Record<string, number>;
  distributions: Array<{
    columnCode: string;
    columnName: string;
    items: Array<{ code: string; label: string; color: string; count: number }>;
  }>;
  numericSummaries: Array<{
    columnCode: string;
    columnName: string;
    dataType: string;
    sum: number;
    avg: number;
    min: number;
    max: number;
  }>;
}

export interface QueryCondition {
  field: string;
  op: 'contains' | 'eq' | 'neq' | 'gt' | 'gte' | 'lt' | 'lte' | 'between' | 'in' | 'is_empty' | 'not_empty' | 'date_preset';
  value?: string;
  value2?: string;
}

export interface StructuredQueryFilter {
  logic: 'AND' | 'OR';
  conditions: QueryCondition[];
}

export interface SysSavedView {
  id: string;
  appId: string;
  tableId: string;
  name: string;
  filters: Record<string, string> | StructuredQueryFilter;
  sortField?: string | null;
  sortDir: string;
  isShared: boolean;
  createdBy: string;
}

export type WidgetType =
  | 'KPI_COUNT'
  | 'KPI_SUM'
  | 'KPI_AVG'
  | 'BAR_CHART'
  | 'DONUT_CHART'
  | 'LINE_CHART'
  | 'RECENT_LIST';

export interface AppWidget {
  id: string;
  appId: string;
  appCode?: string | null;
  tableId: string;
  tableDisplayName?: string | null;
  title: string;
  widgetType: WidgetType | string;
  metricField?: string | null;
  groupField?: string | null;
  filterField?: string | null;
  filterValue?: string | null;
  colSpan: number;
  color: string;
  orderIndex: number;
  isVisible: boolean;
  numericValue: number;
  formattedValue: string;
  subLabel?: string | null;
  chartItems: Array<{ code: string; label: string; color: string; count: number }>;
  recentRows: AppRecord[];
}

export interface EngineStatus {
  dotnet: { online: boolean; version: string; service: string };
  desktop: { role: string; path: string };
}

export interface RecordComment {
  id: string;
  appId: string;
  tableId: string;
  recordId: string;
  author: string;
  content: string;
  mentions: string[];
  createdAt: string;
}

export interface PendingApprovalItem {
  recordId: string;
  recordKey?: string | null;
  appId: string;
  appCode: string;
  appName: string;
  appColor: string;
  tableId: string;
  tableDisplayName: string;
  createdBy: string;
  createdAt: string;
  summaryText: string;
  currentStepOrder?: number;
  currentStepName?: string | null;
  totalSteps?: number;
}

export interface SysApiKey {
  id: string;
  name: string;
  keyPrefix: string;
  appId: string;
  appCode?: string | null;
  tableId?: string | null;
  scopes: string[];
  isActive: boolean;
  lastUsedAt?: string | null;
  createdBy: string;
  createdAt: string;
}

export interface AppTemplateSummary {
  templateKey: string;
  code: string;
  name: string;
  description: string;
  department: string;
  category: string;
  icon: string;
  color: string;
  bgColor: string;
  highlights: string[];
  features: string[];
  moduleCount: number;
  tableCount: number;
  sampleRecordCount: number;
  widgetCount: number;
}

export interface WorkflowHistoryEntry {
  stepOrder: number;
  stepName: string;
  action: 'SUBMIT' | 'APPROVE' | 'REJECT' | string;
  actor: string;
  actorRole?: string | null;
  note?: string | null;
  at: string;
}

export interface WorkflowStep {
  stepOrder: number;
  name: string;
  approverRoles: string[];
  approverUsers: string[];
  slaHours: number;
  requireCommentOnReject: boolean;
  approvalMode?: 'ANY' | 'ALL' | 'QUORUM' | string;
  quorumCount?: number;
  allowReturnToPrevious?: boolean;
}

export interface WorkflowStepApprovalVote {
  stepOrder: number;
  actor: string;
  actorRole?: string | null;
  delegatedFrom?: string | null;
  at: string;
}

export interface SysWorkflowDelegation {
  id: string;
  delegatorUsername: string;
  delegateUsername: string;
  appId?: string | null;
  startsAt: string;
  expiresAt: string;
  reason?: string | null;
  isActive: boolean;
  createdAt: string;
}

export interface BusinessHoliday {
  id: string;
  holidayDate: string;
  name: string;
  isRecurring: boolean;
}

export interface GlobalSearchHit {
  kind: 'APP' | 'RECORD' | 'CHAT' | 'ATTACHMENT' | string;
  id: string;
  title: string;
  subtitle: string;
  url: string;
  badge?: string | null;
  timestamp?: string | null;
}

export interface GlobalSearchResult {
  query: string;
  apps: GlobalSearchHit[];
  records: GlobalSearchHit[];
  chats: GlobalSearchHit[];
  attachments: GlobalSearchHit[];
}

export interface AppWorkflow {
  id: string;
  name: string;
  description?: string | null;
  appId: string;
  appCode?: string | null;
  appName?: string | null;
  tableId: string;
  tableDisplayName?: string | null;
  triggerType: string;
  conditionField?: string | null;
  conditionOperator: string;
  conditionValue?: string | null;
  steps: WorkflowStep[];
  isActive: boolean;
  pendingCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface AiSystemSettings {
  isEnabled: boolean;
  defaultProviderId?: string | null;
  fallbackToRuleEngine: boolean;
  maskPiiBeforeCloudLlm?: boolean;
  monthlyTokenBudget?: number;
  preferLocalForSensitive?: boolean;
  temperature: number;
  maxTokens: number;
  timeoutSeconds: number;
  systemPromptPrefix: string;
  enabledFeatures: string[];
  updatedAt?: string;
}

export interface AiProvider {
  id: string;
  code: string;
  providerType: 'GEMINI' | 'OPENAI' | 'ANTHROPIC' | 'OPENAI_COMPATIBLE' | string;
  displayName: string;
  isEnabled: boolean;
  isDefault: boolean;
  hasApiKey: boolean;
  apiKeyPreview?: string | null;
  baseUrl: string;
  model: string;
  suggestedModels: string[];
  temperature?: number | null;
  maxTokens?: number | null;
  timeoutSeconds: number;
  organizationId?: string | null;
  orderIndex: number;
  isConfigured: boolean;
  updatedAt?: string;
}

export interface AiUsageLog {
  id: string;
  providerId: string;
  providerName: string;
  featureCode: string;
  model: string;
  username: string;
  status: 'SUCCESS' | 'FAILED' | string;
  latencyMs: number;
  promptTokens: number;
  completionTokens: number;
  message?: string | null;
  createdAt: string;
}

export interface AiCapability {
  code: string;
  name: string;
  module: string;
  status: 'INTEGRATED' | 'PROPOSED' | string;
  description: string;
  benefit: string;
  targetRoute: string;
  recommendedTier: string;
}

export interface AiStatus {
  isEnabled: boolean;
  isConfigured: boolean;
  activeProviderId?: string | null;
  activeProviderName?: string | null;
  activeProviderType?: string | null;
  activeModel?: string | null;
  fallbackToRuleEngine: boolean;
  maskPiiBeforeCloudLlm?: boolean;
  monthlyTokenBudget?: number;
  monthlyTokensUsed?: number;
  preferLocalForSensitive?: boolean;
  enabledFeatures: string[];
}

export interface AiTestResult {
  ok: boolean;
  providerName: string;
  model: string;
  latencyMs: number;
  message: string;
  replyPreview?: string | null;
}

export interface SysOrgUnit {
  id: string;
  code: string;
  name: string;
  parentId?: string | null;
  managerUsername?: string | null;
  orderIndex: number;
  isActive: boolean;
  createdAt?: string;
  updatedAt?: string;
}

export interface AttachmentInfo {
  id: string;
  appId?: string | null;
  tableId?: string | null;
  recordId?: string | null;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  uploadedBy: string;
  createdAt: string;
}

export interface StorageQuota {
  id: string;
  scopeType: 'GLOBAL' | 'APP' | 'USER' | string;
  scopeId: string;
  quotaMb: number;
  updatedBy: string;
  updatedAt: string;
}

export interface StorageStats {
  totalUsedBytes: number;
  totalFiles: number;
  quotas: StorageQuota[];
  usedByApp: Record<string, number>;
  usedByUser: Record<string, number>;
}
