# Progress / Kemajuan

Made by Gravicode Studios, led by Kang Fadhil · Dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil

## 2026-10-25

### Native
- Project Chrono 10.0.0 (core + vehicle, Bullet, Eigen 3.4.1) built from source with MSVC 19.51 / Ninja and installed to `external/chrono-install`.
  - Vehicle-models library skipped; 6 models compiled into the shim (≈280 fewer files).
  - Build needs ≤4 parallel jobs on 16 GB RAM (single TUs reach ~1.8 GB); first build ≈2.5 h on a 4-core laptop.
  - Fixed: paths passed to CMake must use forward slashes (they are baked into `ChronoConfig.cmake`).
  - Fixed: the shim finds the core package and links `Chrono::Chrono_vehicle` directly (the `Vehicle` component would require the skipped models library).
- C ABI shim (`native/`): written against the 10.0 headers; syntax-checked (`cl /Zs`). API mismatches fixed: `ChVisualShape` base, `ChMesh::GetNode` → `ChNodeBase`, `RigidTerrain` has no `GetSystem`.

### Managed
- `Chrono.Net`, `Chrono.Net.Rendering`, `Chrono.Net.Samples` (16 samples), `Chrono.Net.Avalonia` (+ shared theme), `Chrono.Net.Blazor` (three.js r160 bundled) — all build.
- Apps: Avalonia Gallery, Blazor Gallery (static assets verified over HTTP), Simulator (3 example scenes), CLI `chrono`, headless screenshot tool, `dotnet new` templates — all build.
- Tests: 18 total — 6 math tests pass; 12 physics tests skip until the native DLL is present.

### Docs & assets
- README (EN) + README.id.md, docs/en + docs/id (7 pages each), CLAUDE.md updated with commands and architecture.
- Icon generated with the Rodin MCP server (double pendulum + trajectory arc), exported to PNG/ICO.
- Fonts: Barlow Condensed, JetBrains Mono (OFL).

### Verification (native build complete)
- Shim linked; all 18 tests pass (free fall checked against the exact semi-implicit Euler solution, pendulum period, spring frequency, cantilever FL³/3EI, state restore, motors, delegates, JSON scenes).
- Every sample runs headless via the CLI. Fixes driven by the real engine:
  - Dominoes / wrecking ball: bodies spawned in contact were popped up by penetration recovery → spawn gaps + `MaxPenetrationRecoverySpeed = 0.05`; dominoes get an explicit push.
  - Galton board rebuilt: hopper throat was narrower than a ball, open lattice diagonals let balls fly to the walls, bins sat under the exit gaps. Now wall-to-wall staggered pegs sized to close the diagonals, bins under the last row, slick glass/hopper → single bell-shaped pile (σ ≈ 0.6–0.7 m; Bullet broadphase order makes runs vary slightly).
  - Robot arm motor laws start at 0 (motors are relative to the start pose).
- Performance (4-core laptop): wheeled vehicles ≈2× real time, dominoes ≈0.55×, wrecking ball ≈0.33×, Galton ≈0.36×, mixer ≈0.1–0.3×, **M113 tracked ≈0.03× (heavy sample)**.
- Avalonia: headless screenshot hang fixed (DispatcherTimer auto-start, bitmap lock moved out of `Render`); plots use a robust range so start-up impulses do not flatten the signal.
- Blazor (checked in headless Edge over CDP): fonts/icon were served empty (linked under `wwwroot` → moved to `shared-assets`), `Float32Array` crashed on unaligned Blazor byte views, frame push now drops frames while the previous one is in flight so a slow browser no longer throttles the physics.
- CLI `--help` crashed on `[options]` parsed as markup → plain text cells.
- Packages: `build/pack.ps1` produces 7 packages; templates installed from the package — console runs, Avalonia and Blazor build (Blazor template was missing `Components.Routing` import).

### Release
- Moved to `DotNetVibeCoderz/Vibe_Physics` (folder `Chrono.Net/`), license MIT + `THIRD-PARTY-NOTICES.md` (Chrono BSD-3, Eigen MPL-2.0, three.js MIT, fonts OFL).
- `.github/workflows/chrono-net.yml`: builds Chrono (cached), tests, packs; publishes with the `NUGET_API_KEY` secret when a GitHub Release is published for a `chrono-net-v<version>` tag, or on a manual run with `publish: true`.

### ChronoAppGen (Chrono.Net App Generator)
- New tool `tools/Chrono.Net.AppGen`: Avalonia IDE (explorer, tabbed AvaloniaEdit editor with TextMate highlighting, find/replace, go to line, Roslyn/XML/JSON formatting, line numbers toggle, logs Output/Build/Jack, status bar, menu + toolbar: New/Open/Close project, Build, Run/Stop, Deploy, Exit) and **Jack — The Code Bender** in a resizable, hideable chat panel (model picker, image attachments, Ctrl+Enter, stop, clear thread, markdown with copy/insert code blocks, tool-call work trace).
- Jack = Semantic Kernel kernel functions + each provider's `IChatClient`: OpenAI, Azure OpenAI (v1 endpoint), Claude (Anthropic SDK; workspace header), Gemini and Ollama (OpenAI-compatible), DeepSeek. Plugins: workspace files, build/self-test/launch/NuGet/dotnet, Chrono guide + reflected API + samples + templates, Tavily search, web scraping, math/units, date/time. Reasoning models that reject `temperature` are retried without it.
- All settings in `app.config` (editable in Settings; layout, active model and recent projects persist).
- 25 templates (2 blank + 23 across simulator, science, military, entertainment, work, industry, education, disaster & safety, tools); `--verify-templates` generates, builds and self-tests all of them — all pass. Physics checked: projectile vacuum landing 22.91 m vs 22.94 m theory, elastic/inelastic carts conserve momentum (160 kg·m/s) and lose exactly the expected energy, crane tension = load weight, conveyor diverts all red cartons.
- Tested with real LLMs (keys from the local test file, written only to the git-ignored build output config): DeepSeek built a chaos double-pendulum app end to end (create, write, build, self-test, energy check); Claude and Azure gpt-5-mini used time/math/template tools; DeepSeek + Tavily returned sourced material data; in the IDE window (headless), DeepSeek and Claude edited, built and self-tested template projects.
- Finding to investigate: NSC friction from motor-driven spinning cylinders barely transports boxes (roller conveyor) — the template now uses a friction-limited belt-drive force instead.

### Next
1. First CI run (cold Chrono build ≈2 h on a hosted runner, cached afterwards); then publish a GitHub Release for tag `chrono-net-v0.1.0` to push the packages.
2. Templates pin `Chrono.Net` 0.1.0 — bump them together with the package version.
3. Investigate roller/cylinder friction transport in the shim (see above).
4. Phase 2 items in PLAN.md.
