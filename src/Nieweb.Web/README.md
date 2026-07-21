# Nieweb.Web

React 19 + TypeScript + Vite SPA. Scaffolded by Phase 1 backlog item **F1**.
Later frontend items (F2-F9) layer TanStack Router, Mantine v7, react-i18next,
ECharts, filters, KPI cards, saved views, and print CSS on top of this shell.

## Prerequisites

- Node.js >= 22 (LTS). Verify with `node --version`.
- The API host (`Nieweb.Api`) if you want live `/api/sources` responses in
  dev; otherwise the SPA just shows the "Failed to load" fallback.

## Scripts

```pwsh
npm ci            # first-time install (uses package-lock.json)
npm run dev       # Vite dev server on http://localhost:5173, proxying /api -> Kestrel
npm run build     # type-check + build into ../Nieweb.Api/wwwroot/app/
npm run preview   # serve the built bundle for a smoke check
npm run lint      # ESLint 10 flat config
npm run test      # Vitest, one-shot
npm run test:watch
```

## Build layout

Vite is configured (`vite.config.ts`) with:

- `base: "/app/"` so all asset URLs in the built `index.html` resolve under
  the ASP.NET Core host's `/app` prefix.
- `build.outDir` pointing at `../Nieweb.Api/wwwroot/app/`. The API's csproj
  runs `npm ci && npm run build` before publish so the SPA always ships with
  the API artifact.

## Dev proxy

`vite.config.ts` proxies `/api/*` to `http://localhost:5000` by default (the
Kestrel dev port). Override with `VITE_API_URL=http://localhost:5001 npm run dev`
if you started the API on a different port.
