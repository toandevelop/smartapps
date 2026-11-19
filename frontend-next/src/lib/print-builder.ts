/**
 * Visual Print Builder schema & HTML compiler.
 * Compiles block-based print layouts into standard HTML templates with an embedded
 * SMARTAPPS_BUILDER_V1 metadata comment for lossless round-trip editing.
 */

export type PrintBuilderFont = "'Segoe UI', Arial, sans-serif" | "'Times New Roman', serif" | "monospace";

export interface PrintBuilderPageSettings {
  fontFamily: PrintBuilderFont;
  fontSize: number;
  padding: number;
  textColor: string;
}

export interface HeaderBlock {
  id: string;
  type: 'header';
  layout: 'split' | 'centered' | 'vn_national';
  title: string;
  subtitle: string;
  showRecordKey: boolean;
  showPrintDate: boolean;
  showApprovalStatus: boolean;
}

export interface FieldItemConfig {
  columnCode: string;
  label: string;
  pipe?: '' | 'currency' | 'number' | 'vnd_words' | 'date' | 'datetime' | 'boolean' | 'image';
}

export interface FieldsBlock {
  id: string;
  type: 'fields';
  title?: string;
  columnsCount: 1 | 2 | 3;
  bordered: boolean;
  items: FieldItemConfig[];
}

export interface SubTableFieldConfig {
  key: string;
  label: string;
  align: 'left' | 'center' | 'right';
  pipe?: '' | 'currency' | 'number' | 'date';
}

export interface SubTableBlock {
  id: string;
  type: 'subtable';
  columnCode: string;
  title?: string;
  showIndex: boolean;
  fields: SubTableFieldConfig[];
  totalKey?: string;
  totalLabel?: string;
  totalPipe?: 'currency' | 'number';
}

export interface SummaryBlock {
  id: string;
  type: 'summary';
  amountField: string;
  amountLabel: string;
  showWords: boolean;
  align: 'left' | 'right';
}

export interface SignatureItem {
  title: string;
  token: string;
}

export interface SignaturesBlock {
  id: string;
  type: 'signatures';
  columnsCount: 2 | 3 | 4;
  items: SignatureItem[];
}

export interface BarcodeBlock {
  id: string;
  type: 'barcode';
  field: string;
  label?: string;
  align: 'left' | 'center' | 'right';
}

export interface TextBlock {
  id: string;
  type: 'text';
  content: string;
  align: 'left' | 'center' | 'right';
  fontSize: number;
  bold: boolean;
  italic: boolean;
}

export interface DividerBlock {
  id: string;
  type: 'divider';
  lineStyle: 'solid' | 'dashed';
  spacing: number;
}

export interface PageBreakBlock {
  id: string;
  type: 'pagebreak';
}

export type PrintBuilderBlock =
  | HeaderBlock
  | FieldsBlock
  | SubTableBlock
  | SummaryBlock
  | SignaturesBlock
  | BarcodeBlock
  | TextBlock
  | DividerBlock
  | PageBreakBlock;

export interface PrintBuilderSchema {
  version: 1;
  page: PrintBuilderPageSettings;
  blocks: PrintBuilderBlock[];
}

export interface BuilderColumnMeta {
  columnCode: string;
  columnName: string;
  dataType?: string;
}

function escapeAttrText(s: string): string {
  return (s || '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;');
}

export function inferPipeForDataType(dataType?: string): FieldItemConfig['pipe'] {
  const dt = (dataType || '').toUpperCase();
  if (dt === 'CURRENCY') return 'currency';
  if (dt === 'NUMBER') return 'number';
  if (dt === 'DATE') return 'date';
  if (dt === 'DATETIME') return 'datetime';
  if (dt === 'BOOLEAN') return 'boolean';
  if (dt === 'IMAGE') return 'image';
  return '';
}

export function createDefaultBuilderSchema(tableDisplayName: string, columns: BuilderColumnMeta[]): PrintBuilderSchema {
  const scalarCols = columns.filter((c) => (c.dataType || '').toUpperCase() !== 'SUB_TABLE').slice(0, 8);
  const subTableCol = columns.find((c) => (c.dataType || '').toUpperCase() === 'SUB_TABLE');
  const currencyCol = columns.find((c) => (c.dataType || '').toUpperCase() === 'CURRENCY');

  const blocks: PrintBuilderBlock[] = [
    {
      id: 'blk_header',
      type: 'header',
      layout: 'split',
      title: tableDisplayName.toUpperCase() || '{{table_name}}',
      subtitle: '{{app_name}} • {{app_department}}',
      showRecordKey: true,
      showPrintDate: true,
      showApprovalStatus: true,
    },
    {
      id: 'blk_fields',
      type: 'fields',
      title: 'Thông tin chung',
      columnsCount: 2,
      bordered: true,
      items: scalarCols.map((c) => ({
        columnCode: c.columnCode,
        label: c.columnName,
        pipe: inferPipeForDataType(c.dataType),
      })),
    },
  ];

  if (subTableCol) {
    blocks.push({
      id: 'blk_subtable',
      type: 'subtable',
      columnCode: subTableCol.columnCode,
      title: subTableCol.columnName,
      showIndex: true,
      fields: [
        { key: 'ten_hang', label: 'Nội dung / Hạng mục', align: 'left', pipe: '' },
        { key: 'so_luong', label: 'Số lượng', align: 'right', pipe: 'number' },
        { key: 'thanh_tien', label: 'Thành tiền', align: 'right', pipe: 'currency' },
      ],
      totalKey: 'thanh_tien',
      totalLabel: 'Tổng cộng',
      totalPipe: 'currency',
    });
  }

  if (currencyCol) {
    blocks.push({
      id: 'blk_summary',
      type: 'summary',
      amountField: currencyCol.columnCode,
      amountLabel: currencyCol.columnName,
      showWords: true,
      align: 'right',
    });
  }

  blocks.push({
    id: 'blk_signatures',
    type: 'signatures',
    columnsCount: 3,
    items: [
      { title: 'Người lập phiếu', token: '{{created_by}}' },
      { title: 'Kế toán trưởng', token: '(Ký, họ tên)' },
      { title: 'Giám đốc duyệt', token: '{{approved_by}}' },
    ],
  });

  return {
    version: 1,
    page: {
      fontFamily: "'Segoe UI', Arial, sans-serif",
      fontSize: 13,
      padding: 16,
      textColor: '#111827',
    },
    blocks,
  };
}

export function createBlockByType(type: PrintBuilderBlock['type'], columns: BuilderColumnMeta[]): PrintBuilderBlock {
  const id = `blk_${Date.now().toString(36)}_${Math.random().toString(36).slice(2, 6)}`;
  const scalarCols = columns.filter((c) => (c.dataType || '').toUpperCase() !== 'SUB_TABLE').slice(0, 4);
  const subTableCol = columns.find((c) => (c.dataType || '').toUpperCase() === 'SUB_TABLE');
  const currencyCol = columns.find((c) => (c.dataType || '').toUpperCase() === 'CURRENCY');

  switch (type) {
    case 'header':
      return {
        id,
        type: 'header',
        layout: 'vn_national',
        title: '{{table_name}}',
        subtitle: '{{app_name}} • {{app_department}}',
        showRecordKey: true,
        showPrintDate: true,
        showApprovalStatus: false,
      };
    case 'fields':
      return {
        id,
        type: 'fields',
        title: 'Thông tin chi tiết',
        columnsCount: 2,
        bordered: true,
        items: scalarCols.map((c) => ({
          columnCode: c.columnCode,
          label: c.columnName,
          pipe: inferPipeForDataType(c.dataType),
        })),
      };
    case 'subtable':
      return {
        id,
        type: 'subtable',
        columnCode: subTableCol?.columnCode || 'chi_tiet',
        title: subTableCol?.columnName || 'Bảng chi tiết',
        showIndex: true,
        fields: [
          { key: 'ten_hang', label: 'Hạng mục', align: 'left', pipe: '' },
          { key: 'so_luong', label: 'Số lượng', align: 'right', pipe: 'number' },
          { key: 'thanh_tien', label: 'Thành tiền', align: 'right', pipe: 'currency' },
        ],
        totalKey: 'thanh_tien',
        totalLabel: 'Tổng cộng',
        totalPipe: 'currency',
      };
    case 'summary':
      return {
        id,
        type: 'summary',
        amountField: currencyCol?.columnCode || 'tong_tien',
        amountLabel: currencyCol?.columnName || 'Tổng thanh toán',
        showWords: true,
        align: 'right',
      };
    case 'signatures':
      return {
        id,
        type: 'signatures',
        columnsCount: 3,
        items: [
          { title: 'Người lập phiếu', token: '{{created_by}}' },
          { title: 'Kiểm soát', token: '(Ký, họ tên)' },
          { title: 'Phê duyệt', token: '{{approved_by}}' },
        ],
      };
    case 'barcode':
      return {
        id,
        type: 'barcode',
        field: 'record_key',
        label: 'Mã chứng từ',
        align: 'center',
      };
    case 'text':
      return {
        id,
        type: 'text',
        content: 'Ghi chú: Chứng từ có giá trị khi có đầy đủ chữ ký xác nhận.',
        align: 'left',
        fontSize: 12.5,
        bold: false,
        italic: true,
      };
    case 'divider':
      return { id, type: 'divider', lineStyle: 'dashed', spacing: 10 };
    case 'pagebreak':
      return { id, type: 'pagebreak' };
  }
}

function renderToken(columnCode: string, pipe?: string): string {
  return pipe ? `{{${columnCode} | ${pipe}}}` : `{{${columnCode}}}`;
}

function compileHeaderBlock(b: HeaderBlock): string {
  const metaLines = [
    b.showRecordKey ? `<div>Số: <strong>{{record_key}}</strong></div>` : '',
    b.showPrintDate ? `<div>Ngày in: {{print_date}}</div>` : '',
    b.showApprovalStatus ? `<div>Trạng thái: <strong>{{approval_status}}</strong></div>` : '',
  ]
    .filter(Boolean)
    .join('');

  if (b.layout === 'vn_national') {
    return `<div style="margin-bottom:14px;">
  <div style="display:flex;justify-content:space-between;align-items:flex-start;margin-bottom:10px;">
    <div style="font-size:12px;">
      <strong>${escapeAttrText(b.subtitle)}</strong>
      ${metaLines ? `<div style="margin-top:4px;">${metaLines}</div>` : ''}
    </div>
    <div style="text-align:center;font-size:12px;">
      <strong>CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM</strong><br/>
      <u>Độc lập - Tự do - Hạnh phúc</u>
    </div>
  </div>
  <h1 style="text-align:center;margin:10px 0 4px;font-size:19px;font-weight:800;">${escapeAttrText(b.title)}</h1>
</div>`;
  }

  if (b.layout === 'centered') {
    return `<div style="text-align:center;border-bottom:2px solid #111827;padding-bottom:10px;margin-bottom:14px;">
  <div style="font-size:12px;font-weight:700;color:#4b5563;text-transform:uppercase;">${escapeAttrText(b.subtitle)}</div>
  <h1 style="margin:4px 0;font-size:20px;font-weight:800;">${escapeAttrText(b.title)}</h1>
  ${metaLines ? `<div style="font-size:12px;display:flex;justify-content:center;gap:14px;flex-wrap:wrap;">${metaLines}</div>` : ''}
</div>`;
  }

  return `<div style="display:flex;justify-content:space-between;align-items:flex-start;border-bottom:2px solid #111827;padding-bottom:10px;margin-bottom:14px;">
  <div>
    <div style="font-size:12px;font-weight:700;text-transform:uppercase;color:#4b5563;">${escapeAttrText(b.subtitle)}</div>
    <h1 style="margin:4px 0 0;font-size:20px;font-weight:800;">${escapeAttrText(b.title)}</h1>
  </div>
  ${metaLines ? `<div style="text-align:right;font-size:12px;">${metaLines}</div>` : ''}
</div>`;
}

function compileFieldsBlock(b: FieldsBlock): string {
  if (!b.items.length) return '';
  const heading = b.title ? `<div style="font-weight:700;font-size:13px;margin-bottom:6px;text-transform:uppercase;color:#374151;">${escapeAttrText(b.title)}</div>` : '';
  const cols = b.columnsCount || 2;
  const cells = b.items
    .map((it) => {
      const borderStyle = b.bordered ? 'border:1px solid #d1d5db;padding:6px 8px;' : 'padding:4px 6px;';
      return `<div style="${borderStyle}"><span style="color:#4b5563;font-size:12px;">${escapeAttrText(it.label)}:</span> <strong>${renderToken(it.columnCode, it.pipe)}</strong></div>`;
    })
    .join('\n    ');
  return `<div style="margin-bottom:14px;">
  ${heading}
  <div style="display:grid;grid-template-columns:repeat(${cols}, minmax(0, 1fr));gap:${b.bordered ? '0' : '6px'};">
    ${cells}
  </div>
</div>`;
}

function compileSubTableBlock(b: SubTableBlock): string {
  if (!b.fields.length) return '';
  const heading = b.title ? `<div style="font-weight:700;font-size:13px;margin-bottom:6px;text-transform:uppercase;color:#374151;">${escapeAttrText(b.title)}</div>` : '';
  const ths = [
    b.showIndex ? `<th style="border:1px solid #d1d5db;padding:6px;width:40px;text-align:center;">STT</th>` : '',
    ...b.fields.map(
      (f) => `<th style="border:1px solid #d1d5db;padding:6px;text-align:${f.align};">${escapeAttrText(f.label)}</th>`,
    ),
  ]
    .filter(Boolean)
    .join('');

  const tds = [
    b.showIndex ? `<td style="border:1px solid #d1d5db;padding:6px;text-align:center;">{{@index1}}</td>` : '',
    ...b.fields.map(
      (f) => `<td style="border:1px solid #d1d5db;padding:6px;text-align:${f.align};">${renderToken(f.key, f.pipe)}</td>`,
    ),
  ]
    .filter(Boolean)
    .join('');

  const totalCols = (b.showIndex ? 1 : 0) + b.fields.length;
  const tfoot = b.totalKey
    ? `<tfoot>
      <tr style="background:#f9fafb;font-weight:700;">
        <td colspan="${Math.max(1, totalCols - 1)}" style="border:1px solid #d1d5db;padding:6px;text-align:right;">${escapeAttrText(b.totalLabel || 'Tổng cộng')}</td>
        <td style="border:1px solid #d1d5db;padding:6px;text-align:right;">{{#sum ${b.columnCode}.${b.totalKey} | ${b.totalPipe || 'currency'}}}</td>
      </tr>
    </tfoot>`
    : '';

  return `<div style="margin-bottom:14px;">
  ${heading}
  <table style="width:100%;border-collapse:collapse;font-size:12.5px;">
    <thead>
      <tr style="background:#f3f4f6;">${ths}</tr>
    </thead>
    <tbody>
      {{#each ${b.columnCode}}}
      <tr>${tds}</tr>
      {{/each}}
    </tbody>
    ${tfoot}
  </table>
</div>`;
}

function compileSummaryBlock(b: SummaryBlock): string {
  return `<div style="margin-bottom:14px;text-align:${b.align};">
  <div style="font-size:14px;">${escapeAttrText(b.amountLabel)}: <strong>{{${b.amountField} | currency}}</strong></div>
  ${b.showWords ? `<div style="font-style:italic;font-size:12.5px;margin-top:3px;">Bằng chữ: <strong>{{${b.amountField} | vnd_words}}</strong></div>` : ''}
</div>`;
}

function compileSignaturesBlock(b: SignaturesBlock): string {
  const cols = b.columnsCount || 3;
  const items = b.items.slice(0, cols);
  const cells = items
    .map(
      (s) => `<div><strong>${escapeAttrText(s.title)}</strong><div style="margin-top:46px;">${escapeAttrText(s.token)}</div></div>`,
    )
    .join('\n    ');
  return `<div style="display:grid;grid-template-columns:repeat(${cols}, 1fr);margin-top:28px;text-align:center;font-size:12.5px;gap:12px;">
    ${cells}
</div>`;
}

function compileBlock(b: PrintBuilderBlock): string {
  switch (b.type) {
    case 'header':
      return compileHeaderBlock(b);
    case 'fields':
      return compileFieldsBlock(b);
    case 'subtable':
      return compileSubTableBlock(b);
    case 'summary':
      return compileSummaryBlock(b);
    case 'signatures':
      return compileSignaturesBlock(b);
    case 'barcode':
      return `<div style="margin:10px 0;text-align:${b.align};">${b.label ? `<div style="font-size:11.5px;color:#4b5563;margin-bottom:4px;">${escapeAttrText(b.label)}</div>` : ''}{{barcode:${b.field}}}<div style="font-size:11.5px;font-weight:700;margin-top:2px;">{{${b.field}}}</div></div>`;
    case 'text':
      return `<p style="margin:8px 0;text-align:${b.align};font-size:${b.fontSize}px;font-weight:${b.bold ? 700 : 400};font-style:${b.italic ? 'italic' : 'normal'};">${escapeAttrText(b.content)}</p>`;
    case 'divider':
      return `<hr style="border:none;border-top:1px ${b.lineStyle} #9ca3af;margin:${b.spacing}px 0;" />`;
    case 'pagebreak':
      return `<div style="page-break-after:always;break-after:page;height:1px;margin:8px 0;border-top:1px dotted #cbd5e1;" title="Ngắt trang"></div>`;
  }
}

export function compilePrintBuilderToHtml(schema: PrintBuilderSchema): string {
  const { page, blocks } = schema;
  const body = blocks.map(compileBlock).filter(Boolean).join('\n  ');
  const encoded = encodeSchemaBase64(schema);
  return `<!-- SMARTAPPS_BUILDER_V1:${encoded} -->
<div style="font-family:${page.fontFamily};font-size:${page.fontSize}px;color:${page.textColor};line-height:1.5;padding:${page.padding}px;">
  ${body}
</div>`;
}

function encodeSchemaBase64(schema: PrintBuilderSchema): string {
  const json = JSON.stringify(schema);
  if (typeof Buffer !== 'undefined') {
    return Buffer.from(json, 'utf8').toString('base64');
  }
  const bytes = new TextEncoder().encode(json);
  let bin = '';
  for (const b of bytes) bin += String.fromCharCode(b);
  return btoa(bin);
}

function decodeSchemaBase64(b64: string): PrintBuilderSchema | null {
  try {
    let json = '';
    if (typeof Buffer !== 'undefined') {
      json = Buffer.from(b64, 'base64').toString('utf8');
    } else {
      const bin = atob(b64);
      const bytes = new Uint8Array(bin.length);
      for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
      json = new TextDecoder().decode(bytes);
    }
    const parsed = JSON.parse(json) as PrintBuilderSchema;
    if (parsed && parsed.version === 1 && Array.isArray(parsed.blocks) && parsed.page) {
      return parsed;
    }
  } catch {
    return null;
  }
  return null;
}

export function parseBuilderSchemaFromHtml(html: string): PrintBuilderSchema | null {
  const match = (html || '').match(/<!--\s*SMARTAPPS_BUILDER_V1:([A-Za-z0-9+/=]+)\s*-->/);
  if (!match) return null;
  return decodeSchemaBase64(match[1]);
}
