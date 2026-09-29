import { CliCommand } from "./command-args";

/** Commands that open a live database connection with the user's credential and need a modal confirmation. */
export type LiveDatabaseCommand = Extract<CliCommand, "snapshot" | "baseline" | "verify-shape">;

export function isLiveDatabaseCommand(command: CliCommand): command is LiveDatabaseCommand {
    return command === "snapshot" || command === "baseline" || command === "verify-shape";
}

const HOST_KEYS = ["server", "data source", "datasource", "host", "hostname", "address", "addr", "network address"];

/**
 * Extracts the host from an ADO.NET style (`Server=…;`), EZConnect (`host:port/service`), Oracle TNS
 * (`(HOST=…)`) or URI (`scheme://user:pw@host/db`) connection string and masks it for display.
 * Returns `undefined` when no host can be identified; never returns any other connection-string part.
 */
export function maskConnectionHost(connectionString: string | undefined): string | undefined {
    if (connectionString === undefined || connectionString.trim().length === 0) {
        return undefined;
    }
    const host = extractHost(connectionString.trim());
    return host === undefined ? undefined : maskHost(host);
}

function extractHost(value: string): string | undefined {
    const uri = /^[a-z][a-z0-9+.-]*:\/\/(?:[^@/\s]*@)?\[?([^\]/:?#\s]+)/i.exec(value);
    if (uri) {
        return uri[1];
    }
    for (const part of value.split(";")) {
        const separator = part.indexOf("=");
        if (separator < 0) {
            continue;
        }
        const key = part.slice(0, separator).trim().toLowerCase();
        if (!HOST_KEYS.includes(key)) {
            continue;
        }
        return hostFromDataSource(part.slice(separator + 1).trim());
    }
    return undefined;
}

function hostFromDataSource(raw: string): string | undefined {
    const tns = /\(\s*HOST\s*=\s*([^)\s]+)\s*\)/i.exec(raw);
    if (tns) {
        return tns[1];
    }
    // tcp:host,1433 | host\instance | host:1521/service | //host:1521/service
    const stripped = raw.replace(/^tcp:/i, "").replace(/^\/\//, "");
    const host = stripped.split(/[,\\:/]/)[0]?.trim();
    return host && host.length > 0 ? host : undefined;
}

/** `db-prod.corp.example.com` -> `db-***.com`; `10.20.30.40` -> `10.***.40`; `localhost` -> `loc***`. */
function maskHost(host: string): string {
    const labels = host.split(".");
    if (labels.length > 1 && labels.every((label) => /^\d+$/.test(label))) {
        return `${labels[0]}.***.${labels[labels.length - 1]}`;
    }
    const first = labels[0];
    const head = first.length > 3 ? `${first.slice(0, 3)}***` : `${first.slice(0, 1)}***`;
    return labels.length > 1 ? `${head}.${labels[labels.length - 1]}` : head;
}

/**
 * Counts the `read` queries in a CLI `summary.json` scan report. The report is external CLI data
 * that the host casts rather than validates, so every level is checked: a missing report or a
 * `queries` field that is not an array yields `undefined` (count unknown), never an exception.
 */
export function countReadQueries(report: unknown): number | undefined {
    if (!report || typeof report !== "object") {
        return undefined;
    }
    const queries = (report as { queries?: unknown }).queries;
    if (!Array.isArray(queries)) {
        return undefined;
    }
    return queries.filter((query: unknown) => {
        if (!query || typeof query !== "object") {
            return false;
        }
        const operation = (query as { operation?: unknown }).operation;
        return typeof operation === "string" && operation.toLowerCase() === "read";
    }).length;
}

export interface LiveDatabaseConfirmationInput {
    readonly command: LiveDatabaseCommand;
    /** Output of {@link maskConnectionHost}; `undefined` when no SecretStorage credential exists. */
    readonly maskedHost: string | undefined;
    /** Read queries found by the last `scan` in this session; `undefined` when no scan has run. */
    readonly readQueryCount?: number;
}

/** Builds the modal confirmation shown before any live-database command runs. */
export function buildLiveDatabaseConfirmation(input: LiveDatabaseConfirmationInput): string {
    const target = input.maskedHost !== undefined
        ? `host ${input.maskedHost} (your SecretStorage credential)`
        : "the database configured in .dataguard.yml or DATAGUARD_CONNECTION_STRING (no SecretStorage credential is stored)";
    switch (input.command) {
        case "snapshot":
            return `DataGuard will refresh the schema snapshot from ${target}. Continue?`;
        case "baseline":
            return `DataGuard will create a baseline using ${target}. Continue?`;
        case "verify-shape": {
            const count = input.readQueryCount;
            const literals = count !== undefined && Number.isInteger(count) && count >= 0
                ? `${count} read SQL quer${count === 1 ? "y" : "ies"} from the last scan`
                : "every read SQL query discovered in this workspace";
            return `DataGuard will send ${literals} to ${target} to describe their result shapes. Continue?`;
        }
    }
}
