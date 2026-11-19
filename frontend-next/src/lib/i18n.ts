export type AppLanguage = 'vi' | 'en';

export const TRANSLATIONS = {
  vi: {
    // Navigation & Sidebar
    'nav.section.platform': 'Nền tảng',
    'nav.section.apps': 'Ứng dụng Doanh nghiệp',
    'nav.portal': 'Cổng Ứng dụng',
    'nav.store': 'Kho Apps Online',
    'nav.studio': 'App Builder Studio',
    'nav.admin': 'Quản trị & Quy trình',
    'nav.tasks': 'Nhiệm vụ & Công việc',
    'nav.chat': 'Chat Nội bộ',
    'nav.notifications': 'Thông báo',
    'nav.settings': 'Cài đặt Hệ thống',
    'nav.systemReady': 'Hệ thống Sẵn sàng',
    'nav.systemReadyTitle': 'Máy chủ dữ liệu đang hoạt động',

    // Navbar & Quick Actions
    'navbar.searchPlaceholder': 'Tìm nhanh Ứng dụng, Hồ sơ, Công việc... (Ctrl+K)',
    'navbar.syncOffline': 'Đồng bộ Offline ({count})',
    'navbar.syncOfflineTitle': 'Đồng bộ dữ liệu đã lưu Offline lên máy chủ',
    'navbar.desktopApp': 'Desktop App',
    'navbar.desktop': 'Desktop',
    'navbar.desktopConfigTitle': 'Cấu hình kết nối Desktop & Offline',
    'navbar.appStudio': 'App Studio',
    'navbar.toggleTheme': 'Chuyển chế độ Sáng / Tối',
    'navbar.toLight': 'Chuyển sang Giao diện Sáng',
    'navbar.toDark': 'Chuyển sang Giao diện Tối (Dark Mode)',
    'navbar.toggleDensity': 'Chuyển mật độ hiển thị Gọn / Thoải mái',
    'navbar.compactOn': 'Đã bật chế độ hiển thị Gọn (Compact)',
    'navbar.compactOff': 'Đã chuyển sang chế độ Thoải mái',
    'navbar.languageLabel': 'Ngôn ngữ hiển thị',
    'navbar.languageChanged': 'Đã chuyển ngôn ngữ sang Tiếng Việt',
    'navbar.changePassword': 'Đổi mật khẩu',
    'navbar.accountSettings': 'Cài đặt tài khoản & Giao diện',
    'navbar.logout': 'Đăng xuất',
    'navbar.onlineUsers': '{count} người đang online',
    'navbar.onlineUsersTitle': 'Người dùng đang trực tuyến',

    // Command Palette
    'cmd.placeholder': 'Tìm nhanh Ứng dụng, Hồ sơ (mã/từ khóa), hoặc Trang...',
    'cmd.searching': 'Đang tìm...',
    'cmd.empty': 'Không tìm thấy kết quả phù hợp.',
    'cmd.group.apps': 'Ứng dụng',
    'cmd.group.nav': 'Điều hướng nhanh',
    'cmd.group.actions': 'Hành động nhanh',
    'cmd.group.records': 'Hồ sơ khớp từ khóa',

    // Portal
    'portal.title': 'Cổng Ứng dụng & Điều hành Doanh nghiệp',
    'portal.subtitle': 'Khởi chạy các ứng dụng động, theo dõi chỉ số điều hành và xử lý phê duyệt tập trung.',
    'portal.createApp': 'Tạo App mới',
    'portal.fromExcel': 'Tạo từ Excel',
    'portal.templates': 'Kho mẫu',
    'portal.searchApps': 'Tìm ứng dụng theo tên, mã hoặc phòng ban...',
    'portal.allDepartments': 'Mọi khối / phòng ban',
    'portal.openApp': 'Mở Ứng dụng',
    'portal.onboarding.title': 'Chào mừng đến với SmartApps Platform',
    'portal.onboarding.subtitle': 'Bắt đầu xây dựng không gian làm việc doanh nghiệp chỉ với 3 bước đơn giản:',

    // Table & App Runtime
    'table.addRecord': 'Thêm hồ sơ',
    'table.importExcel': 'Import Excel',
    'table.exportExcel': 'Xuất Excel',
    'table.exportFiltered': 'Xuất kết quả đang lọc (.xlsx)',
    'table.exportAll': 'Xuất toàn bộ bảng (.xlsx)',
    'table.downloadTemplate': 'Tải file mẫu nhập (.xlsx)',
    'table.printList': 'In danh sách',
    'table.activityHistory': 'Lịch sử thao tác',
    'table.bulkEdit': 'Cập nhật hàng loạt',
    'table.undo': 'Hoàn tác',

    // Settings
    'settings.languageTitle': 'Ngôn ngữ giao diện (Language)',
    'settings.languageDesc': 'Chọn ngôn ngữ hiển thị cho thanh điều hướng, bảng dữ liệu và thông báo hệ thống.',
  },
  en: {
    // Navigation & Sidebar
    'nav.section.platform': 'Platform',
    'nav.section.apps': 'Enterprise Apps',
    'nav.portal': 'App Portal',
    'nav.store': 'Online App Store',
    'nav.studio': 'App Builder Studio',
    'nav.admin': 'Admin & Workflows',
    'nav.tasks': 'Tasks & Work',
    'nav.chat': 'Internal Chat',
    'nav.notifications': 'Notifications',
    'nav.settings': 'System Settings',
    'nav.systemReady': 'System Ready',
    'nav.systemReadyTitle': 'Data server is online',

    // Navbar & Quick Actions
    'navbar.searchPlaceholder': 'Quick search Apps, Records, Tasks... (Ctrl+K)',
    'navbar.syncOffline': 'Sync Offline ({count})',
    'navbar.syncOfflineTitle': 'Sync offline queued mutations to server',
    'navbar.desktopApp': 'Desktop App',
    'navbar.desktop': 'Desktop',
    'navbar.desktopConfigTitle': 'Desktop & Offline connection settings',
    'navbar.appStudio': 'App Studio',
    'navbar.toggleTheme': 'Toggle Light / Dark mode',
    'navbar.toLight': 'Switch to Light Mode',
    'navbar.toDark': 'Switch to Dark Mode',
    'navbar.toggleDensity': 'Toggle Compact / Comfortable density',
    'navbar.compactOn': 'Compact display mode enabled',
    'navbar.compactOff': 'Switched to Comfortable display mode',
    'navbar.languageLabel': 'Display Language',
    'navbar.languageChanged': 'Language switched to English',
    'navbar.changePassword': 'Change password',
    'navbar.accountSettings': 'Account & Appearance Settings',
    'navbar.logout': 'Sign out',
    'navbar.onlineUsers': '{count} online',
    'navbar.onlineUsersTitle': 'Online Users',

    // Command Palette
    'cmd.placeholder': 'Search Apps, Records (key/keyword), or Pages...',
    'cmd.searching': 'Searching...',
    'cmd.empty': 'No matching results found.',
    'cmd.group.apps': 'Applications',
    'cmd.group.nav': 'Quick Navigation',
    'cmd.group.actions': 'Quick Actions',
    'cmd.group.records': 'Matching Records',

    // Portal
    'portal.title': 'Enterprise App & Executive Portal',
    'portal.subtitle': 'Launch dynamic business apps, monitor executive KPIs, and process approvals in one place.',
    'portal.createApp': 'Create App',
    'portal.fromExcel': 'From Excel',
    'portal.templates': 'Templates',
    'portal.searchApps': 'Search apps by name, code, or department...',
    'portal.allDepartments': 'All departments',
    'portal.openApp': 'Open App',
    'portal.onboarding.title': 'Welcome to SmartApps Platform',
    'portal.onboarding.subtitle': 'Set up your enterprise workspace in 3 simple steps:',

    // Table & App Runtime
    'table.addRecord': 'Add Record',
    'table.importExcel': 'Import Excel',
    'table.exportExcel': 'Export Excel',
    'table.exportFiltered': 'Export filtered results (.xlsx)',
    'table.exportAll': 'Export entire table (.xlsx)',
    'table.downloadTemplate': 'Download import template (.xlsx)',
    'table.printList': 'Print list',
    'table.activityHistory': 'Activity history',
    'table.bulkEdit': 'Bulk update',
    'table.undo': 'Undo',

    // Settings
    'settings.languageTitle': 'Display Language',
    'settings.languageDesc': 'Choose the display language for navigation, tables, and system notifications.',
  },
} as const;

export type TranslationKey = keyof typeof TRANSLATIONS.vi;

export function normalizeLanguage(lang?: string | null): AppLanguage {
  return String(lang || '').trim().toLowerCase().startsWith('en') ? 'en' : 'vi';
}

export function translate(
  lang: AppLanguage,
  key: TranslationKey,
  vars?: Record<string, string | number>,
): string {
  const dict = TRANSLATIONS[lang] || TRANSLATIONS.vi;
  let text: string = dict[key] ?? TRANSLATIONS.vi[key] ?? key;
  if (vars) {
    for (const [k, v] of Object.entries(vars)) {
      text = text.replaceAll(`{${k}}`, String(v));
    }
  }
  return text;
}
