'use client';

import React, { useEffect, useMemo, useState } from 'react';
import * as XLSX from 'xlsx';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import FileDownloadRoundedIcon from '@mui/icons-material/FileDownloadRounded';
import FileUploadRoundedIcon from '@mui/icons-material/FileUploadRounded';
import SaveRoundedIcon from '@mui/icons-material/SaveRounded';
import CategoryRoundedIcon from '@mui/icons-material/CategoryRounded';
import LabelRoundedIcon from '@mui/icons-material/LabelRounded';
import { SysCategory, SysCategoryItem } from '@/lib/types';
import { clearCategoryCache } from '@/lib/category-service';

const CATEGORY_COLOR_PRESETS = [
  '#1a73e8',
  '#1e8e3e',
  '#ea8600',
  '#d93025',
  '#7b1fa2',
  '#00897b',
  '#475569',
];

/* =========================================================
 * 0A. CATEGORY CREATE / RENAME MODAL
 * ========================================================= */
export function CategoryFormModal({
  open,
  editing,
  onClose,
  onSaved,
  toast,
}: {
  open: boolean;
  editing: SysCategory | null;
  onClose: () => void;
  onSaved: (savedCode: string) => Promise<void> | void;
  toast?: (msg: string, type?: 'success' | 'error') => void;
}) {
  if (!open) return null;
  return (
    <CategoryFormModalBody
      key={editing?.categoryCode || '__new__'}
      editing={editing}
      onClose={onClose}
      onSaved={onSaved}
      toast={toast}
    />
  );
}

function CategoryFormModalBody({
  editing,
  onClose,
  onSaved,
  toast,
}: {
  editing: SysCategory | null;
  onClose: () => void;
  onSaved: (savedCode: string) => Promise<void> | void;
  toast?: (msg: string, type?: 'success' | 'error') => void;
}) {
  const [categoryCode, setCategoryCode] = useState(editing?.categoryCode || '');
  const [categoryName, setCategoryName] = useState(editing?.categoryName || '');
  const [saving, setSaving] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    const code = categoryCode.trim().toUpperCase().replace(/\s+/g, '_');
    const name = categoryName.trim();
    if (!code || !name) return;

    setSaving(true);
    try {
      const res = await fetch('/api/categories', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({ categoryCode: code, categoryName: name }),
      }).then((r) => r.json());

      if (res?.success) {
        await onSaved(code);
        toast?.(editing ? 'Đã cập nhật tên danh mục!' : `Đã tạo danh mục ${code}!`);
        onClose();
      } else {
        toast?.(res?.message || 'Không lưu được danh mục.', 'error');
      }
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-dialog" style={{ maxWidth: 460, width: '95vw' }} onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <h3 style={{ margin: 0, fontSize: 16, fontWeight: 700, display: 'flex', alignItems: 'center', gap: 6 }}>
            <CategoryRoundedIcon sx={{ fontSize: 18, color: 'var(--accent-primary)' }} />
            {editing ? 'Cập nhật Danh mục' : 'Tạo Danh mục mới'}
          </h3>
          <button type="button" className="btn btn-outline btn-xs" onClick={onClose}>
            <CloseRoundedIcon sx={{ fontSize: 15 }} />
          </button>
        </div>
        <form onSubmit={handleSubmit}>
          <div className="modal-body" style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
            <div className="form-group">
              <label className="form-label">Mã danh mục *</label>
              <input
                className="form-input"
                placeholder="VD: DM_TINH_THANH"
                value={categoryCode}
                disabled={Boolean(editing)}
                onChange={(e) => setCategoryCode(e.target.value.toUpperCase())}
                required
              />
            </div>
            <div className="form-group">
              <label className="form-label">Tên danh mục *</label>
              <input
                className="form-input"
                placeholder="VD: Danh sách Tỉnh / Thành phố"
                value={categoryName}
                onChange={(e) => setCategoryName(e.target.value)}
                autoFocus
                required
              />
            </div>
          </div>
          <div className="modal-footer">
            <button type="button" className="btn btn-outline btn-sm" onClick={onClose}>
              Hủy
            </button>
            <button type="submit" className="btn btn-primary btn-sm" disabled={saving}>
              <SaveRoundedIcon sx={{ fontSize: 15 }} />
              {saving ? 'Đang lưu...' : editing ? 'Lưu thay đổi' : 'Tạo danh mục'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

/* =========================================================
 * 0B. CATEGORY ITEM CREATE / EDIT MODAL
 * ========================================================= */
export function CategoryItemFormModal({
  open,
  category,
  editing,
  defaultOrder,
  onClose,
  onSaved,
  toast,
}: {
  open: boolean;
  category: SysCategory | null;
  editing: SysCategoryItem | null;
  defaultOrder: number;
  onClose: () => void;
  onSaved: () => Promise<void> | void;
  toast?: (msg: string, type?: 'success' | 'error') => void;
}) {
  if (!open || !category) return null;
  return (
    <CategoryItemFormModalBody
      key={`${category.categoryCode}:${editing?.id || '__new__'}`}
      category={category}
      editing={editing}
      defaultOrder={defaultOrder}
      onClose={onClose}
      onSaved={onSaved}
      toast={toast}
    />
  );
}

function CategoryItemFormModalBody({
  category,
  editing,
  defaultOrder,
  onClose,
  onSaved,
  toast,
}: {
  category: SysCategory;
  editing: SysCategoryItem | null;
  defaultOrder: number;
  onClose: () => void;
  onSaved: () => Promise<void> | void;
  toast?: (msg: string, type?: 'success' | 'error') => void;
}) {
  const [itemCode, setItemCode] = useState(editing?.itemCode || '');
  const [itemName, setItemName] = useState(editing?.itemName || '');
  const [color, setColor] = useState(editing?.color || '#1a73e8');
  const [orderIndex, setOrderIndex] = useState(editing?.orderIndex ?? defaultOrder);
  const [saving, setSaving] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!itemName.trim()) return;
    setSaving(true);
    try {
      const res = await fetch('/api/categories/items', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          id: editing?.id,
          categoryCode: category.categoryCode,
          itemCode: itemCode.trim().toUpperCase(),
          itemName: itemName.trim(),
          color: color || '#1a73e8',
          orderIndex: Number(orderIndex) || 1,
        }),
      }).then((r) => r.json());

      if (res?.success) {
        clearCategoryCache(category.categoryCode);
        await onSaved();
        toast?.(editing ? 'Đã cập nhật giá trị danh mục!' : 'Đã thêm giá trị mới vào danh mục!');
        onClose();
      } else {
        toast?.(res?.message || 'Không lưu được giá trị danh mục.', 'error');
      }
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-dialog" style={{ maxWidth: 500, width: '95vw' }} onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <div>
            <h3 style={{ margin: 0, fontSize: 16, fontWeight: 700, display: 'flex', alignItems: 'center', gap: 6 }}>
              <LabelRoundedIcon sx={{ fontSize: 18, color: 'var(--accent-primary)' }} />
              {editing ? 'Sửa Giá trị Danh mục' : 'Thêm Giá trị Danh mục'}
            </h3>
            <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 2 }}>
              {category.categoryName} (<code>{category.categoryCode}</code>)
            </div>
          </div>
          <button type="button" className="btn btn-outline btn-xs" onClick={onClose}>
            <CloseRoundedIcon sx={{ fontSize: 15 }} />
          </button>
        </div>

        <form onSubmit={handleSubmit}>
          <div className="modal-body" style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 100px', gap: 10 }}>
              <div className="form-group">
                <label className="form-label">Mã giá trị (tự sinh nếu để trống)</label>
                <input
                  className="form-input"
                  placeholder="VD: HCM"
                  value={itemCode}
                  onChange={(e) => setItemCode(e.target.value.toUpperCase())}
                />
              </div>
              <div className="form-group">
                <label className="form-label">Thứ tự</label>
                <input
                  type="number"
                  className="form-input"
                  min={1}
                  value={orderIndex}
                  onChange={(e) => setOrderIndex(Number(e.target.value) || 1)}
                />
              </div>
            </div>

            <div className="form-group">
              <label className="form-label">Tên hiển thị *</label>
              <input
                className="form-input"
                placeholder="VD: TP. Hồ Chí Minh"
                value={itemName}
                onChange={(e) => setItemName(e.target.value)}
                autoFocus
                required
              />
            </div>

            <div className="form-group">
              <label className="form-label">Màu nhãn &amp; Xem trước</label>
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 10 }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                  {CATEGORY_COLOR_PRESETS.map((hex) => {
                    const isSel = color.toLowerCase() === hex.toLowerCase();
                    return (
                      <button
                        key={hex}
                        type="button"
                        onClick={() => setColor(hex)}
                        style={{
                          width: 22,
                          height: 22,
                          borderRadius: '50%',
                          backgroundColor: hex,
                          border: isSel ? '2px solid #0f172a' : '2px solid transparent',
                          cursor: 'pointer',
                          padding: 0,
                        }}
                        title={hex}
                      />
                    );
                  })}
                  <input
                    type="color"
                    value={color}
                    onChange={(e) => setColor(e.target.value)}
                    style={{ width: 26, height: 26, border: 'none', background: 'transparent', cursor: 'pointer', padding: 0 }}
                    title="Chọn màu tùy chỉnh"
                  />
                </div>
                <span
                  style={{
                    display: 'inline-flex',
                    alignItems: 'center',
                    gap: 6,
                    padding: '4px 12px',
                    borderRadius: 999,
                    fontSize: 12,
                    fontWeight: 600,
                    backgroundColor: `${color}18`,
                    color,
                    border: `1px solid ${color}40`,
                  }}
                >
                  <span style={{ width: 7, height: 7, borderRadius: '50%', backgroundColor: color }} />
                  {itemName.trim() || 'Xem trước nhãn'}
                </span>
              </div>
            </div>
          </div>

          <div className="modal-footer">
            <button type="button" className="btn btn-outline btn-sm" onClick={onClose}>
              Hủy
            </button>
            <button type="submit" className="btn btn-primary btn-sm" disabled={saving}>
              <SaveRoundedIcon sx={{ fontSize: 15 }} />
              {saving ? 'Đang lưu...' : editing ? 'Lưu thay đổi' : 'Thêm giá trị'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

/* =========================================================
 * 1. EXPORT EXCEL WITH PREVIEW MODAL
 * ========================================================= */
export function CategoryExportPreviewModal({
  open,
  category,
  items,
  categoriesCount,
  onClose,
  toast,
}: {
  open: boolean;
  category: SysCategory | null;
  items: SysCategoryItem[];
  categoriesCount: number;
  onClose: () => void;
  toast?: (msg: string, type?: 'success' | 'error') => void;
}) {
  if (!open || !category) return null;
  return (
    <CategoryExportPreviewModalBody
      key={category.categoryCode}
      category={category}
      items={items}
      categoriesCount={categoriesCount}
      onClose={onClose}
      toast={toast}
    />
  );
}

function CategoryExportPreviewModalBody({
  category,
  items,
  categoriesCount,
  onClose,
  toast,
}: {
  category: SysCategory;
  items: SysCategoryItem[];
  categoriesCount: number;
  onClose: () => void;
  toast?: (msg: string, type?: 'success' | 'error') => void;
}) {
  const [scope, setScope] = useState<'current' | 'all'>('current');
  const [allItems, setAllItems] = useState<SysCategoryItem[]>([]);
  const [loadingAll, setLoadingAll] = useState(false);
  const [fileName, setFileName] = useState(`danh_muc_${category.categoryCode.toLowerCase()}.xlsx`);

  useEffect(() => {
    if (scope !== 'all') return;
    let cancelled = false;
    void Promise.resolve().then(() => {
      if (cancelled) return;
      setLoadingAll(true);
      fetch('/api/categories/items', { credentials: 'include' })
        .then((r) => r.json())
        .then((res) => {
          if (!cancelled && res?.success && Array.isArray(res.data)) {
            setAllItems(res.data);
          }
        })
        .finally(() => {
          if (!cancelled) setLoadingAll(false);
        });
    });
    return () => {
      cancelled = true;
    };
  }, [scope]);

  const previewRows = scope === 'current' ? items : allItems;

  const handleDownload = () => {
    const aoa: (string | number)[][] =
      scope === 'current'
        ? [
            ['Mã giá trị', 'Tên hiển thị', 'Màu sắc', 'Thứ tự'],
            ...previewRows.map((it) => [it.itemCode, it.itemName, it.color || '#1a73e8', it.orderIndex]),
          ]
        : [
            ['Mã danh mục', 'Mã giá trị', 'Tên hiển thị', 'Màu sắc', 'Thứ tự'],
            ...previewRows.map((it) => [it.categoryCode, it.itemCode, it.itemName, it.color || '#1a73e8', it.orderIndex]),
          ];

    const wb = XLSX.utils.book_new();
    const ws = XLSX.utils.aoa_to_sheet(aoa);
    XLSX.utils.book_append_sheet(wb, ws, scope === 'current' ? category.categoryCode.slice(0, 31) : 'TatCaDanhMuc');
    const finalName = (fileName.trim() || `danh_muc_${category.categoryCode.toLowerCase()}.xlsx`).replace(/(\.xlsx)?$/i, '.xlsx');
    XLSX.writeFile(wb, finalName);
    toast?.(`Đã xuất ${previewRows.length} dòng ra file ${finalName}`);
    onClose();
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div
        className="modal-dialog"
        style={{ maxWidth: 780, width: '95vw' }}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="modal-header">
          <div>
            <h3 style={{ margin: 0, fontSize: 16, fontWeight: 700 }}>
              <FileDownloadRoundedIcon sx={{ fontSize: 18, verticalAlign: 'middle', mr: 0.5 }} />
              Xem trước &amp; Xuất Excel Danh mục
            </h3>
            <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 2 }}>
              Kiểm tra dữ liệu trước khi tải xuống file <code>.xlsx</code>
            </div>
          </div>
          <button type="button" className="btn-icon" onClick={onClose}>
            <CloseRoundedIcon sx={{ fontSize: 18 }} />
          </button>
        </div>

        <div className="modal-body" style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'end' }}>
            <div style={{ flex: 1, minWidth: 200 }}>
              <label className="form-label">Phạm vi xuất</label>
              <select
                className="form-select"
                value={scope}
                onChange={(e) => {
                  const next = e.target.value as 'current' | 'all';
                  setScope(next);
                  setFileName(
                    next === 'current'
                      ? `danh_muc_${category.categoryCode.toLowerCase()}.xlsx`
                      : 'tat_ca_danh_muc.xlsx'
                  );
                }}
              >
                <option value="current">
                  Danh mục hiện tại: {category.categoryName} ({items.length} mục)
                </option>
                <option value="all">Tất cả danh mục dùng chung ({categoriesCount} danh mục)</option>
              </select>
            </div>
            <div style={{ flex: 1, minWidth: 220 }}>
              <label className="form-label">Tên file xuất</label>
              <input
                className="form-input"
                value={fileName}
                onChange={(e) => setFileName(e.target.value)}
              />
            </div>
          </div>

          <div style={{ fontSize: 12.5, fontWeight: 600, color: 'var(--text-secondary)' }}>
            Xem trước dữ liệu ({loadingAll ? 'Đang tải...' : `${previewRows.length} dòng`})
          </div>

          <div className="table-responsive" style={{ maxHeight: 340, border: '1px solid var(--border-light)', borderRadius: 8 }}>
            <table className="data-table">
              <thead>
                <tr>
                  {scope === 'all' && <th>Mã danh mục</th>}
                  <th>Mã giá trị</th>
                  <th>Tên hiển thị</th>
                  <th>Màu sắc</th>
                  <th style={{ width: 80, textAlign: 'center' }}>Thứ tự</th>
                </tr>
              </thead>
              <tbody>
                {previewRows.slice(0, 100).map((it) => (
                  <tr key={it.id}>
                    {scope === 'all' && <td><code>{it.categoryCode}</code></td>}
                    <td><code>{it.itemCode}</code></td>
                    <td>{it.itemName}</td>
                    <td>
                      <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6, fontSize: 12 }}>
                        <span
                          style={{
                            width: 10,
                            height: 10,
                            borderRadius: '50%',
                            backgroundColor: it.color || '#1a73e8',
                            display: 'inline-block',
                          }}
                        />
                        {it.color || '#1a73e8'}
                      </span>
                    </td>
                    <td style={{ textAlign: 'center' }}>{it.orderIndex}</td>
                  </tr>
                ))}
                {!loadingAll && previewRows.length === 0 && (
                  <tr>
                    <td colSpan={scope === 'all' ? 5 : 4} style={{ textAlign: 'center', padding: 16, color: 'var(--text-muted)' }}>
                      Không có dữ liệu để xuất.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
          {previewRows.length > 100 && (
            <div style={{ fontSize: 11.5, color: 'var(--text-muted)' }}>
              Đang hiển thị 100 dòng đầu tiên trên tổng số {previewRows.length} dòng sẽ xuất.
            </div>
          )}
        </div>

        <div className="modal-footer">
          <button type="button" className="btn btn-outline btn-sm" onClick={onClose}>
            Đóng
          </button>
          <button
            type="button"
            className="btn btn-primary btn-sm"
            disabled={loadingAll || previewRows.length === 0}
            onClick={handleDownload}
          >
            <FileDownloadRoundedIcon sx={{ fontSize: 16 }} /> Tải xuống Excel ({previewRows.length} dòng)
          </button>
        </div>
      </div>
    </div>
  );
}

/* =========================================================
 * 2. IMPORT EXCEL WITH PREVIEW MODAL
 * ========================================================= */
export function CategoryImportPreviewModal({
  open,
  category,
  existingItems,
  onClose,
  onImported,
  toast,
}: {
  open: boolean;
  category: SysCategory | null;
  existingItems: SysCategoryItem[];
  onClose: () => void;
  onImported: () => Promise<void> | void;
  toast?: (msg: string, type?: 'success' | 'error') => void;
}) {
  if (!open) return null;
  return (
    <CategoryImportPreviewModalBody
      key={category?.categoryCode || '__none__'}
      category={category}
      existingItems={existingItems}
      onClose={onClose}
      onImported={onImported}
      toast={toast}
    />
  );
}

function CategoryImportPreviewModalBody({
  category,
  existingItems,
  onClose,
  onImported,
  toast,
}: {
  category: SysCategory | null;
  existingItems: SysCategoryItem[];
  onClose: () => void;
  onImported: () => Promise<void> | void;
  toast?: (msg: string, type?: 'success' | 'error') => void;
}) {
  const [workbook, setWorkbook] = useState<XLSX.WorkBook | null>(null);
  const [sheetNames, setSheetNames] = useState<string[]>([]);
  const [selectedSheet, setSelectedSheet] = useState('');
  const [headers, setHeaders] = useState<string[]>([]);
  const [rawRows, setRawRows] = useState<Record<string, unknown>[]>([]);
  const [codeCol, setCodeCol] = useState('');
  const [nameCol, setNameCol] = useState('');
  const [orderCol, setOrderCol] = useState('');
  const [colorCol, setColorCol] = useState('');
  const [importing, setImporting] = useState(false);

  const loadSheet = (wb: XLSX.WorkBook, sheetName: string) => {
    setSelectedSheet(sheetName);
    const ws = wb.Sheets[sheetName];
    if (!ws) return;
    const rows = XLSX.utils.sheet_to_json<Record<string, unknown>>(ws, { defval: '' });
    const cols = rows.length > 0 ? Object.keys(rows[0]) : [];
    setHeaders(cols);
    setRawRows(rows);

    const findCol = (patterns: RegExp[]) =>
      cols.find((c) => patterns.some((p) => p.test(c.trim().toLowerCase()))) || '';
    setCodeCol(findCol([/^mã giá trị$/, /^mã$/, /item.?code/, /^code$/]));
    setNameCol(findCol([/^tên hiển thị$/, /^tên$/, /item.?name/, /^name$/, /giá trị/]));
    setOrderCol(findCol([/thứ tự/, /order/, /^stt$/]));
    setColorCol(findCol([/màu/, /color/]));
  };

  const existingByCode = useMemo(() => {
    const map = new Map<string, SysCategoryItem>();
    for (const it of existingItems) {
      map.set(it.itemCode.trim().toUpperCase(), it);
    }
    return map;
  }, [existingItems]);

  const previewRows = useMemo(() => {
    return rawRows.map((row, idx) => {
      const itemName = nameCol ? String(row[nameCol] ?? '').trim() : '';
      const itemCode = codeCol ? String(row[codeCol] ?? '').trim().toUpperCase().replace(/\s+/g, '_') : '';
      const rawOrd = orderCol ? Number(row[orderCol]) : NaN;
      const orderIndex = Number.isFinite(rawOrd) && rawOrd > 0 ? Math.trunc(rawOrd) : idx + 1;
      const color = colorCol ? String(row[colorCol] ?? '').trim() || '#1a73e8' : '#1a73e8';

      const isValid = itemName.length > 0;
      const isUpdate = Boolean(itemCode && existingByCode.has(itemCode));
      return {
        rowNum: idx + 1,
        itemCode,
        itemName,
        orderIndex,
        color,
        isValid,
        isUpdate,
      };
    });
  }, [rawRows, codeCol, nameCol, orderCol, colorCol, existingByCode]);

  if (!open || !category) return null;

  const validRows = previewRows.filter((r) => r.isValid);
  const invalidCount = previewRows.length - validRows.length;

  const handleFileChange = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    const buf = await file.arrayBuffer();
    const wb = XLSX.read(buf, { type: 'array' });
    setWorkbook(wb);
    setSheetNames(wb.SheetNames);
    if (wb.SheetNames.length > 0) {
      loadSheet(wb, wb.SheetNames[0]);
    }
  };

  const handleDownloadTemplate = () => {
    const wb = XLSX.utils.book_new();
    const ws = XLSX.utils.aoa_to_sheet([
      ['Mã giá trị', 'Tên hiển thị', 'Thứ tự', 'Màu sắc'],
      ['MAU_01', 'Giá trị mẫu 1', 1, '#1a73e8'],
      ['', 'Giá trị mẫu 2 (tự sinh mã nếu để trống)', 2, '#1e8e3e'],
    ]);
    XLSX.utils.book_append_sheet(wb, ws, 'MauNhap');
    XLSX.writeFile(wb, `mau_nhap_${category.categoryCode.toLowerCase()}.xlsx`);
  };

  const handleConfirmImport = async () => {
    if (validRows.length === 0) return;
    setImporting(true);
    try {
      const res = await fetch('/api/categories/items/bulk', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          categoryCode: category.categoryCode,
          items: validRows.map((r) => ({
            categoryCode: category.categoryCode,
            itemCode: r.itemCode,
            itemName: r.itemName,
            orderIndex: r.orderIndex,
            color: r.color,
          })),
        }),
      }).then((r) => r.json());

      if (res?.success) {
        clearCategoryCache(category.categoryCode);
        await onImported();
        toast?.(`Đã nhập ${res.count ?? validRows.length} mục vào danh mục ${category.categoryCode}!`);
        onClose();
      } else {
        toast?.(res?.message || 'Import danh mục thất bại.', 'error');
      }
    } finally {
      setImporting(false);
    }
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div
        className="modal-dialog"
        style={{ maxWidth: 820, width: '95vw' }}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="modal-header">
          <div>
            <h3 style={{ margin: 0, fontSize: 16, fontWeight: 700 }}>
              <FileUploadRoundedIcon sx={{ fontSize: 18, verticalAlign: 'middle', mr: 0.5 }} />
              Import Excel có Xem trước — {category.categoryName}
            </h3>
            <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 2 }}>
              Hỗ trợ <code>.xlsx</code>, <code>.xls</code>, <code>.csv</code>. Nếu cột Mã giá trị để trống, hệ thống tự sinh mã không dấu từ Tên hiển thị.
            </div>
          </div>
          <button type="button" className="btn-icon" onClick={onClose}>
            <CloseRoundedIcon sx={{ fontSize: 18 }} />
          </button>
        </div>

        <div className="modal-body" style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between' }}>
            <input type="file" accept=".xlsx,.xls,.csv" onChange={handleFileChange} className="form-input" style={{ maxWidth: 360 }} />
            <button type="button" className="btn btn-outline btn-xs" onClick={handleDownloadTemplate}>
              <FileDownloadRoundedIcon sx={{ fontSize: 14 }} /> Tải file mẫu (.xlsx)
            </button>
          </div>

          {workbook && (
            <>
              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(150px, 1fr))', gap: 8 }}>
                {sheetNames.length > 1 && (
                  <div>
                    <label className="form-label">Sheet</label>
                    <select
                      className="form-select"
                      value={selectedSheet}
                      onChange={(e) => loadSheet(workbook, e.target.value)}
                    >
                      {sheetNames.map((s) => (
                        <option key={s} value={s}>{s}</option>
                      ))}
                    </select>
                  </div>
                )}
                <div>
                  <label className="form-label">Cột Tên hiển thị *</label>
                  <select className="form-select" value={nameCol} onChange={(e) => setNameCol(e.target.value)}>
                    <option value="">-- Chọn cột --</option>
                    {headers.map((h) => <option key={h} value={h}>{h}</option>)}
                  </select>
                </div>
                <div>
                  <label className="form-label">Cột Mã giá trị (tùy chọn)</label>
                  <select className="form-select" value={codeCol} onChange={(e) => setCodeCol(e.target.value)}>
                    <option value="">-- Tự sinh từ Tên --</option>
                    {headers.map((h) => <option key={h} value={h}>{h}</option>)}
                  </select>
                </div>
                <div>
                  <label className="form-label">Cột Thứ tự</label>
                  <select className="form-select" value={orderCol} onChange={(e) => setOrderCol(e.target.value)}>
                    <option value="">-- Tự đánh số --</option>
                    {headers.map((h) => <option key={h} value={h}>{h}</option>)}
                  </select>
                </div>
                <div>
                  <label className="form-label">Cột Màu sắc</label>
                  <select className="form-select" value={colorCol} onChange={(e) => setColorCol(e.target.value)}>
                    <option value="">-- Mặc định --</option>
                    {headers.map((h) => <option key={h} value={h}>{h}</option>)}
                  </select>
                </div>
              </div>

              <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
                <span className="badge badge-info">Tổng: {previewRows.length} dòng</span>
                <span className="badge badge-success">Hợp lệ: {validRows.length}</span>
                {invalidCount > 0 && <span className="badge badge-danger">Lỗi (thiếu tên): {invalidCount}</span>}
              </div>

              <div className="table-responsive" style={{ maxHeight: 300, border: '1px solid var(--border-light)', borderRadius: 8 }}>
                <table className="data-table">
                  <thead>
                    <tr>
                      <th style={{ width: 55 }}>#</th>
                      <th>Mã giá trị</th>
                      <th>Tên hiển thị</th>
                      <th style={{ width: 70, textAlign: 'center' }}>Thứ tự</th>
                      <th style={{ width: 120 }}>Trạng thái</th>
                    </tr>
                  </thead>
                  <tbody>
                    {previewRows.slice(0, 100).map((r) => (
                      <tr key={r.rowNum}>
                        <td>{r.rowNum}</td>
                        <td>
                          {r.itemCode ? (
                            <code>{r.itemCode}</code>
                          ) : (
                            <span style={{ fontSize: 11.5, color: 'var(--text-muted)' }}>Tự sinh</span>
                          )}
                        </td>
                        <td>{r.itemName || <span style={{ color: '#d93025' }}>(Trống)</span>}</td>
                        <td style={{ textAlign: 'center' }}>{r.orderIndex}</td>
                        <td>
                          {!r.isValid ? (
                            <span className="badge badge-danger">Bỏ qua</span>
                          ) : r.isUpdate ? (
                            <span className="badge badge-warning">Cập nhật</span>
                          ) : (
                            <span className="badge badge-success">Thêm mới</span>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </>
          )}
        </div>

        <div className="modal-footer">
          <button type="button" className="btn btn-outline btn-sm" onClick={onClose}>
            Hủy
          </button>
          <button
            type="button"
            className="btn btn-primary btn-sm"
            disabled={importing || validRows.length === 0}
            onClick={handleConfirmImport}
          >
            <FileUploadRoundedIcon sx={{ fontSize: 16 }} />
            {importing ? 'Đang nhập...' : `Xác nhận Import (${validRows.length} dòng)`}
          </button>
        </div>
      </div>
    </div>
  );
}
