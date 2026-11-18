'use client';

import React, { useEffect, useMemo, useRef, useState } from 'react';
import * as XLSX from 'xlsx';
import type { SysColumn } from '@/lib/types';
import { useAuth } from '@/lib/auth-context';
import { getImportSourceRows, ImportPreview, mapImportRows, MAX_IMPORT_ROWS, suggestImportMapping } from '@/lib/excel-import';

interface Props {
  appId: string; moduleId: string; tableId: string; tableName: string; columns: SysColumn[];
  onClose: () => void; onImported: (inserted: number, updated: number) => void;
}

export default function ExcelImportDialog({ appId, moduleId, tableId, tableName, columns, onClose, onImported }: Props) {
  const { user } = useAuth();
  const dialogRef = useRef<HTMLDialogElement>(null);
  const summaryRef = useRef<HTMLDivElement>(null);
  const requestRef = useRef<AbortController | null>(null);
  const [workbook, setWorkbook] = useState<XLSX.WorkBook | null>(null);
  const [fileName, setFileName] = useState('');
  const [sheetName, setSheetName] = useState('');
  const [grid, setGrid] = useState<unknown[][]>([]);
  const [headerRow, setHeaderRow] = useState(1);
  const [mapping, setMapping] = useState<string[]>([]);
  const [numberFormat, setNumberFormat] = useState('invariant');
  const [dateFormat, setDateFormat] = useState('yyyy-MM-dd');
  const [upsert, setUpsert] = useState(true);
  const [preview, setPreview] = useState<ImportPreview | null>(null);
  const [checkedRequest, setCheckedRequest] = useState<Record<string, unknown> | null>(null);
  const [busy, setBusy] = useState<'file' | 'preview' | 'commit' | null>(null);
  const [error, setError] = useState('');
  const [onlyErrors, setOnlyErrors] = useState(false);
  const [page, setPage] = useState(0);
  const admin = Boolean(user?.isAdmin || user?.roleCode === 'ADMIN' || user?.roleCodes?.includes('ADMIN'));
  const roles = useMemo(() => new Set([user?.roleCode, ...(user?.roleCodes || [])].map(r => r?.toUpperCase())), [user?.roleCode, user?.roleCodes]);
  const visibleColumns = useMemo(() => columns.filter(c => admin || !(c.hiddenForRoles || []).some(r => roles.has(r.toUpperCase()))), [columns, admin, roles]);
  const importColumns = useMemo(() => visibleColumns.filter(c => !c.formula && (admin || !(c.readOnlyForRoles || []).some(r => roles.has(r.toUpperCase())))), [visibleColumns, admin, roles]);
  const sourceRows = useMemo(() => getImportSourceRows(grid, headerRow), [grid, headerRow]);
  const headers = grid[headerRow - 1] || [];
  const duplicateMapping = mapping.some((target, index) => target && mapping.indexOf(target) !== index);
  const filteredRows = useMemo(() => (preview?.rows || []).filter(row => !onlyErrors || row.errors.some(e => e.severity === 'ERROR')), [preview, onlyErrors]);
  const pages = Math.max(1, Math.ceil(filteredRows.length / 25));
  const displayedRows = filteredRows.slice(page * 25, page * 25 + 25);

  useEffect(() => {
    const dialog = dialogRef.current;
    dialog?.showModal();
    return () => { requestRef.current?.abort(); dialog?.close(); };
  }, []);
  useEffect(() => { if (error || preview) summaryRef.current?.focus(); }, [error, preview]);

  const invalidate = () => { setPreview(null); setCheckedRequest(null); setError(''); setPage(0); };

  const selectSheet = (book: XLSX.WorkBook, name: string, row = 1) => {
    invalidate();
    const sheet = book.Sheets[name];
    const range = XLSX.utils.decode_range(sheet['!ref'] || 'A1');
    if (range.e.r >= 10000 || range.e.c >= 200) throw new Error('Sheet vượt 10.000 dòng hoặc 200 cột nguồn. Hãy tách file trước khi nhập.');
    // range:0 preserves original worksheet row numbers, including leading blank rows.
    const values = XLSX.utils.sheet_to_json<unknown[]>(sheet, { header: 1, range: 0, defval: null, blankrows: true, raw: true });
    setGrid(values); setSheetName(name); setHeaderRow(row);
    setMapping(suggestImportMapping(values[row - 1] || [], importColumns));
  };

  const chooseFile = async (file?: File) => {
    if (!file) return;
    invalidate(); setWorkbook(null); setGrid([]); setFileName(''); setBusy('file');
    try {
      if (file.size > 10 * 1024 * 1024) throw new Error('Tệp tối đa 10 MB cho mỗi lần nhập.');
      const book = XLSX.read(await file.arrayBuffer(), {
        type: 'array', cellDates: false, raw: true,
        ...(file.name.toLowerCase().endsWith('.csv') ? { codepage: 65001 } : {}),
      });
      if (!book.SheetNames.length) throw new Error('Tệp không có sheet dữ liệu.');
      setWorkbook(book); setFileName(file.name); selectSheet(book, book.SheetNames[0]);
    } catch (e) { setError(e instanceof Error ? e.message : 'Không đọc được tệp Excel.'); }
    finally { setBusy(null); }
  };

  const buildRequest = () => {
    const mapped = mapImportRows(sourceRows, mapping, importColumns, (value, column) => {
      if (column.dataType.toUpperCase() !== 'DATE' || typeof value !== 'number') return value;
      const date = XLSX.SSF.parse_date_code(value, { date1904: Boolean(workbook?.Workbook?.WBProps?.date1904) });
      if (!date) return String(value);
      const y = String(date.y).padStart(4, '0'), m = String(date.m).padStart(2, '0'), d = String(date.d).padStart(2, '0');
      return dateFormat === 'dd/MM/yyyy' ? `${d}/${m}/${y}` : dateFormat === 'MM/dd/yyyy' ? `${m}/${d}/${y}` : `${y}-${m}-${d}`;
    });
    return { appId, moduleId, tableId, upsertByIdentityKey: upsert, numberFormat, dateFormat, ...mapped };
  };

  const checkPreview = async () => {
    invalidate(); setBusy('preview');
    const controller = new AbortController(); requestRef.current = controller;
    try {
      const request = buildRequest();
      const response = await fetch('/api/records/bulk-import/preview', {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, credentials: 'include',
        body: JSON.stringify(request), signal: controller.signal,
      });
      const result = await response.json().catch(() => ({ success: false, message: 'Máy chủ không trả về kết quả kiểm tra hợp lệ. Vui lòng thử lại.' }));
      if (!response.ok || !result.success) throw new Error(result.message || 'Không kiểm tra được dữ liệu.');
      setPreview(result.data); setCheckedRequest(request); setOnlyErrors(result.data.errorRows > 0);
    } catch (e) { if (!controller.signal.aborted) setError(e instanceof TypeError ? 'Lỗi kết nối khi kiểm tra dữ liệu. Vui lòng thử lại.' : e instanceof Error ? e.message : 'Không kiểm tra được dữ liệu.'); }
    finally { if (!controller.signal.aborted) setBusy(null); }
  };

  const commit = async () => {
    if (!preview?.canImport || !checkedRequest || busy) return;
    setBusy('commit'); setError('');
    const controller = new AbortController(); requestRef.current = controller;
    try {
      const response = await fetch('/api/records/bulk-import', {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, credentials: 'include',
        body: JSON.stringify({ ...checkedRequest, previewFingerprint: preview.fingerprint }), signal: controller.signal,
      });
      const result = await response.json().catch(() => ({ success: false, message: 'Chưa xác nhận được kết quả nhập. Hãy đối chiếu dữ liệu và kiểm tra lại.' }));
      if (!response.ok || !result.success) {
        setPreview(result.data?.rows ? result.data : null); setCheckedRequest(null); setPage(0);
        throw new Error(result.message || 'Chưa nhập được dữ liệu. Vui lòng kiểm tra lại.');
      }
      onImported(result.data.inserted, result.data.updated);
    } catch (e) {
      if (!controller.signal.aborted) {
        setCheckedRequest(null);
        setError(e instanceof TypeError ? 'Kết nối gián đoạn. Hãy đối chiếu dữ liệu và kiểm tra lại trước khi gửi tiếp.' : e instanceof Error ? e.message : 'Chưa xác nhận được kết quả nhập. Hãy kiểm tra lại dữ liệu.');
      }
    } finally { if (!controller.signal.aborted) setBusy(null); }
  };

  const downloadTemplate = () => {
    const book = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(book, XLSX.utils.aoa_to_sheet([importColumns.map(c => c.columnCode)]), 'Data');
    XLSX.writeFile(book, 'mau-nhap.xlsx');
  };
  const downloadErrors = () => {
    if (!preview) return;
    const rows = [['Dòng Excel', 'Trường', 'Mức độ', 'Nội dung'], ...preview.rows.flatMap(row => row.errors.map(e => [row.sourceRow, e.field, e.severity, e.message]))];
    const book = XLSX.utils.book_new(); XLSX.utils.book_append_sheet(book, XLSX.utils.aoa_to_sheet(rows), 'Errors');
    XLSX.writeFile(book, 'loi-nhap-excel.xlsx');
  };

  return (
    <dialog ref={dialogRef} className="modal-dialog excel-import-dialog" aria-labelledby="excel-import-title" aria-busy={Boolean(busy)}
      onCancel={e => { e.preventDefault(); if (!busy) onClose(); }} style={{ width: 'min(1100px, calc(100vw - 32px))', maxWidth: 1100, margin: 'auto' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 16 }}>
        <h2 id="excel-import-title" style={{ fontSize: 20 }}>Nhập Excel · {tableName}</h2>
        <button type="button" className="btn btn-outline btn-sm" disabled={Boolean(busy)} onClick={onClose}>Đóng</button>
      </div>
      <p style={{ color: 'var(--text-secondary)', margin: '12px 0' }}>Chọn sheet → Ánh xạ cột → Kiểm tra và xem trước → Xác nhận nhập. Tối đa {MAX_IMPORT_ROWS} dòng/lần; có lỗi thì chưa nhập dòng nào.</p>
      <div ref={summaryRef} tabIndex={-1} role={error || preview?.errorRows ? 'alert' : 'status'} style={{ margin: '12px 0' }}>
        {error && <p style={{ color: 'var(--danger, #b3261e)' }}>{error}</p>}
        {preview && <p><strong>{preview.totalRows} dòng</strong> · Tạo mới {preview.inserted} · Cập nhật {preview.updated} · Hợp lệ {preview.validRows} · Lỗi {preview.errorRows}. {preview.canImport ? 'Chưa ghi dữ liệu; có thể xác nhận nhập.' : 'Sửa tệp hoặc ánh xạ rồi kiểm tra lại.'}</p>}
      </div>
      <fieldset disabled={Boolean(busy)} style={{ border: 0, padding: 0, minWidth: 0 }}>
        <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'end', gap: 16 }}>
          <label className="form-label">Tệp Excel/CSV
            <input type="file" accept=".xlsx,.xls,.csv" className="form-input" onChange={e => { const file = e.target.files?.[0]; e.target.value = ''; void chooseFile(file); }} />
          </label>
          <button type="button" className="btn btn-outline btn-sm" onClick={downloadTemplate}>Tải file mẫu</button>
          {workbook && <>
            <label className="form-label">Sheet
              <select className="form-input" value={sheetName} onChange={e => { try { selectSheet(workbook, e.target.value); } catch (e) { setGrid([]); setError(e instanceof Error ? e.message : 'Không đọc được sheet.'); } }}>
                {workbook.SheetNames.map(name => <option key={name}>{name}</option>)}
              </select>
            </label>
            <label className="form-label">Dòng tiêu đề
              <input type="number" min={1} max={Math.max(1, grid.length)} className="form-input" value={headerRow} style={{ width: 100 }} onChange={e => {
                const next = Math.max(1, Math.min(grid.length || 1, Number(e.target.value) || 1));
                invalidate(); setHeaderRow(next); setMapping(suggestImportMapping(grid[next - 1] || [], importColumns));
              }} />
            </label>
          </>}
        </div>
        {grid.length > 0 && <>
          <p style={{ fontSize: 13, margin: '14px 0' }}>{fileName} · {sourceRows.length} dòng dữ liệu. Dòng trống được bỏ qua; số dòng lỗi giữ theo Excel gốc.</p>
          {sourceRows.length > MAX_IMPORT_ROWS && <p role="alert" style={{ color: '#b3261e' }}>Vượt {MAX_IMPORT_ROWS} dòng dữ liệu. Hãy tách file trước khi kiểm tra.</p>}
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 16, marginBottom: 16 }}>
            <label className="form-label">Định dạng số văn bản
              <select className="form-input" value={numberFormat} onChange={e => { invalidate(); setNumberFormat(e.target.value); }}>
                <option value="invariant">1,234.56 (chấm thập phân)</option><option value="vi-VN">1.234,56 (phẩy thập phân)</option>
              </select>
            </label>
            <label className="form-label">Định dạng ngày văn bản
              <select className="form-input" value={dateFormat} onChange={e => { invalidate(); setDateFormat(e.target.value); }}>
                <option value="yyyy-MM-dd">yyyy-MM-dd</option><option value="dd/MM/yyyy">dd/MM/yyyy</option><option value="MM/dd/yyyy">MM/dd/yyyy</option>
              </select>
            </label>
            <label style={{ alignSelf: 'center' }}><input type="checkbox" checked={upsert} onChange={e => { invalidate(); setUpsert(e.target.checked); }} /> Cập nhật khi trùng mã định danh</label>
          </div>
          <p style={{ fontSize: 13 }}>Trường không ánh xạ được giữ nguyên khi cập nhật; ô trống trong cột đã ánh xạ sẽ xóa giá trị cũ. Dùng mã danh mục/liên kết, không dùng nhãn hiển thị. Mã có số 0 đầu cần lưu dạng văn bản trong Excel.</p>
          {duplicateMapping && <p id="mapping-error" role="alert" style={{ color: '#b3261e' }}>Mỗi trường đích chỉ được ánh xạ một lần.</p>}
          <div style={{ overflowX: 'auto', maxHeight: 300, margin: '12px 0' }}>
            <table className="data-table" style={{ width: '100%' }}>
              <caption style={{ textAlign: 'left' }}>Ánh xạ cột nguồn vào trường dữ liệu</caption>
              <thead><tr><th>Cột Excel</th><th>Ví dụ dòng đầu</th><th>Trường đích</th></tr></thead>
              <tbody>{headers.map((header, index) => <tr key={index}>
                <td>{XLSX.utils.encode_col(index)} · {String(header ?? '(trống)')}</td>
                <td style={{ maxWidth: 260, overflowWrap: 'anywhere' }}>{String(sourceRows[0]?.values[index] ?? '').slice(0, 150)}</td>
                <td><select className="form-input" aria-label={`Trường đích cho cột ${XLSX.utils.encode_col(index)}`} aria-invalid={Boolean(mapping[index] && mapping.indexOf(mapping[index]) !== index)} aria-describedby={duplicateMapping ? 'mapping-error' : undefined}
                  value={mapping[index] || ''} onChange={e => { invalidate(); setMapping(current => current.map((value, i) => i === index ? e.target.value : value)); }}>
                  <option value="">Bỏ qua cột này</option>
                  {importColumns.map(c => <option key={c.columnCode} value={c.columnCode}>{c.columnName} ({c.columnCode}){c.isRequired ? ' *' : ''}</option>)}
                </select></td>
              </tr>)}</tbody>
            </table>
          </div>
          <button type="button" className="btn btn-outline" disabled={duplicateMapping || sourceRows.length === 0 || sourceRows.length > MAX_IMPORT_ROWS} onClick={checkPreview}>Kiểm tra và xem trước</button>
        </>}
      </fieldset>
      {preview && <section aria-label="Xem trước kết quả nhập" style={{ marginTop: 20 }}>
        <div style={{ display: 'flex', gap: 16, flexWrap: 'wrap', alignItems: 'center' }}>
          <label><input type="checkbox" checked={onlyErrors} onChange={e => { setOnlyErrors(e.target.checked); setPage(0); }} /> Chỉ dòng lỗi</label>
          <button type="button" className="btn btn-outline btn-sm" onClick={downloadErrors} disabled={Boolean(busy) || !preview.rows.some(row => row.errors.length)}>Tải báo cáo lỗi</button>
        </div>
        <div style={{ overflowX: 'auto', maxHeight: 360, marginTop: 12 }}>
          <table className="data-table" style={{ width: '100%' }}>
            <caption style={{ textAlign: 'left' }}>Dữ liệu sau chuẩn hóa và kiểm tra trên máy chủ</caption>
            <thead><tr><th>Dòng Excel</th><th>Thao tác</th>{visibleColumns.map(c => <th key={c.columnCode}>{c.columnName}</th>)}<th>Lỗi / cảnh báo</th></tr></thead>
            <tbody>{displayedRows.map(row => <tr key={row.sourceRow}>
              <td>{row.sourceRow}</td><td>{row.operation === 'UPDATE' ? 'Cập nhật' : 'Tạo mới'}</td>
              {visibleColumns.map(c => <td key={c.columnCode} style={{ maxWidth: 240, overflowWrap: 'anywhere' }}>{typeof row.data[c.columnCode] === 'object' && row.data[c.columnCode] != null ? JSON.stringify(row.data[c.columnCode]) : String(row.data[c.columnCode] ?? '')}</td>)}
              <td style={{ minWidth: 240 }}>{row.errors.length ? <ul>{row.errors.map((issue, i) => <li key={i} style={{ color: issue.severity === 'ERROR' ? '#b3261e' : 'var(--text-secondary)' }}><strong>{issue.field || 'Dòng'}:</strong> {issue.message}</li>)}</ul> : 'Hợp lệ'}</td>
            </tr>)}</tbody>
          </table>
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 12, margin: '12px 0' }}>
          <button type="button" className="btn btn-outline btn-sm" disabled={page === 0} onClick={() => setPage(p => p - 1)}>Trang trước</button>
          <span>Trang {page + 1}/{pages} · {filteredRows.length} dòng</span>
          <button type="button" className="btn btn-outline btn-sm" disabled={page + 1 >= pages} onClick={() => setPage(p => p + 1)}>Trang sau</button>
        </div>
      </section>}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 16, marginTop: 20 }}>
        <span role="status">{busy === 'file' ? 'Đang đọc tệp...' : busy === 'preview' ? 'Đang kiểm tra dữ liệu...' : busy === 'commit' ? 'Đang nhập dữ liệu, vui lòng chờ...' : 'Xem trước không ghi dữ liệu.'}</span>
        <button type="button" className="btn btn-primary" disabled={Boolean(busy) || !preview?.canImport || !checkedRequest} onClick={commit}>Xác nhận nhập {preview?.totalRows || 0} dòng</button>
      </div>
    </dialog>
  );
}
