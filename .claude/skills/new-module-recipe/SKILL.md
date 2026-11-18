---
name: new-module-recipe
description: The recipe for adding a page or list/grid module to the Kindred frontend so it looks and behaves like the existing ones — file layout (api service → facade → page → route → nav), the four view states, the shared primitives and tokens, and the page-level conventions that give Kindred its look. Load BEFORE writing any new page markup in apps/frontend.
---

# New module recipe — Kindred frontend

Kindred's look is "polished brutalist": thick borders, hard offset shadows, big display type,
two accents (coral + sun yellow) on a warm paper/ink base. Almost all of it comes from
`src/styles/_tokens.scss` and `src/styles/_primitives.scss`. A new page that uses them
correctly looks right with very little page SCSS. A new page that re-implements them looks
wrong and breaks dark mode.

Reference implementations — open them before you write anything:

- List of cards: `apps/frontend/src/app/pages/courses/` (grid, actions per card, inline action error)
- Grouped list: `apps/frontend/src/app/pages/classmates/` (groups with a head rule, people cards, empty state with art)

## 1. The five files, in this order

All paths under `apps/frontend/src/app/`. Example: a "My groups" page.

| # | File | What goes in it |
|---|---|---|
| 1 | `core/models/api.models.ts` | The DTO interface, mirroring the API's JSON exactly (camelCase). One shared file — append, don't reorganize. |
| 2 | `core/api/<area>.api.ts` | One method per endpoint: a thin `HttpClient` call returning `Observable<Dto>`. Use `API_BASE` from `api.config.ts`. No state, no error handling. |
| 3 | `core/state/<name>.facade.ts` | `@Injectable({ providedIn: 'root' })`. Private writable signals (`_items`, `_loading`, `_error`), public `asReadonly()` views, `computed()` for derived data, and methods (`load()`, actions). Errors go through `toErrorMessage(err)`. Copy `classmates.facade.ts`. |
| 4 | `pages/<name>/<name>.page.{ts,html,scss}` | The routed component. Injects **facades only**. `ngOnInit` calls `facade.load()`. |
| 5 | `app.routes.ts` + `layout/header/header.ts` | One lazy route with `canActivate: [signedInGuard]` and a `title`; one entry in the header's `nav` array. |

The signed-in student comes from `SessionFacade` (`studentId()`, `student()`); inject it in the
facade, not in the page.

## 2. Component rules

```ts
@Component({
  selector: 'app-groups-page',
  imports: [RouterLink],                       // only what the template uses
  templateUrl: './groups.page.html',           // never inline template
  styleUrl: './groups.page.scss',              // never inline styles
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GroupsPage implements OnInit {
  protected readonly facade = inject(GroupsFacade);
  protected readonly skeletons = [0, 1, 2];
  ngOnInit(): void { this.facade.load(); }
}
```

Standalone (the default), OnPush, `inject()`, signals, `@if` / `@for` with `track`. No
`HttpClient`, no api service and no `subscribe()` in a page.

## 3. Page skeleton — always the same four states

```html
<div class="container">
  <div class="page-head">
    <h1 class="title">My groups</h1>
    <p class="lede">One sentence saying what this page is for.</p>
  </div>

  @if (facade.error(); as message) {
    <div class="alert" role="alert">
      <span>{{ message }}</span>
      <button type="button" class="btn btn--ghost btn--sm" (click)="facade.load()">Retry</button>
    </div>
  } @else if (facade.loading()) {
    <div class="grid-cards" aria-busy="true" aria-label="Loading groups">
      @for (n of skeletons; track n) { <div class="skeleton"></div> }
    </div>
  } @else if (facade.items().length === 0) {
    <div class="empty">
      <h2>No groups yet</h2>
      <p>Say what to do next, and offer the way there.</p>
      <a class="btn" routerLink="/courses">Manage courses</a>
    </div>
  } @else {
    <ul class="grid-cards">
      @for (item of facade.items(); track item.id) {
        <li class="card"> … </li>
      }
    </ul>
  }
</div>
```

Order is error → loading → empty → loaded. Never ship a page without all four.

## 4. Primitives — use these, never re-implement them

Defined in `src/styles/_primitives.scss`, available everywhere.

| Need | Class |
|---|---|
| Page width + gutters | `.container` |
| Title row (title left, meta right) | `.page-head`; the `h1` gets a page-local `.title { font-size: var(--text-hero); }` |
| Intro sentence | `.lede` |
| Small uppercase label | `.eyebrow` |
| Button | `.btn` (coral, primary) · `.btn--ghost` (secondary) · `.btn--sm` · `.btn--block` |
| Card | `.card` · `.card--interactive` when the whole card is a button/link |
| Count / status pill | `.badge` · `.badge--accent` (coral) · `.badge--sun` (yellow) |
| Person avatar | `<span class="avatar" [class]="'tint-' + tintOf(name)">{{ initialOf(name) }}</span>` (`core/util/tint.ts`) |
| Form field | `.field` wrapping a `label` + `input`; `.field__error` for the message; `aria-invalid` on the input |
| Error banner | `.alert` with `role="alert"` |
| Loading placeholder | `.skeleton` |
| Empty state | `.empty` (optionally with `<app-brand-image>` art) |
| Responsive card grid | `.grid-cards` |
| Screen-reader-only text | `.visually-hidden` |

If a control you need is not here, add it to `_primitives.scss` (it is a shared file — in a
multi-team change the orchestrator assigns it to one team) rather than styling it inside a page.

## 5. Page SCSS — layout and page-specific blocks only

```scss
:host { display: block; padding-bottom: var(--space-8); }
.title { font-size: var(--text-hero); }

.group {                                   // a page-specific block, BEM-ish
  display: grid;
  gap: var(--space-4);
  &__head { display: flex; align-items: center; gap: var(--space-4);
            padding-bottom: var(--space-3); border-bottom: var(--border); }
}
```

- **Tokens only.** Colours `var(--color-*)`, spacing `var(--space-1..8)`, type
  `var(--text-*)` + `var(--font-display|body)`, shape `var(--border)` `var(--radius-*)`
  `var(--shadow-hard|-sm|-lg)`. No hex, no `rgb()`, no pixel font sizes.
- **Never write a dark-mode rule.** Dark mode is the same tokens with different values.
  If something looks wrong in dark, you used a raw colour or the wrong token pair — text on
  `--color-accent` is `--color-on-accent`, on `--color-accent-2` it is `--color-on-accent-2`.
- Do not restyle `.btn`, `.card`, `.badge` from a page. Add a modifier in `_primitives.scss` if
  a real variant is needed.

## 6. Conventions that carry the look

- **Hierarchy by size and weight, not colour.** Page title in `--text-hero`, section titles
  `--text-xl`, both in the display font. Codes and labels are small, bold, uppercase, tracked
  (`letter-spacing: 0.08em`) — see `.course__code`.
- **State is a filled surface.** "On" things turn sun yellow (`--color-accent-2`) — the
  enrolled course card, the active nav link. Coral (`--color-accent`) is for primary actions
  and the badge that counts what matters.
- **Section heads have a thick rule under them** (`border-bottom: var(--border)`) with the
  code chip left, the title in the middle and a count badge right — see `.group__head`.
- **Motion is the button/card press**: lift on hover, drop on active. It is built into `.btn`
  and `.card--interactive`; add no other animation.
- **Numbers get a badge**, and the badge says what it counts ("3 shared", "6 enrolled").
- **Action errors stay on the page** as an `.alert` above the list with a Dismiss button, and
  show the API's own `message` (`toErrorMessage`). Buttons stay enabled so the server decides.
- **Copy is short and plain.** Sentence case. Empty states say what to do next.
- **Brand art** goes through `<app-brand-image src="assets/brand/<file>.png" alt="" ratio="4 / 3" />`,
  which shows a patterned placeholder until the file exists. Slots are listed in
  `public/assets/brand/README.md`.

## 7. Before you call it done

- Works at phone width (the grids collapse on their own; check long names do not overflow).
- Works in both themes (toggle in the header).
- Keyboard: every action reachable, focus visible.
- The page has no `HttpClient`, no raw colour, no inline template or styles.
