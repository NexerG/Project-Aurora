---
date: 2026-10-16
Status: Current
tags:
  - Engine
  - d_Filing
  - d_System
cssclasses:
  - Aurora.css
Linker:
  - "[[Arctis Aurora]]"
System:
  - "[[ENCRYPTION]]"
Class:
  - "[[Arx Archives]]"
Used by:
  - "[[Virtual File System]]"
  - "[[Paths]]"
Type:
  - Public
Namespace: ArctisAurora.Core.Filing.Arx
SourceFile: AuroraEngine/Core/Filing/Arx/
VerifiedAgainst: 2026-10-16
---
## Description

An `.arx` file is how a finished Aurora app carries its data. `_Build arx <App>.build.axml [rid]` publishes the app in Release, self-contained, once for each `<Target>` the build file declares, into `<Output>/<rid>/`, and packs the engine's Data, the app's Data and the app's compiled `.spv` shaders into one encrypted, signed `<App>.arx` beside each exe. Files that a `<Loose>` rule matches, such as UI XML that is meant to stay editable, are copied as plain files into `Data/` beside the exe instead. A Release exe that finds an `*.arx` beside itself mounts it before anything reads Data, and from then on every Data file, texture, mesh and shader is read through it. Debug, and Release without an archive, behave exactly as before.

The archive is one per app and not one per mount. The builder merges the engine's Data and the app's Data when it builds, and the first `<Data>` source to hold a relative path wins, which is the same override rule the mounts use at run time. Two archives would need a mount order recorded somewhere and resolved at run time.

A target is a `<Target Rid="…" Framework="…"/>` element, for example `win-x64` with `net10.0-windows10.0.22621.0` and `linux-x64` with `net10.0`. The framework is named by the user because a multi-targeted project refuses `dotnet publish` without `-f`, and the builder would otherwise have to hardcode which runtime identifier belongs to which framework. Each target is published with `-r <rid> --self-contained`, so the folder carries only its own operating system's natives and the user needs no .NET install. Data does not differ between platforms, so the archive is packed once, written beside the first target's exe and copied byte for byte into every later target's folder. A `rid` argument on the command line builds only that target. AuroraEngine and Thorium build for both the Windows framework and a plain `net10.0`, so a non-Windows publish carries no WinRT assemblies; code that needs WinRT sits under `#if WINDOWS`. macOS and mobile are not targets yet: the engine has no Vulkan loader for macOS and no mobile windowing backend.

An entry is either a blob or XML. A blob is stored as it is: `.spv`, `.png`, `.fbx`, `.agd`, `.afm` and atlases. XML is stored as a binary tree of string ids against one string table shared by the whole archive, and is decoded back to an `XElement` when read, so no consumer changed beyond its load call. Comments and processing instructions are dropped, and an element written `<a></a>` stays non-empty. Per-format compiled binaries were rejected, since that is a reader per format for about fifteen formats, each kept in step with the XSD layer.

On disk the file runs header, payloads, table of contents, signature. The 24-byte header holds the magic `ARX\0`, the version, where the table starts and how long it is, and how long the signature is. The payloads start at offset 32, each one an encrypted blob aligned to 16 bytes. The encrypted table follows them, holding the string table and, per entry, its path, kind, flags, offset, stored length and the SHA-256 of its stored bytes. The 64-byte signature over the header and the encrypted table closes the file, and an archive whose sizes do not add up to the file's length is refused.

Each payload is encrypted with the AES-256-GCM `Cipher` from [[ENCRYPTION]], with the entry's path as associated data, so two entries cannot be swapped. The table of contents is encrypted too, because it holds the string table and leaving it plain would expose every XML string. The header and the encrypted table are signed with ECDsa P-256, and the exe carries only the public key. The signature is what stops tampering: AES with a key inside the exe only deters casual extraction, because whoever pulls the key out can edit the data and encrypt it again. Every entry's SHA-256 is checked when it is opened, and nothing is cached, since Data is read once during boot.

An entry's full path is `<archive file>\<logical path>`, so the archive acts like a folder. That keeps `Path.GetFullPath` and `Path.Combine` working for callers that build paths from `Paths.Doc(...)`, which an `arx:` prefix would break on Windows.

The keys belong to the project and not to a build, so a later patch or DLC archive can be read by an app that is already installed. They live in `%APPDATA%\Arktis\BuildKeys\<App>.keys.axml`, outside the repo, and are created on the first build. Losing that file means shipped archives can no longer be patched, so it needs a backup off the machine. The keys reach the exe as the MSBuild properties `ArxKey` and `ArxPublicKey`, which the app's csproj turns into `AssemblyMetadata`; the private signing key never leaves the key file.

A planned later stage of the builder turns XML (UI documents, bootstrap, palettes and so on) into C#, so the shipped app builds its trees without parsing them. The binary XML entries are the step before that, and nothing for it exists in code yet. Compression is also not built: the `Compressed` flag bit is reserved, and compression would have to run before encryption because encrypted bytes do not compress.

See [[Virtual File System]] for the mount layer `ArxMount` plugs into and [[Paths]] for how Release mounts the loose `Data/` folder first and then each archive.

## API summary

| Member | Kind | Summary |
| --- | --- | --- |
| `ArxWriter.AddBlob` / `AddXml` | instance | Queue an entry as raw bytes / as an encoded XML tree. |
| `ArxWriter.Write(file, ArxKeys)` | instance | Encrypt, build the table, sign, write the archive. |
| `ArxArchive.Open(file, ArxKeys)` | static | Map the file, check the signature, decrypt the table of contents. |
| `ArxArchive.ReadBytes` / `ReadXml` | instance | Decrypt one entry and check its SHA-256. |
| `ArxKeys.LoadOrCreate(app)` | static | Build-side key set from the key file, created if absent. |
| `ArxKeys.FromEntryAssembly()` | static | Run-side key set from the exe's `AssemblyMetadata`. |
| `ArxMount.TryLocate(fullPath, out archive, out entry)` | static | Find the archive and entry a full path points into. |
| `DataFiles.Exists` / `Open` / `LoadXml` | static | Read a disk path or an archive-entry path alike. |
| `DataFiles.ReadShader(fileName)` | static | Shader bytes from the archive when it holds them, else from disk. |
| `AppBuilder.Run(buildFile, rid)` | static | The `arx` build verb; a null `rid` builds every `<Target>`. |

## Methods

### `AppBuilder.Run`

```
Run(buildFile, rid)
	definition = load the BuildDefinition
	targets = the Targets whose Rid is rid, or every Target when rid is null
	if targets is empty
		log an error
		return false
	keys = ArxKeys.LoadOrCreate(definition.App)
	for each Data source in order
		for each file under the source
			if the file is under XML/Schemas or matches an Exclude rule, skip it
			if its relative path was already taken by an earlier source, skip it
			if it matches a Loose rule
				remember it as a loose file
			else if it is XML
				writer.AddXml
			else
				writer.AddBlob
	for each Shaders source
		writer.AddBlob each .spv
	for each target
		dotnet publish definition.Project in Release with -f target.Framework -r target.Rid --self-contained -o Output/target.Rid, passing ArxKey and ArxPublicKey, with ErrorOnDuplicatePublishOutputFiles=false
		delete Data/ and Shaders/ from the output
		recreate an empty Data/ in the output
		copy the loose files to Data/ beside the exe
		if this is the first target
			writer.Write(the .arx beside the exe, keys)
		else
			copy the first target's .arx beside the exe
		log the entries packed and the loose files copied
```

The publish step copies `Data/` and `Shaders/` into the output, and the loose `Data/` folder is mounted before the archive. Left in place, that copy would shadow every entry, so the app would never read the archive and would ship every file in clear.

### `Paths.Mount` (Release, packed)

```
Mount
	mount the loose Data/ folder beside the exe
	for each *.arx beside the exe
		keys = ArxKeys.FromEntryAssembly()
		if ArxArchive.Open(file, keys) fails
			log FATAL [Arx] cannot mount the app's archives
			exit 1
		mount an ArxMount for it
	Paths.packed = true
```

## Related
- [[Virtual File System]] — `ArxMount` is one of its mounts
- [[Paths]] — mounts the loose Data and the archives in Release
- [[ENCRYPTION]] — the cipher the payloads use
- [[XML-XSD]] — a packed app skips schema generation
