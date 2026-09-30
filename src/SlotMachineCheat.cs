using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Donteco.Minigames;
using UnityEngine;

namespace GhostWatchersTrainer
{
    // The lobby slot machine rolls locally: value = Random.value, then the first win line
    // with value <= Probability wins, otherwise the roll fails. Forcing an outcome = rewriting
    // the Probability thresholds (earlier lines -> never, chosen line -> always).
    internal static class SlotMachineCheat
    {
        private static readonly FieldInfo WinLinesField =
            typeof(SlotMachineMiniGame).GetField("winLines", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly Dictionary<SlotsLinesWin, float> original = new Dictionary<SlotsLinesWin, float>();

        // -1 = normal odds, otherwise index into the win lines
        public static int ForcedIndex = -1;

        public static SlotsLinesWin[] Lines()
        {
            SlotMachineMiniGame machine = Object.FindObjectOfType<SlotMachineMiniGame>();
            return machine != null ? (SlotsLinesWin[])WinLinesField.GetValue(machine) : null;
        }

        public static float OriginalProbability(SlotsLinesWin line)
        {
            return original.TryGetValue(line, out float p) ? p : line.Probability;
        }

        // Called regularly: machines only exist in the lobby and get recreated on scene load
        public static void Apply()
        {
            foreach (SlotMachineMiniGame machine in Object.FindObjectsOfType<SlotMachineMiniGame>())
            {
                SlotsLinesWin[] lines = (SlotsLinesWin[])WinLinesField.GetValue(machine);
                if (lines == null) continue;
                for (int i = 0; i < lines.Length; i++)
                {
                    SlotsLinesWin line = lines[i];
                    if (!original.ContainsKey(line)) original[line] = line.Probability;

                    if (ForcedIndex < 0 || ForcedIndex >= lines.Length) line.Probability = original[line];
                    else if (i < ForcedIndex) line.Probability = -1f;
                    else if (i == ForcedIndex) line.Probability = 2f;
                    else line.Probability = original[line];
                }
            }
            // drop entries of destroyed machines
            if (original.Count > 64) original.Clear();
        }

        public static string Describe(SlotsLinesWin line)
        {
            string name = string.IsNullOrEmpty(line.Name) ? line.Item.ToString() : line.Name;
            return $"{name}  x{line.Multiply:0.##}";
        }
    }
}
