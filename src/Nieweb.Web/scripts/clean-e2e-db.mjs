// Deletes leftover SQLite artifacts from a previous Playwright E2E run.
// Runs as part of `pretest:e2e`, before Playwright launches the webServer,
// so the API process can freshly seed the bootstrap admin.
import { existsSync, rmSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const apiDir = resolve(here, "..", "..", "Nieweb.Api");
const artifacts = [
    "nieweb-e2e.db",
    "nieweb-e2e.db-journal",
    "nieweb-e2e.db-wal",
    "nieweb-e2e.db-shm",
];

for (const name of artifacts) {
    const p = resolve(apiDir, name);
    if (existsSync(p)) {
        try {
            rmSync(p, { force: true });
            console.log(`[clean-e2e-db] removed ${p}`);
        } catch (err) {
            console.error(`[clean-e2e-db] failed to remove ${p}:`, err);
            process.exit(1);
        }
    }
}
