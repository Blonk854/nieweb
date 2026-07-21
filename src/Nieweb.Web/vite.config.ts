/// <reference types="vitest/config" />
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";

// Nieweb SPA is served from the ASP.NET Core host under /app. Vite must
// therefore emit assets with a /app/ base so the built index.html
// references /app/assets/*.js instead of /assets/*.js.
//
// The build output is written directly into ../Nieweb.Api/wwwroot/app/
// so that `dotnet publish` on Nieweb.Api sweeps up the SPA together
// with the API. Nieweb.Api.csproj runs `npm ci && npm run build`
// before publish so the folder is always fresh.
const here = dirname(fileURLToPath(import.meta.url));

// https://vitejs.dev/config/
export default defineConfig({
    plugins: [react()],
    base: "/app/",
    build: {
        outDir: resolve(here, "..", "Nieweb.Api", "wwwroot", "app"),
        emptyOutDir: true,
        sourcemap: true,
    },
    server: {
        port: 5173,
        strictPort: true,
        // In dev, forward /api/* to the Kestrel host so the SPA can hit
        // Nieweb.Api without dealing with CORS or reverse-proxy setup.
        // The API's default dev port is 5000; override with VITE_API_URL
        // if you're running Kestrel on a different port.
        proxy: {
            "/api": {
                target: process.env.VITE_API_URL ?? "http://localhost:5000",
                changeOrigin: true,
                secure: false,
            },
        },
    },
    test: {
        environment: "jsdom",
        globals: true,
        setupFiles: ["./src/setupTests.ts"],
        css: false,
        // Vitest owns *.test.ts(x) under src/; Playwright owns e2e/.
        // Keeping the two suites in the same package needs an explicit
        // exclude so vitest doesn't try to interpret Playwright specs.
        include: ["src/**/*.{test,spec}.{ts,tsx}"],
        exclude: [
            "node_modules/**",
            "dist/**",
            "e2e/**",
            "playwright-report/**",
            "test-results/**",
        ],
    },
});
