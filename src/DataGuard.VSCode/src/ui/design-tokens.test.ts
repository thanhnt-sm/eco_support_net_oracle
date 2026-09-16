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
