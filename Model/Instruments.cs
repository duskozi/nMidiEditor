using System;
using System.Collections.Generic;
using System.Linq;

namespace nMIDI.Model
{
    /// <summary>One sound of a bank: bank select (MSB, LSB), program 0–127, its name and the group it is listed under.</summary>
    public sealed record Patch(int Msb, int Lsb, int Program, string Name, string Group);

    /// <summary>A list of sounds to choose from: General MIDI, a set of drum kits, or a SoundFont bank.</summary>
    public sealed record InstrumentBank(string Name, string Hint, bool Drums, bool SendsBank, IReadOnlyList<Patch> Patches)
    {
        /// <summary>The groups, in the order they first appear.</summary>
        public IReadOnlyList<string> Groups => Patches.Select(p => p.Group).Distinct().ToList();

        public override string ToString() => Name;
    }

    /// <summary>
    /// What a program change selects: the program and, when the track sends one, the bank (CC 0 MSB, CC 32 LSB).
    /// A null bank part means that controller is not sent.
    /// </summary>
    public readonly record struct InstrumentChoice(int? Msb, int? Lsb, int Program)
    {
        public bool HasBank => Msb != null || Lsb != null;
    }

    /// <summary>
    /// The sounds the instrument picker offers, and names for bank + program pairs: General MIDI by family,
    /// the GS, GM2 and XG drum kits, and the presets of the SoundFont set in the settings.
    /// </summary>
    public static class InstrumentCatalog
    {
        public static readonly string[] Families =
        [
            "Piano", "Chromatic percussion", "Organ", "Guitar", "Bass", "Strings", "Ensemble", "Brass",
            "Reed", "Pipe", "Synth lead", "Synth pad", "Synth effects", "Ethnic", "Percussive", "Sound effects",
        ];

        public static readonly InstrumentBank GeneralMidi = new(
            "General MIDI", "128 instruments, 16 families of 8 (no bank select)", false, false,
            Model.GeneralMidi.Instruments.Select((n, i) => new Patch(0, 0, i, n, Families[i / 8])).ToList());

        public static readonly InstrumentBank GsDrums = Kits("Drum kits (GS)", "Roland GS and the Windows GS synth: the kit is the program", 0, 0, false,
            (0, "Standard"), (8, "Room"), (16, "Power"), (24, "Electronic"), (25, "TR-808"), (32, "Jazz"), (40, "Brush"),
            (48, "Orchestra"), (56, "SFX"), (127, "CM-64/32L"));

        public static readonly InstrumentBank Gm2Drums = Kits("Drum kits (GM2)", "General MIDI 2: bank MSB 120", 120, 0, true,
            (0, "Standard"), (8, "Room"), (16, "Power"), (24, "Electronic"), (25, "Analog"), (32, "Jazz"), (40, "Brush"),
            (48, "Orchestra"), (56, "SFX"));

        public static readonly InstrumentBank XgDrums = new("Drum kits (XG)", "Yamaha XG: bank MSB 127 (SFX kits 126)", true, true,
        [
            new(127, 0, 0, "Standard Kit", "Kits"), new(127, 0, 1, "Standard Kit 2", "Kits"), new(127, 0, 8, "Room Kit", "Kits"),
            new(127, 0, 16, "Rock Kit", "Kits"), new(127, 0, 24, "Electro Kit", "Kits"), new(127, 0, 25, "Analog Kit", "Kits"),
            new(127, 0, 32, "Jazz Kit", "Kits"), new(127, 0, 40, "Brush Kit", "Kits"), new(127, 0, 48, "Classic Kit", "Kits"),
            new(126, 0, 0, "SFX Kit 1", "Kits"), new(126, 0, 1, "SFX Kit 2", "Kits"),
        ]);

        private static InstrumentBank Kits(string name, string hint, int msb, int lsb, bool sendsBank, params (int Program, string Name)[] kits) =>
            new(name, hint, true, sendsBank, kits.Select(k => new Patch(msb, lsb, k.Program, k.Name, "Kits")).ToList());

        /// <summary>The banks of the loaded SoundFont, melodic then drums.</summary>
        public static IReadOnlyList<InstrumentBank> SoundFontBanks { get; private set; } = [];

        /// <summary>File name of the loaded SoundFont, or null.</summary>
        public static string? SoundFontName { get; private set; }

        // (drums, msb, program) → preset name
        private static Dictionary<(bool, int, int), string> _soundFontNames = new();

        /// <summary>
        /// Use a SoundFont's presets. SoundFont bank 128 and up are drum kits (bank 128 + MSB on channel 10, as
        /// SoundFont players read them); the other banks are the MSB of a melodic sound.
        /// </summary>
        public static void SetSoundFont(string? name, IEnumerable<(int Bank, int Program, string Name)> presets)
        {
            var names = new Dictionary<(bool, int, int), string>();
            var banks = new List<InstrumentBank>();
            if (name != null)
            {
                var list = presets.Where(p => p.Program is >= 0 and <= 127 && p.Bank >= 0).ToList();
                foreach (var g in list.GroupBy(p => p.Bank).OrderBy(g => g.Key >= 128).ThenBy(g => g.Key))
                {
                    bool drums = g.Key >= 128;
                    int msb = Math.Min(127, drums ? g.Key - 128 : g.Key);
                    var patches = g.OrderBy(p => p.Program)
                                   .Select(p => new Patch(msb, 0, p.Program, p.Name.Trim(), drums ? "Kits" : Families[p.Program / 8]))
                                   .ToList();
                    foreach (var p in patches) names.TryAdd((drums, msb, p.Program), p.Name);
                    string title = drums ? (msb == 0 ? "Drum kits" : $"Drum kits, bank {msb}") : $"Bank {msb}";
                    banks.Add(new InstrumentBank($"SoundFont: {title}", $"{name}, {patches.Count} presets", drums, msb != 0, patches));
                }
            }
            _soundFontNames = names;
            SoundFontBanks = banks;
            SoundFontName = name;
        }

        /// <summary>Every bank, the ones for this kind of track (drums or not) first.</summary>
        public static IReadOnlyList<InstrumentBank> Banks(bool drums)
        {
            var all = new List<InstrumentBank> { GeneralMidi, GsDrums, Gm2Drums, XgDrums };
            all.AddRange(SoundFontBanks);
            return all.OrderBy(b => b.Drums != drums).ToList();
        }

        /// <summary>The name of a sound: the SoundFont's preset, a known drum kit, or the General MIDI name.</summary>
        public static string Name(int? msb, int? lsb, int program, bool drums)
        {
            program = Math.Clamp(program, 0, 127);
            int m = msb ?? 0, l = lsb ?? 0;
            if (_soundFontNames.TryGetValue((drums, m, program), out var sf)) return sf;
            if (drums)
            {
                // The kit of this bank, else the GS kit of the program (most devices ignore the drum bank).
                var kit = new[] { GsDrums, Gm2Drums, XgDrums }.SelectMany(b => b.Patches).FirstOrDefault(p => p.Msb == m && p.Program == program)
                          ?? GsDrums.Patches.FirstOrDefault(p => p.Program == program);
                if (kit == null) return $"Kit {program + 1}";
                return kit.Name.Contains("Kit", StringComparison.Ordinal) ? kit.Name : kit.Name + " kit";
            }
            string gm = Model.GeneralMidi.Instruments[program];
            return m == 0 && l == 0 ? gm : $"{gm} (variation)";
        }

        /// <summary>"Bank 8/0 · 25 Nylon Guitar": the bank when sent, the program counted from 1 and the name.</summary>
        public static string Label(InstrumentChoice c, bool drums)
        {
            string bank = c.HasBank ? $"Bank {c.Msb ?? 0}/{c.Lsb ?? 0} · " : "";
            return $"{bank}{c.Program + 1} {Name(c.Msb, c.Lsb, c.Program, drums)}";
        }
    }
}
