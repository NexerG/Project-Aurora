---
date: 2026-10-18
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
  - "[[XML-XSD]]"
Class:
  - "[[File Types]]"
Used by:
  - "[[File Object]]"
Type:
  - Public
Namespace: ArctisAurora.Core.Filing
SourceFile: AuroraEngine/Core/Filing/
VerifiedAgainst: 2026-10-18
---
## Description

A file's suffix says what kind of file it is, and `FileTypes` is the one place that answers. Every file Aurora writes ends in `.axml`: notes, sheets (`.sheet.axml`), planners (`.planner.axml`), engine data, settings. A plain `.xml` is someone else's XML, so Thorium opens it as coloured XML source and never as a rich note. The editor, the note sync, the sheet links and the vault browser used to compare extensions themselves; they now ask `FileTypes.Of` or `FileTypes.KindOf`.

The registry is declared in XML, in `FileTypes.filetypes.axml` beside the other engine documents, and loaded by the first step of the bootstrap phase, before settings. Each `FileType` names a `suffix`, a `kind`, a `description` and a `register` flag. `register` marks the single-extension types an operating system will see (`.axml`, `.xml`, `.md`, `.txt`); `.sheet.axml` and `.planner.axml` are covered by `.axml` at that level. `.pdf` is absent on purpose, because Thorium cannot display one.

Each registered type also carries a `mime` name (`application/x-aurora+xml` for `.axml`, `application/xml`, `text/markdown`, `text/plain`), which only the Linux side uses. `FileAssociations` turns the registered types into the operating system's own records, so that Thorium can be the program Explorer or a Linux file manager opens them with. Settings lists a "File types" category when a host has set `FileAssociations.enabled`: a "Make default" button per registered type and a "Make default for all" button, which act at once rather than through Save or Apply. The builders (`WindowsEntries`, `DesktopEntry`, `MimePackage`) are pure functions, so tests never touch the real registry or home folder; the `Apply` methods are thin.

On Windows everything is written under HKEY_CURRENT_USER, so no administrator is needed. Every press registers all types so Thorium appears in Open With, and only the type or types claimed get the default. Windows protects a default the user chose themselves, and no program may overwrite it, so when a claimed type already has one naming another program Thorium logs that and opens the Default apps page of Windows Settings for the user to finish the switch. Expect `.md` and `.txt` to end up there; `.axml` switches directly. On Linux a claim always takes effect. macOS is not covered, because there is no mac ship target and GLFW does not deliver Finder's open-document events.

A file passed on the command line (a double-click) opens in a new Thorium process that shows a bare tab window with only that file, never in a running copy. That process sets `Background.standalone`, so it has no tray icon and claims no single-instance mutex, closing its window quits it, and the vault's session layout is paused so the window is not recorded over the saved one.

Matching is by the longest suffix that fits, case-insensitively. `Path.GetExtension("x.sheet.axml")` is `.axml`, which would make every sheet a note, so the list is sorted longest suffix first and the first match wins.

## API summary

| Member | Kind | Summary |
| --- | --- | --- |
| `FileTypes.all` | field | Every declared `FileType`, longest suffix first. |
| `FileTypes.Load()` | bootstrap action | Reads `FileTypes.filetypes.axml` and sorts it. Tagged `FileTypes.Load` in the `Bootstrap` category. |
| `FileTypes.Of(path)` | public | The matching `FileType`, or null for an unknown suffix or a null path. |
| `FileTypes.KindOf(path)` | public | `Of(path)?.kind`. |
| `FileType` | record | `suffix`, `kind`, `description`, `register`, `mime`. |
| `FileAssociations.Registered` | property | The `FileTypes.all` entries whose `register` is set. |
| `FileAssociations.enabled` | field | Set by a host that offers its types in Settings; Thorium sets it. |
| `FileAssociations.Claim(type)` | public | Registers every type with the OS and makes the app the default for `type`. |
| `FileAssociations.ClaimAll()` | public | The same, claiming every registered type. |
| `FileAssociations.WindowsEntries(app, exe, claimed)` | pure | The `RegistryEntry` list for HKEY_CURRENT_USER. |
| `FileAssociations.DesktopEntry(app, exe)` | pure | The text of `thorium.desktop`. |
| `FileAssociations.MimePackage()` | pure | The freedesktop mime package as an `XElement`. |
| `RegistryEntry` | record struct | `key`, `name`, `value`; an empty name is the key's default. |
| `FileKind` | enum | `Note`, `Markdown`, `PlainText`, `Tex`, `Xml`, `Sheet`, `Planner`. |
| `FileTypeMap` | XSD root | The `<FileTypes>` container in the data file. |

## Methods

### Of
Walks the list in order, so the longest suffix is tried first, and compares without regard to case.

```
Of(path)
    if path is null -> null
    for type in all
        if path ends with type.suffix, ignoring case
            return type
    return null
```

### KindOf
```
KindOf(path)
    type = Of(path)
    if type is null -> null
    return type.kind
```

### Claim
Registration is done for every type on each press; only the default is per type.

```
Claim(type) -> Apply([type])
ClaimAll()  -> Apply(Registered)

Apply(claimed)
    app = entry assembly's name, exe = this process's path
    if the OS is Windows -> ApplyWindows(app, exe, claimed)
    else if the OS is Linux -> ApplyLinux(app, exe, claimed)
    else
        warn that this OS cannot register file types yet
        return
    log the registered suffixes and the claimed ones
```

### ApplyWindows
```
ApplyWindows(app, exe, claimed)
    for entry in WindowsEntries(app, exe, claimed)
        write entry.value to HKEY_CURRENT_USER entry.key, value name entry.name
    notify the shell that associations changed (SHCNE_ASSOCCHANGED)
    kept = claimed types where ChosenElsewhere(app, type)
    if kept is empty -> return
    log that Windows keeps the user's own choice for those suffixes
    open ms-settings:defaultapps?registeredAppUser=<app>, once
```

### ApplyLinux
```
ApplyLinux(app, exe, claimed)
    write applications/thorium.desktop under $XDG_DATA_HOME from DesktopEntry
    write mime/packages/thorium.xml under $XDG_DATA_HOME from MimePackage
    Run update-mime-database
    Run update-desktop-database
    for type in claimed
        Run xdg-mime default thorium.desktop type.mime
```

`Run` starts the tool and waits for it; a tool that is not installed is logged and skipped. `ApplyWindows` and `ApplyLinux` are built but the Linux path has not been run on a Linux machine.

## Who asks

| Caller | Asks for |
| --- | --- |
| `RichTextDocument.Opens(path)` | Note, Markdown, PlainText, Tex or Xml. `Load` sends `FileKind.Xml` to `LoadSource(path, "xml")`. |
| `DocumentEditorControl` | its own `Kind`, for every format check. |
| `NoteFileSync` | segmented writes for `FileKind.Note` only. |
| `SheetLinks` | the Markdown check when rewriting a link. |
| `VaultBrowserControl` | `Accepts` (any match, or CSV); `IsXml` (Note, Sheet, Planner, Xml); `IsAuroraXml` (Note, Sheet, Planner) for `Encrypt` and `WriteName`, so a plain `.xml` is never encrypted or given a `Name`. |
| `AppBuilder` | packs both `.axml` and `.xml` as `ArxKind.Xml`. |
| `SettingsWindow.FillFileTypes` | `FileAssociations.Registered`, and `Claim` / `ClaimAll` from its buttons. |
| `Thorium.Main` | sets `FileAssociations.enabled`; a file argument selects the standalone start (`OpenStandalone`). |

## Related

[[Arx Archives]], [[File Object]], [[XML-XSD]]
