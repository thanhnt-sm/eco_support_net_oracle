import assert from "node:assert/strict";
import test from "node:test";
import { renderDashboardHtml } from "./dashboard-view";
import type { ScanReport } from "./sql-queries-tree-provider";

test("renderDashboardHtml embeds scan report SQL queries safely into HTML", () => {
    const nonce = "safeNonceSql123";
    const report: ScanReport = {
        filesScanned: 2,
        queriesFound: 1,
        connectionsFound: 1,
        violationsCount: 0,
        connections: [
            { name: "DefaultDb", provider: "sqlserver", hint: "Server=localhost;Password=Secret123;" }
        ],
        queries: [
            {
                sql: "SELECT CUSTOMER_ID, FULL_NAME FROM CUSTOMERS",
                operation: "Read",
                tables: ["CUSTOMERS"],
                targetType: "Customer",
                mappingStatus: "matched",
                action: "shape-check",
                columns: ["CUSTOMER_ID", "FULL_NAME"],
                properties: ["CustomerId", "FullName"],
                unmappedColumns: [],
                unmappedProperties: [],
                location: { file: "/workspace/CustomerRepo.cs", line: 15 }
            }
        ]
    };

    const html = renderDashboardHtml([], nonce, report);

    // Verify SQL mapping tab and table elements exist
    assert.match(html, /SQL ↔ C# Mappings/);
    assert.match(html, /queriesTable/);
    assert.match(html, /CUSTOMER_ID, FULL_NAME FROM CUSTOMERS/);
    // Password in connection hint must be redacted
    assert.doesNotMatch(html, /Secret123/);
});
