# Lorestead.Client

A [Galdr.Native](https://www.nuget.org/packages/Galdr.Native) desktop app: Vue 3
(Composition API, JavaScript) + Tailwind CSS v4 + auto-imported Lucide icons,
with system/light/dark theming and file logging. Targets .NET 10 with AOT.

## Prerequisites

- .NET 10 SDK
- Node.js 20.19+ or 22.12+ (required by Vite 8)

## Run

**Debug (hot reload):**

```sh
cd FrontEnd
npm install
npm run dev
```

Then, in another shell:

```sh
dotnet run -c Debug
```

Debug serves the UI from the Vite dev server at `http://localhost:5174`. Opening
that URL in a plain browser (no C# host) installs a mock backend, so UI work
runs without the desktop shell.

**Release (one shot):**

```sh
dotnet run -c Release
```

The Release build compiles the front end and stages it into `wwwroot`, which the
app serves from disk. No separate front-end step is needed.

## Layout

```
Lorestead.Client.csproj      App project (net10.0, AOT, front-end build wired in)
Program.cs           GaldrBuilder composition root, error/exception logging hooks
Config.cs            Resolves the per-user app-data paths (database, logs)
Services/            C# services behind the invoke bridge (notes, boards, sync,
                     import/export, attachments, updates, logging)
FrontEnd/            Vue 3 + Vite + Tailwind front end
  src/
    components/      Shared components (editor, dialogs, tree, fields)
    composables/     Shared composition functions
    dev/             Mock backend for browser-only UI work
    services/        Thin wrappers over the galdrInvoke bridge (invoke.js)
    stores/          Pinia stores (notes, boards, settings, sync, updates)
    utils/           Helpers (formatting, toolbar, platform)
    views/           Route components (notes/, boards/, SettingsView.vue)
    router.js        vue-router (hash history)
    style.css        Tailwind + theme tokens (light/dark)
```

## Notes

- **Icons:** use any Lucide icon as a component on demand, e.g.
  `<i-lucide-sparkles />`. Add more `@iconify-json/*` collections to use other sets.
- **Theme:** theme and accent live in the application settings (database); a copy
  is cached in `localStorage` only for the first paint before the database loads.
- **Logging:** `FileLoggingService` writes to `logs/lorestead.log` in the per-user
  app-data directory; command and unhandled exceptions are logged automatically.
