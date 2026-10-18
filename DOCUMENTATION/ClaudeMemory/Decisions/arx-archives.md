# Decision — an app ships as one encrypted, signed `.arx` archive built by `_Build arx`

**Date:** 2026-10-16
**Status:** LANDED (2026-10-16) for Thorium. Compression, pre-decoded textures, cooked meshes and source generation are unbuilt. Extended 2026-10-17: one self-contained, RID-specific publish per `<Target>`; AuroraEngine and Thorium multi-target.
**Scope:** `ArctisAurora.Core.Filing.Arx` — `ArxKind`, `ArxEntryFlags`, `ArxLayout`, `ArxWriter`, `ArxArchive`, `ArxStrings`, `ArxXml`, `ArxKeys`, `ArxMount`, `AppBuilder`, `BuildDefinition`, `BuildSource`, `BuildPattern`, `BuildTarget`; `ArctisAurora.Core.Filing.Serialization` — `DataFiles`, `Paths`, `MeshImporter`; `ArctisAurora.Core.Registry` — `XSDGenerator`; `ArctisAurora.Core.Rendering` — `GraphicsPipeline`, `RenderingModule`, `Pathtracing`, `RadianceCascades2D`; `_Build.Program`

## What changed
- `_Build arx <App>.build.axml [rid]` publishes the app in Release, self-contained, once per declared `<Target>` into `<Output>/<rid>/` (a rid argument builds only that target). It packs engine Data + app Data + the app's `.spv` shaders once into one `<App>.arx`, written beside the first target's exe and copied byte-identical beside every later target's. Each target folder carries only its own OS's natives. Files matched by `<Loose>` rules (e.g. UI XML) are copied as plain files to `Data/…` beside the exe instead.
- A Release exe that finds `*.arx` beside itself mounts it in `Paths.Mount`, before anything reads Data (including `Bootstrapper.Load`), and reads every Data file, texture, mesh and shader through it. Debug, and Release without an archive, behave exactly as before.
- `ArxFormat.cs`: `ArxKind` {Blob, Xml}; `ArxEntryFlags` {None, Compressed (reserved, never set in v1)}; internal `ArxLayout` (magic `ARX\0`, version 1, 24-byte header, 16-byte payload alignment); `ArxWriter` (`AddBlob`, `AddXml`, `Write(file, ArxKeys)`, `Count`, static `Normalize`); `ArxArchive` (`Open(file, ArxKeys)` maps the file, checks the ECDsa signature, decrypts the table of contents; `Contains`, `IsXml`, `ReadBytes`, `ReadXml`, `Paths`, `file`, `signatureSize = 64`).
- File order: header (magic u32, version u16, flags u16, tocOffset i64, tocLength i32, signatureLength i32) | payloads from offset 32, each `Cipher` blob 16-aligned | encrypted table of contents (string table, then per entry: path id, kind, flags, offset, stored length, SHA-256 of stored bytes) | 64-byte P1363 signature over header + encrypted table. Open requires tocOffset + tocLength + 64 == file size.
- `ArxXml.cs`: `ArxStrings` (interned string table); `ArxXml.Encode/Decode` — the XML tree as records of string ids (element ns+name, attributes ns+name+value, children: element / text / CDATA). Comments and processing instructions are dropped. An element written `<a></a>` stays non-empty.
- `ArxKeys`: {key, signingKey, publicKey}; `LoadOrCreate(app)` reads or creates `%APPDATA%\Arktis\BuildKeys\<App>.keys.axml` (AES-256 key, ECDsa P-256 PKCS#8 private key, SPKI public key, base64); `FromEntryAssembly()` reads `[AssemblyMetadata("ArxKey")]` and `("ArxPublicKey")` from the running exe.
- `ArxMount : IDataMount`. An entry's full path is `<archive file>\<logical path>`, so the archive acts like a folder. `TryLocate(fullPath, out archive, out entry)` is a static lookup over every `ArxMount` ever constructed.
- `AppBuilder.Run(buildFile, rid = null)`: rid null builds every target, a rid builds only that one; no `<Target>`, or none matching rid, logs an error and returns false. Packs Data/shaders once, then per target: `dotnet publish -c Release -f <Framework> -r <Rid> --self-contained -o <Output>/<Rid>`, deletes the publish's `Data/` and `Shaders/`, copies the loose files, writes the `.arx` (first target) or copies the first target's `.arx` (later targets). `Publish(project, output, BuildTarget target, keys)`. One log line per target: `[Build] <App> <rid>: N entries packed into …\<rid>\<App>.arx, M loose files beside it.`
- XSD types in the `Build` category: `BuildDefinition` ("Build": App, Project, Output, Data*, Shaders*, Loose*, Exclude*, Target*; `targets` is a `List<BuildTarget>`, element `<Target>`), `BuildSource` (Source), `BuildPattern` (Include), `BuildTarget` (`Rid` → `rid`, `Framework` → `framework`). Generated schema `BuildTypeSchema.xsd`, namespace `http://arctisaurora/AuroraBuildTypes`.
- `DataFiles` (new): `Exists`, `Open`, `LoadXml` (each takes a disk path or an archive-entry path), `ReadShader(fileName)` (strips the leading `../`s, reads `Shaders/…` from the archive when it holds it, else from disk relative to `AppContext.BaseDirectory` as before).
- `Paths.packed` (new static bool): true once every archive beside the exe mounted; false in Debug and in Release without one. `Paths.Mount` in Release mounts the loose `Data/` folder first, then every `*.arx` beside the exe; a failed check logs `FATAL [Arx] cannot mount the app's archives` and exits 1.
- `XSDGenerator.GenerateXSD` returns immediately when `Paths.packed`. `AssetImporter.RunImports` already returned early in Release — unchanged.
- The four shader readers (`GraphicsPipeline.ReadFile`, `RenderingModule.ReadFile`, `Pathtracing.ReadFile`, `RadianceCascades2D.LoadShader`) call `DataFiles.ReadShader`; the 17 `"../../../Shaders/…"` call sites are unchanged.
- `MeshImporter.ImportFBX` reads through `DataFiles.Open` + Assimp `ImportFileFromStream` (format hint = file extension).
- 35 read sites in 26 files moved from `XElement.Load` / `XDocument.Load(..).Root` / `File.Exists` / `Image.Load(path)` / `new FileStream` to `DataFiles.*`: AnimationLibrary, Bootstrapper, DataManager, MathConstants, InputHandler, AssetRegistries, SamplerAsset, Context, EntityRegistry, SettingsRegistry, Shutdown, TestRunner (suites + goldens), TexHyphenator, FrameScheduler, ControlXml, Effects, Gradients, Palettes, PlannerAttachments, PdfExport, FontAsset, TextureAsset, AGlfwWindow (app icons), `Serializer.DeserializeAttributed` (private helper now takes `Stream`), `AuroraFont.ReadFaceGlyphs`, PdfType3Font. Import-only, vault, note and results reads stay on the disk APIs.
- `_Build/Program.cs`: verb `arx <file.build.axml> [rid]`. `Thorium.csproj`: `AssemblyMetadata` `ArxKey` and `ArxPublicKey`. `.gitignore`: `Ship/`.
- Data files: `Thorium.build.axml` (App=Thorium, Output=../Ship/Thorium, Data=Data then ../AuroraEngine/Data, Shaders=Shaders, Loose XML/Documents/UI/**); suite `Arx.tests.axml` (XmlRoundTrip, BlobRoundTrip, MountResolves, TamperedEntryRefused, TamperedTocRefused, WrongKeyRefused) with tests in `ArxTests`.
- `Thorium.build.axml` gains `<Target Rid="win-x64" Framework="net10.0-windows10.0.22621.0"/>` and `<Target Rid="linux-x64" Framework="net10.0"/>`. Ship output moves from `Ship/Thorium/` to `Ship/Thorium/<rid>/`.
- `AuroraEngine.csproj`, `Thorium.csproj`: `<TargetFrameworks>net10.0-windows10.0.22621.0;net10.0</TargetFrameworks>`. Carbon, AuroraEditor and `_Build` stay Windows-only and consume the engine's Windows target. WinRT code (`WindowsHello`, `DisplayNames`) sits under `#if WINDOWS`; see [[linux-test-port]].

## Why these choices

**`.arx` is one archive per app, not one per mount.**
The builder merges engine Data and app Data at build time, first `<Data>` wins per relative path (the same override rule the mounts use). Rejected: one archive per mount (engine.arx + thorium.arx), which needs a mount order recorded somewhere and resolved at runtime.

**XML is stored as a binary tree; everything else is stored as-is.**
The tree is decoded back to an `XElement`, so no consumer changed past its load call. `.spv`, `.png`, `.fbx`, `.agd`, `.afm` and atlases go in unchanged. Rejected: per-format compiled binaries (a reader per format, ~15 formats, kept in sync with the XSD layer). Rejected: .NET's `XmlDictionaryWriter.CreateBinaryWriter` — it has no string table shared across the archive, and the custom records are the shape that source generation will later replace. Precedent: Android binary XML, WPF BAML, Godot `.tscn` to `.scn`.

**Encryption alone does not stop tampering, so the archive is signed too (revised 2026-10-16 after comparing with Unreal/Godot practice).**
Each payload is encrypted with `Security.Cipher` (AES-256-GCM), with the entry path as associated data so two entries can't be swapped. The whole table of contents is encrypted as well: it holds the shared string table, so leaving it plain would expose every XML string. Header + encrypted table are signed with ECDsa P-256, and the exe carries only the public key. AES with a key inside the exe only deters casual extraction — whoever pulls the key out can edit and re-encrypt. Only the signature stops that. Precedent: Unreal pak signing.

**One stable key set per project, not per build.**
So a later patch or DLC archive can be read by an app already installed. Kept in `%APPDATA%\Arktis\BuildKeys\<App>.keys.axml`, outside the repo, created on the first build. Rejected: environment variables. Losing the file means shipped archives can't be patched — it needs a backup off the machine. The keys reach the exe as MSBuild properties (`-p:ArxKey=… -p:ArxPublicKey=…` become `AssemblyMetadata` items in the app's csproj); the private signing key never leaves the key file.

**Mount, check and table read happen before boot; entries decrypt when first opened.**
Every entry's SHA-256 is checked on open, nothing is cached (Data is read once during boot). Rejected: decrypting everything into memory before boot — fine at today's ~12 MB but not once meshes and textures grow. Engines map and read on demand.

**An archive entry's path is `<exeDir>\<App>.arx\<logical\path>` (the archive file treated as a folder), not an `arx:` scheme.**
Four editor controls (DocumentEditorControl, PlannerEditorControl, SheetEditorControl, XmlEditorControl) run `Path.GetFullPath(Paths.Doc(...))`, and the test runner builds golden paths with `Path.Combine`; an `arx:` prefix breaks both on Windows.

**No compression in v1.**
The `Compressed` flag bit is reserved: compression must run before encryption, because encrypted bytes don't compress.

**The builder deletes the `Data/` and `Shaders/` folders `dotnet publish` copies into the output, then recreates `Data/` with only the Loose files.**
The loose `Data/` folder is mounted before the archive, so a full raw copy there would shadow every entry — the app would never read the archive and would ship every file in clear. Publish runs with `-p:ErrorOnDuplicatePublishOutputFiles=false`: Engine and Thorium both mark `Data/**` as Content, and duplicate relative paths (`Data/Fonts/arialbd/…`) fail with NETSDK1152; the copy is discarded anyway. Rejected: removing Data from publish in both csprojs, which changes what a plain publish produces.

**`XML/Schemas/**` is always excluded.**
A packed app doesn't generate XSDs (`GenerateXSD` returns early), and nothing reads schemas at runtime.

**Source generation is a later builder stage (user, 2026-10-16).**
It will turn XML (UI documents, bootstrap, palettes, …) into C# so the shipped app builds its trees without parsing. The binary XML entries are the step before that. There is no code stub for it.

**Each target is a RID-specific publish, so the builder filters nothing.**
The old RID-less framework-dependent publish copied every package's natives into `runtimes/` for 8 platforms (linux-arm, linux-arm64, linux-x64, osx-arm64, osx-x64, win-arm64, win-x64, win-x86). Publishing with `-r <rid>` makes the SDK emit only that RID's natives. Measured: the win-x64 and linux-x64 outputs contain no `runtimes/` and no foreign-OS or foreign-arch native binary.

**Self-contained everywhere.**
Users need no .NET 10 install. Cost, measured excluding Data: win-x64 131 MB (the old framework-dependent `Ship/Thorium` was 76 MB total), linux-x64 110 MB. Rejected: framework-dependent with `-r` — smaller, but needs the runtime installed on the target machine.

**AuroraEngine and Thorium multi-target a plain `net10.0` beside the Windows TFM.**
With only the Windows TFM, a linux-x64 publish still carried `Microsoft.Windows.SDK.NET.dll` (26 MB) and `WinRT.Runtime.dll` (0.5 MB) — never loaded on Linux, but Windows dependencies. Three files needed WinRT (`WindowsHello`, `DisplayNames`, `AVulkanHelper`'s unused `using`), so those moved under `#if WINDOWS` or lost the using. Rejected: RID publish only, which keeps the 26.5 MB. Cost: Debug builds compile the engine and Thorium twice. `System.Windows.dll`, `WindowsBase.dll` and `Microsoft.Win32.*.dll` remain in Linux output — they are .NET's own runtime pack in every self-contained build; removing them needs trimming, which the reflection-driven XSD layer cannot survive, so it is not done.

**`Framework` is an attribute on `<Target>`, chosen by the user.**
A multi-targeted project refuses `dotnet publish` without `-f` (NETSDK1129, reproduced), Windows included. Rejected: hardcoding `win-*` to the Windows TFM in `AppBuilder`, which duplicates the csproj string; reading `<TargetFrameworks>` from the csproj, which is the most code.

**The archive is packed once and copied to later targets.**
Data is platform-independent, and copying guarantees byte-identity (measured: the same SHA-256 in both folders).

**macOS is left out of Thorium's targets.**
An osx-arm64 probe publish built (unsigned Mach-O arm64 launcher, `libglfw.3.dylib`) but is not runnable: no Vulkan loader or MoltenVK (needs the new NuGet `Silk.NET.MoltenVK.Native` plus `VK_KHR_portability_enumeration` at instance creation in `Renderer`/`VulkanRenderer`); no arm64 `libassimp` (AssimpNet 4.1.0 ships none); `MeshImporter`'s static resolver maps `libdl.so` to `libdl.so.2` on every non-Windows OS, and that file is absent on macOS; the launcher must be `codesign`ed on a Mac before Apple Silicon runs it; there is no `.app` bundle. The builder accepts any RID, so adding macOS is one `<Target>` line once ported.

**Mobile is not handled by the builder.**
GLFW does not run on Android/iOS. It needs `net10.0-android`/`-ios` TFMs, a new windowing/surface backend and APK/IPA packaging; iOS needs a Mac host. Ties to the deferred mobile-input WIP entry.

**This extends two earlier notes.**
[[asset-pipeline-bake]]'s pak / `PackSource` layer now exists as `ArxMount`, per file rather than per mesh record; GPU residency is still unbuilt. [[asset-manifest-and-import]]'s "PakMount is a documented drop-in" is now real.

## Known gaps
- Build files and `AssemblyMetadata` lines exist only for Thorium; Carbon and AuroraEditor have none.
- Writes into a packed path (font/icon import, `--test-approve` goldens) cannot work. Import is off in Release anyway.
- `ArxMount` registers itself in a static list that is never cleared, so `Arx.MountResolves` leaves one stale entry pointing at a deleted temp file.
- A shipped Thorium's default vault path, which resolves relative to Data, has not been looked at.
- Not built yet: compression, pre-decoded textures (still PNG), cooked meshes (still FBX), source generation.
- During the Debug test runs of this change, `default.import.axml` under Thorium's icons got a new `SourceHash` while the re-baked atlas and `.aid` stayed byte-identical. The cause is unexplained and not this change: the hash is computed from the SVGs on disk by untouched code.
- Test-verified, not run end to end by hand. Debug full suite: 345 passed, 1 failed (Boot, on the pre-existing `[Assets] failed to load the default sampler asset` baseline error; no new error kinds), 1 skipped; the 6 Arx tests pass. `Arx.XmlRoundTrip` compares every `*.axml` under the mounted Data trees except schemas, with `XNode.DeepEquals`, against the source with comments and processing instructions removed. It passes trivially when `Paths.packed`, because a shipped app carries no source trees.
- Shipped build: `_Build arx` with a scratch build file of the same shape as `Thorium.build.axml` (absolute paths, output in a temp folder) packed 286 entries (12.5 MB) plus 7 loose UI files; `Thorium.exe --test` from that folder gave 345 passed, 1 failed (same baseline Boot error), 1 skipped, no `[Vulkan]` lines. Bootstrap, settings, fonts and shaders exist only inside the archive there, so boot reads through it. A second build reused the key file. Flipping one table byte in the shipped `.arx` made the app log `FATAL [Arx] cannot mount the app's archives` and exit 1.
- Per-target build (2026-10-17), verified: full solution builds clean on Windows for both TFMs; `Thorium.csproj -f net10.0` alone builds clean and its bin has no WinRT assemblies. Debug `_Build/test.sh` on Windows gave 345 passed, 1 failed (`Boot`, the baseline sampler error), 1 skipped, 0 new; the shipped `Ship/Thorium/win-x64/Thorium.exe --test` gave identical counts, no new error kinds, no `[Vulkan]` lines, no `created build keys` warning (existing key file reused). The linux-x64 output was checked as build output only, on Windows: ELF x86-64 `Thorium`, `libglfw.so.3`, `libassimp.so`, no WinRT assemblies, no Windows natives, `.arx` byte-identical to win-x64's, only `Data/XML/Documents/UI/**` loose.
- **Pending, not done:** the shipped linux-x64 build booting from its `.arx` and passing tests — a cloud-container check.
- A linux-x64 folder copied off Windows loses the launcher's execute bit; it needs `chmod +x Thorium`. There is no tar/zip packaging.
- macOS and mobile unported (see Why). Carbon and AuroraEditor have no build files and stay Windows-only.
- Not checked: the committed `Thorium.build.axml` itself (its relative paths — the verified build used the scratch copy); PDF export; meshes (no FBX in any app's Data today); the pathtracer and RadianceCascades2D shaders from an archive; opening a note or doc through an editor control from a packed Data path.

Related: [[asset-pipeline-bake]], [[asset-manifest-and-import]], [[paths-from-exe-folder]], [[encryption]], [[linux-test-port]]
