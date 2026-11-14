# nMIDI.Controls

WPF controls for MIDI and DAW-style applications: knobs, faders, meters, a piano keyboard and piano roll, a step
sequencer, controller lanes, transport controls and more. They come with the dark **Graphite** skin and are drawn
with **NinePatchWpf** where that pays off. The library started as DawControls 1.1.1; nMIDI Editor uses it for the mixer window.

## Requirements

- .NET 10 Desktop Runtime, Windows, **x64 only** (the nine-patch renderer is a native 64-bit DLL).
- Microsoft Visual C++ Redistributable (x64), which the native renderer needs. If the native renderer cannot
  load, the controls draw plain vector frames instead (see below), so the app still runs.

## Install

```
dotnet add package nMIDI.Controls
```

Set the app to x64 (`<PlatformTarget>x64</PlatformTarget>` or `<RuntimeIdentifier>win-x64</RuntimeIdentifier>`).
The package copies `NinePatchLib.dll` next to your exe; you don't need to do anything else.

In XAML: `xmlns:nc="urn:nmidi-controls"`, for example

```xml
<nc:Knob Label="Cutoff" Minimum="20" Maximum="20000" Value="{Binding Cutoff}" />
<nc:Fader Orientation="Vertical" Value="{Binding Volume}" />
<nc:LedToggleButton Role="Mute" Content="M" IsChecked="{Binding Muted}" />
```

Colours come from `DawSkinKeys`, for example `{DynamicResource {x:Static nc:DawSkinKeys.Accent}}`. The Graphite
values are in `Skins/Graphite.xaml`; override any key in your own resources to restyle.

## Controls

| Area | Controls |
|---|---|
| Parameters | `Knob`, `Fader`, `ValueBox`, `XYPad`, `EnvelopeEditor` |
| Metering | `LevelMeter` (lock-free `PushLevels` from the audio thread), `ValueScale` |
| Buttons | `LedToggleButton` (Mute / Solo / Arm / Monitor), `TransportButton` |
| Transport | `TransportBar` (`PlayCommand`, `StopCommand`, `ToStartCommand`, ...), `TimeDisplay` |
| MIDI | `PianoKeyboard`, `PianoRoll`, `ControllerLane`, `StepSequencer`, `MidiActivityMonitor`, `PerformanceWheel`, `PianoPedals` |
| Layout | `DawPanel`, `TrackHeader`, `ChannelStrip` |

`PianoKeyboard` has a third key style, `KeyStyle="Grand"`: a hand-drawn piano (ivory keys, red felt, lit keys in
their track colour with finger number and hand, or note name). Feed it with `SetState(keys)` and
`SetRange(low, high)`; `LabelMode` picks fingers, notes or nothing.

`PianoPedals.PedalChanged` reports `IsUserChange`, so you can send MIDI only for clicks and not echo pedal
changes that came from playback.

### Keyboard

Every interactive control can take keyboard focus and shows a focus ring (`DawStyleKeys.FocusVisual`). Knobs,
faders and value boxes respond to the arrow keys, Page Up/Down and Home/End. The step sequencer has a cell cursor:
arrows move it, Space or Enter toggles the step and + / - change its velocity. `EnvelopeEditor` and
`ControllerLane` are mouse-only and are skipped by Tab.

## Rendering

All nine-patch images are **generated in code** from the skin colours: an anti-aliased rounded rectangle with
outline, vertical gradient and a bevel (`Flat`, `Raised`, `Sunken`, `Glow`). There are no image files, and equal
looks share one image.

| What | How |
|---|---|
| Buttons, LED toggles, transport buttons, combo boxes, fader caps and grooves, LCDs, value boxes, panels, channel strips | `NineFrame`, a drop-in replacement for `Border` (Background, BorderBrush, BorderThickness, CornerRadius, Padding, plus `Bevel`) |
| Step sequencer cells, activity LEDs and note map, LED-ladder meter segments, piano roll notes | One bitmap per control, filled with a single native batch call |
| Meter wells | A sunken frame |

Small frames are rendered at the exact device-pixel size and cached, so a hundred equal buttons share one bitmap.
Large frames are nine slices of one render stretched by the GPU, so resizing a window allocates nothing. Frames too
small for their corners lose radius, bevel, then outline.

`NineFrame` uses the nine-patch only for a uniform border thickness and corner radius with solid or two-stop
linear brushes. Anything else (per-side thickness, per-corner radius, image or radial brushes, animated brushes)
is drawn like a normal `Border`.

Knob arcs, envelope and XY curves, wheels, pedals, scales, text and grid lines are vector drawing.

**Without the native renderer** (DLL missing, wrong architecture, no VC++ runtime) the controls draw rounded
vector frames in the same colours. The failure is written once to the debug output and never throws.

## Breaking changes from DawControls 1.1.1

- Namespace and XML namespace: `nMIDI.Controls`, `urn:nmidi-controls` (prefix `nc:` in the docs).
- `MidiNote.Color` is now `MidiNote.Brush`.
- x64 only.
- Rendering helpers (`NineSkins`, `NinePainter`, `NineBatch`, `NoteSkins`, `Kit`, `Palette`) are internal.

## Building from source

The library is built with the editor (`dotnet build ../nMIDIEditor.sln -c Release`). NinePatchWpf comes prebuilt
from `../lib/NinePatchWpf`. Projects that reference `nMIDI.Controls` get `NinePatchLib.dll` copied to their output
automatically.

| Folder | What is in it |
|---|---|
| `nMIDI.Controls/Controls` | The controls |
| `nMIDI.Controls/Rendering` | Nine-patch generation and drawing, note skins, brush and text caches |
| `nMIDI.Controls/Primitives` | Base classes, tapers, formatters, number parsing, `ArcElement`, `FaderPanel` |
| `nMIDI.Controls/Skinning`, `Skins`, `Themes` | Skin keys, the Graphite skin, default styles |
| `nMIDI.Controls/build` | MSBuild targets in the package that copy the native DLL |

### Publishing to NuGet

```
dotnet pack nMIDI.Controls -c Release -o pack
dotnet nuget push pack/nMIDI.Controls.2.0.0.nupkg --api-key <your key> --source https://api.nuget.org/v3/index.json
```

The symbols package (`.snupkg`) next to it is pushed automatically. Raise `<Version>` in the csproj for each
release.

## Licence

GNU General Public License v3.0, see `LICENSE` in the root of the repository.
