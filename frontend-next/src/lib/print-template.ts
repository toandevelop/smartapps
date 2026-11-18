export interface SysPrintTemplate {
  id: string;
  tableId: string;
  name: string;
  paperSize: 'A4' | 'A5' | 'K80' | 'LABEL_50X30' | string;
  orientation: 'portrait' | 'landscape' | string;
  htmlTemplate: string;
  cssStyles?: string | null;
  isDefault: boolean;
  watermarkDraft: boolean;
  requireApproved: boolean;
  createdBy?: string;
  createdAt?: string;
  updatedAt?: string;
}

export interface PrintRenderContext {
  appName: string;
  appDepartment?: string;
  tableName: string;
  recordKey: string;
  approvalStatus?: string;
  createdBy?: string;
  approvedBy?: string | null;
  data: Record<string, unknown>;
}

const DIGITS = ['không', 'một', 'hai', 'ba', 'bốn', 'năm', 'sáu', 'bảy', 'tám', 'chín'];
const UNITS = ['', 'nghìn', 'triệu', 'tỷ', 'nghìn tỷ', 'triệu tỷ'];

function readThreeDigits(num: number, full: boolean): string {
  const hundreds = Math.floor(num / 100);
  const tens = Math.floor((num % 100) / 10);
  const ones = num % 10;
  const parts: string[] = [];

  if (hundreds > 0 || full) {
    parts.push(`${DIGITS[hundreds]} trăm`);
  }
  if (tens > 1) {
    parts.push(`${DIGITS[tens]} mươi`);
    if (ones === 1) parts.push('mốt');
    else if (ones === 4) parts.push('tư');
    else if (ones === 5) parts.push('lăm');
    else if (ones > 0) parts.push(DIGITS[ones]);
  } else if (tens === 1) {
    parts.push('mười');
    if (ones === 5) parts.push('lăm');
    else if (ones > 0) parts.push(DIGITS[ones]);
  } else if (ones > 0) {
    if (hundreds > 0 || full) parts.push('lẻ');
    parts.push(DIGITS[ones]);
  }

  return parts.join(' ');
}

export function numberToVietnameseWords(rawAmount: unknown): string {
  const n = Math.round(Number(rawAmount));
  if (!Number.isFinite(n)) return '';
  if (n === 0) return 'Không đồng';
  const negative = n < 0;
  let abs = Math.abs(n);

  const groups: number[] = [];
  while (abs > 0) {
    groups.push(abs % 1000);
    abs = Math.floor(abs / 1000);
  }

  const words: string[] = [];
  for (let i = groups.length - 1; i >= 0; i--) {
    const g = groups[i];
    if (g === 0) continue;
    const isFull = i < groups.length - 1;
    words.push(`${readThreeDigits(g, isFull)} ${UNITS[i] || ''}`.trim());
  }

  const sentence = words.join(' ').replace(/\s+/g, ' ').trim();
  const capitalized = sentence.charAt(0).toUpperCase() + sentence.slice(1);
  return `${negative ? 'Âm ' : ''}${capitalized} đồng chẵn`;
}

export function escapeHtml(value: unknown): string {
  const s = value === null || value === undefined ? '' : String(value);
  return s
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

function formatPipe(val: unknown, pipe?: string): string {
  if (val === null || val === undefined || val === '') return '';
  const p = (pipe || '').trim().toLowerCase();
  if (p === 'currency') {
    const num = Number(val);
    return Number.isFinite(num) ? `${num.toLocaleString('vi-VN')} đ` : escapeHtml(val);
  }
  if (p === 'number') {
    const num = Number(val);
    return Number.isFinite(num) ? num.toLocaleString('vi-VN') : escapeHtml(val);
  }
  if (p === 'vnd_words') {
    return escapeHtml(numberToVietnameseWords(val));
  }
  if (p === 'date') {
    const s = String(val).slice(0, 10);
    if (/^\d{4}-\d{2}-\d{2}$/.test(s)) {
      const [y, m, d] = s.split('-');
      return `${d}/${m}/${y}`;
    }
    return escapeHtml(s);
  }
  return escapeHtml(val);
}

export function renderPrintTemplateHtml(htmlTemplate: string, ctx: PrintRenderContext): string {
  const sysVars: Record<string, unknown> = {
    app_name: ctx.appName,
    app_department: ctx.appDepartment || '',
    table_name: ctx.tableName,
    record_key: ctx.recordKey,
    approval_status: ctx.approvalStatus || 'DRAFT',
    created_by: ctx.createdBy || '',
    approved_by: ctx.approvedBy || '',
    print_date: new Date().toLocaleDateString('vi-VN'),
  };

  // 1) Resolve {{#each subTable}}...{{/each}}
  let out = htmlTemplate.replace(/\{\{#each\s+([a-zA-Z0-9_]+)\s*\}\}([\s\S]*?)\{\{\/each\}\}/g, (_m, field, inner) => {
    const arr = ctx.data?.[field];
    if (!Array.isArray(arr) || arr.length === 0) return '';
    return arr
      .map((row: Record<string, unknown>, idx: number) =>
        inner.replace(/\{\{\s*(@index1|[a-zA-Z0-9_]+)(?:\s*\|\s*([a-zA-Z0-9_]+))?\s*\}\}/g, (_im: string, key: string, pipe?: string) => {
          if (key === '@index1') return String(idx + 1);
          const v = row && typeof row === 'object' ? row[key] : '';
          return formatPipe(v, pipe);
        }),
      )
      .join('');
  });

  // 2) Resolve scalar {{field | pipe}}
  out = out.replace(/\{\{\s*([a-zA-Z0-9_]+)(?:\s*\|\s*([a-zA-Z0-9_]+))?\s*\}\}/g, (_m, key: string, pipe?: string) => {
    const val = key in (ctx.data || {}) ? ctx.data[key] : sysVars[key];
    return formatPipe(val, pipe);
  });

  return out;
}

export const PRINT_TEMPLATE_PRESETS: Array<{
  key: string;
  name: string;
  paperSize: 'A4' | 'A5' | 'K80' | 'LABEL_50X30';
  orientation: 'portrait' | 'landscape';
  htmlTemplate: string;
}> = [
  {
    key: 'a4_voucher',
    name: 'Phiếu chứng từ chuẩn A4 (3 chữ ký)',
    paperSize: 'A4',
    orientation: 'portrait',
    htmlTemplate: `<div style="font-family: 'Segoe UI', Arial, sans-serif; color: #111827; line-height: 1.5;">
  <div style="display:flex; justify-content:space-between; border-bottom: 2px solid #111827; padding-bottom: 10px; margin-bottom: 16px;">
    <div>
      <div style="font-size: 12px; font-weight: 700; text-transform: uppercase; color: #4b5563;">{{app_name}} • {{app_department}}</div>
      <h1 style="margin: 4px 0 0; font-size: 20px; font-weight: 800;">{{table_name}}</h1>
    </div>
    <div style="text-align:right; font-size: 12px;">
      <div>Số chứng từ: <strong>{{record_key}}</strong></div>
      <div>Ngày in: {{print_date}}</div>
      <div>Trạng thái: <strong>{{approval_status}}</strong></div>
    </div>
  </div>
  <p>Người lập: <strong>{{created_by}}</strong> • Người duyệt: <strong>{{approved_by}}</strong></p>
  <div style="display:grid; grid-template-columns: repeat(3, 1fr); margin-top: 40px; text-align:center; font-size: 13px;">
    <div><strong>Người lập phiếu</strong><div style="margin-top:48px;">{{created_by}}</div></div>
    <div><strong>Kế toán trưởng</strong><div style="margin-top:48px;">(Ký, họ tên)</div></div>
    <div><strong>Giám đốc duyệt</strong><div style="margin-top:48px;">{{approved_by}}</div></div>
  </div>
</div>`,
  },
  {
    key: 'a4_order_detail',
    name: 'Đơn hàng / Phiếu kho có bảng chi tiết & Tiền bằng chữ',
    paperSize: 'A4',
    orientation: 'portrait',
    htmlTemplate: `<div style="font-family: 'Segoe UI', Arial, sans-serif; color: #111827;">
  <div style="text-align:center; margin-bottom: 14px;">
    <div style="font-size: 12px; font-weight: 700; color: #4b5563;">{{app_name}}</div>
    <h2 style="margin: 4px 0; font-size: 20px;">{{table_name}} — {{record_key}}</h2>
    <div style="font-size: 12px;">Ngày lập: {{print_date}}</div>
  </div>
  <table style="width:100%; border-collapse:collapse; font-size: 13px; margin-top: 12px;">
    <thead>
      <tr style="background:#f3f4f6;">
        <th style="border:1px solid #d1d5db; padding:6px; width:40px;">STT</th>
        <th style="border:1px solid #d1d5db; padding:6px; text-align:left;">Hạng mục / Sản phẩm</th>
        <th style="border:1px solid #d1d5db; padding:6px; text-align:right;">Số lượng</th>
        <th style="border:1px solid #d1d5db; padding:6px; text-align:right;">Thành tiền</th>
      </tr>
    </thead>
    <tbody>
      {{#each chi_tiet}}
      <tr>
        <td style="border:1px solid #d1d5db; padding:6px; text-align:center;">{{@index1}}</td>
        <td style="border:1px solid #d1d5db; padding:6px;">{{ten_hang}}</td>
        <td style="border:1px solid #d1d5db; padding:6px; text-align:right;">{{so_luong | number}}</td>
        <td style="border:1px solid #d1d5db; padding:6px; text-align:right;">{{thanh_tien | currency}}</td>
      </tr>
      {{/each}}
    </tbody>
  </table>
  <div style="margin-top: 12px; text-align:right; font-size: 14px;">
    <div>Tổng cộng: <strong>{{tong_tien | currency}}</strong></div>
    <div style="font-style: italic; font-size: 13px; margin-top: 4px;">Bằng chữ: <strong>{{tong_tien | vnd_words}}</strong></div>
  </div>
</div>`,
  },
  {
    key: 'k80_receipt',
    name: 'Hóa đơn bán lẻ khổ nhiệt (K80 - 80mm)',
    paperSize: 'K80',
    orientation: 'portrait',
    htmlTemplate: `<div style="font-family: monospace; font-size: 12px; width: 280px; margin: 0 auto;">
  <div style="text-align:center; border-bottom: 1px dashed #000; padding-bottom: 6px; margin-bottom: 8px;">
    <strong>{{app_name}}</strong><br/>
    <span>PHIẾU THANH TOÁN</span><br/>
    <span>Số: {{record_key}} • {{print_date}}</span>
  </div>
  <div>Thu ngân: {{created_by}}</div>
  <div style="border-top: 1px dashed #000; margin-top: 8px; padding-top: 6px; text-align:right;">
    <strong>TỔNG: {{tong_tien | currency}}</strong>
  </div>
  <div style="text-align:center; margin-top: 10px; font-size: 11px;">Xin cảm ơn quý khách!</div>
</div>`,
  },
];
