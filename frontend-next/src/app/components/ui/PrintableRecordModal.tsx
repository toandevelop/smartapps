'use client';

import React, { useEffect, useState } from 'react';
import PrintRoundedIcon from '@mui/icons-material/PrintRounded';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import QrCode2RoundedIcon from '@mui/icons-material/QrCode2Rounded';
import { AppRecord, SysApp, SysColumn, SysTable } from '@/lib/types';
import { getCategoryItemName } from '@/lib/category-service';
import { resolveLookupLabel } from '@/lib/lookup-service';
import { formatCurrencyVnd } from '@/lib/column-utils';
import { useEscapeKey } from '@/lib/ui-hooks';
import { getPagePrintCss, renderPrintTemplateHtml, SysPrintTemplate } from '@/lib/print-template';

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

function Code39BarcodeSvg({ value, height = 42 }: { value: string; height?: number }) {
  const clean = `*${value.toUpperCase().replace(/[^0-9A-Z.\- ]/g, '-')}*`;
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
  return (
    <svg viewBox={`0 0 ${totalWidth} ${height}`} style={{ width: '100%', maxWidth: 260, height }}>
      <rect x="0" y="0" width={totalWidth} height={height} fill="#ffffff" />
      {bars.map((b, idx) => (
        <rect key={idx} x={b.x} y={2} width={b.w} height={height - 4} fill="#111827" />
      ))}
    </svg>
  );
}

export default function PrintableRecordModal({
  open,
  app,
  table,
  columns,
  record,
  onClose,
}: {
  open: boolean;
  app: SysApp;
  table: SysTable;
  columns: SysColumn[];
  record: AppRecord | null;
  onClose: () => void;
}) {
  const [printMode, setPrintMode] = useState<string>('document');
  const [customTemplates, setCustomTemplates] = useState<SysPrintTemplate[]>([]);
  useEscapeKey(open && Boolean(record), onClose);

  useEffect(() => {
    if (!open || !table?.id) return;
    fetch(`/api/tables/${encodeURIComponent(table.id)}/print-templates`, { credentials: 'include' })
      .then((r) => (r.ok ? r.json() : null))
      .then((res) => {
        if (res?.success && Array.isArray(res.data)) {
          setCustomTemplates(res.data);
          const def = res.data.find((t: SysPrintTemplate) => t.isDefault);
          setPrintMode(def ? def.id : 'document');
        } else {
          setCustomTemplates([]);
          setPrintMode('document');
        }
      })
      .catch(() => {
        setCustomTemplates([]);
        setPrintMode('document');
      });
  }, [open, table?.id]);

  if (!open || !record) return null;

  const activeCustomTemplate = customTemplates.find((t) => t.id === printMode) || null;
  const isNotApproved = (record.approvalStatus || 'DRAFT').toUpperCase() !== 'APPROVED';
  const blockedByApproval = Boolean(activeCustomTemplate?.requireApproved && isNotApproved);

  const codeText = record.recordKey || record.id;

  const resolvedCustomData: Record<string, unknown> = { ...(record.data || {}) };
  if (activeCustomTemplate) {
    for (const col of columns) {
      const val = resolvedCustomData[col.columnCode];
      if (val === null || val === undefined || val === '') continue;
      if (col.refTableId) {
        resolvedCustomData[col.columnCode] = resolveLookupLabel(col, val) || val;
      } else if (col.refCategoryCode) {
        resolvedCustomData[col.columnCode] = getCategoryItemName(col.refCategoryCode, val)?.name || val;
      }
    }
  }

  const dialogMaxWidth = printMode === 'tag'
    ? 460
    : activeCustomTemplate?.orientation === 'landscape'
    ? 980
    : 780;

  const renderCell = (col: SysColumn): React.ReactNode => {
    const val = record.data?.[col.columnCode];
    const dt = (col.dataType || 'STRING').toUpperCase();
    if (val === null || val === undefined || val === '') return '—';

    if (dt === 'SUB_TABLE' && Array.isArray(val)) {
      if (val.length === 0) return '—';
      const subKeys = Object.keys(val[0] || {});
      return (
        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12, marginTop: 4 }}>
          <thead>
            <tr style={{ backgroundColor: '#f1f3f4' }}>
              <th style={{ border: '1px solid #dadce0', padding: '4px 6px', width: 32 }}>#</th>
              {subKeys.map((k) => (
                <th key={k} style={{ border: '1px solid #dadce0', padding: '4px 6px', textAlign: 'left' }}>
                  {k}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {val.map((row: Record<string, unknown>, idx: number) => (
              <tr key={idx}>
                <td style={{ border: '1px solid #dadce0', padding: '4px 6px', textAlign: 'center' }}>{idx + 1}</td>
                {subKeys.map((k) => (
                  <td key={k} style={{ border: '1px solid #dadce0', padding: '4px 6px' }}>
                    {row?.[k] !== undefined && row?.[k] !== null ? String(row[k]) : ''}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      );
    }

    if (dt === 'IMAGE') {
      const urlStr = typeof val === 'string'
        ? val
        : typeof val === 'object' && val !== null && 'url' in val
        ? String((val as { url?: unknown }).url ?? '')
        : '';
      return urlStr ? (
        <img src={urlStr} alt={col.columnName} style={{ maxHeight: 90, borderRadius: 6, border: '1px solid #dadce0' }} />
      ) : (
        '—'
      );
    }

    if (dt === 'FILE') {
      const urlStr = typeof val === 'string'
        ? val
        : typeof val === 'object' && val !== null && 'url' in val
        ? String((val as { url?: unknown }).url ?? '')
        : '';
      const match = urlStr.match(/[?&]name=([^&]+)/);
      return match ? decodeURIComponent(match[1]) : 'Tệp đính kèm';
    }

    if (col.refTableId) return resolveLookupLabel(col, val) || String(val);
    if (col.refCategoryCode) return getCategoryItemName(col.refCategoryCode, val)?.name || String(val);
    if (dt === 'CURRENCY') return formatCurrencyVnd(val);
    if (dt === 'BOOLEAN') return val ? 'Có' : 'Không';
    return String(val);
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-dialog printable-document" style={{ maxWidth: dialogMaxWidth }} onClick={(e) => e.stopPropagation()}>
        <div className="no-print" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 16, gap: 8, flexWrap: 'wrap' }}>
          <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
            <button
              type="button"
              className={`btn btn-xs ${printMode === 'document' ? 'btn-primary' : 'btn-outline'}`}
              onClick={() => setPrintMode('document')}
            >
              Phiếu A4 Đầy đủ
            </button>
            <button
              type="button"
              className={`btn btn-xs ${printMode === 'tag' ? 'btn-primary' : 'btn-outline'}`}
              onClick={() => setPrintMode('tag')}
            >
              <QrCode2RoundedIcon sx={{ fontSize: 15 }} /> Tem Mã vạch
            </button>
            {customTemplates.map((tpl) => (
              <button
                key={tpl.id}
                type="button"
                className={`btn btn-xs ${printMode === tpl.id ? 'btn-primary' : 'btn-outline'}`}
                onClick={() => setPrintMode(tpl.id)}
              >
                {tpl.name} ({tpl.paperSize})
              </button>
            ))}
          </div>
          <div style={{ display: 'flex', gap: 8 }}>
            <button
              type="button"
              className="btn btn-primary btn-sm"
              disabled={blockedByApproval}
              title={blockedByApproval ? 'Mẫu in này yêu cầu hồ sơ ĐÃ PHÊ DUYỆT (APPROVED) mới được in.' : undefined}
              onClick={() => window.print()}
            >
              <PrintRoundedIcon sx={{ fontSize: 16 }} /> In / Xuất PDF
            </button>
            <button type="button" className="btn btn-outline btn-xs" onClick={onClose}>
              <CloseRoundedIcon sx={{ fontSize: 16 }} />
            </button>
          </div>
        </div>

        {blockedByApproval && (
          <div className="no-print" style={{ marginBottom: 12, padding: '8px 12px', borderRadius: 8, backgroundColor: '#fef2f2', color: '#991b1b', fontSize: 12.5, fontWeight: 600 }}>
            Mẫu in &ldquo;{activeCustomTemplate?.name}&rdquo; yêu cầu hồ sơ phải ở trạng thái ĐÃ PHÊ DUYỆT (APPROVED).
          </div>
        )}

        {activeCustomTemplate ? (
          <div
            className="printable-custom-sheet"
            style={{
              position: 'relative',
              border: '1px solid #cbd5e1',
              borderRadius: 10,
              padding: 22,
              backgroundColor: '#fff',
              color: '#111827',
              maxWidth:
                activeCustomTemplate.paperSize === 'K80'
                  ? 340
                  : activeCustomTemplate.paperSize === 'LABEL_50X30'
                  ? 400
                  : activeCustomTemplate.paperSize === 'A5'
                  ? activeCustomTemplate.orientation === 'landscape'
                    ? 740
                    : 560
                  : '100%',
              margin: '0 auto',
            }}
          >
            <style>{getPagePrintCss(activeCustomTemplate.paperSize, activeCustomTemplate.orientation, activeCustomTemplate.cssStyles)}</style>
            {activeCustomTemplate.watermarkDraft && isNotApproved && (
              <div
                style={{
                  position: 'absolute',
                  inset: 0,
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  pointerEvents: 'none',
                  zIndex: 2,
                }}
              >
                <span
                  style={{
                    transform: 'rotate(-24deg)',
                    fontSize: 28,
                    fontWeight: 900,
                    color: 'rgba(220, 38, 38, 0.16)',
                    border: '4px solid rgba(220, 38, 38, 0.16)',
                    padding: '8px 18px',
                    borderRadius: 8,
                    letterSpacing: 2,
                  }}
                >
                  BẢN NHÁP • CHƯA DUYỆT
                </span>
              </div>
            )}
            <div
              dangerouslySetInnerHTML={{
                __html: renderPrintTemplateHtml(activeCustomTemplate.htmlTemplate, {
                  appName: app.name,
                  appDepartment: app.department,
                  tableName: table.displayName,
                  recordKey: codeText,
                  approvalStatus: record.approvalStatus,
                  createdBy: record.createdBy,
                  approvedBy: record.approvedBy,
                  data: resolvedCustomData,
                }),
              }}
            />
          </div>
        ) : printMode === 'tag' ? (
          <div style={{ border: '2px dashed #202124', borderRadius: 12, padding: 18, backgroundColor: '#fff', color: '#111827', textAlign: 'center' }}>
            <div style={{ fontSize: 11, fontWeight: 700, color: '#5f6368', textTransform: 'uppercase' }}>
              {app.code} • {table.displayName}
            </div>
            <div style={{ fontSize: 16, fontWeight: 800, margin: '4px 0 8px 0' }}>
              {String(record.data?.[columns[1]?.columnCode] || codeText)}
            </div>
            <div style={{ display: 'flex', justifyContent: 'center', margin: '8px 0' }}>
              <Code39BarcodeSvg value={codeText} height={46} />
            </div>
            <div style={{ fontSize: 13, fontWeight: 700, letterSpacing: '0.08em' }}>{codeText}</div>
            <div style={{ fontSize: 11, color: '#5f6368', marginTop: 6 }}>
              {columns.slice(2, 4).map((c) => `${c.columnName}: ${record.data?.[c.columnCode] ?? '—'}`).join(' • ')}
            </div>
          </div>
        ) : (
          <div style={{ border: '2px solid #202124', borderRadius: 12, padding: 24, backgroundColor: '#fff', color: '#111827' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', borderBottom: '1px solid #dadce0', paddingBottom: 14, marginBottom: 18 }}>
              <div>
                <div style={{ fontSize: 12, fontWeight: 700, color: '#5f6368', textTransform: 'uppercase' }}>
                  {app.name} • {app.department}
                </div>
                <h2 style={{ fontSize: 20, fontWeight: 800, marginTop: 4 }}>{table.displayName.toUpperCase()}</h2>
              </div>
              <div style={{ textAlign: 'right', fontSize: 12 }}>
                <div>Mã hồ sơ: <strong>{codeText}</strong></div>
                <div>Ngày in: {new Date().toLocaleDateString('vi-VN')}</div>
                {record.approvalStatus && (
                  <div>Trạng thái duyệt: <strong>{record.approvalStatus}</strong></div>
                )}
                <div style={{ marginTop: 6, maxWidth: 160, marginLeft: 'auto' }}>
                  <Code39BarcodeSvg value={codeText} height={26} />
                </div>
              </div>
            </div>

            <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13.5 }}>
              <tbody>
                {columns.map((col) => (
                  <tr key={col.id} style={{ borderBottom: '1px solid #e8eaed' }}>
                    <td style={{ padding: '9px 10px', fontWeight: 600, color: '#3c4043', width: '34%', backgroundColor: '#f8f9fa', verticalAlign: 'top' }}>
                      {col.columnName}
                    </td>
                    <td style={{ padding: '9px 12px', fontWeight: 500, color: '#202124' }}>
                      {renderCell(col)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>

            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', marginTop: 32, textAlign: 'center', fontSize: 13 }}>
              <div>
                <strong>Người lập phiếu</strong>
                <div style={{ marginTop: 44, color: '#202124', fontWeight: 600 }}>{record.createdBy || 'Nhân sự phụ trách'}</div>
              </div>
              <div>
                <strong>Xác nhận Phê duyệt</strong>
                {record.approvedBy ? (
                  <div style={{ marginTop: 18 }}>
                    <div style={{ display: 'inline-block', padding: '4px 12px', border: '2px solid #1e8e3e', color: '#1e8e3e', borderRadius: 6, fontWeight: 800, fontSize: 12 }}>
                      ĐÃ PHÊ DUYỆT
                    </div>
                    <div style={{ marginTop: 8, fontWeight: 600 }}>{record.approvedBy}</div>
                    {record.approvedAt && (
                      <div style={{ fontSize: 11, color: '#5f6368' }}>{new Date(record.approvedAt).toLocaleString('vi-VN')}</div>
                    )}
                  </div>
                ) : (
                  <div style={{ marginTop: 44, color: '#5f6368' }}>(Ký và ghi rõ họ tên)</div>
                )}
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
