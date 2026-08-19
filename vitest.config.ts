import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    include: [
      "packages/core/src/__tests__/**/*.test.ts",
      "packages/cli/src/**/*.test.ts",
      "packages/mcp/src/**/*.test.ts",
    ],
    globals: true,
    coverage: {
      provider: "v8",
      reporter: ["text", "json", "html"],
    },
  },
});