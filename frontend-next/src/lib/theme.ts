import { createTheme } from '@mui/material/styles';
import { GOOGLE_M3_TOKENS } from './design-tokens';
import { buildBrandPalette, DEFAULT_BRAND_HEX } from './color-theme';

export function createSmartAppsTheme(mode: 'light' | 'dark' = 'light', brandHex = DEFAULT_BRAND_HEX) {
  const isDark = mode === 'dark';
  const brand = buildBrandPalette(brandHex, isDark);

  return createTheme({
    palette: {
      mode,
      primary: {
        main: brand.primary,
        light: brand.container,
        dark: brand.active,
        contrastText: brand.onPrimary,
      },
      success: {
        main: GOOGLE_M3_TOKENS.colors.success.default,
        light: GOOGLE_M3_TOKENS.colors.success.container,
        dark: GOOGLE_M3_TOKENS.colors.success.active,
      },
      warning: {
        main: GOOGLE_M3_TOKENS.colors.warning.default,
        light: GOOGLE_M3_TOKENS.colors.warning.container,
        dark: GOOGLE_M3_TOKENS.colors.warning.active,
      },
      error: {
        main: GOOGLE_M3_TOKENS.colors.danger.default,
        light: GOOGLE_M3_TOKENS.colors.danger.container,
        dark: GOOGLE_M3_TOKENS.colors.danger.active,
      },
      background: isDark
        ? {
            default: '#0f172a',
            paper: '#162032',
          }
        : {
            default: GOOGLE_M3_TOKENS.colors.neutral.canvas,
            paper: GOOGLE_M3_TOKENS.colors.neutral.surface,
          },
      text: isDark
        ? {
            primary: '#f1f5f9',
            secondary: '#cbd5e1',
          }
        : {
            primary: GOOGLE_M3_TOKENS.colors.neutral.textPrimary,
            secondary: GOOGLE_M3_TOKENS.colors.neutral.textSecondary,
          },
    },
    typography: {
      fontFamily: GOOGLE_M3_TOKENS.typography.fontFamilies.sans,
    },
    shape: {
      borderRadius: 8,
    },
    components: {
      MuiButton: {
        styleOverrides: {
          root: {
            textTransform: 'none',
            borderRadius: GOOGLE_M3_TOKENS.shape.medium,
            fontWeight: 600,
          },
        },
      },
      MuiDialog: {
        styleOverrides: {
          paper: {
            borderRadius: GOOGLE_M3_TOKENS.shape.large,
          },
        },
      },
    },
  });
}

export const smartAppsTheme = createSmartAppsTheme('light', DEFAULT_BRAND_HEX);

export default smartAppsTheme;
