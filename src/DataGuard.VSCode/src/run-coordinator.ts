export interface CancellableRun {
    cancelled: boolean;
    cancel(): void;
}

/** Owns the single process allowed to run across all VS Code workspaces. */
export class RunCoordinator<T extends CancellableRun> {
    private activeRun: T | undefined;

    get current(): T | undefined {
        return this.activeRun;
    }

    replace(run: T): T | undefined {
        const previous = this.activeRun;
        if (previous) {
            previous.cancelled = true;
            previous.cancel();
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
        if (run) {
            run.cancelled = true;
            run.cancel();
            this.activeRun = undefined;
        }
        return run;
    }
}
