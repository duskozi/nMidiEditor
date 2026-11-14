# Changelog

## 2.4.1 (2026-11-14)

- Microsoft Store package: an MSIX packaging project (`nMIDIEditor.Store.sln`) with tile images, a self-contained
  build and "Open with" for .mid and .midi files.

## 2.4 (2026-11-13)

- Mixer window: per track record, mute, solo, pan, reverb, chorus, delay and brightness knobs, a volume fader
  with dB scale and activity meter, plus a master strip. Controls follow the song until you move them.
- The knobs, faders and meters come from the nMIDI.Controls library, now part of the repository.
- "Pause main view" in the Lanes window: the main window's piano roll stops redrawing while you work in the
  Lanes window, to save CPU.
- The mixer follows the app theme: panels, labels, value displays, meters and the accent take the theme's
  colours, while the knobs, faders and buttons keep their dark hardware look.
- Mixer window icon.

## 2.3 (2026-11-13)

- Fingering engine 1.1: incremental fingering after edits, finger corrections that are kept, finger
  changes on fast repeated notes, cancellation, and a build fix for the .NET 10 SDK.
- Fingering modes per track (auto, both hands, left, right, off), pairs of tracks fingered together
  (found automatically or chosen), a Fingering menu, and fingering settings.
- Fingers on notes in the piano roll, finger corrections with Shift+1…5, saved in the MIDI file.
- Fingering report: hand and finger agreement with the file or a PIG file, corrections, stability,
  scale charts, a per-bar list, and differences outlined in the piano roll.
- Edit several tracks at once (Ctrl+click tracks in the list).
- Piano pedals: a pedal strip in the piano roll, and pedals next to the keyboard that move while
  the song plays (can be switched off).
- Pitch bend and modulation wheels next to the keyboard.
- Four themes: Studio, Light classic, Retro hardware and High contrast (replacing Classic). The piano
  roll, ruler and lanes follow the theme.
- The All outputs box shows the default MIDI output as soon as the editor opens.
- App icon, and icons for the Track lanes, Event list and Fingering report windows and the dialogs.
- Scroll bars in lists, drop-downs and the smaller windows follow the theme.
- Fixed: buttons in the top bar and edit toolbar were hidden in a normal-size window; the time and
  tempo displays shifted sideways; ruler flags overlapped.

## 2.0

First version of nMIDI Editor, built on the nMIDI player.

- Editable song model, with undo and redo for every edit.
- Piano roll tools: select, draw, erase, split. Move, copy and resize by dragging, with snapping.
- Cut, copy, paste, duplicate, delete, transpose, nudge and length commands, and a right-click menu.
- New song, Save and Save As (format 0 or 1), with a prompt for unsaved changes.
- Velocity, controller, pitch bend, pressure, program and tempo lanes; note fields for typing values.
- Quantize, transpose (in scale), length, velocity, humanize, reverse, invert, select by rule; scale
  highlighting and snapping.
- Track commands (add, duplicate, delete, move, merge, split), bar and range commands, tempo, time and
  key signature and marker editing, loop playback.
- Recording from a MIDI input (overdub or replace, count-in, quantize), thru, step input, metronome.
- Event list, export of a track or selection, recent files, autosave and recovery.
- Fingering engine (nPianoFingering 1.0): whole-part hand separation and fingering with scale
  charts, worked out in the background.
- Settings window (default MIDI ports, keyboard and lane height, editor, recording and file defaults).
