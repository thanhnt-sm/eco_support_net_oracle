import assert from "node:assert/strict";
import test from "node:test";
import {
    calculateContrastRatio,
    DESIGN_TOKENS,
    getDashboardCss,
    getRelativeLuminance,
    verifyWcagContrast
} from "./design-tokens";

test("getRelativeLuminance correctly computes luminance bounds", () => {
    assert.equal(getRelativeLuminance("#000000"), 0);
    assert.equal(Math.round(getRelativeLuminance("#FFFFFF") * 100) / 100, 1);
    assert.throws(() => getRelativeLuminance("invalid"), /Invalid 6-character hex/);
});

test("Dark Mode (OLED) text meets WCAG AA and AAA contrast minimums", () => {
    const mainOnDark = calculateContrastRatio(DESIGN_TOKENS.colors.textMain, DESIGN_TOKENS.colors.bgDark);
    const mainOnCard = calculateContrastRatio(DESIGN_TOKENS.colors.textMain, DESIGN_TOKENS.colors.bgCard);
    const mutedOnDark = calculateContrastRatio(DESIGN_TOKENS.colors.textMuted, DESIGN_TOKENS.colors.bgDark);
    const mutedOnCard = calculateContrastRatio(DESIGN_TOKENS.colors.textMuted, DESIGN_TOKENS.colors.bgCard);

    // WCAG AAA requires >= 7.0 for normal text
    assert.ok(mainOnDark >= 7.0, `Expected textMain on bgDark (${mainOnDark.toFixed(2)}) >= 7.0`);
    assert.ok(mainOnCard >= 7.0, `Expected textMain on bgCard (${mainOnCard.toFixed(2)}) >= 7.0`);

    // WCAG AA requires >= 4.5 for text
    assert.ok(mutedOnDark >= 4.5, `Expected textMuted on bgDark (${mutedOnDark.toFixed(2)}) >= 4.5`);
    assert.ok(mutedOnCard >= 4.5, `Expected textMuted on bgCard (${mutedOnCard.toFixed(2)}) >= 4.5`);

    assert.ok(verifyWcagContrast(DESIGN_TOKENS.colors.textMain, DESIGN_TOKENS.colors.bgDark));
    assert.ok(verifyWcagContrast(DESIGN_TOKENS.colors.textMuted, DESIGN_TOKENS.colors.bgDark));
});

test("Action CTA (#F97316) meets contrast requirements for button text and focus outline", () => {
    // Focus ring / badge on deep background must meet WCAG AA
    const ctaOnDark = calculateContrastRatio(DESIGN_TOKENS.colors.ctaAction, DESIGN_TOKENS.colors.bgDark);
    assert.ok(ctaOnDark >= 4.5, `CTA on bgDark (${ctaOnDark.toFixed(2)}) must be >= 4.5`);

    // Text on CTA button (#0F172A on #F97316) must meet WCAG AA (>= 4.5)
    const textOnCta = calculateContrastRatio(DESIGN_TOKENS.colors.ctaText, DESIGN_TOKENS.colors.ctaAction);
    assert.ok(textOnCta >= 4.5, `CTA text on CTA button (${textOnCta.toFixed(2)}) must be >= 4.5`);
});

test("Light Mode palette satisfies WCAG AA contrast standards", () => {
    const mainOnLight = calculateContrastRatio(DESIGN_TOKENS.colors.light.textMain, DESIGN_TOKENS.colors.light.bgMain);
    const mutedOnLight = calculateContrastRatio(DESIGN_TOKENS.colors.light.textMuted, DESIGN_TOKENS.colors.light.bgMain);

    assert.ok(mainOnLight >= 7.0, `Expected textMain on light bg (${mainOnLight.toFixed(2)}) >= 7.0`);
    assert.ok(mutedOnLight >= 4.5, `Expected textMuted on light bg (${mutedOnLight.toFixed(2)}) >= 4.5`);
});

test("getDashboardCss emits Fira Code typography, focus outlines, and smooth transitions", () => {
    const css = getDashboardCss();

    assert.match(css, /Fira Code/);
    assert.match(css, /Fira Sans/);
    assert.match(css, /outline:\s*2px solid var\(--dg-focus-outline,\s*#F97316\)/);
    assert.match(css, /transition/);
    assert.match(css, /#1E293B/);
    assert.match(css, /#2563EB/);
    assert.match(css, /#3B82F6/);
    assert.match(css, /#F97316/);
});

test("Step 1: Tokenized badge colors meet WCAG AA contrast >= 4.5:1", () => {
    const colors = DESIGN_TOKENS.colors;
    assert.ok(colors.badgeMatched, "badgeMatched must be defined");
    assert.ok(colors.badgePartial, "badgePartial must be defined");
    assert.ok(colors.badgeUnmapped, "badgeUnmapped must be defined");
    assert.ok(colors.badgeUntyped, "badgeUntyped must be defined");
    assert.ok(colors.badgeWarning, "badgeWarning must be defined");
    assert.ok(colors.unmappedHighlight, "unmappedHighlight must be defined");

    const matchedRatio = calculateContrastRatio(colors.badgeMatched.fg, colors.badgeMatched.bg);
    const partialRatio = calculateContrastRatio(colors.badgePartial.fg, colors.badgePartial.bg);
    const unmappedRatio = calculateContrastRatio(colors.badgeUnmapped.fg, colors.badgeUnmapped.bg);
    const untypedRatio = calculateContrastRatio(colors.badgeUntyped.fg, colors.badgeUntyped.bg);

    assert.ok(matchedRatio >= 4.5, `badgeMatched contrast ${matchedRatio} must be >= 4.5`);
    assert.ok(partialRatio >= 4.5, `badgePartial contrast ${partialRatio} must be >= 4.5`);
    assert.ok(unmappedRatio >= 4.5, `badgeUnmapped contrast ${unmappedRatio} must be >= 4.5`);
    assert.ok(untypedRatio >= 4.5, `badgeUntyped contrast ${untypedRatio} must be >= 4.5`);
});

test("Step 1: Light mode CTAAction is updated to #C2410C and satisfies WCAG AA >= 4.5:1 against white", () => {
    assert.equal(DESIGN_TOKENS.colors.light.ctaAction, "#C2410C");
    const contrast = calculateContrastRatio(DESIGN_TOKENS.colors.light.ctaText, DESIGN_TOKENS.colors.light.ctaAction);
    assert.ok(contrast >= 4.5, `Light CTA contrast ${contrast} must be >= 4.5`);
});

test("Step 1: getDashboardCss emits badge CSS variables in both :root and body.vscode-light", () => {
    const css = getDashboardCss();

    const expectedVars = [
        "--dg-badge-matched-bg",
        "--dg-badge-matched-fg",
        "--dg-badge-partial-bg",
        "--dg-badge-partial-fg",
        "--dg-badge-unmapped-bg",
        "--dg-badge-unmapped-fg",
        "--dg-badge-untyped-bg",
        "--dg-badge-untyped-fg",
        "--dg-badge-warning-fg",
        "--dg-unmapped-highlight-fg"
    ];

    const rootMatch = css.match(/:root\s*\{([^}]+)\}/);
    assert.ok(rootMatch, ":root block must be present");
    for (const v of expectedVars) {
        assert.ok(rootMatch[1].includes(v), `:root must contain ${v}`);
    }

    const lightMatch = css.match(/body\.vscode-light\s*\{([^}]+)\}/);
    assert.ok(lightMatch, "body.vscode-light block must be present");
    for (const v of expectedVars) {
        assert.ok(lightMatch[1].includes(v), `body.vscode-light must contain ${v}`);
    }

    // No raw hex for badges in badge context in CSS output
    assert.ok(!css.includes("#065f46"), "CSS must not contain old badge hex #065f46");
    assert.ok(!css.includes("#34d399"), "CSS must not contain old badge hex #34d399");
    assert.ok(!css.includes("#7f1d1d"), "CSS must not contain old badge hex #7f1d1d");
});

test("Step 2: Interactive element sizing and viewport offset are updated", () => {
    const css = getDashboardCss();

    assert.match(css, /\.btn\s*\{[^}]*padding:\s*7px\s+14px/);
    assert.match(css, /\.filter-chip\s*\{[^}]*padding:\s*5px\s+12px/);
    assert.match(css, /\.quick-fix-btn\s*\{[^}]*padding:\s*5px\s+10px/);
    assert.match(css, /\.virtual-viewport\s*\{[^}]*height:\s*calc\(100vh\s*-\s*180px\)/);
});

test("Step 3: getDashboardCss contains .finding-card:focus-visible", () => {
    const css = getDashboardCss();
    assert.match(css, /\.finding-card:focus-visible/);
});

test("Step 6: getDashboardCss contains prefers-reduced-motion media query", () => {
    const css = getDashboardCss();
    assert.match(css, /@media\s*\(\s*prefers-reduced-motion:\s*reduce\s*\)/);
});

test("Step 8: getDashboardCss contains .severity-label styling", () => {
    const css = getDashboardCss();
    assert.match(css, /\.severity-label\s*\{[^}]*font-size:\s*10px/);
    assert.match(css, /\.severity-error\s+\.severity-label\s*\{[^}]*color:\s*var\(--dg-error\)/);
});
