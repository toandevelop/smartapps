export const GOOGLE_M3_TOKENS = {
  colors: {
    primary: {
      default: '#1a73e8',
      hover: '#1557b0',
      active: '#174ea6',
      container: '#e8f0fe',
      onContainer: '#174ea6',
      subtle: 'rgba(26, 115, 232, 0.08)',
      focusRing: 'rgba(26, 115, 232, 0.25)',
    },
    success: {
      default: '#1e8e3e',
      hover: '#188038',
      active: '#137333',
      container: '#e6f4ea',
      onContainer: '#137333',
      border: '#ceead6',
    },
    warning: {
      default: '#ea8600',
      hover: '#e37400',
      active: '#b06000',
      container: '#fef7e0',
      onContainer: '#b06000',
      border: '#feefc3',
    },
    danger: {
      default: '#d93025',
      hover: '#c5221f',
      active: '#a50e0e',
      container: '#fce8e6',
      onContainer: '#c5221f',
      border: '#fad2cf',
    },
    neutral: {
      canvas: '#f8f9fa',
      surface: '#ffffff',
      surfaceVariant: '#f1f3f4',
      outline: '#dadce0',
      outlineVariant: '#e8eaed',
      textPrimary: '#202124',
      textSecondary: '#5f6368',
      textMuted: '#80868b',
    },
  },
  typography: {
    fontFamilies: {
      sans: "'Roboto', 'Plus Jakarta Sans', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif",
      mono: "'JetBrains Mono', Consolas, 'SF Mono', monospace",
    },
  },
  shape: {
    pill: '9999px',
    large: '16px',
    medium: '8px',
    small: '4px',
  },
  elevation: {
    level1: '0 1px 2px 0 rgba(60, 64, 67, 0.3), 0 1px 3px 1px rgba(60, 64, 67, 0.15)',
    level2: '0 1px 3px 0 rgba(60, 64, 67, 0.3), 0 4px 8px 3px rgba(60, 64, 67, 0.15)',
    level3: '0 2px 6px 2px rgba(60, 64, 67, 0.15), 0 8px 24px 4px rgba(60, 64, 67, 0.15)',
  },
} as const;

export const APP_COLOR_PRESETS = [
  { label: 'Google Blue', color: '#1a73e8', bg: '#e8f0fe' },
  { label: 'Emerald Teal', color: '#00897b', bg: '#e0f2f1' },
  { label: 'Royal Purple', color: '#7b1fa2', bg: '#f3e5f5' },
  { label: 'Forest Green', color: '#1e8e3e', bg: '#e6f4ea' },
  { label: 'Amber Orange', color: '#ea8600', bg: '#fef7e0' },
  { label: 'Crimson Red', color: '#d93025', bg: '#fce8e6' },
  { label: 'Sky Cyan', color: '#0284c7', bg: '#e0f2fe' },
];
