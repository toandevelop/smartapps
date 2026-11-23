import type { AppRecord, SysColumn } from './types';

export interface MondayChoicePreset {
  code: string;
  label: string;
  bg: string;
  color: string;
}

export const MONDAY_STATUS_PRESETS: MondayChoicePreset[] = [
  { code: 'DONE', label: 'Hoàn thành', bg: '#00c875', color: '#ffffff' },
  { code: 'WORKING', label: 'Đang thực hiện', bg: '#fdab3d', color: '#ffffff' },
  { code: 'REVIEW', label: 'Chờ duyệt', bg: '#579bfc', color: '#ffffff' },
  { code: 'STUCK', label: 'Vướng mắc', bg: '#e2445c', color: '#ffffff' },
  { code: 'PLANNED', label: 'Kế hoạch', bg: '#a25ddc', color: '#ffffff' },
  { code: 'NOT_STARTED', label: 'Chưa bắt đầu', bg: '#c4c4c4', color: '#1f2937' },
];

export const MONDAY_PRIORITY_PRESETS: MondayChoicePreset[] = [
  { code: 'CRITICAL', label: 'Khẩn cấp 🔥', bg: '#e2445c', color: '#ffffff' },
  { code: 'HIGH', label: 'Cao', bg: '#401694', color: '#ffffff' },
  { code: 'MEDIUM', label: 'Trung bình', bg: '#5559df', color: '#ffffff' },
  { code: 'LOW', label: 'Thấp', bg: '#579bfc', color: '#ffffff' },
];

export const MONDAY_COLOR_SWATCHES: string[] = [
  '#00c875',
  '#9cd326',
  '#ffcb00',
  '#fdab3d',
  '#ff642e',
  '#e2445c',
  '#ff158a',
  '#a25ddc',
  '#784bd1',
  '#5559df',
  '#579bfc',
  '#0086c0',
  '#00c2cb',
  '#7f5347',
  '#64748b',
  '#333333',
];

export interface ColumnTypeMeta {
  type: string;
  label: string;
  shortBadge: string;
  category: 'BASIC' | 'WORK_OS' | 'RELATION' | 'MEDIA_ADVANCED';
  accent: string;
  description: string;
}

export const MONDAY_COLUMN_TYPES: ColumnTypeMeta[] = [
  // 1. Work OS (monday.com signature columns)
  { type: 'STATUS', label: 'Trạng thái màu (STATUS)', shortBadge: '◉', category: 'WORK_OS', accent: '#00c875', description: 'Ô màu trạng thái trực quan kiểu monday.com (Hoàn thành, Đang làm, Vướng mắc...)' },
  { type: 'PRIORITY', label: 'Mức độ ưu tiên (PRIORITY)', shortBadge: '⚑', category: 'WORK_OS', accent: '#e2445c', description: 'Gắn cờ ưu tiên (Khẩn cấp, Cao, Trung bình, Thấp) đổi nhanh 1 chạm' },
  { type: 'PROGRESS', label: 'Thanh tiến độ % (PROGRESS)', shortBadge: '▰', category: 'WORK_OS', accent: '#579bfc', description: 'Thanh kéo tiến độ 0–100% tự động đổi màu theo mức hoàn thành' },
  { type: 'RATING', label: 'Đánh giá sao (RATING)', shortBadge: '★', category: 'WORK_OS', accent: '#ffcb00', description: 'Chấm điểm 1–5 sao vàng trực tiếp trên bảng hoặc biểu mẫu' },
  { type: 'TAGS', label: 'Thẻ Hashtag (TAGS)', shortBadge: '#', category: 'WORK_OS', accent: '#a25ddc', description: 'Gắn nhiều thẻ #tag phân loại linh hoạt với màu sắc tự động' },
  { type: 'COLOR', label: 'Bảng chọn màu (COLOR)', shortBadge: '🎨', category: 'WORK_OS', accent: '#ff158a', description: 'Chọn mã màu thương hiệu / phân loại từ bảng màu Work OS' },
  { type: 'LINK', label: 'Liên kết Web / URL (LINK)', shortBadge: '🔗', category: 'WORK_OS', accent: '#0086c0', description: 'Đường dẫn website, tài liệu đám mây bấm mở tab mới an toàn' },

  // 2. Basic data columns
  { type: 'STRING', label: 'Văn bản ngắn (STRING)', shortBadge: 'Aa', category: 'BASIC', accent: '#1a73e8', description: 'Chuỗi văn bản, mã định danh, hỗ trợ quét QR/Barcode' },
  { type: 'TEXTAREA', label: 'Đoạn văn dài (TEXTAREA)', shortBadge: '¶', category: 'BASIC', accent: '#1a73e8', description: 'Ghi chú nhiều dòng, diễn giải chi tiết' },
  { type: 'NUMBER', label: 'Con số (NUMBER)', shortBadge: '123', category: 'BASIC', accent: '#0f766e', description: 'Số lượng, chỉ số, tự động tổng hợp chân bảng' },
  { type: 'CURRENCY', label: 'Tiền tệ VNĐ (CURRENCY)', shortBadge: '₫', category: 'BASIC', accent: '#16a34a', description: 'Số tiền định dạng VNĐ, hỗ trợ đọc số tiền bằng chữ' },
  { type: 'DATE', label: 'Ngày tháng (DATE)', shortBadge: '📅', category: 'BASIC', accent: '#ea580c', description: 'Ngày (yyyy-MM-dd), dùng cho Lịch & Timeline Gantt' },
  { type: 'DATETIME', label: 'Ngày & Giờ (DATETIME)', shortBadge: '🕓', category: 'BASIC', accent: '#ea580c', description: 'Thời điểm chính xác cả ngày lẫn giờ phút' },
  { type: 'TIME', label: 'Giờ phút (TIME)', shortBadge: '⏰', category: 'BASIC', accent: '#ea580c', description: 'Khung giờ ca làm việc, giờ hẹn (HH:mm)' },
  { type: 'BOOLEAN', label: 'Đúng / Sai (BOOLEAN)', shortBadge: '✓', category: 'BASIC', accent: '#10b981', description: 'Công tắc bật/tắt hoặc checkbox hoàn thành' },
  { type: 'EMAIL', label: 'Hòm thư (EMAIL)', shortBadge: '@', category: 'BASIC', accent: '#0284c7', description: 'Địa chỉ email có kiểm tra định dạng tự động' },
  { type: 'TEL', label: 'Điện thoại (TEL)', shortBadge: '☎', category: 'BASIC', accent: '#0284c7', description: 'Số điện thoại liên hệ hỗ trợ gọi nhanh' },

  // 3. Relation & People columns
  { type: 'USER', label: 'Nhân sự phụ trách (USER)', shortBadge: '👤', category: 'RELATION', accent: '#7c3aed', description: 'Chọn người dùng hệ thống hiển thị kèm Avatar tròn' },
  { type: 'SELECT', label: 'Danh mục đơn (SELECT)', shortBadge: '▾', category: 'RELATION', accent: '#2563eb', description: 'Chọn 1 giá trị từ Danh mục dùng chung' },
  { type: 'MULTI_SELECT', label: 'Chọn nhiều danh mục (MULTI_SELECT)', shortBadge: '☷', category: 'RELATION', accent: '#2563eb', description: 'Chọn nhiều mục từ Danh mục dùng chung' },
  { type: 'LOOKUP', label: 'Liên kết Đa App (LOOKUP)', shortBadge: '⇄', category: 'RELATION', accent: '#0369a1', description: 'Tham chiếu bản ghi từ Bảng/Ứng dụng khác (hỗ trợ lọc phụ thuộc)' },
  { type: 'SUB_TABLE', label: 'Bảng con chi tiết (SUB_TABLE)', shortBadge: '⊞', category: 'RELATION', accent: '#4f46e5', description: 'Bảng dòng chi tiết Master–Detail có công thức riêng' },

  // 4. Media & Advanced columns
  { type: 'AUTO_NUMBER', label: 'Mã tự sinh (AUTO_NUMBER)', shortBadge: '#A', category: 'MEDIA_ADVANCED', accent: '#475569', description: 'Sinh mã phiếu tự động theo mẫu (VD: HD-{YYYY}-0001)' },
  { type: 'IMAGE', label: 'Hình ảnh (IMAGE)', shortBadge: '🖼', category: 'MEDIA_ADVANCED', accent: '#db2777', description: 'Tải ảnh đại diện, hiển thị trên Thư viện (Gallery View)' },
  { type: 'FILE', label: 'Tệp đính kèm (FILE)', shortBadge: '📎', category: 'MEDIA_ADVANCED', accent: '#475569', description: 'Đính kèm 1 tài liệu PDF, Word, Excel' },
  { type: 'MULTI_FILE', label: 'Nhiều tệp đính kèm (MULTI_FILE)', shortBadge: '🗂', category: 'MEDIA_ADVANCED', accent: '#475569', description: 'Đính kèm bộ hồ sơ nhiều tệp kèm link chia sẻ' },
  { type: 'SIGNATURE', label: 'Chữ ký tay (SIGNATURE)', shortBadge: '✍', category: 'MEDIA_ADVANCED', accent: '#0f172a', description: 'Khung ký tên trực tiếp trên màn hình cảm ứng/chuột' },
];

const COLUMN_META_MAP = new Map<string, ColumnTypeMeta>(
  MONDAY_COLUMN_TYPES.map((m) => [m.type, m])
);

export function getColumnTypeMeta(dataType?: string | null): ColumnTypeMeta {
  const key = (dataType || 'STRING').trim().toUpperCase();
  return (
    COLUMN_META_MAP.get(key) || {
      type: key,
      label: key,
      shortBadge: 'Aa',
      category: 'BASIC',
      accent: '#1a73e8',
      description: '',
    }
  );
}

export function resolveMondayStatusStyle(
  rawVal: unknown,
  categoryColor?: string | null,
  categoryLabel?: string | null
): MondayChoicePreset {
  const raw = String(rawVal ?? '').trim();
  if (!raw) {
    return { code: '', label: '—', bg: '#c4c4c4', color: '#1f2937' };
  }
  const upper = raw.toUpperCase();
  const preset = MONDAY_STATUS_PRESETS.find(
    (p) => p.code === upper || p.label.toUpperCase() === upper
  );
  if (preset) {
    return { ...preset, label: categoryLabel || preset.label };
  }

  if (categoryColor && /^#[0-9a-fA-F]{6}$/.test(categoryColor.trim())) {
    return {
      code: raw,
      label: categoryLabel || raw,
      bg: categoryColor.trim(),
      color: '#ffffff',
    };
  }

  if (/^(DONE|APPROVED|DA_DUYET|DA_KY_SO|HOAN_THANH|HOAN_TAT|PAID|DA_THANH_TOAN|ACTIVE|DAT|NHAP_KHO)$/i.test(upper)) {
    return { code: raw, label: categoryLabel || raw, bg: '#00c875', color: '#ffffff' };
  }
  if (/^(WORKING|IN_PROGRESS|DANG_XU_LY|DANG_GIAO|DANG_SX|DONG_GOI|PENDING|CHO_DUYET|CHO_KY|UNPAID|CHUA_THANH_TOAN)$/i.test(upper)) {
    return { code: raw, label: categoryLabel || raw, bg: '#fdab3d', color: '#ffffff' };
  }
  if (/^(STUCK|ERROR|REJECTED|TU_CHOI|LOI|LOI_QC|CANCELLED|HUY|THU_HOI|HOAN_TRA|FAILED|QUA_HAN)$/i.test(upper)) {
    return { code: raw, label: categoryLabel || raw, bg: '#e2445c', color: '#ffffff' };
  }
  if (/^(REVIEW|MOI|NEW|CHUAN_BI|DU_THAO|DRAFT)$/i.test(upper)) {
    return { code: raw, label: categoryLabel || raw, bg: '#579bfc', color: '#ffffff' };
  }
  return { code: raw, label: categoryLabel || raw, bg: '#784bd1', color: '#ffffff' };
}

export function resolveMondayPriorityStyle(rawVal: unknown, categoryLabel?: string | null): MondayChoicePreset {
  const raw = String(rawVal ?? '').trim();
  if (!raw) {
    return { code: '', label: '—', bg: '#c4c4c4', color: '#1f2937' };
  }
  const upper = raw.toUpperCase();
  const preset = MONDAY_PRIORITY_PRESETS.find(
    (p) => p.code === upper || p.label.toUpperCase() === upper
  );
  if (preset) {
    return { ...preset, label: categoryLabel || preset.label };
  }
  if (/^(CRITICAL|URGENT|HOA_TOC|KHAN_CAP)$/i.test(upper)) {
    return { code: raw, label: categoryLabel || 'Khẩn cấp 🔥', bg: '#e2445c', color: '#ffffff' };
  }
  if (/^(HIGH|CAO|KHAN)$/i.test(upper)) {
    return { code: raw, label: categoryLabel || 'Cao', bg: '#401694', color: '#ffffff' };
  }
  if (/^(MEDIUM|NORMAL|TRUNG_BINH|THUONG)$/i.test(upper)) {
    return { code: raw, label: categoryLabel || 'Trung bình', bg: '#5559df', color: '#ffffff' };
  }
  if (/^(LOW|THAP)$/i.test(upper)) {
    return { code: raw, label: categoryLabel || 'Thấp', bg: '#579bfc', color: '#ffffff' };
  }
  return { code: raw, label: categoryLabel || raw, bg: '#5559df', color: '#ffffff' };
}

export function clampRatingValue(rawVal: unknown): number {
  if (rawVal === null || rawVal === undefined || rawVal === '') return 0;
  const n = Number(rawVal);
  if (!Number.isFinite(n)) return 0;
  return Math.max(0, Math.min(5, Math.round(n)));
}

export function clampProgressValue(rawVal: unknown): number {
  if (rawVal === null || rawVal === undefined || rawVal === '') return 0;
  const n = Number(rawVal);
  if (!Number.isFinite(n)) return 0;
  return Math.max(0, Math.min(100, Math.round(n)));
}

export function getMondayProgressColor(pct: number): string {
  if (pct >= 100) return '#00c875';
  if (pct >= 70) return '#579bfc';
  if (pct >= 35) return '#fdab3d';
  return '#e2445c';
}

export function parseTagsValue(rawVal: unknown): string[] {
  if (!rawVal) return [];
  const list: string[] = Array.isArray(rawVal)
    ? rawVal.map((x) => String(x ?? '').trim())
    : typeof rawVal === 'string'
    ? (() => {
        const trimmed = rawVal.trim();
        if (trimmed.startsWith('[')) {
          try {
            const parsed = JSON.parse(trimmed);
            if (Array.isArray(parsed)) return parsed.map((x) => String(x ?? '').trim());
          } catch {
            // fall through
          }
        }
        return trimmed.split(/[,;]+/).map((s) => s.trim());
      })()
    : [String(rawVal).trim()];

  const seen = new Set<string>();
  const result: string[] = [];
  for (const item of list) {
    const clean = item.replace(/^#+/, '').trim();
    if (!clean) continue;
    const key = clean.toLowerCase();
    if (!seen.has(key)) {
      seen.add(key);
      result.push(clean);
    }
  }
  return result;
}

export function getTagColor(tag: string): { bg: string; color: string } {
  const palette = [
    { bg: '#e0f2fe', color: '#0369a1' },
    { bg: '#dcfce7', color: '#15803d' },
    { bg: '#fef3c7', color: '#b45309' },
    { bg: '#fce7f3', color: '#be185d' },
    { bg: '#ede9fe', color: '#6d28d9' },
    { bg: '#ffedd5', color: '#c2410c' },
  ];
  let hash = 0;
  for (let i = 0; i < tag.length; i++) {
    hash = (hash * 31 + tag.charCodeAt(i)) >>> 0;
  }
  return palette[hash % palette.length];
}

export function normalizeSafeLinkUrl(rawVal: unknown): { href: string; label: string } | null {
  const raw = String(rawVal ?? '').trim();
  if (!raw) return null;
  if (/^(javascript|data|vbscript):/i.test(raw)) return null;
  const href = /^https?:\/\//i.test(raw) || raw.startsWith('/') ? raw : `https://${raw}`;
  const label = raw.replace(/^https?:\/\/(www\.)?/i, '').replace(/\/$/, '');
  return { href, label: label || raw };
}

export function formatDateTimeDisplay(rawVal: unknown, dataType: string): string {
  const raw = String(rawVal ?? '').trim();
  if (!raw) return '';
  const dt = dataType.toUpperCase();
  if (dt === 'TIME') {
    return raw.slice(0, 5);
  }
  if (dt === 'DATETIME') {
    const m = raw.match(/^(\d{4})-(\d{2})-(\d{2})[T\s](\d{2}:\d{2})/);
    if (m) return `${m[3]}/${m[2]}/${m[1]} ${m[4]}`;
  }
  return raw;
}

export function getAvatarInitialsAndColor(usernameOrName: unknown): { initials: string; bg: string; label: string } {
  const raw = String(usernameOrName ?? '').trim();
  if (!raw) return { initials: '?', bg: '#94a3b8', label: '—' };
  const clean = raw.replace(/^@/, '');
  const parts = clean.split(/[\s._-]+/).filter(Boolean);
  const initials =
    parts.length >= 2
      ? `${parts[0][0]}${parts[parts.length - 1][0]}`.toUpperCase()
      : clean.slice(0, 2).toUpperCase();
  const colors = ['#00c875', '#579bfc', '#a25ddc', '#fdab3d', '#e2445c', '#0086c0', '#5559df', '#ff642e'];
  let hash = 0;
  for (let i = 0; i < clean.length; i++) hash = (hash * 31 + clean.charCodeAt(i)) >>> 0;
  return { initials, bg: colors[hash % colors.length], label: raw };
}

export interface ColumnFooterSummary {
  kind: 'SUM' | 'AVG_PROGRESS' | 'AVG_RATING' | 'STATUS_BAR' | 'EMPTY';
  text?: string;
  numericValue?: number;
  segments?: Array<{ color: string; label: string; count: number; pct: number }>;
}

export function computeMondayColumnSummary(
  records: AppRecord[],
  column: SysColumn
): ColumnFooterSummary {
  if (!records || records.length === 0) return { kind: 'EMPTY' };
  const dt = (column.dataType || 'STRING').toUpperCase();

  if (dt === 'NUMBER' || dt === 'CURRENCY') {
    let sum = 0;
    let count = 0;
    for (const r of records) {
      const v = r.data?.[column.columnCode];
      if (v !== null && v !== undefined && v !== '') {
        const n = Number(v);
        if (Number.isFinite(n)) {
          sum += n;
          count++;
        }
      }
    }
    if (count === 0) return { kind: 'EMPTY' };
    return {
      kind: 'SUM',
      numericValue: sum,
      text:
        dt === 'CURRENCY'
          ? `${new Intl.NumberFormat('vi-VN').format(Math.round(sum))} ₫`
          : new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 2 }).format(sum),
    };
  }

  if (dt === 'PROGRESS') {
    let sum = 0;
    let count = 0;
    for (const r of records) {
      const v = r.data?.[column.columnCode];
      if (v !== null && v !== undefined && v !== '') {
        sum += clampProgressValue(v);
        count++;
      }
    }
    if (count === 0) return { kind: 'EMPTY' };
    const avg = Math.round(sum / count);
    return {
      kind: 'AVG_PROGRESS',
      numericValue: avg,
      text: `TB ${avg}%`,
    };
  }

  if (dt === 'RATING') {
    let sum = 0;
    let count = 0;
    for (const r of records) {
      const v = r.data?.[column.columnCode];
      const rating = clampRatingValue(v);
      if (rating > 0) {
        sum += rating;
        count++;
      }
    }
    if (count === 0) return { kind: 'EMPTY' };
    const avg = Number((sum / count).toFixed(1));
    return {
      kind: 'AVG_RATING',
      numericValue: avg,
      text: `★ ${avg}/5`,
    };
  }

  if (dt === 'STATUS' || dt === 'PRIORITY' || dt === 'SELECT' || column.refCategoryCode) {
    const counts = new Map<string, { color: string; label: string; count: number }>();
    let total = 0;
    for (const r of records) {
      const v = r.data?.[column.columnCode];
      if (v === null || v === undefined || v === '') continue;
      const key = String(v).trim();
      if (!key) continue;
      total++;
      const existing = counts.get(key);
      if (existing) {
        existing.count++;
      } else {
        const style =
          dt === 'PRIORITY'
            ? resolveMondayPriorityStyle(key)
            : resolveMondayStatusStyle(key);
        counts.set(key, { color: style.bg, label: style.label, count: 1 });
      }
    }
    if (total === 0) return { kind: 'EMPTY' };
    const segments = Array.from(counts.values()).map((s) => ({
      ...s,
      pct: Math.round((s.count / total) * 100),
    }));
    return { kind: 'STATUS_BAR', segments };
  }

  return { kind: 'EMPTY' };
}
