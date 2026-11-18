import { createTheme } from '@mui/material/styles';
import { GOOGLE_M3_TOKENS } from './design-tokens';

export const smartAppsTheme = createTheme({
  palette: {
    primary: {
      main: GOOGLE_M3_TOKENS.colors.primary.default,
      light: GOOGLE_M3_TOKENS.colors.primary.container,
      dark: GOOGLE_M3_TOKENS.colors.primary.active,
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
    background: {
      default: GOOGLE_M3_TOKENS.colors.neutral.canvas,
      paper: GOOGLE_M3_TOKENS.colors.neutral.surface,
    },
    text: {
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

export default smartAppsTheme;
