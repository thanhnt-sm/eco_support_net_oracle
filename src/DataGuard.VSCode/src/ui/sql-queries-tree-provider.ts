import * as path from "path";
import * as vscode from "vscode";
import { isPathInWorkspaceFolder } from "../security";
import { redactForUi } from "./redaction";

export interface QueryLocation {
    file?: string;
    line?: number;
}

export interface QueryScanItem {
    sql: string;
    location?: QueryLocation;
    targetTypeLocation?: QueryLocation;
    operation: string;
    tables: string[];
    targetType?: string | null;
    mappingStatus: "matched" | "partial" | "unmapped" | "untyped";
    action: string;
    columns: string[];
    properties: string[];
    unmappedColumns?: string[];
    unmappedProperties?: string[];
}

export interface ScanConnectionItem {
    name: string;
    provider: string;
    hint?: string;
}

export interface ScanReport {
    filesScanned: number;
    queriesFound: number;
    connectionsFound: number;
    violationsCount: number;
    connections: ScanConnectionItem[];
    queries: QueryScanItem[];
}

function isPathInWorkspace(targetPath: string): boolean {
    const folderPaths = (vscode.workspace.workspaceFolders ?? []).filter(f => f.uri.scheme === "file").map(f => f.uri.fsPath);
    return isPathInWorkspaceFolder(targetPath, folderPaths);
}

export class SqlQueryTreeItem extends vscode.TreeItem {
    constructor(
        public readonly label: string,
        public readonly collapsibleState: vscode.TreeItemCollapsibleState,
        public readonly itemType: "group" | "query" | "empty",
        public readonly query?: QueryScanItem,
        public readonly count?: number,
        public readonly groupPath?: string
    ) {
        super(label, collapsibleState);

        if (itemType === "group") {
            this.id = groupPath || label;
            this.iconPath = new vscode.ThemeIcon("file-code");
            this.description = count !== undefined ? `(${count} queries)` : undefined;
            if (groupPath) {
                this.tooltip = groupPath;
            }
            this.contextValue = "sqlGroup";
        } else if (itemType === "query" && query) {
            const rawSql = typeof query.sql === "string" ? query.sql : "";
            const redactedSql = redactForUi(rawSql);
            const truncatedSql = redactedSql.replace(/\s+/g, " ").trim();
            const displaySql = truncatedSql.length > 55 ? truncatedSql.slice(0, 52) + "..." : truncatedSql;
            this.id = `${query.location?.file || ""}:${query.location?.line || ""}:${displaySql}`;
            this.label = displaySql;
            this.description = `${query.targetType ?? "untyped"} [${query.mappingStatus}]`;

            const md = new vscode.MarkdownString();
            md.appendMarkdown(`### ${query.operation.toUpperCase()} Query\n\n`);
            md.appendCodeblock(redactForUi(query.sql), "sql");
            md.appendMarkdown(`\n**Target**: \`${query.targetType ?? "untyped"}\`\n\n`);
            md.appendMarkdown(`**Status**: \`${query.mappingStatus}\` | **Action**: \`${query.action}\`\n\n`);
            if (query.tables && query.tables.length > 0) {
                md.appendMarkdown(`**Tables**: ${query.tables.join(", ")}\n\n`);
            }
            if (query.columns && query.columns.length > 0) {
                md.appendMarkdown(`**Columns**: ${query.columns.join(", ")}\n\n`);
            }
            if (query.properties && query.properties.length > 0) {
                md.appendMarkdown(`**Properties**: ${query.properties.join(", ")}\n\n`);
            }
            if (query.unmappedColumns && query.unmappedColumns.length > 0) {
                md.appendMarkdown(`⚠️ **Unmapped Columns**: ${query.unmappedColumns.join(", ")}\n\n`);
            }
            if (query.unmappedProperties && query.unmappedProperties.length > 0) {
                md.appendMarkdown(`⚠️ **Unmapped Properties**: ${query.unmappedProperties.join(", ")}\n\n`);
            }
            if (query.location?.file && query.location.line) {
                md.appendMarkdown(`**Location**: \`${query.location.file}:${query.location.line}\``);
            }
            this.tooltip = md;

            switch (query.mappingStatus) {
                case "matched":
                    this.iconPath = new vscode.ThemeIcon("pass", new vscode.ThemeColor("testing.iconPassed"));
                    break;
                case "partial":
                    this.iconPath = new vscode.ThemeIcon("warning", new vscode.ThemeColor("editorWarning.foreground"));
                    break;
                case "unmapped":
                    this.iconPath = new vscode.ThemeIcon("error", new vscode.ThemeColor("errorForeground"));
                    break;
                case "untyped":
                default:
                    this.iconPath = new vscode.ThemeIcon("database", new vscode.ThemeColor("symbolIcon.functionForeground"));
                    break;
            }

            this.contextValue = "sqlQuery";

            if (query.location?.file && typeof query.location.line === "number" && isPathInWorkspace(query.location.file)) {
                const line = query.location.line;
                this.command = {
                    command: "vscode.open",
                    title: "Jump to Query",
                    arguments: [
                        vscode.Uri.file(query.location.file),
                        {
                            selection: new vscode.Range(
                                Math.max(0, line - 1),
                                0,
                                Math.max(0, line - 1),
                                0
                            ),
                            preview: true
                        }
                    ]
                };
            }
        }
    }
}

export class DataGuardSqlQueriesTreeProvider implements vscode.TreeDataProvider<SqlQueryTreeItem> {
    private readonly _onDidChangeTreeData = new vscode.EventEmitter<SqlQueryTreeItem | undefined | null | void>();
    public readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

    private currentReport: ScanReport | null = null;

    public setScanReport(report: ScanReport | null): void {
        this.currentReport = report;
        this._onDidChangeTreeData.fire();
    }

    public clear(): void {
        this.currentReport = null;
        this._onDidChangeTreeData.fire();
    }

    public getTreeItem(element: SqlQueryTreeItem): vscode.TreeItem {
        return element;
    }

    public getChildren(element?: SqlQueryTreeItem): Thenable<SqlQueryTreeItem[]> {
        if (!this.currentReport || !Array.isArray(this.currentReport.queries) || this.currentReport.queries.length === 0) {
            return Promise.resolve([
                new SqlQueryTreeItem(
                    "No discovered SQL queries. Run DataGuard Scan to discover queries.",
                    vscode.TreeItemCollapsibleState.None,
                    "empty"
                )
            ]);
        }

        if (!element) {
            // Group queries by file path (handling potential collisions by preserving full path key)
            const fileGroups = new Map<string, { label: string; queries: QueryScanItem[] }>();
            for (const q of this.currentReport.queries) {
                if (!q) {
                    continue;
                }
                const fullPath = q.location?.file || "Unknown Location";
                const normalizedGroupPath = process.platform === "win32" ? fullPath.toLowerCase() : fullPath;
                let group = fileGroups.get(normalizedGroupPath);
                if (!group) {
                    let base = "Unknown Location";
                    if (q.location?.file) {
                        try {
                            const uri = vscode.Uri.file(q.location.file);
                            base = vscode.workspace.asRelativePath(uri, true);
                        } catch {
                            base = q.location.file.replace(/\\/g, "/").split("/").pop() || q.location.file;
                        }
                    }
                    group = { label: base, queries: [] };
                    fileGroups.set(normalizedGroupPath, group);
                }
                group.queries.push(q);
            }

            const items: SqlQueryTreeItem[] = [];
            for (const [fullPath, group] of fileGroups) {
                items.push(
                    new SqlQueryTreeItem(
                        group.label,
                        vscode.TreeItemCollapsibleState.Expanded,
                        "group",
                        undefined,
                        group.queries.length,
                        fullPath
                    )
                );
            }
            return Promise.resolve(items);
        }

        if (element.itemType === "group") {
            const targetPath = element.groupPath || "Unknown Location";
            const queries = this.currentReport.queries.filter(q => {
                if (!q) {
                    return false;
                }
                const fullPath = q.location?.file || "Unknown Location";
                return process.platform === "win32"
                    ? fullPath.toLowerCase() === targetPath.toLowerCase()
                    : fullPath === targetPath;
            });

            const items = queries.map(
                q => new SqlQueryTreeItem(
                    typeof q.sql === "string" ? q.sql : "",
                    vscode.TreeItemCollapsibleState.None,
                    "query",
                    q
                )
            );
            return Promise.resolve(items);
        }

        return Promise.resolve([]);
    }
}
