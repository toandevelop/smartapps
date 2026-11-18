import { SysColumn } from '@/lib/types';

export interface AuditActionMeta {
  label: string;
  badgeClass: string;
}

export interface AuditFieldDiff {
  key: string;
  label: string;
  oldVal: unknown;
  newVal: unknown;
  kind: 'changed' | 'added' | 'removed';
}

const TASK_FIELD_LABELS: Record<string, string> = {
  taskCode: 'Mã công việc',
  title: 'Tiêu đề',
  status: 'Trạng thái',
  priority: 'Độ ưu tiên',
  assigneeUsername: 'Người thực hiện',
  dueDate: 'Hạn hoàn thành',
  progress: 'Tiến độ (%)',
};

export function getAuditActionMeta(action?: string): AuditActionMeta {
  const code = (action || '').trim().toUpperCase();
  switch (code) {
    case 'CREATE':
      return { label: 'Tạo mới', badgeClass: 'badge-success' };
    case 'UPDATE':
      return { label: 'Chỉnh sửa', badgeClass: 'badge-info' };
    case 'SUBMIT_APPROVAL':
      return { label: 'Trình duyệt', badgeClass: 'badge-warning' };
    case 'APPROVE':
      return { label: 'Phê duyệt', badgeClass: 'badge-success' };
    case 'REJECT':
      return { label: 'Từ chối', badgeClass: 'badge-danger' };
    case 'SOFT_DELETE':
      return { label: 'Vào thùng rác', badgeClass: 'badge-danger' };
    case 'DELETE':
      return { label: 'Đã xóa', badgeClass: 'badge-danger' };
    case 'RESTORE':
      return { label: 'Khôi phục', badgeClass: 'badge-success' };
    default:
      return { label: code || 'Hoạt động', badgeClass: 'badge-info' };
  }
}

export function formatAuditValue(val: unknown): string {
  if (val === undefined || val === null || val === '') return '—';
  if (typeof val === 'boolean') return val ? 'Có' : 'Không';
  if (typeof val === 'number') return Number.isFinite(val) ? val.toLocaleString('vi-VN') : String(val);
  if (Array.isArray(val)) {
    if (val.length === 0) return '—';
    return val.map((item) => formatAuditValue(item)).join(', ');
  }
  if (typeof val === 'object') {
    try {
      return JSON.stringify(val);
    } catch {
      return String(val);
    }
  }
  return String(val);
}

function isSameAuditValue(a: unknown, b: unknown): boolean {
  if ((a === undefined || a === null || a === '') && (b === undefined || b === null || b === '')) {
    return true;
  }
  if (a === b) return true;
  try {
    return JSON.stringify(a ?? null) === JSON.stringify(b ?? null);
  } catch {
    return String(a) === String(b);
  }
}

export function computeAuditFieldDiffs(
  action: string | undefined,
  oldData?: Record<string, unknown> | null,
  newData?: Record<string, unknown> | null,
  columns?: SysColumn[]
): AuditFieldDiff[] {
  const colLabelMap = new Map<string, string>();
  (columns || []).forEach((c) => {
    colLabelMap.set(c.columnCode, c.columnName);
  });
  const resolveLabel = (key: string) => colLabelMap.get(key) || TASK_FIELD_LABELS[key] || key;

  const act = (action || '').toUpperCase();
  const oldObj = oldData && typeof oldData === 'object' ? oldData : null;
  const newObj = newData && typeof newData === 'object' ? newData : null;

  if (act === 'CREATE' && newObj) {
    return Object.entries(newObj)
      .filter(([, v]) => v !== undefined && v !== null && v !== '')
      .map(([key, newVal]) => ({
        key,
        label: resolveLabel(key),
        oldVal: undefined,
        newVal,
        kind: 'added' as const,
      }));
  }

  if ((act === 'SOFT_DELETE' || act === 'DELETE') && oldObj && !newObj) {
    return Object.entries(oldObj)
      .filter(([, v]) => v !== undefined && v !== null && v !== '')
      .map(([key, oldVal]) => ({
        key,
        label: resolveLabel(key),
        oldVal,
        newVal: undefined,
        kind: 'removed' as const,
      }));
  }

  if (oldObj && newObj) {
    const keys = Array.from(new Set([...Object.keys(oldObj), ...Object.keys(newObj)]));
    const diffs: AuditFieldDiff[] = [];
    for (const key of keys) {
      const ov = oldObj[key];
      const nv = newObj[key];
      if (isSameAuditValue(ov, nv)) continue;
      const hadOld = ov !== undefined && ov !== null && ov !== '';
      const hasNew = nv !== undefined && nv !== null && nv !== '';
      diffs.push({
        key,
        label: resolveLabel(key),
        oldVal: ov,
        newVal: nv,
        kind: !hadOld && hasNew ? 'added' : hadOld && !hasNew ? 'removed' : 'changed',
      });
    }
    return diffs;
  }

  if (newObj) {
    return Object.entries(newObj)
      .filter(([, v]) => v !== undefined && v !== null && v !== '')
      .map(([key, newVal]) => ({
        key,
        label: resolveLabel(key),
        oldVal: undefined,
        newVal,
        kind: 'added' as const,
      }));
  }

  return [];
}

export function formatRelativeTimeVi(isoDate: string): string {
  const ts = new Date(isoDate).getTime();
  if (!Number.isFinite(ts)) return '';
  const diffSec = Math.max(0, Math.floor((Date.now() - ts) / 1000));
  if (diffSec < 60) return 'Vừa xong';
  const diffMin = Math.floor(diffSec / 60);
  if (diffMin < 60) return `${diffMin} phút trước`;
  const diffHr = Math.floor(diffMin / 60);
  if (diffHr < 24) return `${diffHr} giờ trước`;
  const diffDay = Math.floor(diffHr / 24);
  if (diffDay < 30) return `${diffDay} ngày trước`;
  return new Date(isoDate).toLocaleDateString('vi-VN');
}
