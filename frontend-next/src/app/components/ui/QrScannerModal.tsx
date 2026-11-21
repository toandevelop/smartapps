'use client';

import React, { useEffect, useRef, useState } from 'react';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import QrCodeScannerRoundedIcon from '@mui/icons-material/QrCodeScannerRounded';
import PhotoCameraRoundedIcon from '@mui/icons-material/PhotoCameraRounded';
import UploadFileRoundedIcon from '@mui/icons-material/UploadFileRounded';
import { useEscapeKey } from '@/lib/ui-hooks';

interface DetectedBarcode {
  rawValue?: string;
}

interface BarcodeDetectorLike {
  detect: (source: ImageBitmapSource) => Promise<DetectedBarcode[]>;
}

declare global {
  interface Window {
    BarcodeDetector?: new (opts?: { formats?: string[] }) => BarcodeDetectorLike;
  }
}

interface QrScannerModalProps {
  open: boolean;
  title?: string;
  onClose: () => void;
  onDetected: (value: string) => void;
}

export default function QrScannerModal({
  open,
  title = 'Quét mã QR / Barcode',
  onClose,
  onDetected,
}: QrScannerModalProps) {
  useEscapeKey(open, onClose);
  if (!open) return null;
  return <QrScannerBody title={title} onClose={onClose} onDetected={onDetected} />;
}

function QrScannerBody({
  title,
  onClose,
  onDetected,
}: Omit<QrScannerModalProps, 'open'>) {
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const [status, setStatus] = useState<string>('Đang kiểm tra camera & bộ giải mã...');
  const [cameraActive, setCameraActive] = useState(false);
  const [manualCode, setManualCode] = useState('');

  useEffect(() => {
    let cancelled = false;
    let stream: MediaStream | null = null;
    let rafId = 0;

    async function startCamera() {
      if (typeof window === 'undefined' || !navigator.mediaDevices?.getUserMedia) {
        setStatus('Trình duyệt hiện tại không hỗ trợ truy cập camera. Hãy tải ảnh chứa mã QR hoặc nhập mã thủ công bên dưới.');
        return;
      }
      try {
        stream = await navigator.mediaDevices.getUserMedia({
          video: { facingMode: { ideal: 'environment' } },
          audio: false,
        });
        if (cancelled) {
          stream.getTracks().forEach((t) => t.stop());
          return;
        }
        if (videoRef.current) {
          videoRef.current.srcObject = stream;
          await videoRef.current.play();
        }
        setCameraActive(true);

        if (typeof window.BarcodeDetector === 'function') {
          const detector = new window.BarcodeDetector({
            formats: ['qr_code', 'code_128', 'code_39', 'ean_13', 'ean_8'],
          });
          setStatus('Đưa mã QR hoặc mã vạch vào khung camera...');
          const scanLoop = async () => {
            if (cancelled || !videoRef.current) return;
            try {
              if (videoRef.current.readyState >= 2) {
                const hits = await detector.detect(videoRef.current);
                const raw = hits.find((h) => h.rawValue?.trim())?.rawValue?.trim();
                if (raw) {
                  onDetected(raw);
                  onClose();
                  return;
                }
              }
            } catch {
              // continue scanning
            }
            rafId = window.setTimeout(scanLoop, 260);
          };
          rafId = window.setTimeout(scanLoop, 260);
        } else {
          setStatus('Camera đang bật nhưng trình duyệt chưa có BarcodeDetector tự động. Vui lòng nhập mã hoặc dùng Chrome/Edge mới.');
        }
      } catch {
        if (!cancelled) {
          setStatus('Không thể mở camera (chưa cấp quyền hoặc không có thiết bị). Bạn có thể tải ảnh mã hoặc nhập trực tiếp bên dưới.');
        }
      }
    }

    void startCamera();
    return () => {
      cancelled = true;
      if (rafId) window.clearTimeout(rafId);
      if (stream) stream.getTracks().forEach((t) => t.stop());
    };
  }, [onClose, onDetected]);

  const handleImageFile = async (file: File) => {
    if (typeof window === 'undefined' || typeof window.BarcodeDetector !== 'function') {
      setStatus('Trình duyệt hiện tại chưa hỗ trợ giải mã QR từ ảnh. Vui lòng nhập trực tiếp mã bên dưới.');
      return;
    }
    try {
      const bmp = await createImageBitmap(file);
      const detector = new window.BarcodeDetector({
        formats: ['qr_code', 'code_128', 'code_39', 'ean_13', 'ean_8'],
      });
      const hits = await detector.detect(bmp);
      const raw = hits.find((h) => h.rawValue?.trim())?.rawValue?.trim();
      if (raw) {
        onDetected(raw);
        onClose();
      } else {
        setStatus('Không phát hiện mã QR/Barcode hợp lệ trong ảnh vừa chọn.');
      }
    } catch {
      setStatus('Không thể đọc ảnh đã chọn.');
    }
  };

  return (
    <div className="modal-overlay" onClick={onClose} role="dialog" aria-modal="true" aria-label={title}>
      <div className="modal-dialog" style={{ maxWidth: 460 }} onClick={(e) => e.stopPropagation()}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <QrCodeScannerRoundedIcon sx={{ fontSize: 20, color: 'var(--accent-primary)' }} />
            <h3 style={{ fontSize: 16, fontWeight: 700, margin: 0 }}>{title}</h3>
          </div>
          <button type="button" className="btn btn-icon btn-outline" onClick={onClose} aria-label="Đóng máy quét">
            <CloseRoundedIcon sx={{ fontSize: 18 }} />
          </button>
        </div>

        <div
          style={{
            position: 'relative',
            borderRadius: 12,
            overflow: 'hidden',
            backgroundColor: '#0f172a',
            aspectRatio: '4 / 3',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            marginBottom: 10,
          }}
        >
          <video
            ref={videoRef}
            muted
            playsInline
            style={{ width: '100%', height: '100%', objectFit: 'cover', display: cameraActive ? 'block' : 'none' }}
          />
          {!cameraActive && (
            <div style={{ color: '#94a3b8', display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 6, padding: 16, textAlign: 'center' }}>
              <PhotoCameraRoundedIcon sx={{ fontSize: 34, opacity: 0.7 }} />
              <span style={{ fontSize: 12 }}>Camera chưa khả dụng</span>
            </div>
          )}
        </div>

        <div style={{ fontSize: 12, color: 'var(--text-muted)', marginBottom: 12 }}>{status}</div>

        <div style={{ display: 'flex', gap: 8, marginBottom: 12 }}>
          <input
            type="text"
            className="form-input"
            placeholder="Hoặc nhập / dán mã quét thủ công..."
            value={manualCode}
            onChange={(e) => setManualCode(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && manualCode.trim()) {
                e.preventDefault();
                onDetected(manualCode.trim());
                onClose();
              }
            }}
          />
          <button
            type="button"
            className="btn btn-primary btn-sm"
            disabled={!manualCode.trim()}
            onClick={() => {
              if (!manualCode.trim()) return;
              onDetected(manualCode.trim());
              onClose();
            }}
          >
            Áp dụng
          </button>
        </div>

        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8 }}>
          <label className="btn btn-outline btn-xs" style={{ cursor: 'pointer' }}>
            <UploadFileRoundedIcon sx={{ fontSize: 14 }} /> Chọn ảnh mã QR
            <input
              type="file"
              accept="image/*"
              style={{ display: 'none' }}
              onChange={(e) => {
                const file = e.target.files?.[0];
                if (file) void handleImageFile(file);
                e.target.value = '';
              }}
            />
          </label>
          <button type="button" className="btn btn-secondary btn-xs" onClick={onClose}>
            Đóng
          </button>
        </div>
      </div>
    </div>
  );
}
