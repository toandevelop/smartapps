'use client';

import React, { useState } from 'react';
import ArrowUpwardRoundedIcon from '@mui/icons-material/ArrowUpwardRounded';
import ArrowDownwardRoundedIcon from '@mui/icons-material/ArrowDownwardRounded';
import ContentCopyRoundedIcon from '@mui/icons-material/ContentCopyRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import { SysColumn } from '@/lib/types';
import {
  createBlockByType,
  FieldsBlock,
  inferPipeForDataType,
  PrintBuilderBlock,
  PrintBuilderFont,
  PrintBuilderSchema,
  SubTableBlock,
} from '@/lib/print-builder';

const BLOCK_LABELS: Record<PrintBuilderBlock['type'], string> = {
  header: 'Tiêu đề / Quốc hiệu',
  fields: 'Lưới thông tin',
  subtable: 'Bảng chi tiết',
  summary: 'Tổng tiền & Bằng chữ',
  signatures: 'Khối chữ ký',
  barcode: 'Mã vạch (Barcode)',
  text: 'Văn bản tự do',
  divider: 'Đường kẻ',
  pagebreak: 'Ngắt trang',
};

const PALETTE_TYPES: PrintBuilderBlock['type'][] = [
  'header',
  'fields',
  'subtable',
  'summary',
  'signatures',
  'barcode',
  'text',
  'divider',
  'pagebreak',
];

export default function PrintTemplateBuilder({
  schema,
  columns,
  onChange,
}: {
  schema: PrintBuilderSchema;
  columns: SysColumn[];
  onChange: (next: PrintBuilderSchema) => void;
}) {
  const [selectedId, setSelectedId] = useState<string | null>(schema.blocks[0]?.id ?? null);

  const selectedBlock = schema.blocks.find((b) => b.id === selectedId) || schema.blocks[0] || null;
  const scalarCols = columns.filter((c) => (c.dataType || '').toUpperCase() !== 'SUB_TABLE');
  const subTableCols = columns.filter((c) => (c.dataType || '').toUpperCase() === 'SUB_TABLE');

  const updateBlock = (id: string, updater: (b: PrintBuilderBlock) => PrintBuilderBlock) => {
    onChange({
      ...schema,
      blocks: schema.blocks.map((b) => (b.id === id ? updater(b) : b)),
    });
  };

  const moveBlock = (index: number, delta: -1 | 1) => {
    const target = index + delta;
    if (target < 0 || target >= schema.blocks.length) return;
    const next = [...schema.blocks];
    const [item] = next.splice(index, 1);
    next.splice(target, 0, item);
    onChange({ ...schema, blocks: next });
  };

  const duplicateBlock = (index: number) => {
    const src = schema.blocks[index];
    const copy: PrintBuilderBlock = {
      ...JSON.parse(JSON.stringify(src)),
      id: `blk_${Date.now().toString(36)}_${Math.random().toString(36).slice(2, 5)}`,
    };
    const next = [...schema.blocks];
    next.splice(index + 1, 0, copy);
    onChange({ ...schema, blocks: next });
    setSelectedId(copy.id);
  };

  const removeBlock = (id: string) => {
    const next = schema.blocks.filter((b) => b.id !== id);
    onChange({ ...schema, blocks: next });
    if (selectedId === id) {
      setSelectedId(next[0]?.id ?? null);
    }
  };

  const addBlock = (type: PrintBuilderBlock['type']) => {
    const created = createBlockByType(type, columns);
    onChange({ ...schema, blocks: [...schema.blocks, created] });
    setSelectedId(created.id);
  };

  const renderInspector = (block: PrintBuilderBlock) => {
    switch (block.type) {
      case 'header':
        return (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8 }}>
              <div>
                <label className="form-label">Kiểu tiêu đề</label>
                <select
                  className="form-select"
                  value={block.layout}
                  onChange={(e) =>
                    updateBlock(block.id, () => ({ ...block, layout: e.target.value as typeof block.layout }))
                  }
                >
                  <option value="split">2 cột (Trái tên đơn vị / Phải mã số)</option>
                  <option value="centered">Căn giữa</option>
                  <option value="vn_national">Quốc hiệu Việt Nam (CHXHCNVN)</option>
                </select>
              </div>
              <div>
                <label className="form-label">Tiêu đề chính</label>
                <input
                  className="form-input"
                  value={block.title}
                  onChange={(e) => updateBlock(block.id, () => ({ ...block, title: e.target.value }))}
                />
              </div>
            </div>
            <div>
              <label className="form-label">Tên đơn vị / Phụ đề</label>
              <input
                className="form-input"
                value={block.subtitle}
                onChange={(e) => updateBlock(block.id, () => ({ ...block, subtitle: e.target.value }))}
              />
            </div>
            <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap', fontSize: 12 }}>
              <label style={{ display: 'flex', alignItems: 'center', gap: 5 }}>
                <input
                  type="checkbox"
                  checked={block.showRecordKey}
                  onChange={(e) => updateBlock(block.id, () => ({ ...block, showRecordKey: e.target.checked }))}
                />
                Hiện mã hồ sơ
              </label>
              <label style={{ display: 'flex', alignItems: 'center', gap: 5 }}>
                <input
                  type="checkbox"
                  checked={block.showPrintDate}
                  onChange={(e) => updateBlock(block.id, () => ({ ...block, showPrintDate: e.target.checked }))}
                />
                Hiện ngày in
              </label>
              <label style={{ display: 'flex', alignItems: 'center', gap: 5 }}>
                <input
                  type="checkbox"
                  checked={block.showApprovalStatus}
                  onChange={(e) => updateBlock(block.id, () => ({ ...block, showApprovalStatus: e.target.checked }))}
                />
                Hiện trạng thái duyệt
              </label>
            </div>
          </div>
        );

      case 'fields': {
        const fb = block as FieldsBlock;
        const selectedCodes = new Set(fb.items.map((i) => i.columnCode));
        const toggleCol = (col: SysColumn) => {
          const exists = selectedCodes.has(col.columnCode);
          const nextItems = exists
            ? fb.items.filter((i) => i.columnCode !== col.columnCode)
            : [
                ...fb.items,
                {
                  columnCode: col.columnCode,
                  label: col.columnName,
                  pipe: inferPipeForDataType(col.dataType),
                },
              ];
          updateBlock(fb.id, () => ({ ...fb, items: nextItems }));
        };
        return (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
            <div style={{ display: 'grid', gridTemplateColumns: '2fr 1fr', gap: 8 }}>
              <div>
                <label className="form-label">Tiêu đề khối (tùy chọn)</label>
                <input
                  className="form-input"
                  value={fb.title || ''}
                  onChange={(e) => updateBlock(fb.id, () => ({ ...fb, title: e.target.value }))}
                />
              </div>
              <div>
                <label className="form-label">Số cột lưới</label>
                <select
                  className="form-select"
                  value={fb.columnsCount}
                  onChange={(e) =>
                    updateBlock(fb.id, () => ({ ...fb, columnsCount: Number(e.target.value) as 1 | 2 | 3 }))
                  }
                >
                  <option value={1}>1 cột</option>
                  <option value={2}>2 cột</option>
                  <option value={3}>3 cột</option>
                </select>
              </div>
            </div>
            <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 12 }}>
              <input
                type="checkbox"
                checked={fb.bordered}
                onChange={(e) => updateBlock(fb.id, () => ({ ...fb, bordered: e.target.checked }))}
              />
              Hiển thị khung viền ô
            </label>
            <div>
              <div className="form-label" style={{ marginBottom: 4 }}>Chọn trường hiển thị ({fb.items.length}):</div>
              <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4, maxHeight: 96, overflowY: 'auto', padding: 6, borderRadius: 6, background: 'var(--bg-secondary)', border: '1px solid var(--border-subtle)' }}>
                {scalarCols.map((c) => {
                  const active = selectedCodes.has(c.columnCode);
                  return (
                    <button
                      key={c.id}
                      type="button"
                      className={`btn btn-xs ${active ? 'btn-primary' : 'btn-outline'}`}
                      style={{ fontSize: 11, padding: '2px 7px' }}
                      onClick={() => toggleCol(c)}
                    >
                      {active ? '✓ ' : '+ '}{c.columnName}
                    </button>
                  );
                })}
              </div>
            </div>
            {fb.items.length > 0 && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 4, maxHeight: 130, overflowY: 'auto' }}>
                {fb.items.map((it, idx) => (
                  <div key={it.columnCode} style={{ display: 'grid', gridTemplateColumns: '1fr 110px 26px', gap: 4, alignItems: 'center' }}>
                    <input
                      className="form-input"
                      style={{ fontSize: 11.5, padding: '4px 6px' }}
                      value={it.label}
                      onChange={(e) => {
                        const nextItems = fb.items.map((x, i) => (i === idx ? { ...x, label: e.target.value } : x));
                        updateBlock(fb.id, () => ({ ...fb, items: nextItems }));
                      }}
                    />
                    <select
                      className="form-select"
                      style={{ fontSize: 11.5, padding: '4px 6px' }}
                      value={it.pipe || ''}
                      onChange={(e) => {
                        const nextItems = fb.items.map((x, i) =>
                          i === idx ? { ...x, pipe: e.target.value as typeof it.pipe } : x,
                        );
                        updateBlock(fb.id, () => ({ ...fb, items: nextItems }));
                      }}
                    >
                      <option value="">Mặc định</option>
                      <option value="currency">Tiền (đ)</option>
                      <option value="number">Số</option>
                      <option value="vnd_words">Đọc chữ</option>
                      <option value="date">Ngày</option>
                      <option value="datetime">Ngày giờ</option>
                      <option value="boolean">Có/Không</option>
                      <option value="image">Hình ảnh</option>
                    </select>
                    <button
                      type="button"
                      className="btn btn-outline btn-xs"
                      style={{ color: '#d93025', padding: '2px 4px' }}
                      onClick={() => {
                        const nextItems = fb.items.filter((_, i) => i !== idx);
                        updateBlock(fb.id, () => ({ ...fb, items: nextItems }));
                      }}
                    >
                      ×
                    </button>
                  </div>
                ))}
              </div>
            )}
          </div>
        );
      }

      case 'subtable': {
        const sb = block as SubTableBlock;
        return (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8 }}>
              <div>
                <label className="form-label">Trường bảng con (SUB_TABLE)</label>
                {subTableCols.length > 0 ? (
                  <select
                    className="form-select"
                    value={sb.columnCode}
                    onChange={(e) => updateBlock(sb.id, () => ({ ...sb, columnCode: e.target.value }))}
                  >
                    {subTableCols.map((c) => (
                      <option key={c.id} value={c.columnCode}>{c.columnName} ({c.columnCode})</option>
                    ))}
                  </select>
                ) : (
                  <input
                    className="form-input"
                    value={sb.columnCode}
                    onChange={(e) => updateBlock(sb.id, () => ({ ...sb, columnCode: e.target.value }))}
                    placeholder="chi_tiet"
                  />
                )}
              </div>
              <div>
                <label className="form-label">Tiêu đề bảng</label>
                <input
                  className="form-input"
                  value={sb.title || ''}
                  onChange={(e) => updateBlock(sb.id, () => ({ ...sb, title: e.target.value }))}
                />
              </div>
            </div>
            <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 12 }}>
              <input
                type="checkbox"
                checked={sb.showIndex}
                onChange={(e) => updateBlock(sb.id, () => ({ ...sb, showIndex: e.target.checked }))}
              />
              Hiện cột STT
            </label>
            <div>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 4 }}>
                <span className="form-label" style={{ margin: 0 }}>Cột trong bảng con:</span>
                <button
                  type="button"
                  className="btn btn-outline btn-xs"
                  onClick={() =>
                    updateBlock(sb.id, () => ({
                      ...sb,
                      fields: [...sb.fields, { key: 'cot_moi', label: 'Cột mới', align: 'left', pipe: '' }],
                    }))
                  }
                >
                  + Thêm cột
                </button>
              </div>
              <div style={{ display: 'flex', flexDirection: 'column', gap: 4, maxHeight: 130, overflowY: 'auto' }}>
                {sb.fields.map((f, idx) => (
                  <div key={idx} style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 78px 86px 26px', gap: 4 }}>
                    <input
                      className="form-input"
                      style={{ fontSize: 11.5, padding: '4px 6px' }}
                      placeholder="Mã cột"
                      value={f.key}
                      onChange={(e) => {
                        const fields = sb.fields.map((x, i) => (i === idx ? { ...x, key: e.target.value } : x));
                        updateBlock(sb.id, () => ({ ...sb, fields }));
                      }}
                    />
                    <input
                      className="form-input"
                      style={{ fontSize: 11.5, padding: '4px 6px' }}
                      placeholder="Tên hiển thị"
                      value={f.label}
                      onChange={(e) => {
                        const fields = sb.fields.map((x, i) => (i === idx ? { ...x, label: e.target.value } : x));
                        updateBlock(sb.id, () => ({ ...sb, fields }));
                      }}
                    />
                    <select
                      className="form-select"
                      style={{ fontSize: 11.5, padding: '4px 4px' }}
                      value={f.align}
                      onChange={(e) => {
                        const fields = sb.fields.map((x, i) =>
                          i === idx ? { ...x, align: e.target.value as typeof f.align } : x,
                        );
                        updateBlock(sb.id, () => ({ ...sb, fields }));
                      }}
                    >
                      <option value="left">Trái</option>
                      <option value="center">Giữa</option>
                      <option value="right">Phải</option>
                    </select>
                    <select
                      className="form-select"
                      style={{ fontSize: 11.5, padding: '4px 4px' }}
                      value={f.pipe || ''}
                      onChange={(e) => {
                        const fields = sb.fields.map((x, i) =>
                          i === idx ? { ...x, pipe: e.target.value as typeof f.pipe } : x,
                        );
                        updateBlock(sb.id, () => ({ ...sb, fields }));
                      }}
                    >
                      <option value="">Chữ</option>
                      <option value="number">Số</option>
                      <option value="currency">Tiền</option>
                      <option value="date">Ngày</option>
                    </select>
                    <button
                      type="button"
                      className="btn btn-outline btn-xs"
                      style={{ color: '#d93025', padding: '2px 4px' }}
                      onClick={() => updateBlock(sb.id, () => ({ ...sb, fields: sb.fields.filter((_, i) => i !== idx) }))}
                    >
                      ×
                    </button>
                  </div>
                ))}
              </div>
            </div>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 90px', gap: 6 }}>
              <div>
                <label className="form-label">Mã cột tính tổng</label>
                <input
                  className="form-input"
                  placeholder="vd: thanh_tien"
                  value={sb.totalKey || ''}
                  onChange={(e) => updateBlock(sb.id, () => ({ ...sb, totalKey: e.target.value }))}
                />
              </div>
              <div>
                <label className="form-label">Nhãn dòng tổng</label>
                <input
                  className="form-input"
                  value={sb.totalLabel || 'Tổng cộng'}
                  onChange={(e) => updateBlock(sb.id, () => ({ ...sb, totalLabel: e.target.value }))}
                />
              </div>
              <div>
                <label className="form-label">Kiểu tổng</label>
                <select
                  className="form-select"
                  value={sb.totalPipe || 'currency'}
                  onChange={(e) =>
                    updateBlock(sb.id, () => ({ ...sb, totalPipe: e.target.value as 'currency' | 'number' }))
                  }
                >
                  <option value="currency">Tiền</option>
                  <option value="number">Số</option>
                </select>
              </div>
            </div>
          </div>
        );
      }

      case 'summary':
        return (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8 }}>
              <div>
                <label className="form-label">Mã cột tổng tiền</label>
                <input
                  className="form-input"
                  value={block.amountField}
                  onChange={(e) => updateBlock(block.id, () => ({ ...block, amountField: e.target.value }))}
                />
              </div>
              <div>
                <label className="form-label">Nhãn hiển thị</label>
                <input
                  className="form-input"
                  value={block.amountLabel}
                  onChange={(e) => updateBlock(block.id, () => ({ ...block, amountLabel: e.target.value }))}
                />
              </div>
            </div>
            <div style={{ display: 'flex', gap: 14, alignItems: 'center', fontSize: 12 }}>
              <label style={{ display: 'flex', alignItems: 'center', gap: 5 }}>
                <input
                  type="checkbox"
                  checked={block.showWords}
                  onChange={(e) => updateBlock(block.id, () => ({ ...block, showWords: e.target.checked }))}
                />
                Kèm dòng &ldquo;Bằng chữ&rdquo;
              </label>
              <select
                className="form-select"
                style={{ width: 120 }}
                value={block.align}
                onChange={(e) => updateBlock(block.id, () => ({ ...block, align: e.target.value as 'left' | 'right' }))}
              >
                <option value="right">Căn phải</option>
                <option value="left">Căn trái</option>
              </select>
            </div>
          </div>
        );

      case 'signatures':
        return (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
            <div>
              <label className="form-label">Số ô chữ ký</label>
              <select
                className="form-select"
                value={block.columnsCount}
                onChange={(e) => {
                  const cnt = Number(e.target.value) as 2 | 3 | 4;
                  const items = [...block.items];
                  while (items.length < cnt) items.push({ title: 'Người ký', token: '(Ký, ghi rõ họ tên)' });
                  updateBlock(block.id, () => ({ ...block, columnsCount: cnt, items }));
                }}
              >
                <option value={2}>2 chữ ký</option>
                <option value={3}>3 chữ ký</option>
                <option value={4}>4 chữ ký</option>
              </select>
            </div>
            {block.items.slice(0, block.columnsCount).map((s, idx) => (
              <div key={idx} style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 6 }}>
                <input
                  className="form-input"
                  style={{ fontSize: 12 }}
                  value={s.title}
                  onChange={(e) => {
                    const items = block.items.map((x, i) => (i === idx ? { ...x, title: e.target.value } : x));
                    updateBlock(block.id, () => ({ ...block, items }));
                  }}
                />
                <input
                  className="form-input"
                  style={{ fontSize: 12 }}
                  value={s.token}
                  onChange={(e) => {
                    const items = block.items.map((x, i) => (i === idx ? { ...x, token: e.target.value } : x));
                    updateBlock(block.id, () => ({ ...block, items }));
                  }}
                />
              </div>
            ))}
          </div>
        );

      case 'barcode':
        return (
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 100px', gap: 8 }}>
            <div>
              <label className="form-label">Mã trường</label>
              <input
                className="form-input"
                value={block.field}
                onChange={(e) => updateBlock(block.id, () => ({ ...block, field: e.target.value }))}
              />
            </div>
            <div>
              <label className="form-label">Nhãn</label>
              <input
                className="form-input"
                value={block.label || ''}
                onChange={(e) => updateBlock(block.id, () => ({ ...block, label: e.target.value }))}
              />
            </div>
            <div>
              <label className="form-label">Căn lề</label>
              <select
                className="form-select"
                value={block.align}
                onChange={(e) =>
                  updateBlock(block.id, () => ({ ...block, align: e.target.value as 'left' | 'center' | 'right' }))
                }
              >
                <option value="left">Trái</option>
                <option value="center">Giữa</option>
                <option value="right">Phải</option>
              </select>
            </div>
          </div>
        );

      case 'text':
        return (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
            <textarea
              className="form-input"
              rows={3}
              value={block.content}
              onChange={(e) => updateBlock(block.id, () => ({ ...block, content: e.target.value }))}
            />
            <div style={{ display: 'flex', gap: 10, alignItems: 'center', flexWrap: 'wrap', fontSize: 12 }}>
              <select
                className="form-select"
                style={{ width: 100 }}
                value={block.align}
                onChange={(e) =>
                  updateBlock(block.id, () => ({ ...block, align: e.target.value as 'left' | 'center' | 'right' }))
                }
              >
                <option value="left">Trái</option>
                <option value="center">Giữa</option>
                <option value="right">Phải</option>
              </select>
              <label style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
                <input
                  type="checkbox"
                  checked={block.bold}
                  onChange={(e) => updateBlock(block.id, () => ({ ...block, bold: e.target.checked }))}
                />
                Đậm
              </label>
              <label style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
                <input
                  type="checkbox"
                  checked={block.italic}
                  onChange={(e) => updateBlock(block.id, () => ({ ...block, italic: e.target.checked }))}
                />
                Nghiêng
              </label>
            </div>
          </div>
        );

      case 'divider':
        return (
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8 }}>
            <div>
              <label className="form-label">Kiểu nét</label>
              <select
                className="form-select"
                value={block.lineStyle}
                onChange={(e) =>
                  updateBlock(block.id, () => ({ ...block, lineStyle: e.target.value as 'solid' | 'dashed' }))
                }
              >
                <option value="solid">Nét liền</option>
                <option value="dashed">Nét đứt</option>
              </select>
            </div>
            <div>
              <label className="form-label">Khoảng cách (px)</label>
              <input
                type="number"
                className="form-input"
                value={block.spacing}
                onChange={(e) => updateBlock(block.id, () => ({ ...block, spacing: Number(e.target.value) || 8 }))}
              />
            </div>
          </div>
        );

      case 'pagebreak':
        return (
          <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>
            Chèn dấu ngắt trang khi in hoặc xuất PDF (<code>page-break-after: always</code>).
          </div>
        );
    }
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
      {/* Page Typography & Spacing */}
      <div style={{ display: 'grid', gridTemplateColumns: '2fr 1fr 1fr', gap: 8, padding: 8, borderRadius: 8, background: 'var(--bg-secondary)', border: '1px solid var(--border-subtle)' }}>
        <div>
          <label className="form-label">Font chữ</label>
          <select
            className="form-select"
            value={schema.page.fontFamily}
            onChange={(e) =>
              onChange({ ...schema, page: { ...schema.page, fontFamily: e.target.value as PrintBuilderFont } })
            }
          >
            <option value="'Segoe UI', Arial, sans-serif">Segoe UI / Arial (Hiện đại)</option>
            <option value="'Times New Roman', serif">Times New Roman (Hành chính VN)</option>
            <option value="monospace">Monospace (Hóa đơn nhiệt K80)</option>
          </select>
        </div>
        <div>
          <label className="form-label">Cỡ chữ (px)</label>
          <input
            type="number"
            min={10}
            max={18}
            className="form-input"
            value={schema.page.fontSize}
            onChange={(e) => onChange({ ...schema, page: { ...schema.page, fontSize: Number(e.target.value) || 13 } })}
          />
        </div>
        <div>
          <label className="form-label">Lề trong (px)</label>
          <input
            type="number"
            min={4}
            max={40}
            className="form-input"
            value={schema.page.padding}
            onChange={(e) => onChange({ ...schema, page: { ...schema.page, padding: Number(e.target.value) || 16 } })}
          />
        </div>
      </div>

      {/* Block Palette */}
      <div>
        <div style={{ fontSize: 11.5, fontWeight: 700, color: 'var(--text-muted)', marginBottom: 4 }}>
          THÊM KHỐI NỘI DUNG (BLOCKS):
        </div>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4 }}>
          {PALETTE_TYPES.map((t) => (
            <button
              key={t}
              type="button"
              className="btn btn-outline btn-xs"
              style={{ fontSize: 11, padding: '3px 8px', display: 'inline-flex', alignItems: 'center', gap: 2 }}
              onClick={() => addBlock(t)}
            >
              <AddRoundedIcon sx={{ fontSize: 13 }} /> {BLOCK_LABELS[t]}
            </button>
          ))}
        </div>
      </div>

      {/* Block List */}
      <div style={{ display: 'flex', flexDirection: 'column', gap: 4, maxHeight: 165, overflowY: 'auto', padding: 6, borderRadius: 8, border: '1px solid var(--border-subtle)' }}>
        {schema.blocks.length === 0 ? (
          <div style={{ fontSize: 12, color: 'var(--text-muted)', textAlign: 'center', padding: 12 }}>
            Chưa có khối nào. Nhấn các nút phía trên để thêm khối vào mẫu in.
          </div>
        ) : (
          schema.blocks.map((blk, idx) => {
            const active = selectedBlock?.id === blk.id;
            return (
              <div
                key={blk.id}
                onClick={() => setSelectedId(blk.id)}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  padding: '5px 8px',
                  borderRadius: 6,
                  cursor: 'pointer',
                  fontSize: 12,
                  background: active ? 'rgba(26, 115, 232, 0.1)' : 'var(--bg-primary)',
                  border: active ? '1px solid #1a73e8' : '1px solid var(--border-subtle)',
                }}
              >
                <span style={{ fontWeight: active ? 700 : 500 }}>
                  {idx + 1}. {BLOCK_LABELS[blk.type]}
                </span>
                <div style={{ display: 'flex', gap: 2 }} onClick={(e) => e.stopPropagation()}>
                  <button
                    type="button"
                    className="btn btn-outline btn-xs"
                    style={{ padding: '1px 4px' }}
                    disabled={idx === 0}
                    onClick={() => moveBlock(idx, -1)}
                    title="Lên"
                  >
                    <ArrowUpwardRoundedIcon sx={{ fontSize: 13 }} />
                  </button>
                  <button
                    type="button"
                    className="btn btn-outline btn-xs"
                    style={{ padding: '1px 4px' }}
                    disabled={idx === schema.blocks.length - 1}
                    onClick={() => moveBlock(idx, 1)}
                    title="Xuống"
                  >
                    <ArrowDownwardRoundedIcon sx={{ fontSize: 13 }} />
                  </button>
                  <button
                    type="button"
                    className="btn btn-outline btn-xs"
                    style={{ padding: '1px 4px' }}
                    onClick={() => duplicateBlock(idx)}
                    title="Nhân bản"
                  >
                    <ContentCopyRoundedIcon sx={{ fontSize: 13 }} />
                  </button>
                  <button
                    type="button"
                    className="btn btn-outline btn-xs"
                    style={{ padding: '1px 4px', color: '#d93025' }}
                    onClick={() => removeBlock(blk.id)}
                    title="Xóa khối"
                  >
                    <DeleteOutlineRoundedIcon sx={{ fontSize: 13 }} />
                  </button>
                </div>
              </div>
            );
          })
        )}
      </div>

      {/* Inspector */}
      {selectedBlock && (
        <div style={{ padding: 10, borderRadius: 8, background: 'var(--bg-secondary)', border: '1px solid var(--border-subtle)' }}>
          <div style={{ fontSize: 11.5, fontWeight: 700, color: '#1a73e8', marginBottom: 8 }}>
            CẤU HÌNH KHỐI: {BLOCK_LABELS[selectedBlock.type].toUpperCase()}
          </div>
          {renderInspector(selectedBlock)}
        </div>
      )}
    </div>
  );
}
