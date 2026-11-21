# Changelog

## 2.5 (2026-11-19)

- Instrument picker with banks: choose a sound by bank and name (General MIDI by family, GS / GM2 / XG drum
  kits, the presets of a SoundFont), search by name, try it, or type bank MSB / LSB and program. Used by track
  settings, the program change lane (a click opens it) and the event list. Bank select is written with the
  program change and always sent before it.
- The track list toggle works again after switching to Practice mode and back.

- Falling notes: the **Falling notes** button above the keyboard shows notes falling onto the keys in place of
  the piano roll, in the track colours with finger numbers, with bar and beat lines and the pedals (a column each
  for soft, sostenuto and sustain, and a light band while sustain is down). Smooth motion at the screen's frame
  rate; the mouse wheel shows more or fewer seconds. Without a song it shows what you play (clicked keys and the
  MIDI input, pedals included) rising up from the keys. On by default in Practice mode; each mode remembers it.
- Practice mode: the **Edit / Practice** switch in the top bar (or F9) turns the window into a practice view with a
  bigger keyboard. Choose the track and the hand (left, right or both); the editor plays everything else.
  - **Wait for me**: the song stops at each note or chord until you have played its keys (in any order). The keys
    to play pulse on the keyboard with their finger, turn green when pressed, a wrong key flashes red, and the
    next step shows faintly so you can prepare.
  - Without wait mode you play along in time: hits, misses, wrong keys and whether you are early or late.
  - **Speed up each loop**: with a loop range, start slower (e.g. 60 %) and go a few percent faster each time
    through, up to 100 %.
  - A score for every run (and every time through the loop) in the practice bar.
- Chord names: a strip along the top of the piano roll names the chords of the visible tracks beat by beat
  (broken chords and arpeggios too, passing notes are ignored), and the chord of the keys that sound now (the
  song, a clicked key or the MIDI input) shows above the keyboard. The **Chords** button above the keyboard turns
  both on and off.
- Export audio: Song ▾ → **Export audio (WAV / MP3)…** plays the song (or the loop range) through a SoundFont
  (.sf2) and saves it as MP3 (128, 192 or 320 kbit/s) or 16-bit WAV. Mute, solo and the mixer settings apply.
- MIDI activity window (the **Monitor** button): a note LED, a control LED, a live note map and the last control
  message for every channel of the MIDI output, and a row of LEDs for the MIDI input.
- Keys held on the MIDI input light up on the keyboard.
- Three new themes in a hardware style: *Alloy* (lit brushed silver with a dark LCD), *Alloy night*
  (dark metal) and *Alloy ember* (champagne metal with an amber LCD). The piano keyboard keeps its look.
- The mixer's displays use the theme's LCD colours, like the time display in the top bar.
- Updated DryWetMIDI from 7.1.0 to 8.0.3.

## 2.4.2 (2026-11-14)

- Fixed the top bar switching back and forth between one and two rows, which made the main window jump
  while it was resized.

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
