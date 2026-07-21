# Image pipeline design (Phase 2)

Status: **Draft — for review**. Sketch produced as part of the Phase 1
sprint so downstream Review / Analyse UI work can start against a
stable contract.

## 1. Scope

Nieweb needs to display AOI defect imagery and reference imagery next
to every panel / card / component / pad the user opens in the Review
and Analyse UIs. The legacy stack solves this three different ways:

| Producer              | Format                | Location                            | Consumer                                                |
| --------------------- | --------------------- | ----------------------------------- | ------------------------------------------------------- |
| VIT AOI Superviseur   | `.otr`                | `\\<aoi-host>\SupervisorImageStorage\...` | Sigmalink Review defect image widget (with pin overlay) |
| Sigmalink Review OIS  | `.ois`, plus `.png`   | `<ois_folder_path>` per `OISPlugin.xml` (see `sigmalink-review` skill §11) | Downstream MES / OIS re-import; some legacy reports     |
| Sigmalink iCAD import | `.ois`, `.vbi`, `.otr`, `.png`, `.bmp`, `.jpg` | Reference-image bank on the AOI recipe volume | Review "Reference image" widget                         |
| Sigmalink CAD editor  | SVG / raster tiles    | iCAD project directory                | CAD Editor canvas (JavaFX in legacy, WebGL/Canvas in Nieweb) |

Nieweb takes over the **display and caching** of these assets; the
**production of `.otr` / `.ois` remains with the AOI Superviseur and
Sigmalink Review pipelines** — Nieweb never generates these files.

Out of scope for this document (own future docs):

- CAD Editor rewrite (Phase 3).
- OIS **export** (the outbound plug-in that ships images to a MES). If
  we need it we can port the Sigmalink `OISPlugin` semantics later.

## 2. File-format reference

### 2.1 `.otr` — VIT AOI region-of-interest snapshot

Small proprietary container holding one greyscale (or RGB, depending
on the AOI head) crop of the panel around a single defect, plus the
absolute pixel coordinates of the pin / component and the anomaly
mask. The Superviseur writes one `.otr` per defect at inspection
time and the path is derivable from `PANEL_ID` +
`TESTED_OBJECT.OBJECT_TYPE_ID` (documented in the
`sigmalink-review` skill under "OIS export path templates").

Nieweb responsibility: decode → SkiaSharp `SKBitmap` → transcode to
PNG (lossless) or WebP (lossy for thumbnails); overlay the pin/error
mask with the same colour convention Sigmalink Review uses so
operators aren't retrained.

Because the format is not publicly documented, the decoder lives in a
single adapter class `Nieweb.Imaging.Otr.OtrDecoder` and is written
against test fixtures captured from the pre-reflow and post-reflow
lines. We keep the fixtures under `tests/fixtures/otr/` — small
crops with the customer's approval and scrubbed of any part numbers.

### 2.2 `.ois` — Sigmalink Review OIS export

Reviewer-annotated variant of `.otr`: same crop, plus the review
verdict, comment, and any custom-message code. Written by the OIS
plug-in when the reviewer sanctions or repairs a defect.

Nieweb responsibility: same as `.otr`, plus surface the review
metadata (verdict, comment, message code) in the same JSON payload
so the UI can render "reviewed by X on Y, verdict Z" without a
second round trip.

### 2.3 Reference-image bank

Plain images (`png`, `bmp`, `jpg`, `jpeg`, `ois`, `vbi`, `otr`)
named per the Sigmalink convention:

    Preview_<component-side>_H###mm_W###mm_###um.<ext>

where `H###mm` / `W###mm` is the bounding-box in millimetres and
`###um` is the pixel pitch. Filenames are load-bearing — historical
joins from the AOI DB rely on the exact naming — so Nieweb does
**not** rename them (documented in the `sigmalink-cad-import` skill
§12 and enforced in the `sigmalink-review` skill §7).

Nieweb responsibility: look up by
`(component_bounding_box, pixel_pitch, side)` and serve.

## 3. Pipeline

```mermaid
flowchart LR
    subgraph AOI[On the AOI machine]
        OTR[".otr / .ois / preview\n(SMB share, read-only)"]
    end

    subgraph Nieweb[Nieweb.Api]
        A[Image request\nGET /images/panel/{id}/defect/{tid}]
        B[ImageRouter]
        C{cache hit?}
        D[SmbImageSource]
        E[OtrDecoder / OisDecoder / PreviewLoader]
        F[Rasterizer\n(SkiaSharp, overlay)]
        G[BlobCache\n(local FS, SHA-256 keyed)]
        H[HTTP response\nContent-Type: image/png\nETag: <sha256>]
    end

    AOI --> D
    A --> B --> C
    C -- yes --> G --> H
    C -- no  --> D --> E --> F --> G --> H
```

Design intent:

1. **All paths derive from DB rows.** The client asks for a defect by
   `(panel_id, tested_object_id)` or a reference image by
   `(product_id, side, w_mm, h_mm, um)`. The router queries the
   appropriate AOI Superviseur DB (via the DB1 read-only adapter) to
   resolve the on-disk path. **No client-supplied file paths** — that
   prevents directory-traversal attacks and hides the SMB share layout.
2. **SMB access is read-only.** `Nieweb.Imaging.SmbImageSource` opens
   the share with a service account that has `List / Read` NTFS
   permissions and nothing else. No write, no delete.
3. **Cache is filesystem-backed and hash-keyed.**
   `Nieweb.Imaging.BlobCache` writes to
   `%NIEWEB_IMAGE_CACHE%\<first-2-of-hash>\<hash>.<ext>` with a TTL
   sweeper. Hash inputs = source file bytes + rasterizer version +
   overlay parameters, so a rasterizer bug fix invalidates the cache.
4. **ETag = the cache hash.** Browsers get 304 Not Modified after the
   first request; there is no auth-side cost to a re-render.
5. **Access control.** The images endpoint is `[Authorize]` and the
   permission check runs against the same product / line ACL that
   gates the Report / Review views.

## 4. .NET layout

New project: `Nieweb.Imaging` (net10.0, class library).

```
src/Nieweb.Imaging/
├── ImageRouter.cs             // resolves DB row -> file path
├── ImageRequest.cs / .Result  // request DTOs
├── Sources/
│   ├── IImageSource.cs
│   ├── SmbImageSource.cs      // \\host\share, credentials from config
│   └── LocalImageSource.cs    // dev / test fallback
├── Decoders/
│   ├── OtrDecoder.cs
│   ├── OisDecoder.cs
│   └── PreviewLoader.cs
├── Rasterization/
│   ├── Rasterizer.cs          // SkiaSharp entry point
│   ├── DefectOverlay.cs       // pin / mask overlay
│   └── ThumbnailPipeline.cs
├── Cache/
│   ├── IBlobCache.cs
│   └── FileBlobCache.cs       // SHA-256 keyed on FS
└── DependencyInjection/
    └── ImagingServiceCollectionExtensions.cs
```

Wired into `Nieweb.Api` with a new endpoint group
`Endpoints/ImageEndpoints.cs`:

- `GET /images/panel/{panelId:int}/defect/{testedObjectId:int}` —
  main defect image with overlay.
- `GET /images/panel/{panelId:int}/defect/{testedObjectId:int}/thumb` —
  thumbnail (256 px longest edge).
- `GET /images/reference?productId=&side=&wMm=&hMm=&um=` — reference
  image bank lookup.

All three are `[Authorize]`, cache-headered, and rate-limited.

## 5. Packages

| Purpose                | Package                                    | Notes                                    |
| ---------------------- | ------------------------------------------ | ---------------------------------------- |
| Raster + overlays      | `SkiaSharp` (latest 10.x)                  | Cross-platform, GPU optional.            |
| SMB / UNC I/O          | `System.IO` + platform SMB                 | No third-party — use `File.OpenRead`.    |
| Hashing                | `System.Security.Cryptography.SHA256`      | Built-in.                                |
| Rate limiting          | `Microsoft.AspNetCore.RateLimiting`        | Built-in in .NET 7+.                     |
| Optional WebP encoder  | `SkiaSharp` (built-in `SKEncodedImageFormat.Webp`) | Thumbnails only.                 |

## 6. Configuration

`appsettings.json` skeleton (values via env vars in prod):

```json
"Nieweb": {
  "Imaging": {
    "Cache": {
      "Root": "C:/nieweb/image-cache",
      "MaxSizeBytes": 21474836480,
      "TtlDays": 30
    },
    "Sources": {
      "Postreflow": {
        "OtrRoot": "\\\\hly-aoi-2\\SupervisorImageStorage",
        "PreviewRoot": "\\\\hly-aoi-2\\Recipes\\ReferenceImages"
      },
      "Prereflow": {
        "OtrRoot": "\\\\hly-aoi-1\\SupervisorImageStorage",
        "PreviewRoot": "\\\\hly-aoi-1\\Recipes\\ReferenceImages"
      }
    }
  }
}
```

Credentials for the SMB share live in `.env` and are consumed by
Windows service-account impersonation, never passed on the wire.

## 7. Threat model — quick pass

| Threat                                     | Mitigation                                                                       |
| ------------------------------------------ | -------------------------------------------------------------------------------- |
| Directory traversal via user-supplied path | Client never supplies a path; router derives it from DB primary keys.            |
| Cache poisoning                             | Hash inputs include rasterizer version + overlay params; cache is per-source.    |
| Auth bypass                                 | Same JWT bearer pipeline as `/auth/whoami` (A1). No anonymous image endpoints.   |
| Read amplification / DoS                    | ASP.NET Core rate limiter (per user + per IP) in front of image endpoints.       |
| SMB creds leaking                           | Credentials from `.env` only; never logged; only granted to the app pool user.   |
| PII in filenames                            | Reference-image bank names contain no PII; audit new sources before onboarding.  |

## 8. Test plan

- **Unit:** `OtrDecoder` / `OisDecoder` against golden fixtures.
- **Unit:** `Rasterizer` overlay pixel diff within tolerance vs golden.
- **Unit:** `FileBlobCache` — key derivation stability, TTL sweep.
- **Integration:** `ImageEndpoints` via `WebApplicationFactory` with a
  `LocalImageSource` pointed at `tests/fixtures/`.
- **Load:** measure P99 latency on cache hit (target < 20 ms) and
  cache miss (target < 250 ms for a 4 MP crop with overlay).

## 9. Rollout

1. **Phase 2a — Read path.** Ship the router, `SmbImageSource`,
   `OtrDecoder`, `Rasterizer`, `FileBlobCache`, and the three GET
   endpoints against post-reflow only. Front-end Review UI consumes
   them.
2. **Phase 2b — `.ois` review metadata.** Add `OisDecoder` and extend
   the response to include the reviewer verdict / comment payload.
3. **Phase 2c — Pre-reflow source.** Register the pre-reflow SMB
   source and expand the router to pick the correct source from the
   AOI capability flags (`Capabilities.PastePrintMetrics` implies
   pre-reflow paths).
4. **Phase 2d — Reference images.** Add `PreviewLoader` and the
   `/images/reference` endpoint. Cache aggressively — the reference
   bank is nearly static.

## 10. Open questions

- **Colour space.** Are the `.otr` crops always 8-bit greyscale, or
  does the CR5 head emit 12-bit? Needs a fixture sweep.
- **Overlay parity.** Do we need to match the pixel-for-pixel overlay
  colour Sigmalink Review uses, or is "same intent" enough for MVP?
  (Line engineers will tell us fast if they don't match.)
- **Cache location.** Local FS is simplest but a shared cache would
  cut warm-up time for a second Nieweb node. Defer until we actually
  scale out.
- **WebP vs PNG.** Thumbnails clearly want WebP; the main defect view
  is small enough that PNG is fine — confirm during Phase 2a review.
