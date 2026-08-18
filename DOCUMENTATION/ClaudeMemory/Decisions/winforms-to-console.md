# Decision — the engine assembly is a plain console app; WinForms is off

**Date:** 2026-08-18
**Status:** LANDED. All four projects build clean; `ArctisAurora.exe` starts and exits 0.
**Scope:** `AuroraEngine.csproj`, `ArctisAurora` (`Program`, `Engine`, `Simulators.*`, `Forces.*`,
`ParticleTypes.*`), `ArctisAurora.Core.UISystem.Controls.*`, `Periodic.Editor.CustomControls`.

## Decisions

### 1. `UseWindowsForms` was carrying a dead form and three name clashes

`AuroraEngine` shipped `<UseWindowsForms>true</UseWindowsForms>` and a `Main` that ran
`Application.Run(new Frame())`. `Frame` was the 2D SPH particle-simulator control panel — brush
size, emissiveness, layer index — wired to `RadianceCascades2D` and `Layer`. Its `Frame_Load` was
entirely commented out, so it never initialised the engine; the real hosts (`Periodic`, `AuroraEditor`)
have their own `Main` and boot `Engine` themselves. Running the engine assembly popped a leftover
control panel over nothing.

The cost was not the dead form. `UseWindowsForms` adds implicit `using System.Windows.Forms;`, and
because the framework reference is transitive it reached `Periodic` and `AuroraEditor` too. That
produced a recurring tax: `ScrollableControl` had to be aliased in four files and `Keys` in one, and
`document-selection.md` had already recorded the clash as a third occurrence with "turn WinForms off"
as the standing fix. That fix is what this is.

`Program.Main` is now empty. The engine assembly stays `OutputType=Exe` because three projects
reference it and the entry point costs nothing, but nothing runs from it — it is a library with a
vestigial `Main`, the same shape as `_Build`.

### 2. TFM stays `net10.0-windows10.0.22621.0`

Only `UseWindowsForms` makes a project WinForms; the Windows TFM does not. Dropping to bare `net10.0`
would be the literal console-app default but is a separate decision with its own blast radius —
`Engine` P/Invokes `kernel32`, and the other three projects all target the Windows TFM. Left alone.

### 3. GDI+ went with it, so two legacy fields were deleted rather than re-referenced

`System.Drawing.Primitives` (`PointF`, `Color`, `Rectangle`) is in the base shared framework and
survives; `System.Drawing.Common` (`Brush`, `Pen`, `Bitmap`, `.Imaging`, `.Drawing2D`) does not —
it arrives only with the WindowsDesktop framework reference.

`Particle2D.color` and `Particle3D.color` were `Brush color = new Pen(Color.FromArgb(...)).Brush`,
never read by anything, left from when particles were drawn to a `PictureBox`. Deleted. The
alternative — a `System.Drawing.Common` `PackageReference` — would pull GDI+ back into a Vulkan
renderer to satisfy two unread fields.

`System.Drawing.Drawing2D` in `MCRaytracing` and `System.Drawing.Imaging` in `RadianceCascades2D`
were unused usings and went with it. `Forces.Force`, `Forces.Gravity`, `Simulator_DEPRECATED` and
`Simulator3D` use `PointF` and picked up an explicit `using System.Drawing;` in place of the implicit
one.

### 4. The dead `Frame` plumbing forced two constructor signature changes

`Engine.SC`, `Simulator_DEPRECATED.SC` and `Simulator3D.SC` were `Frame` fields that were assigned
and never read. Removing the type meant removing them, which meant the `Frame` parameter came off
four constructors — all uncalled:

| Was | Now |
|---|---|
| `Simulator_DEPRECATED(List<Particle2D>, Frame)` | `Simulator_DEPRECATED(List<Particle2D>)` |
| `Simulator_DEPRECATED(Frame, List<Particle2D>, Vector2)` | `Simulator_DEPRECATED(List<Particle2D>, Vector2)` |
| `Simulator3D(List<Particle3D>, Frame)` | `Simulator3D(List<Particle3D>)` |
| `Simulator3D(Frame, List<Particle3D>, Vector3D<float>)` | **deleted** |

The last one is the only outright deletion. Dropping its `Frame` would have made it a duplicate of
the existing `Simulator3D(List<Particle3D>, Vector3D<float>)`, which is the one `SPHSimComponent`
actually calls. The two differed only in gravity sign (`+9.8f` vs `-9.8f` on Y) and a call to the
empty `UpdateUI()`, so keeping the `Frame` variant would have meant inventing a new signature for
dead code.

## Left standing

- `AuroraEngine/Properties/Resources.resx` + `Resources.Designer.cs` — WinForms template scaffolding
  with zero entries and zero references. Compiles fine without WinForms, so it was not touched.
- `UICollisionHandling`'s `ScrollableControl` alias — now unnecessary but never carried a WinForms
  comment, and it is the only thing importing the type into that file.

Related: [[document-selection]], [[periodic-editor-architecture]], [[scrollbar-thumb]]
