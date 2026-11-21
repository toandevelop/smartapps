'use client';

import React from 'react';
import Link from 'next/link';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import StorefrontRoundedIcon from '@mui/icons-material/StorefrontRounded';
import AutoAwesomeRoundedIcon from '@mui/icons-material/AutoAwesomeRounded';
import CloudUploadRoundedIcon from '@mui/icons-material/CloudUploadRounded';
import AddBoxRoundedIcon from '@mui/icons-material/AddBoxRounded';
import AppsRoundedIcon from '@mui/icons-material/AppsRounded';
import { useEscapeKey } from '@/lib/ui-hooks';

interface AppCreationHubModalProps {
  open: boolean;
  onClose: () => void;
  onOpenAiCopilot: () => void;
  onOpenExcelImport: () => void;
  onOpenBlankApp: () => void;
  onOpenTemplateGallery: () => void;
}

export default function AppCreationHubModal({
  open,
  onClose,
  onOpenAiCopilot,
  onOpenExcelImport,
  onOpenBlankApp,
  onOpenTemplateGallery,
}: AppCreationHubModalProps) {
  useEscapeKey(open, onClose);
  if (!open) return null;

  return (
    <div className="modal-overlay" onClick={onClose} role="dialog" aria-modal="true" aria-label="Trung tâm Tạo & Cài đặt Ứng dụng">
      <div className="modal-dialog" style={{ maxWidth: 680 }} onClick={(e) => e.stopPropagation()}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 12, marginBottom: 16 }}>
          <div>
            <h3 style={{ fontSize: 18, fontWeight: 700, margin: 0, color: 'var(--text-primary)' }}>
              Thêm hoặc Tạo Ứng dụng Mới
            </h3>
            <p style={{ fontSize: 12.5, color: 'var(--text-muted)', marginTop: 4 }}>
              Chọn phương thức nhanh nhất để mở rộng không gian làm việc doanh nghiệp của bạn.
            </p>
          </div>
          <button type="button" className="btn btn-icon btn-outline" onClick={onClose} aria-label="Đóng hộp thoại">
            <CloseRoundedIcon sx={{ fontSize: 18 }} />
          </button>
        </div>

        <div className="creation-hub-grid">
          <Link href="/store" className="creation-hub-card" onClick={onClose}>
            <span
              style={{
                width: 38,
                height: 38,
                borderRadius: 10,
                backgroundColor: 'var(--accent-primary-container)',
                color: 'var(--accent-primary)',
                display: 'inline-flex',
                alignItems: 'center',
                justifyContent: 'center',
              }}
            >
              <StorefrontRoundedIcon sx={{ fontSize: 22 }} />
            </span>
            <div style={{ fontSize: 14.5, fontWeight: 700 }}>Kho Apps Online (20 gói v2.0)</div>
            <div style={{ fontSize: 12.5, color: 'var(--text-muted)', lineHeight: 1.45 }}>
              Cài 1-Click các ứng dụng hoàn chỉnh (CRM, Kho, Mua sắm VN, BĐS, Phòng khám, MES...) kèm quy trình duyệt &amp; mẫu in.
            </div>
          </Link>

          <button
            type="button"
            className="creation-hub-card"
            onClick={() => {
              onClose();
              onOpenAiCopilot();
            }}
          >
            <span
              style={{
                width: 38,
                height: 38,
                borderRadius: 10,
                backgroundColor: 'var(--info-bg)',
                color: 'var(--info-text)',
                display: 'inline-flex',
                alignItems: 'center',
                justifyContent: 'center',
              }}
            >
              <AutoAwesomeRoundedIcon sx={{ fontSize: 22 }} />
            </span>
            <div style={{ fontSize: 14.5, fontWeight: 700 }}>Tạo App bằng AI Copilot</div>
            <div style={{ fontSize: 12.5, color: 'var(--text-muted)', lineHeight: 1.45 }}>
              Mô tả nghiệp vụ bằng tiếng Việt — AI tự động sinh phân hệ, bảng động, cột công thức, quy tắc form &amp; dữ liệu mẫu.
            </div>
          </button>

          <button
            type="button"
            className="creation-hub-card"
            onClick={() => {
              onClose();
              onOpenExcelImport();
            }}
          >
            <span
              style={{
                width: 38,
                height: 38,
                borderRadius: 10,
                backgroundColor: 'var(--success-bg)',
                color: 'var(--success-text)',
                display: 'inline-flex',
                alignItems: 'center',
                justifyContent: 'center',
              }}
            >
              <CloudUploadRoundedIcon sx={{ fontSize: 22 }} />
            </span>
            <div style={{ fontSize: 14.5, fontWeight: 700 }}>Sinh App từ File Excel (.xlsx)</div>
            <div style={{ fontSize: 12.5, color: 'var(--text-muted)', lineHeight: 1.45 }}>
              Tải lên bảng tính đang dùng trong công ty để tự động nhận diện cột, kiểu dữ liệu và nạp sẵn bản ghi.
            </div>
          </button>

          <button
            type="button"
            className="creation-hub-card"
            onClick={() => {
              onClose();
              onOpenBlankApp();
            }}
          >
            <span
              style={{
                width: 38,
                height: 38,
                borderRadius: 10,
                backgroundColor: 'var(--warning-bg)',
                color: 'var(--warning-text)',
                display: 'inline-flex',
                alignItems: 'center',
                justifyContent: 'center',
              }}
            >
              <AddBoxRoundedIcon sx={{ fontSize: 22 }} />
            </span>
            <div style={{ fontSize: 14.5, fontWeight: 700 }}>Tạo App Trống Từ Đầu</div>
            <div style={{ fontSize: 12.5, color: 'var(--text-muted)', lineHeight: 1.45 }}>
              Thiết kế hoàn toàn chủ động mã App, biểu tượng, màu sắc, phân hệ và bảng động trong App Builder Studio.
            </div>
          </button>
        </div>

        <div
          style={{
            marginTop: 14,
            paddingTop: 12,
            borderTop: '1px solid var(--border-light)',
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            flexWrap: 'wrap',
            gap: 8,
          }}
        >
          <button
            type="button"
            className="btn btn-outline btn-xs"
            onClick={() => {
              onClose();
              onOpenTemplateGallery();
            }}
          >
            <AppsRoundedIcon sx={{ fontSize: 15 }} />
            Cài nhanh bộ App Mẫu nội bộ
          </button>
          <button type="button" className="btn btn-secondary btn-xs" onClick={onClose}>
            Đóng
          </button>
        </div>
      </div>
    </div>
  );
}
