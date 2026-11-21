export type DensityMode = 'compact' | 'comfortable' | 'spacious';

export interface BrandPalette {
  primary: string;
  hover: string;
  active: string;
  onPrimary: '#ffffff' | '#111827';
  container: string;
  onContainer: string;
  ring: string;
}

export const DEFAULT_BRAND_HEX = '#1a73e8';

export function normalizeHexColor(raw?: string | null, fallback = DEFAULT_BRAND_HEX): string {
  const val = (raw ?? '').trim();
  if (/^#[0-9a-fA-F]{6}$/.test(val)) {
    return val.toLowerCase();
  }
  return fallback.toLowerCase();
}

export function hexToRgb(hex: string): { r: number; g: number; b: number } {
  const clean = normalizeHexColor(hex).slice(1);
  return {
    r: parseInt(clean.slice(0, 2), 16),
    g: parseInt(clean.slice(2, 4), 16),
    b: parseInt(clean.slice(4, 6), 16),
  };
}

function rgbToHex(r: number, g: number, b: number): string {
  const clamp = (n: number) => Math.max(0, Math.min(255, Math.round(n)));
  return `#${clamp(r).toString(16).padStart(2, '0')}${clamp(g).toString(16).padStart(2, '0')}${clamp(b).toString(16).padStart(2, '0')}`;
}

function mixRgb(
  a: { r: number; g: number; b: number },
  b: { r: number; g: number; b: number },
  ratioB: number,
): string {
  const t = Math.max(0, Math.min(1, ratioB));
  return rgbToHex(
    a.r + (b.r - a.r) * t,
    a.g + (b.g - a.g) * t,
    a.b + (b.b - a.b) * t,
  );
}

/**
 * WCAG 2.1 relative luminance in [0, 1]
 */
export function computeRelativeLuminance(hex: string): number {
  const { r, g, b } = hexToRgb(hex);
  const toLinear = (channel: number) => {
    const s = channel / 255;
    return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
  };
  return 0.2126 * toLinear(r) + 0.7152 * toLinear(g) + 0.0722 * toLinear(b);
}

/**
 * WCAG 2.1 contrast ratio between two hex colors (1..21)
 */
export function computeContrastRatio(hexA: string, hexB: string): number {
  const l1 = computeRelativeLuminance(hexA);
  const l2 = computeRelativeLuminance(hexB);
  const lighter = Math.max(l1, l2);
  const darker = Math.min(l1, l2);
  return (lighter + 0.05) / (darker + 0.05);
}

/**
 * Picks `#ffffff` or `#111827` maximizing WCAG contrast against a solid background.
 */
export function pickContrastText(bgHex: string): '#ffffff' | '#111827' {
  const whiteContrast = computeContrastRatio(bgHex, '#ffffff');
  const darkContrast = computeContrastRatio(bgHex, '#111827');
  return whiteContrast >= darkContrast ? '#ffffff' : '#111827';
}

export function resolveEffectiveBrandColor(userAccent?: string | null, companyBrand?: string | null): string {
  const companyHex = normalizeHexColor(companyBrand, DEFAULT_BRAND_HEX);
  const trimmed = (userAccent ?? '').trim();
  if (!trimmed || trimmed.toLowerCase() === 'brand') {
    return companyHex;
  }
  return normalizeHexColor(trimmed, companyHex);
}

export function buildBrandPalette(baseHex: string, isDark: boolean): BrandPalette {
  const normalized = normalizeHexColor(baseHex, DEFAULT_BRAND_HEX);
  const baseRgb = hexToRgb(normalized);
  const white = { r: 255, g: 255, b: 255 };
  const black = { r: 0, g: 0, b: 0 };

  // In dark mode, lift very dark brand colors iteratively (Material 3 tonal lift)
  // so primary has >= 3.4 contrast against dark surface AND >= 4.5 against onPrimary text.
  let primary = normalized;
  if (isDark) {
    for (let step = 0.25; step <= 0.85; step += 0.08) {
      const surfaceOk = computeContrastRatio(primary, '#162032') >= 3.4;
      const textOk = computeContrastRatio(primary, pickContrastText(primary)) >= 4.5;
      if (surfaceOk && textOk) break;
      primary = mixRgb(baseRgb, white, step);
    }
  }

  const primaryRgb = hexToRgb(primary);
  const hover = isDark ? mixRgb(primaryRgb, white, 0.14) : mixRgb(primaryRgb, black, 0.16);
  const active = isDark ? mixRgb(primaryRgb, white, 0.24) : mixRgb(primaryRgb, black, 0.26);
  const onPrimary = pickContrastText(primary);
  const container = isDark
    ? `rgba(${primaryRgb.r}, ${primaryRgb.g}, ${primaryRgb.b}, 0.2)`
    : mixRgb(primaryRgb, white, 0.88);
  const onContainer = isDark ? mixRgb(primaryRgb, white, 0.55) : mixRgb(primaryRgb, black, 0.28);
  const ring = `rgba(${primaryRgb.r}, ${primaryRgb.g}, ${primaryRgb.b}, 0.28)`;

  return {
    primary,
    hover,
    active,
    onPrimary,
    container,
    onContainer,
    ring,
  };
}

export function normalizeDensityMode(raw?: string | null, compactFallback = false): DensityMode {
  const val = (raw ?? '').trim().toLowerCase();
  if (val === 'compact' || val === 'comfortable' || val === 'spacious') {
    return val;
  }
  return compactFallback ? 'compact' : 'comfortable';
}

export function normalizePinnedApps(list?: readonly string[] | null): string[] {
  if (!Array.isArray(list)) return [];
  const seen = new Set<string>();
  const out: string[] = [];
  for (const item of list) {
    const code = String(item ?? '').trim().toUpperCase();
    if (!code || code.length > 64 || seen.has(code)) continue;
    seen.add(code);
    out.push(code);
    if (out.length >= 12) break;
  }
  return out;
}
