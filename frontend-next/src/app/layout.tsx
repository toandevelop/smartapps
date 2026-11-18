import type { Metadata } from 'next';
import { Roboto, Plus_Jakarta_Sans, Inter, JetBrains_Mono } from 'next/font/google';
import './globals.css';
import ThemeRegistry from './components/ThemeRegistry';
import Sidebar from './components/Sidebar';
import Navbar from './components/Navbar';

const roboto = Roboto({
  subsets: ['latin', 'vietnamese'],
  weight: ['300', '400', '500', '700'],
  variable: '--font-roboto',
  display: 'swap',
});

const plusJakartaSans = Plus_Jakarta_Sans({
  subsets: ['latin', 'vietnamese'],
  weight: ['400', '500', '600', '700', '800'],
  variable: '--font-jakarta',
  display: 'swap',
});

const inter = Inter({
  subsets: ['latin', 'vietnamese'],
  weight: ['400', '500', '600', '700'],
  variable: '--font-inter',
  display: 'swap',
});

const jetbrainsMono = JetBrains_Mono({
  subsets: ['latin', 'vietnamese'],
  weight: ['400', '500', '600'],
  variable: '--font-jetbrains',
  display: 'swap',
});

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
    <html lang="vi" className={`${roboto.variable} ${plusJakartaSans.variable} ${inter.variable} ${jetbrainsMono.variable}`}>
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
