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

const CODE39_TABLE: Record<string, string> = {
  '0': 'nnnwwnwnn', '1': 'wnnwnnnnw', '2': 'nnwwnnnnw', '3': 'wnwwnnnnn',
  '4': 'nnnwwnnnw', '5': 'wnnwwnnnn', '6': 'nnwwwnnnn', '7': 'nnnwnnwnw',
  '8': 'wnnwnnwnn', '9': 'nnwwnnwnn', A: 'wnnnnwnnw', B: 'nnwnnwnnw',
  C: 'wnwnnwnnn', D: 'nnnnwwnnw', E: 'wnnnwwnnn', F: 'nnwnwwnnn',
  G: 'nnnnnwwnw', H: 'wnnnnwwnn', I: 'nnwnnwwnn', J: 'nnnnwwwnn',
  K: 'wnnnnnnww', L: 'nnwnnnnww', M: 'wnwnnnnwn', N: 'nnnnwnnww',
  O: 'wnnnwnnwn', P: 'nnwnwnnwn', Q: 'nnnnnnwww', R: 'wnnnnnwwn',
  S: 'nnwnnnwwn', T: 'nnnnwnwwn', U: 'wwnnnnnnw', V: 'nwwnnnnnw',
  W: 'wwwnnnnnn', X: 'nwnnwnnnw', Y: 'wwnnwnnnn', Z: 'nwwnwnnnn',
  '-': 'nwnnnnwnw', '.': 'wwnnnnwnn', ' ': 'nwwnnnwnn', '*': 'nwnnwnwnn',
};

export function renderCode39SvgString(value: string, height = 38): string {
  const raw = (value || '').trim();
  if (!raw) return '';
  const clean = `*${raw.toUpperCase().replace(/[^0-9A-Z.\- ]/g, '-')}*`;
  const bars: Array<{ x: number; w: number }> = [];
  let x = 4;
  for (const ch of clean) {
    const pattern = CODE39_TABLE[ch] || CODE39_TABLE['-'];
    for (let i = 0; i < 9; i++) {
      const width = pattern[i] === 'w' ? 4 : 1.6;
      if (i % 2 === 0) bars.push({ x, w: width });
      x += width;
    }
    x += 2;
  }
  const totalWidth = x + 4;
  const rects = bars.map((b) => `<rect x="${b.x}" y="2" width="${b.w}" height="${height - 4}" fill="#111827"/>`).join('');
  return `<svg viewBox="0 0 ${totalWidth} ${height}" style="width:100%;max-width:220px;height:${height}px;display:inline-block;vertical-align:middle;"><rect x="0" y="0" width="${totalWidth}" height="${height}" fill="#ffffff"/>${rects}</svg>`;
}

function extractImageUrl(val: unknown): string {
  if (typeof val === 'string') return val.trim();
  if (typeof val === 'object' && val !== null && 'url' in val) {
    return String((val as { url?: unknown }).url ?? '').trim();
  }
  return '';
}

function isSafeImageUrl(url: string): boolean {
  if (!url) return false;
  if (url.startsWith('/') || url.startsWith('https://') || url.startsWith('http://')) return true;
  if (url.startsWith('data:image/png;base64,') || url.startsWith('data:image/jpeg;base64,') || url.startsWith('data:image/webp;base64,')) return true;
  return false;
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
  if (p === 'datetime') {
    const d = new Date(String(val));
    if (!Number.isNaN(d.getTime())) {
      return escapeHtml(d.toLocaleString('vi-VN'));
    }
    return escapeHtml(val);
  }
  if (p === 'boolean') {
    return val === true || val === 'true' || val === 1 || val === '1' ? 'Có' : 'Không';
  }
  if (p === 'image') {
    const url = extractImageUrl(val);
    if (!isSafeImageUrl(url)) return '';
    return `<img src="${escapeHtml(url)}" alt="image" style="max-height:84px;max-width:180px;border-radius:4px;border:1px solid #d1d5db;object-fit:contain;" />`;
  }
  return escapeHtml(val);
}

function isTruthyValue(val: unknown): boolean {
  if (val === null || val === undefined || val === false) return false;
  if (typeof val === 'string') return val.trim().length > 0 && val.trim().toLowerCase() !== 'false';
  if (Array.isArray(val)) return val.length > 0;
  return true;
}

export function sanitizePrintHtml(html: string): string {
  return (html || '')
    .replace(/<!--\s*SMARTAPPS_BUILDER_V1:[\s\S]*?-->/g, '')
    .replace(/<\s*(script|iframe|object|embed|link|meta|base)[^>]*>[\s\S]*?<\s*\/\s*\1\s*>/gi, '')
    .replace(/<\s*(script|iframe|object|embed|link|meta|base)[^>]*\/?\s*>/gi, '')
    .replace(/\son[a-z]+\s*=\s*(?:"[^"]*"|'[^']*'|[^\s>]+)/gi, '')
    .replace(/(href|src)\s*=\s*(["'])\s*javascript:[^"']*\2/gi, '$1=$2#$2');
}

export function getPagePrintCss(paperSize: string, orientation: string, customCss?: string | null): string {
  const ps = (paperSize || 'A4').toUpperCase();
  const orient = (orientation || 'portrait').toLowerCase() === 'landscape' ? 'landscape' : 'portrait';
  let sizeRule = `A4 ${orient}`;
  let marginRule = '12mm';
  if (ps === 'A5') {
    sizeRule = `A5 ${orient}`;
    marginRule = '10mm';
  } else if (ps === 'K80') {
    sizeRule = '80mm auto';
    marginRule = '4mm';
  } else if (ps === 'LABEL_50X30') {
    sizeRule = orient === 'landscape' ? '50mm 30mm' : '30mm 50mm';
    marginRule = '2mm';
  }
  return `@media print { @page { size: ${sizeRule}; margin: ${marginRule}; } }\n${customCss || ''}`;
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

  const resolveRaw = (key: string): unknown => (key in (ctx.data || {}) ? ctx.data[key] : sysVars[key]);

  // 0) Strip embedded builder metadata comment
  let out = htmlTemplate.replace(/<!--\s*SMARTAPPS_BUILDER_V1:[\s\S]*?-->/g, '');

  // 1) Resolve {{#if field}}...{{else}}...{{/if}}
  out = out.replace(/\{\{#if\s+([a-zA-Z0-9_]+)\s*\}\}([\s\S]*?)\{\{\/if\}\}/g, (_m, field: string, body: string) => {
    const [trueBranch, falseBranch = ''] = body.split(/\{\{else\}\}/);
    return isTruthyValue(resolveRaw(field)) ? trueBranch : falseBranch;
  });

  // 2) Resolve {{#sum subTable.field | pipe}}
  out = out.replace(/\{\{#sum\s+([a-zA-Z0-9_]+)\.([a-zA-Z0-9_]+)(?:\s*\|\s*([a-zA-Z0-9_]+))?\s*\}\}/g, (_m, tableField: string, colField: string, pipe?: string) => {
    const arr = ctx.data?.[tableField];
    if (!Array.isArray(arr)) return formatPipe(0, pipe);
    const total = arr.reduce((acc, row) => {
      const n = row && typeof row === 'object' ? Number((row as Record<string, unknown>)[colField] ?? 0) : 0;
      return acc + (Number.isFinite(n) ? n : 0);
    }, 0);
    return formatPipe(total, pipe || 'number');
  });

  // 3) Resolve {{#each subTable}}...{{/each}}
  out = out.replace(/\{\{#each\s+([a-zA-Z0-9_]+)\s*\}\}([\s\S]*?)\{\{\/each\}\}/g, (_m, field, inner) => {
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

  // 4) Resolve {{barcode:field}}
  out = out.replace(/\{\{\s*barcode:([a-zA-Z0-9_]+)\s*\}\}/g, (_m, key: string) => {
    const val = resolveRaw(key);
    return val ? renderCode39SvgString(String(val)) : '';
  });

  // 5) Resolve scalar {{field | pipe}}
  out = out.replace(/\{\{\s*([a-zA-Z0-9_]+)(?:\s*\|\s*([a-zA-Z0-9_]+))?\s*\}\}/g, (_m, key: string, pipe?: string) => {
    return formatPipe(resolveRaw(key), pipe);
  });

  return sanitizePrintHtml(out);
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
  {
    key: 'vn_payment_request',
    name: 'Đề nghị Thanh toán Doanh nghiệp VN (Quốc hiệu & 4 chữ ký)',
    paperSize: 'A4',
    orientation: 'portrait',
    htmlTemplate: `<div style="font-family: 'Times New Roman', serif; color: #111827; line-height: 1.55;">
  <div style="display:flex; justify-content:space-between; align-items:flex-start; margin-bottom:14px;">
    <div style="font-size:12.5px;">
      <strong>{{app_name}}</strong><br/>
      <span>Phòng ban: {{app_department}}</span>
    </div>
    <div style="text-align:center; font-size:12.5px;">
      <strong>CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM</strong><br/>
      <u>Độc lập - Tự do - Hạnh phúc</u><br/>
      <i style="font-size:11.5px;">Ngày in: {{print_date}}</i>
    </div>
  </div>
  <h2 style="text-align:center; margin:12px 0 4px; font-size:19px;">GIẤY ĐỀ NGHỊ THANH TOÁN</h2>
  <div style="text-align:center; font-size:12.5px; margin-bottom:14px;">Số hồ sơ: <strong>{{record_key}}</strong> • Trạng thái: <strong>{{approval_status}}</strong></div>
  <p>Người đề nghị: <strong>{{created_by}}</strong></p>
  <p>Tổng số tiền đề nghị: <strong>{{tong_tien | currency}}</strong></p>
  <p>Số tiền viết bằng chữ: <i><strong>{{tong_tien | vnd_words}}</strong></i></p>
  <div style="display:grid; grid-template-columns: repeat(4, 1fr); margin-top: 32px; text-align:center; font-size: 12.5px;">
    <div><strong>Người đề nghị</strong><div style="margin-top:46px;">{{created_by}}</div></div>
    <div><strong>Trưởng bộ phận</strong><div style="margin-top:46px;">(Ký, ghi rõ họ tên)</div></div>
    <div><strong>Kế toán trưởng</strong><div style="margin-top:46px;">(Ký, ghi rõ họ tên)</div></div>
    <div><strong>Ban Giám đốc</strong><div style="margin-top:46px;">{{approved_by}}</div></div>
  </div>
</div>`,
  },
  {
    key: 'vn_handover_minutes',
    name: 'Biên bản Bàn giao / Hợp đồng Dịch vụ A4',
    paperSize: 'A4',
    orientation: 'portrait',
    htmlTemplate: `<div style="font-family: 'Times New Roman', serif; color: #111827; line-height: 1.55;">
  <div style="text-align:center; margin-bottom:12px;">
    <strong>CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM</strong><br/>
    <u>Độc lập - Tự do - Hạnh phúc</u>
    <h2 style="margin:12px 0 4px; font-size:19px;">BIÊN BẢN BÀN GIAO / XÁC NHẬN</h2>
    <div style="font-size:12.5px;">Mã số: <strong>{{record_key}}</strong> • {{print_date}}</div>
  </div>
  <p>Hôm nay, ngày {{print_date}}, tại đơn vị <strong>{{app_department}}</strong> ({{app_name}}), hai bên cùng lập biên bản bàn giao cho danh mục <strong>{{table_name}}</strong> như sau:</p>
  <table style="width:100%; border-collapse:collapse; font-size:13px; margin:10px 0;">
    <thead>
      <tr style="background:#f3f4f6;">
        <th style="border:1px solid #9ca3af; padding:6px; width:40px;">STT</th>
        <th style="border:1px solid #9ca3af; padding:6px; text-align:left;">Nội dung / Hạng mục</th>
        <th style="border:1px solid #9ca3af; padding:6px; text-align:right;">Số lượng</th>
        <th style="border:1px solid #9ca3af; padding:6px; text-align:left;">Ghi chú</th>
      </tr>
    </thead>
    <tbody>
      {{#each chi_tiet}}
      <tr>
        <td style="border:1px solid #9ca3af; padding:6px; text-align:center;">{{@index1}}</td>
        <td style="border:1px solid #9ca3af; padding:6px;">{{ten_hang}}</td>
        <td style="border:1px solid #9ca3af; padding:6px; text-align:right;">{{so_luong | number}}</td>
        <td style="border:1px solid #9ca3af; padding:6px;">{{ghi_chu}}</td>
      </tr>
      {{/each}}
    </tbody>
  </table>
  <div style="display:grid; grid-template-columns: repeat(2, 1fr); margin-top: 32px; text-align:center; font-size: 13px;">
    <div><strong>ĐẠI DIỆN BÊN GIAO</strong><div style="margin-top:52px;">{{created_by}}</div></div>
    <div><strong>ĐẠI DIỆN BÊN NHẬN / DUYỆT</strong><div style="margin-top:52px;">{{approved_by}}</div></div>
  </div>
</div>`,
  },
];
