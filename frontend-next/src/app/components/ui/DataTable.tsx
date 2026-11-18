'use client';

import React, { useRef, useEffect, useState, useMemo, useCallback } from 'react';
import { createPortal } from 'react-dom';
import SearchableSelect from './SearchableSelect';
import EmptyState from './EmptyState';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import SearchRoundedIcon from '@mui/icons-material/SearchRounded';
import ViewColumnRoundedIcon from '@mui/icons-material/ViewColumnRounded';
import ArrowUpwardRoundedIcon from '@mui/icons-material/ArrowUpwardRounded';
import ArrowDownwardRoundedIcon from '@mui/icons-material/ArrowDownwardRounded';
import UnfoldMoreRoundedIcon from '@mui/icons-material/UnfoldMoreRounded';
import FilterAltRoundedIcon from '@mui/icons-material/FilterAltRounded';
import FilterAltOffRoundedIcon from '@mui/icons-material/FilterAltOffRounded';
import RestartAltRoundedIcon from '@mui/icons-material/RestartAltRounded';
import PushPinRoundedIcon from '@mui/icons-material/PushPinRounded';
import PushPinOutlinedIcon from '@mui/icons-material/PushPinOutlined';
import TableRowsRoundedIcon from '@mui/icons-material/TableRowsRounded';
import ViewAgendaRoundedIcon from '@mui/icons-material/ViewAgendaRounded';
import MobileCardView from './MobileCardView';

export interface ColumnDef<T> {
  key: string;
  header: React.ReactNode;
  label?: string; // Plain text label for filter box & chips
  width?: string | number;
  align?: 'left' | 'center' | 'right';
  render?: (row: T, index: number) => React.ReactNode;
  sortable?: boolean;
  filterable?: boolean;
  filterType?: 'text' | 'select' | 'date';
  filterOptions?: Array<{ value: string; label: string; subLabel?: string }>;
  refCategoryCode?: string | null;
  defaultHidden?: boolean;
  pinned?: 'left' | 'right' | false;
}

export interface DataTableProps<T extends { id: string }> {
  // Google Workspace Header / Title Props
  title?: React.ReactNode;
  subtitle?: React.ReactNode;
  badge?: React.ReactNode;
  headerActions?: React.ReactNode;

  // Search & Filter Props
  searchPlaceholder?: string;
  searchValue?: string;
  onSearchChange?: (value: string) => void;
  statusFilter?: {
    value: string;
    onChange: (val: string) => void;
    options: Array<{ value: string; label: string }>;
  };

  columns: ColumnDef<T>[];
  data: T[];
  loading?: boolean;
  enableSelection?: boolean; // Default: true
  selectedIds?: string[];
  onSelectionChange?: (selectedIds: string[]) => void;
  onBatchDelete?: (selectedIds: string[]) => void;
  batchActions?: React.ReactNode;
  emptyText?: string;
  emptyAction?: React.ReactNode;
  storageKey?: string; // Key to persist column visibility in localStorage
  enableColumnVisibility?: boolean;
  enableSorting?: boolean;
  enableFiltering?: boolean;
  enablePinning?: boolean;
  serverSort?: {
    sortKey: string;
    direction: 'asc' | 'desc';
    onSortChange: (key: string, dir: 'asc' | 'desc') => void;
  };
  pagination?: {
    page: number;
    pageSize: number;
    total: number;
    totalPages: number;
    onPageChange: (page: number) => void;
    onPageSizeChange: (pageSize: number) => void;
  };
  toolbarExtra?: React.ReactNode;
  hideToolbar?: boolean;
}

function getRowFieldValue<T>(row: T, key: string): any {
  const r = row as Record<string, any>;
  if (r.data && typeof r.data === 'object' && key in r.data) {
    return r.data[key];
  }
  return r[key];
}

export default function DataTable<T extends { id: string }>({
  title,
  subtitle,
  badge,
  headerActions,
  searchPlaceholder,
  searchValue,
  onSearchChange,
  statusFilter,
  hideToolbar = false,
  columns,
  data,
  loading = false,
  enableSelection = true,
  selectedIds: selectedIdsProp,
  onSelectionChange: onSelectionChangeProp,
  onBatchDelete,
  batchActions,
  emptyText = 'Chưa có bản ghi nào phù hợp với bộ lọc.',
  emptyAction,
  storageKey,
  enableColumnVisibility = true,
  enableSorting = true,
  enableFiltering = true,
  enablePinning = true,
  serverSort,
  pagination,
  toolbarExtra,
}: DataTableProps<T>) {
  const checkAllRef = useRef<HTMLInputElement>(null);
  const colPickerRef = useRef<HTMLDivElement>(null);

  // Search state (Controlled or Uncontrolled)
  const isControlledSearch = searchValue !== undefined;
  const [internalGlobalSearch, setInternalGlobalSearch] = useState('');
  const globalSearch = isControlledSearch ? searchValue : internalGlobalSearch;

  const handleSearchChange = (val: string) => {
    if (!isControlledSearch) {
      setInternalGlobalSearch(val);
    }
    onSearchChange?.(val);
  };

  // Selection state (Controlled or Uncontrolled)
  const [internalSelectedIds, setInternalSelectedIds] = useState<string[]>([]);
  const isControlledSelection = onSelectionChangeProp !== undefined;
  const selectedIds = isControlledSelection ? (selectedIdsProp || []) : internalSelectedIds;
  const onSelectionChange = isControlledSelection ? onSelectionChangeProp : setInternalSelectedIds;
  const hasSelection = enableSelection !== false;

  // Column Visibility State
  const [showColPicker, setShowColPicker] = useState(false);
  const [colSearchTerm, setColSearchTerm] = useState('');
  const [hiddenCols, setHiddenCols] = useState<Set<string>>(() => {
    if (typeof window !== 'undefined' && storageKey) {
      try {
        const saved = localStorage.getItem(`dt_cols_${storageKey}`);
        if (saved) {
          return new Set(JSON.parse(saved));
        }
      } catch (e) {
        console.warn('Failed to load hidden columns from localStorage:', e);
      }
    }
    const defaultHidden = new Set<string>();
    columns.forEach((c) => {
      if (c.defaultHidden) defaultHidden.add(c.key);
    });
    return defaultHidden;
  });

  // Column Pinning State (Left pinned columns)
  const [pinnedLeftCols, setPinnedLeftCols] = useState<Set<string>>(() => {
    if (typeof window !== 'undefined' && storageKey) {
      try {
        const saved = localStorage.getItem(`dt_pinned_${storageKey}`);
        if (saved) return new Set(JSON.parse(saved));
      } catch (e) {
        // ignore
      }
    }
    const defaults = new Set<string>();
    columns.forEach((c) => {
      if (c.pinned === 'left') defaults.add(c.key);
    });
    return defaults;
  });

  const togglePinColumn = (key: string) => {
    setPinnedLeftCols((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      if (typeof window !== 'undefined' && storageKey) {
        try {
          localStorage.setItem(`dt_pinned_${storageKey}`, JSON.stringify(Array.from(next)));
        } catch (e) {
          // ignore
        }
      }
      return next;
    });
  };

  // Client-side Sorting State
  const [clientSortKey, setClientSortKey] = useState<string | null>(null);
  const [clientSortDir, setClientSortDir] = useState<'asc' | 'desc'>('asc');

  // Filter Popover Box & Filter State
  const filterBoxRef = useRef<HTMLDivElement>(null);
  const filterPopoverRef = useRef<HTMLDivElement>(null);
  const colPopoverRef = useRef<HTMLDivElement>(null);
  const [showFilterBox, setShowFilterBox] = useState(false);
  const [filterBoxSearch, setFilterBoxSearch] = useState('');
  const [showFilterRow, setShowFilterRow] = useState(false);
  const [columnFilters, setColumnFilters] = useState<Record<string, string>>({});
  const [filterPos, setFilterPos] = useState<{ top?: number; bottom?: number; left: number; width: number; maxHeight: number } | null>(null);
  const [colPos, setColPos] = useState<{ top?: number; bottom?: number; left: number; width: number; maxHeight: number } | null>(null);

  const computePopoverPos = useCallback((anchorEl: HTMLElement | null, targetWidth: number, alignRight = false) => {
    if (!anchorEl || typeof window === 'undefined') return null;
    const rect = anchorEl.getBoundingClientRect();
    const margin = 12;
    const width = Math.min(targetWidth, window.innerWidth - margin * 2);
    const rawLeft = alignRight ? rect.right - width : rect.left;
    const left = Math.max(margin, Math.min(rawLeft, window.innerWidth - width - margin));
    const spaceBelow = window.innerHeight - rect.bottom - margin;
    const spaceAbove = rect.top - margin;

    if (spaceBelow >= 300 || spaceBelow >= spaceAbove) {
      return {
        top: rect.bottom + 6,
        left,
        width,
        maxHeight: Math.max(220, Math.min(540, spaceBelow - 6)),
      };
    }
    return {
      bottom: window.innerHeight - rect.top + 6,
      left,
      width,
      maxHeight: Math.max(220, Math.min(540, spaceAbove - 6)),
    };
  }, []);

  useEffect(() => {
    if (!showFilterBox) {
      setFilterPos(null);
      return;
    }
    const update = () => setFilterPos(computePopoverPos(filterBoxRef.current, 600, false));
    update();
    window.addEventListener('resize', update);
    window.addEventListener('scroll', update, true);
    return () => {
      window.removeEventListener('resize', update);
      window.removeEventListener('scroll', update, true);
    };
  }, [showFilterBox, computePopoverPos]);

  useEffect(() => {
    if (!showColPicker) {
      setColPos(null);
      return;
    }
    const update = () => setColPos(computePopoverPos(colPickerRef.current, 270, true));
    update();
    window.addEventListener('resize', update);
    window.addEventListener('scroll', update, true);
    return () => {
      window.removeEventListener('resize', update);
      window.removeEventListener('scroll', update, true);
    };
  }, [showColPicker, computePopoverPos]);

  // Persist column visibility when changed
  const toggleColumnVisibility = (key: string) => {
    setHiddenCols((prev) => {
      const next = new Set(prev);
      if (next.has(key)) {
        next.delete(key);
      } else {
        // Prevent hiding all columns
        if (next.size >= columns.length - 1) return prev;
        next.add(key);
      }
      if (typeof window !== 'undefined' && storageKey) {
        try {
          localStorage.setItem(`dt_cols_${storageKey}`, JSON.stringify(Array.from(next)));
        } catch (e) {
          // ignore
        }
      }
      return next;
    });
  };

  const resetColumnsToDefault = () => {
    const next = new Set<string>();
    columns.forEach((c) => {
      if (c.defaultHidden) next.add(c.key);
    });
    setHiddenCols(next);
    if (typeof window !== 'undefined' && storageKey) {
      localStorage.removeItem(`dt_cols_${storageKey}`);
    }
  };

  const showAllColumns = () => {
    setHiddenCols(new Set());
    if (typeof window !== 'undefined' && storageKey) {
      localStorage.setItem(`dt_cols_${storageKey}`, JSON.stringify([]));
    }
  };

  // Close column picker and filter box on outside click or Escape
  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      const target = event.target as Node;
      if (
        showColPicker &&
        colPickerRef.current &&
        !colPickerRef.current.contains(target) &&
        (!colPopoverRef.current || !colPopoverRef.current.contains(target))
      ) {
        setShowColPicker(false);
      }
      if (
        showFilterBox &&
        filterBoxRef.current &&
        !filterBoxRef.current.contains(target) &&
        (!filterPopoverRef.current || !filterPopoverRef.current.contains(target))
      ) {
        setShowFilterBox(false);
      }
    }
    function handleEsc(e: KeyboardEvent) {
      if (e.key === 'Escape') {
        if (showFilterBox) setShowFilterBox(false);
        if (showColPicker) setShowColPicker(false);
      }
    }
    if (showColPicker || showFilterBox) {
      document.addEventListener('mousedown', handleClickOutside);
      document.addEventListener('keydown', handleEsc);
    }
    return () => {
      document.removeEventListener('mousedown', handleClickOutside);
      document.removeEventListener('keydown', handleEsc);
    };
  }, [showColPicker, showFilterBox]);

  // Visible columns
  const visibleColumns = useMemo(() => {
    return columns.filter((c) => !hiddenCols.has(c.key));
  }, [columns, hiddenCols]);

  // Responsive View Mode (Auto-cards on mobile/tablet viewports for wide tables)
  const [viewMode, setViewMode] = useState<'auto' | 'table' | 'cards'>('auto');
  const [isMobileViewport, setIsMobileViewport] = useState(false);

  useEffect(() => {
    const handleResize = () => {
      setIsMobileViewport(window.innerWidth < 768);
    };
    handleResize();
    window.addEventListener('resize', handleResize);
    return () => window.removeEventListener('resize', handleResize);
  }, []);

  const isCardViewActive =
    viewMode === 'cards' || (viewMode === 'auto' && isMobileViewport && visibleColumns.length >= 6);


  // Handle Sort Click
  const handleSortClick = (key: string, sortable?: boolean) => {
    if (!enableSorting || sortable === false) return;

    if (serverSort) {
      const nextDir = serverSort.sortKey === key && serverSort.direction === 'asc' ? 'desc' : 'asc';
      serverSort.onSortChange(key, nextDir);
      return;
    }

    if (clientSortKey === key) {
      if (clientSortDir === 'asc') {
        setClientSortDir('desc');
      } else {
        setClientSortKey(null);
        setClientSortDir('asc');
      }
    } else {
      setClientSortKey(key);
      setClientSortDir('asc');
    }
  };

  // Process data (Client-side Filter & Sort if serverSort not provided)
  const processedData = useMemo(() => {
    let result = [...data];

    // 1. Global Search
    if (globalSearch.trim() && !isControlledSearch) {
      const q = globalSearch.trim().toLowerCase();
      result = result.filter((row) => {
        return visibleColumns.some((col) => {
          const val = getRowFieldValue(row, col.key);
          if (val === null || val === undefined) return false;
          return String(val).toLowerCase().includes(q);
        });
      });
    }

    // 2. Column Filters (category, date, and text support)
    const activeFilters = Object.entries(columnFilters).filter(([_, v]) => v && v.trim() !== '');
    if (activeFilters.length > 0) {
      result = result.filter((row) => {
        return activeFilters.every(([colKey, filterVal]) => {
          const val = getRowFieldValue(row, colKey);
          if (val === null || val === undefined) return false;
          const colDef = columns.find((c) => c.key === colKey);
          const fv = filterVal.trim().toLowerCase();
          const rv = String(val).trim().toLowerCase();

          // Exact or option label match for select / category filter
          if (colDef?.filterType === 'select') {
            if (rv === fv) return true;
            const opt = colDef.filterOptions?.find((o) => o.value.toLowerCase() === fv);
            if (opt && rv === opt.label.toLowerCase()) return true;
            return false;
          }

          // Date match (prefix or includes)
          if (colDef?.filterType === 'date') {
            return rv.startsWith(fv);
          }

          // Default text substring match
          return rv.includes(fv);
        });
      });
    }

    // 3. Client Sort
    if (!serverSort && clientSortKey) {
      result.sort((a, b) => {
        const valA = getRowFieldValue(a, clientSortKey);
        const valB = getRowFieldValue(b, clientSortKey);

        if (valA === valB) return 0;
        if (valA === null || valA === undefined) return 1;
        if (valB === null || valB === undefined) return -1;

        let comp = 0;
        if (typeof valA === 'number' && typeof valB === 'number') {
          comp = valA - valB;
        } else if (valA instanceof Date && valB instanceof Date) {
          comp = valA.getTime() - valB.getTime();
        } else {
          comp = String(valA).localeCompare(String(valB), 'vi', { sensitivity: 'base' });
        }
        return clientSortDir === 'asc' ? comp : -comp;
      });
    }

    return result;
  }, [data, globalSearch, columnFilters, clientSortKey, clientSortDir, serverSort, visibleColumns, columns, isControlledSearch]);

  // Pre-calculate pinned offsets for left and right columns
  const { leftOffsets, rightOffsets, lastPinnedLeftKey, firstPinnedRightKey } = useMemo(() => {
    const leftMap: Record<string, number> = {};
    const rightMap: Record<string, number> = {};

    let curLeft = hasSelection ? 46 : 0;
    let lastLeft = '';

    visibleColumns.forEach((col) => {
      const isPinnedLeft = pinnedLeftCols.has(col.key) || col.pinned === 'left';
      if (isPinnedLeft) {
        leftMap[col.key] = curLeft;
        lastLeft = col.key;
        let w = 120;
        if (typeof col.width === 'number') w = col.width;
        else if (typeof col.width === 'string' && col.width.endsWith('px')) {
          w = parseInt(col.width, 10) || 120;
        }
        curLeft += w;
      }
    });

    let curRight = 0;
    let firstRight = '';
    for (let i = visibleColumns.length - 1; i >= 0; i--) {
      const col = visibleColumns[i];
      const isPinnedRight = col.pinned === 'right';
      if (isPinnedRight) {
        rightMap[col.key] = curRight;
        firstRight = col.key;
        let w = 90;
        if (typeof col.width === 'number') w = col.width;
        else if (typeof col.width === 'string' && col.width.endsWith('px')) {
          w = parseInt(col.width, 10) || 90;
        }
        curRight += w;
      }
    }

    return {
      leftOffsets: leftMap,
      rightOffsets: rightMap,
      lastPinnedLeftKey: lastLeft,
      firstPinnedRightKey: firstRight,
    };
  }, [visibleColumns, hasSelection, pinnedLeftCols]);

  const activeColumnFilterCount = Object.values(columnFilters).filter((v) => v && v.trim() !== '').length;
  const activeFilterCount = activeColumnFilterCount + (globalSearch.trim() ? 1 : 0);

  const clearColumnFilters = () => {
    setColumnFilters({});
  };

  const clearAllFilters = () => {
    setColumnFilters({});
    handleSearchChange('');
  };

  // Filterable columns list for filter box popover
  const filterableColumns = useMemo(() => {
    return columns.filter((c) => c.filterable !== false && c.key !== '_actions' && c.key !== 'actions' && c.key !== 'stt');
  }, [columns]);

  const filteredFieldList = useMemo(() => {
    if (!filterBoxSearch.trim()) return filterableColumns;
    const q = filterBoxSearch.trim().toLowerCase();
    return filterableColumns.filter((c) => {
      const label = c.label || (typeof c.header === 'string' ? c.header : c.key);
      const catCode = c.refCategoryCode || '';
      return label.toLowerCase().includes(q) || c.key.toLowerCase().includes(q) || catCode.toLowerCase().includes(q);
    });
  }, [filterableColumns, filterBoxSearch]);

  const getOptionsForColumn = (col: ColumnDef<T>): Array<{ value: string; label: string; subLabel?: string }> => {
    if (col.filterOptions && col.filterOptions.length > 0) {
      return col.filterOptions;
    }
    const uniqueVals = new Set<string>();
    data.forEach((row) => {
      const val = getRowFieldValue(row, col.key);
      if (val !== null && val !== undefined && String(val).trim() !== '') {
        uniqueVals.add(String(val).trim());
      }
    });
    return Array.from(uniqueVals)
      .sort((a, b) => a.localeCompare(b, 'vi'))
      .slice(0, 100)
      .map((v) => ({ value: v, label: v }));
  };

  // Selection logic
  const isAllSelected = processedData.length > 0 && selectedIds.length === processedData.length;
  const isIndeterminate = selectedIds.length > 0 && selectedIds.length < processedData.length;

  useEffect(() => {
    if (checkAllRef.current) {
      checkAllRef.current.indeterminate = isIndeterminate;
    }
  }, [isIndeterminate]);

  const handleToggleAll = () => {
    if (isAllSelected) {
      onSelectionChange([]);
    } else {
      onSelectionChange(processedData.map((r) => r.id));
    }
  };

  const handleToggleRow = (id: string) => {
    if (selectedIds.includes(id)) {
      onSelectionChange(selectedIds.filter((item) => item !== id));
    } else {
      onSelectionChange([...selectedIds, id]);
    }
  };

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        backgroundColor: 'var(--bg-surface)',
        border: '1px solid var(--border-subtle)',
        borderRadius: '10px',
        boxShadow: '0 1px 3px rgba(60,64,67,0.08), 0 1px 2px rgba(60,64,67,0.04)',
        overflow: 'visible',
      }}
    >
      {/* 1. TOP HEADER BAR (Title, Code, Badge, Subtitle & Primary Header Actions) */}
      {(title || headerActions) && (
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            padding: '12px 18px',
            borderBottom: '1px solid var(--border-light)',
            backgroundColor: 'var(--bg-surface)',
            borderTopLeftRadius: '10px',
            borderTopRightRadius: '10px',
            flexWrap: 'wrap',
            gap: '12px',
          }}
        >
          <div style={{ display: 'flex', flexDirection: 'column', gap: '3px' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
              {typeof title === 'string' ? (
                <h3 style={{ fontSize: '15px', fontWeight: 700, color: 'var(--text-primary)', margin: 0 }}>
                  {title}
                </h3>
              ) : (
                title
              )}
              {badge && (
                <span
                  style={{
                    fontSize: '11px',
                    fontWeight: 700,
                    padding: '2px 8px',
                    borderRadius: '12px',
                    backgroundColor: 'var(--info-bg)',
                    color: '#1a73e8',
                  }}
                >
                  {badge}
                </span>
              )}
            </div>
            {subtitle && (
              <div style={{ fontSize: '12px', color: 'var(--text-muted)', display: 'flex', alignItems: 'center', gap: '6px' }}>
                {subtitle}
              </div>
            )}
          </div>

          {headerActions && (
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
              {headerActions}
            </div>
          )}
        </div>
      )}

      {/* 2. UNIFIED GOOGLE WORKSPACE TOOLBAR */}
      {!hideToolbar && (
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            padding: '8px 14px',
            backgroundColor: 'var(--bg-primary)',
            borderBottom: '1px solid var(--border-subtle)',
            flexWrap: 'wrap',
            gap: '8px',
          }}
        >
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap', flex: 1 }}>
            {/* Google-style Pill Search Input */}
            <div
              style={{
                position: 'relative',
                display: 'inline-flex',
                alignItems: 'center',
              }}
            >
              <SearchRoundedIcon
                sx={{
                  fontSize: 18,
                  color: 'var(--text-muted)',
                  position: 'absolute',
                  left: '10px',
                  pointerEvents: 'none',
                }}
              />
              <input
                type="text"
                placeholder={searchPlaceholder || 'Tìm nhanh trên bảng...'}
                aria-label={searchPlaceholder || 'Tìm kiếm nhanh trên bảng'}
                value={globalSearch}
                onChange={(e) => handleSearchChange(e.target.value)}
                style={{
                  paddingLeft: '32px',
                  paddingRight: globalSearch ? '28px' : '10px',
                  height: '32px',
                  fontSize: '12.5px',
                  width: '230px',
                  borderRadius: '18px',
                  border: '1px solid var(--border-subtle)',
                  backgroundColor: 'var(--bg-surface)',
                  outline: 'none',
                  transition: 'all 0.15s ease',
                  color: 'var(--text-primary)',
                }}
                onFocus={(e) => {
                  e.currentTarget.style.borderColor = '#1a73e8';
                  e.currentTarget.style.boxShadow = '0 1px 3px rgba(26, 115, 232, 0.2)';
                }}
                onBlur={(e) => {
                  e.currentTarget.style.borderColor = 'var(--border-subtle)';
                  e.currentTarget.style.boxShadow = 'none';
                }}
              />
              {globalSearch && (
                <button
                  type="button"
                  onClick={() => handleSearchChange('')}
                  style={{
                    position: 'absolute',
                    right: '8px',
                    background: 'none',
                    border: 'none',
                    color: 'var(--text-muted)',
                    cursor: 'pointer',
                    padding: '2px',
                    display: 'flex',
                    alignItems: 'center',
                  }}
                  title="Xóa tìm kiếm"
                >
                  <CloseRoundedIcon sx={{ fontSize: 14 }} />
                </button>
              )}
            </div>

            {/* Status Filter Dropdown if provided */}
            {statusFilter && (
              <select
                value={statusFilter.value}
                onChange={(e) => statusFilter.onChange(e.target.value)}
                style={{
                  height: '32px',
                  fontSize: '12px',
                  padding: '0 10px',
                  borderRadius: '16px',
                  border: '1px solid var(--border-subtle)',
                  backgroundColor: statusFilter.value !== 'ALL' ? 'var(--info-bg)' : 'var(--bg-surface)',
                  color: statusFilter.value !== 'ALL' ? '#1a73e8' : 'var(--text-secondary)',
                  fontWeight: statusFilter.value !== 'ALL' ? 600 : 400,
                  cursor: 'pointer',
                  outline: 'none',
                }}
              >
                {statusFilter.options.map((opt) => (
                  <option key={opt.value} value={opt.value}>{opt.label}</option>
                ))}
              </select>
            )}

            {/* Filter Popover Box Button */}
            {enableFiltering && (
              <div style={{ position: 'relative' }} ref={filterBoxRef}>
                <button
                  type="button"
                  onClick={() => setShowFilterBox(!showFilterBox)}
                  style={{
                    height: '32px',
                    padding: '0 12px',
                    fontSize: '12px',
                    borderRadius: '16px',
                    border: (showFilterBox || activeColumnFilterCount > 0) ? '1px solid #1a73e8' : '1px solid var(--border-subtle)',
                    backgroundColor: (showFilterBox || activeColumnFilterCount > 0) ? 'var(--info-bg)' : 'var(--bg-surface)',
                    color: (showFilterBox || activeColumnFilterCount > 0) ? '#1a73e8' : 'var(--text-secondary)',
                    display: 'inline-flex',
                    alignItems: 'center',
                    gap: '6px',
                    cursor: 'pointer',
                    fontWeight: (showFilterBox || activeColumnFilterCount > 0) ? 600 : 500,
                    boxShadow: (showFilterBox || activeColumnFilterCount > 0) ? '0 1px 2px rgba(26,115,232,0.15)' : 'none',
                    transition: 'all 0.15s ease',
                  }}
                  title="Mở bảng điều khiển lọc dữ liệu nâng cao theo từng trường & danh mục"
                >
                  <FilterAltRoundedIcon sx={{ fontSize: 16, color: (showFilterBox || activeColumnFilterCount > 0) ? '#1a73e8' : 'var(--text-muted)' }} />
                  <span>Bộ lọc</span>
                  {activeColumnFilterCount > 0 && (
                    <span
                      style={{
                        backgroundColor: '#1a73e8',
                        color: '#ffffff',
                        fontSize: '11.5px',
                        fontWeight: 700,
                        padding: '1px 6px',
                        borderRadius: '10px',
                      }}
                    >
                      {activeColumnFilterCount}
                    </span>
                  )}
                </button>

                {/* Popover Filter Box (Portaled to body so short tables or overflow containers never clip it) */}
                {showFilterBox && typeof document !== 'undefined' && filterPos && createPortal(
                  <div
                    ref={filterPopoverRef}
                    role="dialog"
                    aria-label="Bộ lọc dữ liệu theo trường"
                    style={{
                      position: 'fixed',
                      ...(filterPos.top !== undefined ? { top: `${filterPos.top}px` } : { bottom: `${filterPos.bottom}px` }),
                      left: `${filterPos.left}px`,
                      width: `${filterPos.width}px`,
                      maxHeight: `${filterPos.maxHeight}px`,
                      backgroundColor: 'var(--bg-surface)',
                      borderRadius: '12px',
                      boxShadow: '0 14px 36px rgba(15, 23, 42, 0.22), 0 2px 6px rgba(15, 23, 42, 0.08)',
                      border: '1px solid var(--border-subtle)',
                      zIndex: 1400,
                      display: 'flex',
                      flexDirection: 'column',
                      animation: 'fadeIn 0.12s ease',
                      overflow: 'hidden',
                    }}
                  >
                    {/* Header */}
                    <div
                      style={{
                        padding: '12px 16px',
                        backgroundColor: 'var(--bg-primary)',
                        borderBottom: '1px solid var(--border-light)',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'space-between',
                        flexShrink: 0,
                      }}
                    >
                      <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                        <div
                          style={{
                            width: '32px',
                            height: '32px',
                            borderRadius: '8px',
                            backgroundColor: 'var(--info-bg)',
                            color: '#1a73e8',
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'center',
                          }}
                        >
                          <FilterAltRoundedIcon sx={{ fontSize: 18 }} />
                        </div>
                        <div>
                          <div style={{ fontSize: '13.5px', fontWeight: 700, color: 'var(--text-primary)', display: 'flex', alignItems: 'center', gap: '8px' }}>
                            <span>Bộ Lọc Dữ Liệu Theo Trường</span>
                            {activeColumnFilterCount > 0 && (
                              <span style={{ fontSize: '11px', backgroundColor: 'var(--info-bg)', color: '#1a73e8', padding: '1px 8px', borderRadius: '10px', fontWeight: 600 }}>
                                {activeColumnFilterCount} điều kiện áp dụng
                              </span>
                            )}
                          </div>
                          <div style={{ fontSize: '11px', color: 'var(--text-muted)', marginTop: '1px' }}>
                            Chọn giá trị hoặc tìm kiếm theo danh mục MoET & kiểu dữ liệu
                          </div>
                        </div>
                      </div>

                      <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                        {activeColumnFilterCount > 0 && (
                          <button
                            type="button"
                            onClick={clearColumnFilters}
                            style={{
                              background: 'none',
                              border: 'none',
                              color: '#d93025',
                              fontSize: '11.5px',
                              fontWeight: 600,
                              cursor: 'pointer',
                              padding: '4px 8px',
                              borderRadius: '4px',
                            }}
                          >
                            Xóa tất cả
                          </button>
                        )}
                        <button
                          type="button"
                          onClick={() => setShowFilterBox(false)}
                          style={{
                            background: 'none',
                            border: 'none',
                            color: 'var(--text-muted)',
                            cursor: 'pointer',
                            padding: '4px',
                            borderRadius: '50%',
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'center',
                          }}
                        >
                          <CloseRoundedIcon sx={{ fontSize: 18 }} />
                        </button>
                      </div>
                    </div>

                    {/* Quick Search inside filter box */}
                    <div style={{ padding: '8px 16px', borderBottom: '1px solid var(--border-light)', backgroundColor: 'var(--bg-surface)', display: 'flex', alignItems: 'center', gap: '8px', flexShrink: 0 }}>
                      <SearchRoundedIcon sx={{ fontSize: 18, color: 'var(--text-muted)' }} />
                      <input
                        type="text"
                        placeholder="Tìm nhanh trường cần lọc (VD: giới tính, dân tộc, tên, trạng thái...)"
                        value={filterBoxSearch}
                        onChange={(e) => setFilterBoxSearch(e.target.value)}
                        style={{
                          border: 'none',
                          outline: 'none',
                          fontSize: '12px',
                          flex: 1,
                          backgroundColor: 'transparent',
                          color: 'var(--text-primary)',
                        }}
                      />
                      {filterBoxSearch && (
                        <button
                          type="button"
                          onClick={() => setFilterBoxSearch('')}
                          style={{
                            background: 'none',
                            border: 'none',
                            color: 'var(--text-muted)',
                            cursor: 'pointer',
                            padding: '2px',
                            display: 'flex',
                          }}
                        >
                          <CloseRoundedIcon sx={{ fontSize: 15 }} />
                        </button>
                      )}
                    </div>

                    {/* Body: 2-column Grid of filterable fields */}
                    <div
                      style={{
                        padding: '14px 16px',
                        flex: 1,
                        minHeight: 0,
                        overflowY: 'auto',
                        display: 'grid',
                        gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))',
                        gap: '12px',
                        backgroundColor: '#fbfcfd',
                      }}
                    >
                      {filteredFieldList.length === 0 ? (
                        <div style={{ gridColumn: 'span 2', textAlign: 'center', padding: '30px 0', color: 'var(--text-muted)', fontSize: '12.5px' }}>
                          Không tìm thấy trường nào phù hợp với từ khóa &quot;{filterBoxSearch}&quot;
                        </div>
                      ) : (
                        filteredFieldList.map((col) => {
                          const colKey = col.key;
                          const filterVal = columnFilters[colKey] || '';
                          const isHasVal = Boolean(filterVal.trim());
                          const isSelect = col.filterType === 'select' || Boolean(col.refCategoryCode) || (col.filterOptions && col.filterOptions.length > 0);
                          const isDate = col.filterType === 'date';
                          const label = col.label || (typeof col.header === 'string' ? col.header : col.key);
                          const options = getOptionsForColumn(col);

                          return (
                            <div
                              key={colKey}
                              style={{
                                backgroundColor: isHasVal ? '#f0f7ff' : 'var(--bg-surface)',
                                border: isHasVal ? '1.5px solid #1a73e8' : '1px solid var(--border-subtle)',
                                borderRadius: '8px',
                                padding: '8px 10px',
                                display: 'flex',
                                flexDirection: 'column',
                                gap: '6px',
                                boxShadow: isHasVal ? '0 1px 3px rgba(26,115,232,0.12)' : 'none',
                                transition: 'all 0.15s ease',
                              }}
                            >
                              {/* Field Label & Badge */}
                              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '6px' }}>
                                <div style={{ fontSize: '11.5px', fontWeight: 600, color: isHasVal ? '#1a73e8' : 'var(--text-primary)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }} title={label}>
                                  {label}
                                </div>
                                <div style={{ display: 'flex', alignItems: 'center', gap: '4px' }}>
                                  {col.refCategoryCode ? (
                                    <span
                                      style={{
                                        fontSize: '11.5px',
                                        padding: '1px 5px',
                                        borderRadius: '4px',
                                        backgroundColor: '#f3e8ff',
                                        color: '#7c3aed',
                                        fontWeight: 600,
                                      }}
                                      title={`Danh mục MoET: ${col.refCategoryCode}`}
                                    >
                                      DM
                                    </span>
                                  ) : isDate ? (
                                    <span style={{ fontSize: '11.5px', padding: '1px 5px', borderRadius: '4px', backgroundColor: 'var(--warning-bg)', color: 'var(--warning-text)', fontWeight: 600 }}>
                                      Ngày
                                    </span>
                                  ) : isSelect ? (
                                    <span style={{ fontSize: '11.5px', padding: '1px 5px', borderRadius: '4px', backgroundColor: '#e0f2fe', color: '#0369a1', fontWeight: 600 }}>
                                      Chọn
                                    </span>
                                  ) : null}

                                  {isHasVal && (
                                    <button
                                      type="button"
                                      onClick={() => setColumnFilters((prev) => ({ ...prev, [colKey]: '' }))}
                                      style={{
                                        background: 'none',
                                        border: 'none',
                                        color: '#d93025',
                                        cursor: 'pointer',
                                        padding: '0 2px',
                                        fontSize: '11px',
                                        fontWeight: 700,
                                        display: 'flex',
                                        alignItems: 'center',
                                      }}
                                      title="Xóa lọc trường này"
                                    >
                                      <CloseRoundedIcon sx={{ fontSize: 13 }} />
                                    </button>
                                  )}
                                </div>
                              </div>

                              {/* Control */}
                              <div>
                                {isSelect ? (
                                  <SearchableSelect
                                    value={filterVal}
                                    onChange={(v) => setColumnFilters((prev) => ({ ...prev, [colKey]: v }))}
                                    options={[
                                      { value: '', label: `-- Tất cả ${label} --` },
                                      ...options,
                                    ]}
                                    placeholder={`-- Tất cả --`}
                                    searchPlaceholder={`Tìm ${label}...`}
                                    size="sm"
                                    style={{ width: '100%' }}
                                  />
                                ) : isDate ? (
                                  <input
                                    type="date"
                                    className="form-input"
                                    value={filterVal}
                                    onChange={(e) => setColumnFilters((prev) => ({ ...prev, [colKey]: e.target.value }))}
                                    style={{ height: '30px', fontSize: '11.5px', width: '100%', padding: '2px 8px' }}
                                  />
                                ) : (
                                  <div style={{ position: 'relative', display: 'flex', alignItems: 'center' }}>
                                    <input
                                      type="text"
                                      className="form-input"
                                      placeholder={`Nhập từ khóa...`}
                                      value={filterVal}
                                      onChange={(e) => setColumnFilters((prev) => ({ ...prev, [colKey]: e.target.value }))}
                                      style={{ height: '30px', fontSize: '11.5px', width: '100%', paddingRight: isHasVal ? '24px' : '8px' }}
                                    />
                                    {isHasVal && (
                                      <button
                                        type="button"
                                        onClick={() => setColumnFilters((prev) => ({ ...prev, [colKey]: '' }))}
                                        style={{
                                          position: 'absolute',
                                          right: '6px',
                                          background: 'none',
                                          border: 'none',
                                          color: 'var(--text-muted)',
                                          cursor: 'pointer',
                                          padding: '2px',
                                          display: 'flex',
                                          alignItems: 'center',
                                        }}
                                      >
                                        <CloseRoundedIcon sx={{ fontSize: 14 }} />
                                      </button>
                                    )}
                                  </div>
                                )}
                              </div>
                            </div>
                          );
                        })
                      )}
                    </div>

                    {/* Footer */}
                    <div
                      style={{
                        padding: '10px 16px',
                        backgroundColor: 'var(--bg-primary)',
                        borderTop: '1px solid var(--border-light)',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'space-between',
                        flexShrink: 0,
                      }}
                    >
                      <label style={{ display: 'flex', alignItems: 'center', gap: '6px', cursor: 'pointer', fontSize: '11.5px', color: 'var(--text-muted)', margin: 0 }}>
                        <input
                          type="checkbox"
                          checked={showFilterRow}
                          onChange={(e) => setShowFilterRow(e.target.checked)}
                          style={{ accentColor: '#1a73e8' }}
                        />
                        <span>Hiện hàng lọc trực tiếp trên từng cột</span>
                      </label>

                      <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                        {activeColumnFilterCount > 0 && (
                          <button
                            type="button"
                            onClick={clearColumnFilters}
                            className="btn btn-secondary"
                            style={{ height: '30px', padding: '0 12px', fontSize: '12px' }}
                          >
                            Đặt lại
                          </button>
                        )}
                        <button
                          type="button"
                          onClick={() => setShowFilterBox(false)}
                          className="btn btn-primary"
                          style={{ height: '30px', padding: '0 14px', fontSize: '12px', fontWeight: 600 }}
                        >
                          Áp dụng & Đóng
                        </button>
                      </div>
                    </div>
                  </div>,
                  document.body
                )}
              </div>
            )}

            {/* Reset Filters button if any active */}
            {activeFilterCount > 0 && (
              <button
                type="button"
                onClick={clearAllFilters}
                style={{
                  height: '32px',
                  padding: '0 9px',
                  fontSize: '11.5px',
                  borderRadius: '16px',
                  color: '#d93025',
                  backgroundColor: 'var(--danger-bg)',
                  border: '1px solid var(--danger-border)',
                  cursor: 'pointer',
                  display: 'inline-flex',
                  alignItems: 'center',
                  gap: '4px',
                }}
                title="Xóa tất cả các bộ lọc hiện tại"
              >
                <FilterAltOffRoundedIcon sx={{ fontSize: 14 }} />
                <span>Xóa lọc ({activeFilterCount})</span>
              </button>
            )}

            {toolbarExtra}
          </div>

          {/* Right side controls: Column Visibility */}
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            {enableColumnVisibility && (
              <div style={{ position: 'relative' }} ref={colPickerRef}>
                <button
                  type="button"
                  onClick={() => setShowColPicker(!showColPicker)}
                  style={{
                    height: '32px',
                    padding: '0 11px',
                    fontSize: '12px',
                    borderRadius: '16px',
                    border: showColPicker ? '1px solid #1a73e8' : '1px solid var(--border-subtle)',
                    backgroundColor: showColPicker ? 'var(--info-bg)' : 'var(--bg-surface)',
                    color: showColPicker ? '#1a73e8' : 'var(--text-muted)',
                    cursor: 'pointer',
                    display: 'inline-flex',
                    alignItems: 'center',
                    gap: '5px',
                    fontWeight: 500,
                  }}
                  aria-expanded={showColPicker}
                  aria-haspopup="dialog"
                  aria-label={`Tùy chỉnh cột hiển thị (${visibleColumns.length}/${columns.length} cột đang bật)`}
                  title="Bật/tắt các cột hiển thị trên bảng"
                >
                  <ViewColumnRoundedIcon sx={{ fontSize: 16, color: '#1a73e8' }} />
                  <span>Cột hiển thị</span>
                  <span
                    style={{
                      backgroundColor: 'var(--info-bg)',
                      color: '#1a73e8',
                      padding: '1px 6px',
                      borderRadius: '10px',
                      fontSize: '11px',
                      fontWeight: 700,
                    }}
                  >
                    {visibleColumns.length}/{columns.length}
                  </span>
                </button>

              {/* Popover Dropdown (Portaled to body so short tables never clip it) */}
              {showColPicker && typeof document !== 'undefined' && colPos && createPortal(
                <div
                  ref={colPopoverRef}
                  role="dialog"
                  aria-label="Cấu hình cột hiển thị"
                  style={{
                    position: 'fixed',
                    ...(colPos.top !== undefined ? { top: `${colPos.top}px` } : { bottom: `${colPos.bottom}px` }),
                    left: `${colPos.left}px`,
                    width: `${colPos.width}px`,
                    maxHeight: `${colPos.maxHeight}px`,
                    backgroundColor: 'var(--bg-surface)',
                    border: '1px solid var(--border-subtle)',
                    borderRadius: '10px',
                    boxShadow: '0 12px 28px rgba(15, 23, 42, 0.18)',
                    zIndex: 1400,
                    display: 'flex',
                    flexDirection: 'column',
                    overflow: 'hidden',
                    animation: 'fadeIn 0.12s ease',
                  }}
                >
                  {/* Popover Header */}
                  <div
                    style={{
                      padding: '10px 12px',
                      borderBottom: '1px solid var(--border-light)',
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'space-between',
                      flexShrink: 0,
                    }}
                  >
                    <span style={{ fontSize: '12.5px', fontWeight: 700, color: 'var(--text-primary)' }}>
                      Cấu hình cột ({visibleColumns.length}/{columns.length})
                    </span>
                    <button
                      type="button"
                      onClick={() => setShowColPicker(false)}
                      style={{
                        background: 'none',
                        border: 'none',
                        color: 'var(--text-muted)',
                        cursor: 'pointer',
                        padding: '2px',
                      }}
                    >
                      <CloseRoundedIcon sx={{ fontSize: 16 }} />
                    </button>
                  </div>

                  {/* Search inside columns */}
                  <div style={{ padding: '8px 10px', borderBottom: '1px solid var(--border-light)', flexShrink: 0 }}>
                    <input
                      type="text"
                      placeholder="Tìm tên cột..."
                      value={colSearchTerm}
                      onChange={(e) => setColSearchTerm(e.target.value)}
                      className="form-input"
                      style={{ height: '28px', fontSize: '11.5px', padding: '2px 8px', width: '100%' }}
                      autoFocus
                    />
                  </div>

                  {/* Column List */}
                  <div style={{ flex: 1, minHeight: 0, overflowY: 'auto', padding: '6px 8px' }}>
                    {columns
                      .filter((c) => {
                        if (!colSearchTerm.trim()) return true;
                        const headerStr = typeof c.header === 'string' ? c.header : c.key;
                        return headerStr.toLowerCase().includes(colSearchTerm.trim().toLowerCase());
                      })
                      .map((col) => {
                        const isVisible = !hiddenCols.has(col.key);
                        const isPinnedLeft = pinnedLeftCols.has(col.key) || col.pinned === 'left';
                        const isPinnedRight = col.pinned === 'right';

                        return (
                          <div
                            key={col.key}
                            style={{
                              display: 'flex',
                              alignItems: 'center',
                              justifyContent: 'space-between',
                              gap: '8px',
                              padding: '5px 8px',
                              borderRadius: '4px',
                              fontSize: '12px',
                              backgroundColor: isVisible ? 'transparent' : 'rgba(0,0,0,0.02)',
                              color: isVisible ? 'var(--text-primary)' : 'var(--text-muted)',
                              transition: 'background-color 0.1s',
                            }}
                            onMouseEnter={(e) => (e.currentTarget.style.backgroundColor = 'var(--bg-secondary)')}
                            onMouseLeave={(e) =>
                              (e.currentTarget.style.backgroundColor = isVisible ? 'transparent' : 'rgba(0,0,0,0.02)')
                            }
                          >
                            <label
                              style={{
                                display: 'flex',
                                alignItems: 'center',
                                gap: '8px',
                                flex: 1,
                                cursor: 'pointer',
                                overflow: 'hidden',
                              }}
                            >
                              <input
                                type="checkbox"
                                checked={isVisible}
                                onChange={() => toggleColumnVisibility(col.key)}
                                style={{ width: '15px', height: '15px', accentColor: '#1a73e8', cursor: 'pointer' }}
                              />
                              <span style={{ flex: 1, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                                {col.header}
                              </span>
                            </label>

                            {/* Pin Toggle Button */}
                            {enablePinning && !isPinnedRight && (
                              <button
                                type="button"
                                onClick={(e) => {
                                  e.stopPropagation();
                                  togglePinColumn(col.key);
                                }}
                                style={{
                                  background: 'none',
                                  border: 'none',
                                  cursor: 'pointer',
                                  padding: '2px 4px',
                                  borderRadius: '4px',
                                  color: isPinnedLeft ? '#1a73e8' : '#9aa0a6',
                                  display: 'inline-flex',
                                  alignItems: 'center',
                                }}
                                title={isPinnedLeft ? 'Bỏ ghim cột bên trái' : 'Ghim cố định cột bên trái'}
                              >
                                {isPinnedLeft ? (
                                  <PushPinRoundedIcon sx={{ fontSize: 15 }} />
                                ) : (
                                  <PushPinOutlinedIcon sx={{ fontSize: 15 }} />
                                )}
                              </button>
                            )}
                          </div>
                        );
                      })}
                  </div>

                  {/* Popover Footer (Quick Actions) */}
                  <div
                    style={{
                      padding: '8px 10px',
                      borderTop: '1px solid var(--border-light)',
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'space-between',
                      backgroundColor: 'var(--bg-primary)',
                      flexShrink: 0,
                    }}
                  >
                    <button
                      type="button"
                      onClick={showAllColumns}
                      style={{
                        background: 'none',
                        border: 'none',
                        color: '#1a73e8',
                        fontSize: '11px',
                        fontWeight: 600,
                        cursor: 'pointer',
                        padding: '2px 4px',
                      }}
                    >
                      Hiện tất cả
                    </button>
                    <button
                      type="button"
                      onClick={resetColumnsToDefault}
                      style={{
                        background: 'none',
                        border: 'none',
                        color: 'var(--text-muted)',
                        fontSize: '11px',
                        cursor: 'pointer',
                        display: 'flex',
                        alignItems: 'center',
                        gap: '2px',
                        padding: '2px 4px',
                      }}
                    >
                      <RestartAltRoundedIcon sx={{ fontSize: 13 }} />
                      <span>Mặc định</span>
                    </button>
                  </div>
                </div>,
                document.body
              )}
            </div>
          )}

          {/* View Mode Toggle (Table / Mobile Cards) */}
          <div
            style={{
              display: 'inline-flex',
              border: '1px solid var(--border-subtle)',
              borderRadius: '16px',
              overflow: 'hidden',
              backgroundColor: 'var(--bg-surface)',
              height: '32px',
            }}
            role="group"
            aria-label="Chế độ hiển thị dạng bảng hoặc thẻ di động"
          >
            <button
              type="button"
              onClick={() => setViewMode('table')}
              style={{
                padding: '0 9px',
                border: 'none',
                background: !isCardViewActive ? 'var(--info-bg)' : 'transparent',
                color: !isCardViewActive ? '#1a73e8' : 'var(--text-secondary)',
                cursor: 'pointer',
                display: 'inline-flex',
                alignItems: 'center',
                gap: '4px',
                fontSize: '11.5px',
                fontWeight: 600,
              }}
              aria-pressed={!isCardViewActive}
              title="Chế độ xem dạng bảng chi tiết"
            >
              <TableRowsRoundedIcon sx={{ fontSize: 16 }} />
              <span>Bảng</span>
            </button>
            <button
              type="button"
              onClick={() => setViewMode('cards')}
              style={{
                padding: '0 9px',
                border: 'none',
                background: isCardViewActive ? 'var(--info-bg)' : 'transparent',
                color: isCardViewActive ? '#1a73e8' : 'var(--text-secondary)',
                cursor: 'pointer',
                display: 'inline-flex',
                alignItems: 'center',
                gap: '4px',
                fontSize: '11.5px',
                fontWeight: 600,
              }}
              aria-pressed={isCardViewActive}
              title="Chế độ xem dạng thẻ di động"
            >
              <ViewAgendaRoundedIcon sx={{ fontSize: 16 }} />
              <span>Thẻ</span>
            </button>
          </div>
        </div>
      </div>
      )}

      {/* Active Filter Chips Bar */}
      {activeColumnFilterCount > 0 && (
        <div
          style={{
            padding: '6px 16px',
            backgroundColor: 'var(--bg-primary)',
            borderTop: '1px solid #edf2f7',
            borderBottom: '1px solid var(--border-subtle)',
            display: 'flex',
            alignItems: 'center',
            gap: '8px',
            flexWrap: 'wrap',
            fontSize: '11.5px',
          }}
        >
          <span style={{ fontWeight: 600, color: 'var(--text-muted)', display: 'flex', alignItems: 'center', gap: '4px' }}>
            <FilterAltRoundedIcon sx={{ fontSize: 14, color: '#1a73e8' }} /> Đang lọc ({activeColumnFilterCount}):
          </span>
          {Object.entries(columnFilters).map(([colKey, val]) => {
            if (!val || !val.trim()) return null;
            const col = columns.find((c) => c.key === colKey);
            const label = col?.label || (typeof col?.header === 'string' ? col.header : colKey);
            const opt = col?.filterOptions?.find((o) => o.value === val);
            const displayVal = opt ? opt.label : val;

            return (
              <span
                key={colKey}
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  gap: '5px',
                  padding: '2px 8px',
                  borderRadius: '12px',
                  backgroundColor: 'var(--info-bg)',
                  color: '#1a73e8',
                  border: '1px solid var(--info-border)',
                  fontSize: '11.5px',
                  fontWeight: 500,
                }}
              >
                <strong style={{ fontWeight: 600 }}>{label}:</strong>
                <span>{displayVal}</span>
                <button
                  type="button"
                  onClick={() => setColumnFilters((prev) => ({ ...prev, [colKey]: '' }))}
                  style={{
                    background: 'none',
                    border: 'none',
                    color: '#1a73e8',
                    cursor: 'pointer',
                    padding: '0 2px',
                    display: 'flex',
                    alignItems: 'center',
                    fontWeight: 700,
                    fontSize: '11px',
                  }}
                  title={`Bỏ lọc ${label}`}
                >
                  <CloseRoundedIcon sx={{ fontSize: 13 }} />
                </button>
              </span>
            );
          })}
          <button
            type="button"
            onClick={clearColumnFilters}
            style={{
              background: 'none',
              border: 'none',
              color: '#d93025',
              fontSize: '11px',
              fontWeight: 600,
              cursor: 'pointer',
              padding: '2px 6px',
              borderRadius: '4px',
              textDecoration: 'underline',
            }}
          >
            Xóa tất cả
          </button>
        </div>
      )}

      {/* Batch Action Bar when rows are selected */}
      {hasSelection && selectedIds.length > 0 && (
        <div
          style={{
            padding: '10px 16px',
            backgroundColor: 'rgba(26, 115, 232, 0.08)',
            border: '1px solid #1a73e8',
            borderRadius: '8px',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            animation: 'fadeIn 0.15s ease',
          }}
        >
          <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
            <span
              style={{
                fontSize: '13px',
                fontWeight: 700,
                color: '#1a73e8',
                display: 'flex',
                alignItems: 'center',
                gap: '6px',
              }}
            >
              <span>Đã chọn</span>
              <span
                style={{
                  backgroundColor: '#1a73e8',
                  color: '#ffffff',
                  padding: '1px 8px',
                  borderRadius: '12px',
                  fontSize: '12px',
                }}
              >
                {selectedIds.length}
              </span>
              <span>/ {processedData.length} bản ghi</span>
            </span>

            <button
              type="button"
              onClick={() => onSelectionChange?.([])}
              style={{
                background: 'none',
                border: 'none',
                color: 'var(--text-secondary)',
                fontSize: '12px',
                cursor: 'pointer',
                textDecoration: 'underline',
                padding: '2px 6px',
              }}
            >
              Bỏ chọn tất cả
            </button>
          </div>

          <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            {batchActions}
            {onBatchDelete && (
              <button
                type="button"
                className="btn btn-outline"
                onClick={() => onBatchDelete(selectedIds)}
                style={{
                  borderColor: 'var(--danger)',
                  color: 'var(--danger)',
                  backgroundColor: 'rgba(217, 48, 37, 0.06)',
                  fontSize: '12px',
                  padding: '5px 12px',
                  display: 'flex',
                  alignItems: 'center',
                  gap: '6px',
                  fontWeight: 600,
                  borderRadius: '6px',
                }}
              >
                <DeleteOutlineRoundedIcon sx={{ fontSize: 16 }} />
                <span>Xóa {selectedIds.length} mục đã chọn</span>
              </button>
            )}
          </div>
        </div>
      )}

      {/* Main Table or Mobile Card View */}
      {isCardViewActive ? (
        <div style={{ padding: '16px 12px' }}>
          <MobileCardView
            columns={visibleColumns}
            data={processedData}
            selectedIds={selectedIds}
            onSelectRow={handleToggleRow}
            enableSelection={hasSelection}
            emptyText={typeof emptyText === 'string' ? emptyText : 'Không có bản ghi nào.'}
            loading={loading}
            batchActions={batchActions}
          />
        </div>
      ) : (
        <div
          className="table-responsive"
          style={{
          border: 'none',
          borderTop: '1px solid var(--border-subtle)',
          overflowX: 'auto',
          maxWidth: '100%',
          width: '100%',
          WebkitOverflowScrolling: 'touch',
        }}
      >
        <table
          className="data-table"
          role="table"
          aria-label={typeof title === 'string' ? title : 'Bảng dữ liệu'}
          style={{ width: '100%', borderCollapse: 'separate', borderSpacing: 0, minWidth: '850px' }}
        >
          <thead>
            <tr>
              {hasSelection && (
                <th
                  scope="col"
                  style={{
                    width: '46px',
                    minWidth: '46px',
                    maxWidth: '46px',
                    textAlign: 'center',
                    padding: '12px 8px',
                    position: 'sticky',
                    left: 0,
                    zIndex: 12,
                    backgroundColor: 'var(--bg-primary)',
                    borderRight: '1px solid var(--border-subtle)',
                    borderBottom: '1px solid var(--border-subtle)',
                  }}
                >
                  <input
                    ref={checkAllRef}
                    type="checkbox"
                    checked={isAllSelected}
                    onChange={handleToggleAll}
                    aria-label={isAllSelected ? 'Bỏ chọn tất cả các dòng' : 'Chọn tất cả các dòng trên bảng'}
                    style={{
                      width: '16px',
                      height: '16px',
                      cursor: 'pointer',
                      accentColor: '#1a73e8',
                    }}
                    title={isAllSelected ? 'Bỏ chọn tất cả' : 'Chọn tất cả bản ghi trên bảng'}
                  />
                </th>
              )}
              {visibleColumns.map((col) => {
                const isSortActive = serverSort
                  ? serverSort.sortKey === col.key
                  : clientSortKey === col.key;
                const sortDir = serverSort ? serverSort.direction : clientSortDir;
                const isSortable = enableSorting && col.sortable !== false;
                const isPinnedLeft = pinnedLeftCols.has(col.key) || col.pinned === 'left';
                const isPinnedRight = col.pinned === 'right';

                let stickyThStyle: React.CSSProperties = {};
                if (isPinnedLeft) {
                  stickyThStyle = {
                    position: 'sticky',
                    left: leftOffsets[col.key] ?? 0,
                    zIndex: 10,
                    backgroundColor: isSortActive ? 'var(--accent-primary-container)' : 'var(--bg-primary)',
                    borderRight: col.key === lastPinnedLeftKey ? '2px solid var(--border-subtle)' : '1px solid var(--border-subtle)',
                    boxShadow: col.key === lastPinnedLeftKey ? '3px 0 6px rgba(0,0,0,0.06)' : undefined,
                  };
                } else if (isPinnedRight) {
                  stickyThStyle = {
                    position: 'sticky',
                    right: rightOffsets[col.key] ?? 0,
                    zIndex: 10,
                    backgroundColor: isSortActive ? 'var(--accent-primary-container)' : 'var(--bg-primary)',
                    borderLeft: col.key === firstPinnedRightKey ? '2px solid var(--border-subtle)' : '1px solid var(--border-subtle)',
                    boxShadow: col.key === firstPinnedRightKey ? '-3px 0 6px rgba(0,0,0,0.06)' : undefined,
                  };
                }

                return (
                  <th
                    key={col.key}
                    scope="col"
                    role="columnheader"
                    tabIndex={isSortable ? 0 : undefined}
                    aria-sort={isSortActive ? (sortDir === 'asc' ? 'ascending' : 'descending') : (isSortable ? 'none' : undefined)}
                    aria-label={isSortable ? `Cột ${col.label || (typeof col.header === 'string' ? col.header : col.key)}, ${isSortActive ? (sortDir === 'asc' ? 'đang sắp xếp tăng dần' : 'đang sắp xếp giảm dần') : 'nhấn Enter hoặc cách để sắp xếp'}` : undefined}
                    onKeyDown={(e) => {
                      if (isSortable && (e.key === 'Enter' || e.key === ' ')) {
                        e.preventDefault();
                        handleSortClick(col.key, col.sortable);
                      }
                    }}
                    onClick={() => handleSortClick(col.key, col.sortable)}
                    style={{
                      width: col.width,
                      minWidth: col.width,
                      textAlign: col.align || 'left',
                      padding: '10px 14px',
                      fontSize: '12px',
                      fontWeight: 600,
                      letterSpacing: '0.01em',
                      textTransform: 'none',
                      color: isSortActive ? '#1a73e8' : 'var(--text-muted)',
                      backgroundColor: isSortActive ? '#f1f5fd' : 'var(--bg-primary)',
                      borderBottom: '1px solid var(--border-subtle)',
                      whiteSpace: 'nowrap',
                      cursor: isSortable ? 'pointer' : 'default',
                      userSelect: 'none',
                      transition: 'background-color 0.1s ease',
                      ...stickyThStyle,
                    }}
                    title={isSortable ? 'Nhấp để sắp xếp' : undefined}
                  >
                    <div
                      style={{
                        display: 'inline-flex',
                        alignItems: 'center',
                        gap: '4px',
                        justifyContent:
                          col.align === 'center'
                            ? 'center'
                            : col.align === 'right'
                            ? 'flex-end'
                            : 'flex-start',
                        width: '100%',
                      }}
                    >
                      <span>{col.header}</span>
                      {isSortable && (
                        <span style={{ display: 'inline-flex', opacity: isSortActive ? 1 : 0.35 }}>
                          {isSortActive ? (
                            sortDir === 'asc' ? (
                              <ArrowUpwardRoundedIcon sx={{ fontSize: 15, color: '#1a73e8' }} />
                            ) : (
                              <ArrowDownwardRoundedIcon sx={{ fontSize: 15, color: '#1a73e8' }} />
                            )
                          ) : (
                            <UnfoldMoreRoundedIcon sx={{ fontSize: 15 }} />
                          )}
                        </span>
                      )}
                    </div>
                  </th>
                );
              })}
            </tr>

            {/* Per-Column Filter Row */}
            {enableFiltering && showFilterRow && (
              <tr style={{ backgroundColor: 'var(--bg-secondary)', borderBottom: '2px solid var(--border-subtle)' }}>
                {hasSelection && (
                  <th
                    style={{
                      padding: '4px',
                      position: 'sticky',
                      left: 0,
                      zIndex: 11,
                      backgroundColor: 'var(--bg-secondary)',
                      borderRight: '1px solid var(--border-subtle)',
                      borderBottom: '2px solid var(--border-subtle)',
                    }}
                  ></th>
                )}
                {visibleColumns.map((col) => {
                  const filterVal = columnFilters[col.key] || '';
                  const isPinnedLeft = pinnedLeftCols.has(col.key) || col.pinned === 'left';
                  const isPinnedRight = col.pinned === 'right';

                  let stickyFilterStyle: React.CSSProperties = {};
                  if (isPinnedLeft) {
                    stickyFilterStyle = {
                      position: 'sticky',
                      left: leftOffsets[col.key] ?? 0,
                      zIndex: 9,
                      backgroundColor: 'var(--bg-secondary)',
                      borderRight: col.key === lastPinnedLeftKey ? '2px solid var(--border-subtle)' : '1px solid var(--border-subtle)',
                      boxShadow: col.key === lastPinnedLeftKey ? '3px 0 6px rgba(0,0,0,0.06)' : undefined,
                    };
                  } else if (isPinnedRight) {
                    stickyFilterStyle = {
                      position: 'sticky',
                      right: rightOffsets[col.key] ?? 0,
                      zIndex: 9,
                      backgroundColor: 'var(--bg-secondary)',
                      borderLeft: col.key === firstPinnedRightKey ? '2px solid var(--border-subtle)' : '1px solid var(--border-subtle)',
                      boxShadow: col.key === firstPinnedRightKey ? '-3px 0 6px rgba(0,0,0,0.06)' : undefined,
                    };
                  }

                  if (col.filterable === false) {
                    return (
                      <th
                        key={col.key}
                        style={{
                          padding: '4px',
                          borderBottom: '2px solid var(--border-subtle)',
                          ...stickyFilterStyle,
                        }}
                      ></th>
                    );
                  }

                  return (
                    <th
                      key={col.key}
                      style={{
                        padding: '4px 6px',
                        borderBottom: '2px solid var(--border-subtle)',
                        ...stickyFilterStyle,
                      }}
                    >
                      {col.filterType === 'select' && col.filterOptions ? (
                        <select
                          className="form-input"
                          value={filterVal}
                          onChange={(e) =>
                            setColumnFilters((prev) => ({ ...prev, [col.key]: e.target.value }))
                          }
                          style={{
                            height: '26px',
                            fontSize: '11px',
                            padding: '1px 4px',
                            width: '100%',
                            borderRadius: '4px',
                          }}
                        >
                          <option value="">Tất cả</option>
                          {col.filterOptions.map((opt) => (
                            <option key={opt.value} value={opt.value}>
                              {opt.label}
                            </option>
                          ))}
                        </select>
                      ) : (
                        <div style={{ position: 'relative', width: '100%' }}>
                          <input
                            type="text"
                            placeholder="Lọc..."
                            value={filterVal}
                            onChange={(e) =>
                              setColumnFilters((prev) => ({ ...prev, [col.key]: e.target.value }))
                            }
                            className="form-input"
                            style={{
                              height: '26px',
                              fontSize: '11px',
                              padding: '2px 20px 2px 6px',
                              width: '100%',
                              borderRadius: '4px',
                            }}
                          />
                          {filterVal && (
                            <button
                              type="button"
                              onClick={() =>
                                setColumnFilters((prev) => {
                                  const next = { ...prev };
                                  delete next[col.key];
                                  return next;
                                })
                              }
                              style={{
                                position: 'absolute',
                                right: '4px',
                                top: '50%',
                                transform: 'translateY(-50%)',
                                background: 'none',
                                border: 'none',
                                color: 'var(--text-muted)',
                                cursor: 'pointer',
                                padding: '1px',
                              }}
                            >
                              <CloseRoundedIcon sx={{ fontSize: 12 }} />
                            </button>
                          )}
                        </div>
                      )}
                    </th>
                  );
                })}
              </tr>
            )}
          </thead>
          <tbody style={loading && processedData.length > 0 ? { opacity: 0.6, transition: 'opacity 120ms ease', pointerEvents: 'none' } : undefined}>
            {loading && processedData.length === 0 ? (
              <tr>
                <td
                  colSpan={visibleColumns.length + (hasSelection ? 1 : 0)}
                  style={{ textAlign: 'center', padding: '36px 16px', color: 'var(--text-muted)' }}
                >
                  <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: '8px' }}>
                    <div style={{ fontSize: '13px', fontWeight: 600 }}>Đang tải dữ liệu...</div>
                  </div>
                </td>
              </tr>
            ) : processedData.length === 0 ? (
              <tr>
                <td
                  colSpan={visibleColumns.length + (hasSelection ? 1 : 0)}
                  style={{ textAlign: 'center', padding: '40px 16px', color: 'var(--text-muted)' }}
                >
                  {activeFilterCount > 0 ? (
                    <EmptyState
                      compact
                      title="Không có bản ghi phù hợp"
                      description="Không có bản ghi nào thỏa mãn điều kiện bộ lọc hoặc từ khóa tìm kiếm hiện tại."
                      action={
                        <button
                          type="button"
                          className="btn btn-outline"
                          onClick={clearAllFilters}
                          style={{ fontSize: '12px', padding: '4px 14px' }}
                        >
                          Xóa Tất Cả Bộ Lọc
                        </button>
                      }
                    />
                  ) : (
                    <EmptyState
                      title="Chưa có bản ghi nào"
                      description={typeof emptyText === 'string' ? emptyText : 'Dữ liệu của bảng hiện đang trống.'}
                      action={emptyAction}
                    />
                  )}
                </td>
              </tr>
            ) : (
              processedData.map((row, index) => {
                const isSelected = selectedIds.includes(row.id);
                const selectedSolidBg = 'color-mix(in srgb, var(--accent-primary) 12%, var(--bg-surface))';
                const rowBg = isSelected ? selectedSolidBg : (index % 2 === 0 ? 'var(--bg-surface)' : 'var(--bg-primary)');

                return (
                  <tr
                    key={row.id}
                    style={{
                      backgroundColor: rowBg,
                      transition: 'background-color 0.12s ease',
                      borderBottom: '1px solid var(--border-light)',
                    }}
                  >
                    {hasSelection && (
                      <td
                        style={{
                          width: '46px',
                          minWidth: '46px',
                          maxWidth: '46px',
                          textAlign: 'center',
                          padding: '10px 8px',
                          position: 'sticky',
                          left: 0,
                          zIndex: 5,
                          backgroundColor: rowBg,
                          borderRight: '1px solid var(--border-subtle)',
                          borderBottom: '1px solid var(--border-light)',
                        }}
                      >
                        <input
                          type="checkbox"
                          checked={isSelected}
                          onChange={() => handleToggleRow(row.id)}
                          aria-label={`Chọn hàng dữ liệu ${index + 1}`}
                          style={{
                            width: '16px',
                            height: '16px',
                            cursor: 'pointer',
                            accentColor: '#1a73e8',
                          }}
                        />
                      </td>
                    )}
                    {visibleColumns.map((col) => {
                      const isPinnedLeft = pinnedLeftCols.has(col.key) || col.pinned === 'left';
                      const isPinnedRight = col.pinned === 'right';

                      let stickyTdStyle: React.CSSProperties = {};
                      if (isPinnedLeft) {
                        stickyTdStyle = {
                          position: 'sticky',
                          left: leftOffsets[col.key] ?? 0,
                          zIndex: 4,
                          backgroundColor: rowBg,
                          borderRight: col.key === lastPinnedLeftKey ? '2px solid var(--border-subtle)' : '1px solid var(--border-light)',
                          boxShadow: col.key === lastPinnedLeftKey ? '3px 0 6px rgba(0,0,0,0.06)' : undefined,
                        };
                      } else if (isPinnedRight) {
                        stickyTdStyle = {
                          position: 'sticky',
                          right: rightOffsets[col.key] ?? 0,
                          zIndex: 4,
                          backgroundColor: rowBg,
                          borderLeft: col.key === firstPinnedRightKey ? '2px solid var(--border-subtle)' : '1px solid var(--border-light)',
                          boxShadow: col.key === firstPinnedRightKey ? '-3px 0 6px rgba(0,0,0,0.06)' : undefined,
                        };
                      }

                      return (
                        <td
                          key={col.key}
                          style={{
                            textAlign: col.align || 'left',
                            padding: '10px 14px',
                            verticalAlign: 'middle',
                            fontSize: '13px',
                            borderBottom: '1px solid var(--border-light)',
                            ...stickyTdStyle,
                          }}
                        >
                          {col.render ? col.render(row, index) : getRowFieldValue(row, col.key) ?? '—'}
                        </td>
                      );
                    })}
                  </tr>
                );
              })
            )}
          </tbody>
        </table>
      </div>
      )}

      {/* Unified Pagination Bar */}
      {pagination && (
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            padding: '10px 4px 4px',
            fontSize: '12.5px',
            color: 'var(--text-secondary)',
            flexWrap: 'wrap',
            gap: '12px',
          }}
        >
          <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
            <span>
              Hiển thị{' '}
              <strong>
                {pagination.total === 0 ? 0 : (pagination.page - 1) * pagination.pageSize + 1} -{' '}
                {Math.min(pagination.page * pagination.pageSize, pagination.total)}
              </strong>{' '}
              trong tổng số <strong>{pagination.total}</strong> bản ghi
            </span>

            <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
              <span style={{ fontSize: '12px', color: 'var(--text-muted)' }}>Hiển thị:</span>
              <select
                className="form-input"
                value={pagination.pageSize}
                onChange={(e) => pagination.onPageSizeChange(Number(e.target.value))}
                style={{ padding: '2px 8px', fontSize: '12px', width: 'auto', borderRadius: '6px' }}
              >
                <option value={10}>10 dòng / trang</option>
                <option value={25}>25 dòng / trang</option>
                <option value={50}>50 dòng / trang</option>
                <option value={100}>100 dòng / trang</option>
              </select>
            </div>
          </div>

          <div
            role="navigation"
            aria-label="Phân trang bảng dữ liệu"
            style={{ display: 'flex', alignItems: 'center', gap: '4px' }}
          >
            <button
              className="btn btn-outline"
              disabled={pagination.page <= 1}
              onClick={() => pagination.onPageChange(1)}
              style={{ padding: '3px 8px', fontSize: '11px', borderRadius: '4px' }}
              aria-label="Trang đầu tiên"
              title="Trang đầu"
            >
              « Đầu
            </button>
            <button
              className="btn btn-outline"
              disabled={pagination.page <= 1}
              onClick={() => pagination.onPageChange(pagination.page - 1)}
              style={{ padding: '3px 9px', fontSize: '11px', borderRadius: '4px' }}
              aria-label="Trang trước"
              title="Trang trước"
            >
              ‹ Trước
            </button>

            <span
              aria-current="page"
              aria-label={`Trang hiện tại: ${pagination.page} trên ${Math.max(1, pagination.totalPages)}`}
              style={{
                padding: '3px 10px',
                fontSize: '12px',
                fontWeight: 600,
                backgroundColor: 'var(--info-bg)',
                color: '#1a73e8',
                borderRadius: '6px',
                border: '1px solid #1a73e8',
              }}
            >
              Trang {pagination.page} / {Math.max(1, pagination.totalPages)}
            </span>

            <button
              className="btn btn-outline"
              disabled={pagination.page >= pagination.totalPages}
              onClick={() => pagination.onPageChange(pagination.page + 1)}
              style={{ padding: '3px 9px', fontSize: '11px', borderRadius: '4px' }}
              aria-label="Trang kế tiếp"
              title="Trang sau"
            >
              Sau ›
            </button>
            <button
              className="btn btn-outline"
              disabled={pagination.page >= pagination.totalPages}
              onClick={() => pagination.onPageChange(pagination.totalPages)}
              style={{ padding: '3px 8px', fontSize: '11px', borderRadius: '4px' }}
              aria-label="Trang cuối cùng"
              title="Trang cuối"
            >
              Cuối »
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
