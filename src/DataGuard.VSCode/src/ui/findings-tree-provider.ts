import * as path from "path";
import * as vscode from "vscode";
import { FindingItem, FindingSeverity } from "./redaction";

export type GroupingMode = "severity" | "rule" | "file";

export class FindingTreeItem extends vscode.TreeItem {
    constructor(
        public readonly label: string,
        public readonly collapsibleState: vscode.TreeItemCollapsibleState,
        public readonly finding?: FindingItem,
        public readonly groupType?: "root" | "group",
        public readonly count?: number
    ) {
        super(label, collapsibleState);

        if (finding) {
            this.id = finding.id;
            this.description = `Line ${finding.startLine}:${finding.startColumn}`;
            this.tooltip = new vscode.MarkdownString(
                `### [${finding.ruleId}] ${finding.severity.toUpperCase()}\n\n` +
                `**Message**: ${finding.message}\n\n` +
                `**File**: \`${finding.filePath}:${finding.startLine}\`\n\n` +
                (finding.quickFixAvailable ? `⚡ **Quick-Fix**: ${finding.quickFixTitle}` : "")
            );

            this.contextValue = finding.quickFixAvailable ? "findingWithQuickFix" : "finding";

            // Visual icon matching severity
            switch (finding.severity) {
                case "error":
                    this.iconPath = new vscode.ThemeIcon("error", new vscode.ThemeColor("errorForeground"));
                    break;
                case "warning":
                    this.iconPath = new vscode.ThemeIcon("warning", new vscode.ThemeColor("editorWarning.foreground"));
                    break;
                case "information":
                default:
                    this.iconPath = new vscode.ThemeIcon("info", new vscode.ThemeColor("editorInfo.foreground"));
                    break;
            }

            // Command to jump directly to code in the editor
            this.command = {
                command: "vscode.open",
                title: "Jump to Diagnostic",
                arguments: [
                    vscode.Uri.file(finding.filePath),
                    {
                        selection: new vscode.Range(
                            Math.max(0, finding.startLine - 1),
                            Math.max(0, finding.startColumn - 1),
                            Math.max(0, finding.endLine - 1),
                            Math.max(0, finding.endColumn - 1)
                        ),
                        preview: true
                    }
                ]
            };
        } else if (groupType === "group") {
            this.description = count !== undefined ? `(${count})` : undefined;
            this.iconPath = new vscode.ThemeIcon("folder");
        }
    }
}

export class DataGuardFindingsTreeProvider implements vscode.TreeDataProvider<FindingTreeItem> {
    private _onDidChangeTreeData = new vscode.EventEmitter<FindingTreeItem | undefined | null | void>();
    readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

    private allFindings: FindingItem[] = [];
    private filteredFindings: FindingItem[] = [];
    private groupingMode: GroupingMode = "severity";
    private filterQuery = "";
    private filterSeverity?: FindingSeverity;

    constructor() {}

    public setFindings(findings: FindingItem[]): void {
        this.allFindings = [...findings];
        this.applyFilter();
    }

    public clear(): void {
        this.allFindings = [];
        this.filteredFindings = [];
        this._onDidChangeTreeData.fire();
    }

    public getFindings(): FindingItem[] {
        return [...this.filteredFindings];
    }

    public getAllFindingsCount(): number {
        return this.allFindings.length;
    }

    public setGroupingMode(mode: GroupingMode): void {
        this.groupingMode = mode;
        this._onDidChangeTreeData.fire();
    }

    public setFilter(query = "", severity?: FindingSeverity): void {
        this.filterQuery = query.toLowerCase().trim();
        this.filterSeverity = severity;
        this.applyFilter();
    }

    private applyFilter(): void {
        this.filteredFindings = this.allFindings.filter((finding) => {
            if (this.filterSeverity && finding.severity !== this.filterSeverity) {
                return false;
            }
            if (this.filterQuery) {
                const query = this.filterQuery;
                const matchesRule = finding.ruleId.toLowerCase().includes(query);
                const matchesMsg = finding.message.toLowerCase().includes(query);
                const matchesPath = finding.filePath.toLowerCase().includes(query);
                return matchesRule || matchesMsg || matchesPath;
            }
            return true;
        });

        this._onDidChangeTreeData.fire();
    }

    getTreeItem(element: FindingTreeItem): vscode.TreeItem {
        return element;
    }

    getChildren(element?: FindingTreeItem): Thenable<FindingTreeItem[]> {
        if (!element) {
            // Root level: return groups
            if (this.filteredFindings.length === 0) {
                return Promise.resolve([]);
            }
            return Promise.resolve(this.getRootGroups());
        }

        // Child level: return items belonging to this group
        const groupKey = element.label;
        const items = this.getGroupItems(groupKey);
        return Promise.resolve(
            items.map((item) => new FindingTreeItem(
                `[${item.ruleId}] ${item.message}`,
                vscode.TreeItemCollapsibleState.None,
                item
            ))
        );
    }

    private getRootGroups(): FindingTreeItem[] {
        const groups = new Map<string, number>();

        for (const finding of this.filteredFindings) {
            let key: string;
            switch (this.groupingMode) {
                case "rule":
                    key = finding.ruleId;
                    break;
                case "file":
                    key = path.basename(finding.filePath);
                    break;
                case "severity":
                default:
                    key = finding.severity.toUpperCase();
                    break;
            }
            groups.set(key, (groups.get(key) ?? 0) + 1);
        }

        const result: FindingTreeItem[] = [];
        for (const [key, count] of groups.entries()) {
            result.push(new FindingTreeItem(
                key,
                vscode.TreeItemCollapsibleState.Expanded,
                undefined,
                "group",
                count
            ));
        }

        return result;
    }

    private getGroupItems(groupKey: string): FindingItem[] {
        return this.filteredFindings.filter((finding) => {
            switch (this.groupingMode) {
                case "rule":
                    return finding.ruleId === groupKey;
                case "file":
                    return path.basename(finding.filePath) === groupKey;
                case "severity":
                default:
                    return finding.severity.toUpperCase() === groupKey;
            }
        });
    }
}
