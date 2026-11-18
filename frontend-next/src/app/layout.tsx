import type { Metadata } from 'next';
import './globals.css';
import ThemeRegistry from './components/ThemeRegistry';
import Sidebar from './components/Sidebar';
import Navbar from './components/Navbar';

export const metadata: Metadata = {
  title: 'SmartApps Platform - Hệ thống Đa ứng dụng Dữ liệu Động Doanh nghiệp',
  description: 'Xây dựng và vận hành nhiều ứng dụng doanh nghiệp trên cùng 1 hệ thống với giao diện & bảng động (Next.js + C# .NET 9 + Rust + PostgreSQL JSONB)',
  manifest: '/manifest.json',
  appleWebApp: {
    capable: true,
    statusBarStyle: 'default',
    title: 'SmartApps',
  },
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="vi">
      <body>
        <ThemeRegistry>
          <div className="app-container">
            <Sidebar />
            <main className="app-main">
              <Navbar />
              {children}
            </main>
          </div>
        </ThemeRegistry>
      </body>
    </html>
  );
}
