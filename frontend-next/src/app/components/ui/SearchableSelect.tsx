'use client';

import React, { useState, useRef, useEffect, useMemo } from 'react';
import SearchRoundedIcon from '@mui/icons-material/SearchRounded';
import ExpandMoreRoundedIcon from '@mui/icons-material/ExpandMoreRounded';
import CheckRoundedIcon from '@mui/icons-material/CheckRounded';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';

export interface SearchableSelectOption {
  value: string;
  label: string;
  subLabel?: string;
  searchText?: string;
}

export interface SearchableSelectProps {
  value: string | number;
  onChange: (val: string) => void;
  options: SearchableSelectOption[];
  placeholder?: string;
  searchPlaceholder?: string;
  onSearchChange?: (term: string) => void;
  disabled?: boolean;
  size?: 'sm' | 'md';
  isClearable?: boolean;
  emptyText?: string;
  onCreateOption?: (inputText: string) => Promise<void> | void;
  creatingOption?: boolean;
  style?: React.CSSProperties;
}

export default function SearchableSelect({
  value,
  onChange,
  options,
  placeholder = '-- Chọn --',
  searchPlaceholder = 'Tìm nhanh...',
  onSearchChange,
  disabled = false,
  size = 'md',
  isClearable = false,
  emptyText = 'Không có dữ liệu',
  onCreateOption,
  creatingOption = false,
  style,
}: SearchableSelectProps) {
  const [isOpen, setIsOpen] = useState(false);
  const [searchTerm, setSearchTerm] = useState('');
  const containerRef = useRef<HTMLDivElement>(null);
  const searchInputRef = useRef<HTMLInputElement>(null);

  const currentVal = String(value ?? '');
  const selectedOption = useMemo(
    () => options.find((o) => String(o.value) === currentVal),
    [options, currentVal]
  );

  const filtered = useMemo(() => {
    const q = searchTerm.trim().toLowerCase();
    if (!q) return options;
    return options.filter(
      (o) =>
        o.label.toLowerCase().includes(q) ||
        o.value.toLowerCase().includes(q) ||
        (o.subLabel && o.subLabel.toLowerCase().includes(q)) ||
        (o.searchText && o.searchText.toLowerCase().includes(q))
    );
  }, [options, searchTerm]);

  useEffect(() => {
    if (!isOpen) return;
    function onOutside(e: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        setIsOpen(false);
      }
    }
    document.addEventListener('mousedown', onOutside);
    setTimeout(() => searchInputRef.current?.focus(), 20);
    return () => document.removeEventListener('mousedown', onOutside);
  }, [isOpen]);

  return (
    <div ref={containerRef} style={{ position: 'relative', width: '100%', ...style }}>
      <button
        type="button"
        disabled={disabled}
        onClick={() => setIsOpen((p) => !p)}
        style={{
          width: '100%',
          height: size === 'sm' ? 30 : 38,
          padding: size === 'sm' ? '0 8px' : '0 12px',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          gap: 6,
          backgroundColor: disabled ? '#f1f3f4' : '#ffffff',
          border: `1px solid ${isOpen ? 'var(--accent-primary)' : 'var(--border-subtle)'}`,
          borderRadius: 8,
          fontSize: size === 'sm' ? 12 : 13,
          color: selectedOption ? 'var(--text-primary)' : 'var(--text-muted)',
          cursor: disabled ? 'not-allowed' : 'pointer',
          textAlign: 'left',
        }}
      >
        <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', flex: 1 }}>
          {selectedOption ? selectedOption.label : placeholder}
        </span>
        <span style={{ display: 'inline-flex', alignItems: 'center', gap: 2, flexShrink: 0 }}>
          {isClearable && currentVal && !disabled && (
            <CloseRoundedIcon
              sx={{ fontSize: 15, color: '#5f6368' }}
              onClick={(e) => {
                e.stopPropagation();
                onChange('');
              }}
            />
          )}
          <ExpandMoreRoundedIcon sx={{ fontSize: 18, color: '#5f6368' }} />
        </span>
      </button>

      {isOpen && (
        <div
          style={{
            position: 'absolute',
            top: 'calc(100% + 4px)',
            left: 0,
            minWidth: '100%',
            zIndex: 1200,
            backgroundColor: 'var(--bg-surface)',
            border: '1px solid var(--border-subtle)',
            borderRadius: 10,
            boxShadow: 'var(--shadow-lg)',
            overflow: 'hidden',
          }}
        >
          <div style={{ padding: 8, borderBottom: '1px solid var(--border-light)', display: 'flex', alignItems: 'center', gap: 6 }}>
            <SearchRoundedIcon sx={{ fontSize: 16, color: 'var(--text-muted)' }} />
            <input
              ref={searchInputRef}
              value={searchTerm}
              onChange={(e) => {
                setSearchTerm(e.target.value);
                onSearchChange?.(e.target.value);
              }}
              placeholder={searchPlaceholder}
              style={{ border: 'none', outline: 'none', width: '100%', fontSize: 12.5, backgroundColor: 'transparent', color: 'var(--text-primary)' }}
            />
          </div>
          <div style={{ maxHeight: 240, overflowY: 'auto' }}>
            {filtered.length === 0 ? (
              <div style={{ padding: '12px 14px', fontSize: 12.5, color: 'var(--text-muted)', textAlign: 'center' }}>
                {emptyText}
              </div>
            ) : (
              filtered.map((opt) => {
                const isSelected = String(opt.value) === currentVal;
                const showSub = Boolean(opt.subLabel && opt.subLabel !== opt.label && !opt.label.endsWith(`— ${opt.subLabel}`));
                return (
                  <button
                    key={opt.value}
                    type="button"
                    onClick={() => {
                      onChange(opt.value);
                      setIsOpen(false);
                    }}
                    style={{
                      width: '100%',
                      padding: '8px 12px',
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'space-between',
                      gap: 8,
                      border: 'none',
                      borderBottom: '1px solid #f1f5f9',
                      backgroundColor: isSelected ? '#e8f0fe' : '#ffffff',
                      color: isSelected ? '#1a73e8' : '#202124',
                      fontWeight: isSelected ? 600 : 400,
                      fontSize: 12.5,
                      cursor: 'pointer',
                      textAlign: 'left',
                    }}
                  >
                    <span style={{ display: 'flex', flexDirection: 'column', minWidth: 0, flex: 1 }}>
                      <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                        {opt.label}
                      </span>
                      {showSub && (
                        <span style={{ fontSize: 11, color: '#64748b', fontWeight: 400, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', marginTop: 1 }}>
                          {opt.subLabel}
                        </span>
                      )}
                    </span>
                    {isSelected && <CheckRoundedIcon sx={{ fontSize: 16, color: '#1a73e8', flexShrink: 0 }} />}
                  </button>
                );
              })
            )}
            {onCreateOption &&
              searchTerm.trim().length > 0 &&
              !options.some((o) => o.label.trim().toLowerCase() === searchTerm.trim().toLowerCase()) && (
                <button
                  type="button"
                  disabled={creatingOption}
                  onClick={async () => {
                    const text = searchTerm.trim();
                    if (!text || creatingOption) return;
                    await onCreateOption(text);
                    setSearchTerm('');
                    setIsOpen(false);
                  }}
                  style={{
                    width: '100%',
                    padding: '9px 12px',
                    display: 'flex',
                    alignItems: 'center',
                    gap: 6,
                    border: 'none',
                    borderTop: '1px solid var(--border-light)',
                    backgroundColor: '#f0f6ff',
                    color: '#1a73e8',
                    fontWeight: 600,
                    fontSize: 12.5,
                    cursor: creatingOption ? 'wait' : 'pointer',
                    textAlign: 'left',
                  }}
                >
                  <span>{creatingOption ? 'Đang thêm...' : `+ Thêm mới "${searchTerm.trim()}"`}</span>
                </button>
              )}
          </div>
        </div>
      )}
    </div>
  );
}
