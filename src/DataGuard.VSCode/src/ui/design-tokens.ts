/**
 * Design system tokens and styles for DataGuard IDE extensions (ui-ux-pro-max standard).
 * Designed for Dark Mode (OLED) with high contrast, Fira Code typography,
 * smooth 150-300ms interactive transitions, and visible focus indicators.
 */

export const DESIGN_TOKENS = {
    colors: {
        // OLED Dark Mode Palette
        bgOled: "#000000",
        bgDark: "#0F172A", // Deep background container
        bgCard: "#1E293B", // Card / panel background
        bgCardHover: "#273549",
        border: "#334155",
        borderHighlight: "#3B82F6",

        // Typography Colors
        textMain: "#F8FAFC",
        textMuted: "#94A3B8",
        textInverse: "#0F172A",

        // Brand & Interactive Accents
        primary: "#2563EB",       // Blue Primary
        primaryHover: "#1D4ED8",
        secondary: "#3B82F6",     // Light Blue Secondary
        ctaAction: "#F97316",     // Action / Quick-Fix CTA (Orange)
        ctaActionHover: "#EA580C",
        ctaText: "#0F172A",       // High contrast text on CTA button (6.0:1)

        // Diagnostic Severity Badges
        severityError: "#EF4444",
        severityWarning: "#F59E0B",
        severityInfo: "#3B82F6",
        severitySuccess: "#10B981",

        // Focus & Accessibility
        focusOutline: "#F97316",

        // Light Mode Overrides (for light theme compatibility)
        light: {
            bgMain: "#FFFFFF",
            bgCard: "#F1F5F9",
            bgCardHover: "#E2E8F0",
            border: "#CBD5E1",
            textMain: "#0F172A",
            textMuted: "#475569",
            primary: "#2563EB",
            secondary: "#1D4ED8",
            ctaAction: "#EA580C",
            ctaText: "#FFFFFF",
            focusOutline: "#EA580C"
        }
    },
    typography: {
        // Monospace font stack for code, diagnostics, and SQL snippets
        fontCode: "'Fira Code', 'Fira Sans', Consolas, 'Courier New', monospace",
        // Interface font stack
        fontUi: "'Fira Sans', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif",
        sizeSm: "12px",
        sizeBase: "13px",
        sizeMd: "14px",
        sizeLg: "16px",
        sizeXl: "20px"
    },
    transitions: {
        fast: "150ms ease",
        normal: "200ms ease",
        smooth: "250ms cubic-bezier(0.4, 0, 0.2, 1)"
    }
} as const;

/**
 * Calculates the relative luminance of a sRGB hexadecimal color.
 * Follows W3C WCAG 2.1 algorithm.
 */
export function getRelativeLuminance(hex: string): number {
    const cleanHex = hex.replace("#", "").trim();
    if (cleanHex.length !== 6) {
        throw new Error(`Invalid 6-character hex color: ${hex}`);
    }

    const r = parseInt(cleanHex.substring(0, 2), 16) / 255;
    const g = parseInt(cleanHex.substring(2, 4), 16) / 255;
    const b = parseInt(cleanHex.substring(4, 6), 16) / 255;

    const [rs, gs, bs] = [r, g, b].map((val) =>
        val <= 0.03928 ? val / 12.92 : Math.pow((val + 0.055) / 1.055, 2.4)
    );

    return 0.2126 * rs + 0.7152 * gs + 0.0722 * bs;
}

/**
 * Calculates WCAG contrast ratio between two hex colors.
 * Returns a number between 1.0 and 21.0.
 */
export function calculateContrastRatio(hex1: string, hex2: string): number {
    const l1 = getRelativeLuminance(hex1);
    const l2 = getRelativeLuminance(hex2);
    const brightest = Math.max(l1, l2);
    const darkest = Math.min(l1, l2);
    return (brightest + 0.05) / (darkest + 0.05);
}

/**
 * Asserts whether two colors satisfy the minimum WCAG contrast standard (default 4.5:1 for normal text).
 */
export function verifyWcagContrast(foreground: string, background: string, minRatio = 4.5): boolean {
    return calculateContrastRatio(foreground, background) >= minRatio;
}

/**
 * Generates the unified CSS stylesheet for DataGuard Webviews, implementing
 * the OLED dark theme with high contrast, Fira Code typography, smooth transitions,
 * and prominent focus rings.
 */
export function getDashboardCss(): string {
    return `
:root {
    --dg-bg-oled: ${DESIGN_TOKENS.colors.bgOled};
    --dg-bg-dark: ${DESIGN_TOKENS.colors.bgDark};
    --dg-bg-card: ${DESIGN_TOKENS.colors.bgCard};
    --dg-bg-card-hover: ${DESIGN_TOKENS.colors.bgCardHover};
    --dg-border: ${DESIGN_TOKENS.colors.border};
    --dg-border-highlight: ${DESIGN_TOKENS.colors.borderHighlight};
    --dg-text-main: ${DESIGN_TOKENS.colors.textMain};
    --dg-text-muted: ${DESIGN_TOKENS.colors.textMuted};
    --dg-text-inverse: ${DESIGN_TOKENS.colors.textInverse};
    --dg-primary: ${DESIGN_TOKENS.colors.primary};
    --dg-primary-hover: ${DESIGN_TOKENS.colors.primaryHover};
    --dg-secondary: ${DESIGN_TOKENS.colors.secondary};
    --dg-cta: ${DESIGN_TOKENS.colors.ctaAction};
    --dg-cta-hover: ${DESIGN_TOKENS.colors.ctaActionHover};
    --dg-cta-text: ${DESIGN_TOKENS.colors.ctaText};
    --dg-error: ${DESIGN_TOKENS.colors.severityError};
    --dg-warning: ${DESIGN_TOKENS.colors.severityWarning};
    --dg-info: ${DESIGN_TOKENS.colors.severityInfo};
    --dg-success: ${DESIGN_TOKENS.colors.severitySuccess};
    --dg-focus-outline: ${DESIGN_TOKENS.colors.focusOutline};
    --dg-font-code: ${DESIGN_TOKENS.typography.fontCode};
    --dg-font-ui: ${DESIGN_TOKENS.typography.fontUi};
    --dg-transition: ${DESIGN_TOKENS.transitions.normal};
}

body.vscode-light {
    --dg-bg-oled: ${DESIGN_TOKENS.colors.light.bgMain};
    --dg-bg-dark: ${DESIGN_TOKENS.colors.light.bgMain};
    --dg-bg-card: ${DESIGN_TOKENS.colors.light.bgCard};
    --dg-bg-card-hover: ${DESIGN_TOKENS.colors.light.bgCardHover};
    --dg-border: ${DESIGN_TOKENS.colors.light.border};
    --dg-text-main: ${DESIGN_TOKENS.colors.light.textMain};
    --dg-text-muted: ${DESIGN_TOKENS.colors.light.textMuted};
    --dg-cta: ${DESIGN_TOKENS.colors.light.ctaAction};
    --dg-cta-text: ${DESIGN_TOKENS.colors.light.ctaText};
    --dg-focus-outline: ${DESIGN_TOKENS.colors.light.focusOutline};
}

* {
    box-sizing: border-box;
    margin: 0;
    padding: 0;
}

body {
    background-color: var(--dg-bg-dark, #0F172A);
    color: var(--dg-text-main, #F8FAFC);
    font-family: var(--dg-font-ui);
    font-size: ${DESIGN_TOKENS.typography.sizeBase};
    line-height: 1.5;
    overflow-x: hidden;
    padding: 16px;
}

/* Typography elements */
code, pre, .code-snippet, .rule-badge, .parameter-type {
    font-family: var(--dg-font-code);
}

/* Focus indicators across all interactive elements */
button:focus-visible,
input:focus-visible,
select:focus-visible,
.finding-item:focus-visible,
.quick-fix-btn:focus-visible {
    outline: 2px solid var(--dg-focus-outline, #F97316) !important;
    outline-offset: 2px !important;
}

/* Header & Controls */
.dashboard-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    border-bottom: 1px solid var(--dg-border);
    padding-bottom: 12px;
    margin-bottom: 16px;
    flex-wrap: wrap;
    gap: 12px;
}

.dashboard-title {
    display: flex;
    align-items: center;
    gap: 10px;
    font-size: ${DESIGN_TOKENS.typography.sizeLg};
    font-weight: 600;
}

.dashboard-title .badge-count {
    background-color: var(--dg-primary);
    color: #FFFFFF;
    font-size: ${DESIGN_TOKENS.typography.sizeSm};
    padding: 2px 8px;
    border-radius: 9999px;
    font-weight: 500;
}

.toolbar-controls {
    display: flex;
    align-items: center;
    gap: 8px;
    flex-wrap: wrap;
}

/* Buttons */
.btn {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    padding: 6px 12px;
    border-radius: 4px;
    font-size: ${DESIGN_TOKENS.typography.sizeSm};
    font-weight: 500;
    cursor: pointer;
    border: 1px solid transparent;
    transition: all var(--dg-transition);
}

.btn-primary {
    background-color: var(--dg-primary);
    color: #FFFFFF;
}

.btn-primary:hover {
    background-color: var(--dg-primary-hover);
}

.btn-cta {
    background-color: var(--dg-cta);
    color: var(--dg-cta-text);
    font-weight: 600;
}

.btn-cta:hover {
    background-color: var(--dg-cta-hover);
    transform: translateY(-1px);
}

.btn-secondary {
    background-color: var(--dg-bg-card);
    color: var(--dg-text-main);
    border-color: var(--dg-border);
}

.btn-secondary:hover {
    background-color: var(--dg-bg-card-hover);
    border-color: var(--dg-border-highlight);
}

/* Filter bar */
.filter-bar {
    display: flex;
    gap: 10px;
    margin-bottom: 16px;
    align-items: center;
    flex-wrap: wrap;
}

.search-input {
    flex: 1;
    min-width: 200px;
    background-color: var(--dg-bg-card);
    border: 1px solid var(--dg-border);
    color: var(--dg-text-main);
    padding: 6px 12px;
    border-radius: 4px;
    font-size: ${DESIGN_TOKENS.typography.sizeSm};
    transition: border-color var(--dg-transition);
}

.search-input:focus {
    border-color: var(--dg-secondary);
}

.filter-chip-group {
    display: flex;
    gap: 6px;
}

.filter-chip {
    padding: 4px 10px;
    border-radius: 12px;
    font-size: ${DESIGN_TOKENS.typography.sizeSm};
    background-color: var(--dg-bg-card);
    border: 1px solid var(--dg-border);
    color: var(--dg-text-muted);
    cursor: pointer;
    transition: all var(--dg-transition);
    user-select: none;
}

.filter-chip.active {
    background-color: var(--dg-primary);
    color: #FFFFFF;
    border-color: var(--dg-primary);
}

/* Virtualized list container */
.virtual-viewport {
    position: relative;
    overflow-y: auto;
    overflow-x: hidden;
    height: calc(100vh - 170px);
    min-height: 300px;
    border: 1px solid var(--dg-border);
    border-radius: 6px;
    background-color: var(--dg-bg-oled);
}

.virtual-scroll-spacer {
    position: absolute;
    top: 0;
    left: 0;
    width: 100%;
    pointer-events: none;
    z-index: -1;
}

.virtual-content {
    position: absolute;
    top: 0;
    left: 0;
    width: 100%;
}

/* Finding card */
.finding-card {
    background-color: var(--dg-bg-card);
    border: 1px solid var(--dg-border);
    border-left: 4px solid var(--dg-border);
    border-radius: 4px;
    margin: 6px 8px;
    padding: 10px 14px;
    display: flex;
    flex-direction: column;
    gap: 6px;
    transition: all var(--dg-transition);
    cursor: pointer;
}

.finding-card:hover {
    background-color: var(--dg-bg-card-hover);
    border-color: var(--dg-border-highlight);
    transform: translateX(2px);
}

.finding-card.severity-error {
    border-left-color: var(--dg-error);
}

.finding-card.severity-warning {
    border-left-color: var(--dg-warning);
}

.finding-card.severity-info {
    border-left-color: var(--dg-info);
}

.finding-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    gap: 8px;
}

.rule-badge {
    font-size: ${DESIGN_TOKENS.typography.sizeSm};
    font-weight: 700;
    padding: 2px 6px;
    border-radius: 3px;
    background-color: rgba(37, 99, 235, 0.2);
    color: var(--dg-secondary);
    border: 1px solid rgba(59, 130, 246, 0.3);
}

.rule-badge.error {
    background-color: rgba(239, 68, 68, 0.2);
    color: var(--dg-error);
    border-color: rgba(239, 68, 68, 0.3);
}

.rule-badge.warning {
    background-color: rgba(245, 158, 11, 0.2);
    color: var(--dg-warning);
    border-color: rgba(245, 158, 11, 0.3);
}

.finding-location {
    font-size: ${DESIGN_TOKENS.typography.sizeSm};
    color: var(--dg-text-muted);
    font-family: var(--dg-font-code);
    text-overflow: ellipsis;
    overflow: hidden;
    white-space: nowrap;
}

.finding-message {
    font-size: ${DESIGN_TOKENS.typography.sizeBase};
    color: var(--dg-text-main);
    word-break: break-word;
}

.finding-actions {
    display: flex;
    align-items: center;
    gap: 8px;
    margin-top: 4px;
}

.quick-fix-btn {
    background-color: var(--dg-cta);
    color: var(--dg-cta-text);
    border: none;
    padding: 3px 8px;
    border-radius: 3px;
    font-size: ${DESIGN_TOKENS.typography.sizeSm};
    font-weight: 600;
    cursor: pointer;
    display: inline-flex;
    align-items: center;
    gap: 4px;
    transition: all var(--dg-transition);
}

.quick-fix-btn:hover {
    background-color: var(--dg-cta-hover);
}

.empty-state {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    height: 240px;
    color: var(--dg-text-muted);
    gap: 12px;
}
`.trim();
}
