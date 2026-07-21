import { useEffect, useState } from "react";
import "./App.css";

// Minimal placeholder shell. F2 adds TanStack Router, Mantine, layout, etc.
// This just proves the toolchain end-to-end: React 19, Vite bundle, and a
// live call to /api/sources via the dev proxy.
type SourceDescriptor = {
    id: string;
    displayName: string;
    schemaVersion?: string;
};

export default function App() {
    const [sources, setSources] = useState<SourceDescriptor[] | null>(null);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        const controller = new AbortController();
        fetch("/api/sources", { signal: controller.signal })
            .then((r) => (r.ok ? r.json() : Promise.reject(new Error(`HTTP ${r.status}`))))
            .then((data: SourceDescriptor[]) => setSources(data))
            .catch((e: unknown) => {
                if (e instanceof DOMException && e.name === "AbortError") return;
                setError(e instanceof Error ? e.message : String(e));
            });
        return () => controller.abort();
    }, []);

    return (
        <main className="app-shell">
            <header>
                <h1>Nieweb</h1>
                <p className="subtitle">Phase 1 MVP - scaffold only (F1).</p>
            </header>
            <section>
                <h2>Configured AOI sources</h2>
                {error && (
                    <p className="error" role="alert">
                        Failed to load /api/sources: {error}
                    </p>
                )}
                {!error && sources === null && <p>Loading&hellip;</p>}
                {sources && sources.length === 0 && <p>No sources configured.</p>}
                {sources && sources.length > 0 && (
                    <ul>
                        {sources.map((s) => (
                            <li key={s.id}>
                                <strong>{s.displayName}</strong>{" "}
                                <span className="muted">
                                    ({s.id}
                                    {s.schemaVersion ? `, schema ${s.schemaVersion}` : ""})
                                </span>
                            </li>
                        ))}
                    </ul>
                )}
            </section>
        </main>
    );
}
