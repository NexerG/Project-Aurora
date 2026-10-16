# Decision — an app ships as one encrypted, signed `.arx` archive built by `_Build arx`

**Date:** 2026-10-16
**Status:** LANDED (2026-10-16) for Thorium. Compression, pre-decoded textures, cooked meshes and source generation are unbuilt.
**Scope:** `ArctisAurora.Core.Filing.Arx` — `ArxKind`, `ArxEntryFlags`, `ArxLayout`, `ArxWriter`, `ArxArchive`, `ArxStrings`, `ArxXml`, `ArxKeys`, `ArxMount`, `AppBuilder`, `BuildDefinition`, `BuildSource`, `BuildPattern`; `ArctisAurora.Core.Filing.Serialization` — `DataFiles`, `Paths`, `MeshImporter`; `ArctisAurora.Core.Registry` — `XSDGenerator`; `ArctisAurora.Core.Rendering` — `GraphicsPipeline`, `RenderingModule`, `Pathtracing`, `RadianceCascades2D`; `_Build.Program`

## What changed
- `_Build arx <App>.build.xml` publishes the app in Release, then packs engine Data + app Data + the app's `.spv` shaders into one `<App>.arx` beside the exe. Files matched by `<Loose>` rules (e.g. UI XML) are copied as plain files to `Data/…` beside the exe instead.
- A Release exe that finds `*.arx` beside itself mounts it in `Paths.Mount`, before anything reads Data (including `Bootstrapper.Load`), and reads every Data file, texture, mesh and shader through it. Debug, and Release without an archive, behave exactly as before.
- `ArxFormat.cs`: `ArxKind` {Blob, Xml}; `ArxEntryFlags` {None, Compressed (reserved, never set in v1)}; internal `ArxLayout` (magic `ARX\0`, version 1, 24-byte header, 16-byte payload alignment); `ArxWriter` (`AddBlob`, `AddXml`, `Write(file, ArxKeys)`, `Count`, static `Normalize`); `ArxArchive` (`Open(file, ArxKeys)` maps the file, checks the ECDsa signature, decrypts the table of contents; `Contains`, `IsXml`, `ReadBytes`, `ReadXml`, `Paths`, `file`, `signatureSize = 64`).
- File order: header (magic u32, version u16, flags u16, tocOffset i64, tocLength i32, signatureLength i32) | payloads from offset 32, each `Cipher` blob 16-aligned | encrypted table of contents (string table, then per entry: path id, kind, flags, offset, stored length, SHA-256 of stored bytes) | 64-byte P1363 signature over header + encrypted table. Open requires tocOffset + tocLength + 64 == file size.
- `ArxXml.cs`: `ArxStrings` (interned string table); `ArxXml.Encode/Decode` — the XML tree as records of string ids (element ns+name, attributes ns+name+value, children: element / text / CDATA). Comments and processing instructions are dropped. An element written `<a></a>` stays non-empty.
- `ArxKeys`: {key, signingKey, publicKey}; `LoadOrCreate(app)` reads or creates `%APPDATA%\Arktis\BuildKeys\<App>.keys.xml` (AES-256 key, ECDsa P-256 PKCS#8 private key, SPKI public key, base64); `FromEntryAssembly()` reads `[AssemblyMetadata("ArxKey")]` and `("ArxPublicKey")` from the running exe.
- `ArxMount : IDataMount`. An entry's full path is `<archive file>\<logical path>`, so the archive acts like a folder. `TryLocate(fullPath, out archive, out entry)` is a static lookup over every `ArxMount` ever constructed.
- `AppBuilder.Run(buildFile)`; XSD types in the new `Build` category: `BuildDefinition` ("Build": App, Project, Output, Data*, Shaders*, Loose*, Exclude*), `BuildSource` (Source), `BuildPattern` (Include). Generated schema `BuildTypeSchema.xsd`, namespace `http://arctisaurora/AuroraBuildTypes`.
- `DataFiles` (new): `Exists`, `Open`, `LoadXml` (each takes a disk path or an archive-entry path), `ReadShader(fileName)` (strips the leading `../`s, reads `Shaders/…` from the archive when it holds it, else from disk relative to `AppContext.BaseDirectory` as before).
- `Paths.packed` (new static bool): true once every archive beside the exe mounted; false in Debug and in Release without one. `Paths.Mount` in Release mounts the loose `Data/` folder first, then every `*.arx` beside the exe; a failed check logs `FATAL [Arx] cannot mount the app's archives` and exits 1.
- `XSDGenerator.GenerateXSD` returns immediately when `Paths.packed`. `AssetImporter.RunImports` already returned early in Release — unchanged.
- The four shader readers (`GraphicsPipeline.ReadFile`, `RenderingModule.ReadFile`, `Pathtracing.ReadFile`, `RadianceCascades2D.LoadShader`) call `DataFiles.ReadShader`; the 17 `"../../../Shaders/…"` call sites are unchanged.
- `MeshImporter.ImportFBX` reads through `DataFiles.Open` + Assimp `ImportFileFromStream` (format hint = file extension).
- 35 read sites in 26 files moved from `XElement.Load` / `XDocument.Load(..).Root` / `File.Exists` / `Image.Load(path)` / `new FileStream` to `DataFiles.*`: AnimationLibrary, Bootstrapper, DataManager, MathConstants, InputHandler, AssetRegistries, SamplerAsset, Context, EntityRegistry, SettingsRegistry, Shutdown, TestRunner (suites + goldens), TexHyphenator, FrameScheduler, ControlXml, Effects, Gradients, Palettes, PlannerAttachments, PdfExport, FontAsset, TextureAsset, AGlfwWindow (app icons), `Serializer.DeserializeAttributed` (private helper now takes `Stream`), `AuroraFont.ReadFaceGlyphs`, PdfType3Font. Import-only, vault, note and results reads stay on the disk APIs.
- `_Build/Program.cs`: verb `arx <file.build.xml>`. `Thorium.csproj`: `AssemblyMetadata` `ArxKey` and `ArxPublicKey`. `.gitignore`: `Ship/`.
- Data files: `Thorium.build.xml` (App=Thorium, Output=../Ship/Thorium, Data=Data then ../AuroraEngine/Data, Shaders=Shaders, Loose XML/Documents/UI/**); suite `Arx.tests.xml` (XmlRoundTrip, BlobRoundTrip, MountResolves, TamperedEntryRefused, TamperedTocRefused, WrongKeyRefused) with tests in `ArxTests`.

## Why these choices

**`.arx` is one archive per app, not one per mount.**
The builder merges engine Data and app Data at build time, first `<Data>` wins per relative path (the same override rule the mounts use). Rejected: one archive per mount (engine.arx + thorium.arx), which needs a mount order recorded somewhere and resolved at runtime.

**XML is stored as a binary tree; everything else is stored as-is.**
The tree is decoded back to an `XElement`, so no consumer changed past its load call. `.spv`, `.png`, `.fbx`, `.agd`, `.afm` and atlases go in unchanged. Rejected: per-format compiled binaries (a reader per format, ~15 formats, kept in sync with the XSD layer). Rejected: .NET's `XmlDictionaryWriter.CreateBinaryWriter` — it has no string table shared across the archive, and the custom records are the shape that source generation will later replace. Precedent: Android binary XML, WPF BAML, Godot `.tscn` to `.scn`.

**Encryption alone does not stop tampering, so the archive is signed too (revised 2026-10-16 after comparing with Unreal/Godot practice).**
Each payload is encrypted with `Security.Cipher` (AES-256-GCM), with the entry path as associated data so two entries can't be swapped. The whole table of contents is encrypted as well: it holds the shared string table, so leaving it plain would expose every XML string. Header + encrypted table are signed with ECDsa P-256, and the exe carries only the public key. AES with a key inside the exe only deters casual extraction — whoever pulls the key out can edit and re-encrypt. Only the signature stops that. Precedent: Unreal pak signing.

**One stable key set per project, not per build.**
So a later patch or DLC archive can be read by an app already installed. Kept in `%APPDATA%\Arktis\BuildKeys\<App>.keys.xml`, outside the repo, created on the first build. Rejected: environment variables. Losing the file means shipped archives can't be patched — it needs a backup off the machine. The keys reach the exe as MSBuild properties (`-p:ArxKey=… -p:ArxPublicKey=…` become `AssemblyMetadata` items in the app's csproj); the private signing key never leaves the key file.

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

**This extends two earlier notes.**
[[asset-pipeline-bake]]'s pak / `PackSource` layer now exists as `ArxMount`, per file rather than per mesh record; GPU residency is still unbuilt. [[asset-manifest-and-import]]'s "PakMount is a documented drop-in" is now real.

## Known gaps
- Build files and `AssemblyMetadata` lines exist only for Thorium; Carbon and AuroraEditor have none.
- Writes into a packed path (font/icon import, `--test-approve` goldens) cannot work. Import is off in Release anyway.
- `ArxMount` registers itself in a static list that is never cleared, so `Arx.MountResolves` leaves one stale entry pointing at a deleted temp file.
- A shipped Thorium's default vault path, which resolves relative to Data, has not been looked at.
- Not built yet: compression, pre-decoded textures (still PNG), cooked meshes (still FBX), source generation.
- During the Debug test runs of this change, `default.import.xml` under Thorium's icons got a new `SourceHash` while the re-baked atlas and `.aid` stayed byte-identical. The cause is unexplained and not this change: the hash is computed from the SVGs on disk by untouched code.
- Test-verified, not run end to end by hand. Debug full suite: 345 passed, 1 failed (Boot, on the pre-existing `[Assets] failed to load the default sampler asset` baseline error; no new error kinds), 1 skipped; the 6 Arx tests pass. `Arx.XmlRoundTrip` compares every `*.xml` under the mounted Data trees except schemas, with `XNode.DeepEquals`, against the source with comments and processing instructions removed. It passes trivially when `Paths.packed`, because a shipped app carries no source trees.
- Shipped build: `_Build arx` with a scratch build file of the same shape as `Thorium.build.xml` (absolute paths, output in a temp folder) packed 286 entries (12.5 MB) plus 7 loose UI files; `Thorium.exe --test` from that folder gave 345 passed, 1 failed (same baseline Boot error), 1 skipped, no `[Vulkan]` lines. Bootstrap, settings, fonts and shaders exist only inside the archive there, so boot reads through it. A second build reused the key file. Flipping one table byte in the shipped `.arx` made the app log `FATAL [Arx] cannot mount the app's archives` and exit 1.
- Not checked: the committed `Thorium.build.xml` itself (its relative paths — the verified build used the scratch copy); PDF export; meshes (no FBX in any app's Data today); the pathtracer and RadianceCascades2D shaders from an archive; opening a note or doc through an editor control from a packed Data path.

Related: [[asset-pipeline-bake]], [[asset-manifest-and-import]], [[paths-from-exe-folder]], [[encryption]]
