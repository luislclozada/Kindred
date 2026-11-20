# Kindred frontend

Angular 21 (standalone, zoneless, OnPush, signals). No UI library, no state library.

```powershell
npm install
npm start        # http://localhost:11200, /api proxied to http://localhost:11000 (the gateway)
npm run build    # production build into dist/
```

## Pattern: component -> facade -> api service -> HttpClient

| Layer | Folder | Rule |
|---|---|---|
| API service | `src/app/core/api/*.api.ts` | Thin `HttpClient` wrapper, returns `Observable<Dto>`. No state. |
| Facade | `src/app/core/state/*.facade.ts` | Holds signals (data, loading, error) and exposes methods. |
| Page | `src/app/pages/<name>/<name>.page.{ts,html,scss}` | Talks to facades only, never to `HttpClient` or API services. |

## Add a page (e.g. "My groups")

1. Add a typed DTO in `core/models/api.models.ts` and one method in an API service.
2. Add `core/state/groups.facade.ts` (copy `classmates.facade.ts`).
3. Add `pages/groups/groups.page.*` and a lazy route in `app.routes.ts` (use `signedInGuard`).
4. Add one entry to `nav` in `layout/header/header.ts`.

## Styling

Tokens live in `src/styles/_tokens.scss` (light, dark via `prefers-color-scheme` or `[data-theme]`).
Components use tokens only. Shared classes (`.btn .card .badge .avatar .field .alert .skeleton`) are in
`src/styles/_primitives.scss`. Brand image slots: see `public/assets/brand/README.md`.
