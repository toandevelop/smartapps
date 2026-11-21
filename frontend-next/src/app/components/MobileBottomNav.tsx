'use client';

import React, { useState } from 'react';
import Link from 'next/link';
import { usePathname, useRouter } from 'next/navigation';
import DashboardRoundedIcon from '@mui/icons-material/DashboardRounded';
import StorefrontRoundedIcon from '@mui/icons-material/StorefrontRounded';
import QrCodeScannerRoundedIcon from '@mui/icons-material/QrCodeScannerRounded';
import ForumRoundedIcon from '@mui/icons-material/ForumRounded';
import NotificationsRoundedIcon from '@mui/icons-material/NotificationsRounded';
import { useSettings } from '@/lib/settings-context';
import QrScannerModal from './ui/QrScannerModal';

export default function MobileBottomNav() {
  const pathname = usePathname() || '/';
  const router = useRouter();
  const { unreadSummary } = useSettings();
  const [scanOpen, setScanOpen] = useState(false);

  if (pathname === '/login' || pathname.startsWith('/forms/')) return null;

  return (
    <>
      <nav className="mobile-bottom-nav no-print" aria-label="Điều hướng di động">
        <Link href="/" className={`mobile-bottom-item ${pathname === '/' ? 'active' : ''}`}>
          <DashboardRoundedIcon sx={{ fontSize: 21 }} />
          <span>Trang chủ</span>
        </Link>
        <Link href="/store" className={`mobile-bottom-item ${pathname.startsWith('/store') ? 'active' : ''}`}>
          <StorefrontRoundedIcon sx={{ fontSize: 21 }} />
          <span>Kho Apps</span>
        </Link>
        <button
          type="button"
          className="mobile-bottom-item mobile-bottom-scan"
          onClick={() => setScanOpen(true)}
          aria-label="Quét mã QR tra cứu nhanh"
        >
          <QrCodeScannerRoundedIcon sx={{ fontSize: 22 }} />
          <span>Quét QR</span>
        </button>
        <Link href="/chat" className={`mobile-bottom-item ${pathname.startsWith('/chat') ? 'active' : ''}`}>
          <ForumRoundedIcon sx={{ fontSize: 21 }} />
          <span>
            Chat{unreadSummary.unreadChatCount > 0 ? ` (${unreadSummary.unreadChatCount})` : ''}
          </span>
        </Link>
        <Link href="/notifications" className={`mobile-bottom-item ${pathname.startsWith('/notifications') ? 'active' : ''}`}>
          <NotificationsRoundedIcon sx={{ fontSize: 21 }} />
          <span>
            Thông báo{unreadSummary.unreadNotifCount > 0 ? ` (${unreadSummary.unreadNotifCount})` : ''}
          </span>
        </Link>
      </nav>

      <QrScannerModal
        open={scanOpen}
        title="Quét QR tra cứu hồ sơ / mở liên kết"
        onClose={() => setScanOpen(false)}
        onDetected={(raw) => {
          const val = raw.trim();
          if (val.startsWith('/apps/') || val.startsWith('/forms/')) {
            router.push(val);
            return;
          }
          try {
            const parsed = new URL(val);
            if (typeof window !== 'undefined' && parsed.origin === window.location.origin) {
              router.push(`${parsed.pathname}${parsed.search}`);
              return;
            }
          } catch {
            // plain code -> jump to home with search query or dispatch global command palette
          }
          window.dispatchEvent(new CustomEvent('smartapps_open_search', { detail: { query: val } }));
        }}
      />
    </>
  );
}
