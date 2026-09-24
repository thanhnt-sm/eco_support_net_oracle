export interface CancellableRun {
    cancelled: boolean;
    cancel(): void;
}

/** Owns the single process allowed to run across all VS Code workspaces. */
export class RunCoordinator<T extends CancellableRun> {
    private activeRun: T | undefined;
    private currentToken = 0;

    nextReservation(): number {
        this.cancel();
        return this.currentToken;
    }

    isReservationCurrent(token: number): boolean {
        return token === this.currentToken;
    }
    get current(): T | undefined {
        return this.activeRun;
    }

    replace(run: T): T | undefined {
        const previous = this.activeRun;
        if (previous) {
            previous.cancelled = true;
            try {
                previous.cancel();
            } catch {
                // Ignore cancel errors to maintain coordinator invariants
            }
        }
        this.activeRun = run;
        return previous;
    }

    clear(run: T): void {
        if (this.activeRun === run) {
            this.activeRun = undefined;
        }
    }

    cancel(): T | undefined {
        const run = this.activeRun;
        try {
            if (run) {
                run.cancelled = true;
                try {
                    run.cancel();
                } catch {
                    // Ignore cancel errors to preserve state invariants
                }
                this.activeRun = undefined;
            }
        } finally {
            this.currentToken++;
        }
        return run;
    }
}
