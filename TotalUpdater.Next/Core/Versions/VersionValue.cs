using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace TotalUpdater.Next.Core.Versions
{
    public enum VersionComparison { Less, Equal, Greater, Unknown }
    public enum VersionStage { Beta = 0, Rc = 1, Final = 2 }

    public sealed class VersionValue
    {
        private static readonly Regex Pattern = new Regex(@"^\s*(?<numbers>\d+(?:\s*[.,]\s*\d+){0,3})(?:\s*(?<stage>alpha|a|beta|b|rc)\s*(?<stageNumber>\d*)?)?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        public static readonly VersionValue Unknown = new VersionValue("", new int[0], VersionStage.Final, "", 0, false);

        private VersionValue(string raw, IReadOnlyList<int> numbers, VersionStage stage, string stageLabel, int stageNumber, bool isKnown)
        {
            Raw = raw; Numbers = numbers; Stage = stage; StageLabel = stageLabel; StageNumber = stageNumber; IsKnown = isKnown;
        }

        public string Raw { get; private set; }
        public IReadOnlyList<int> Numbers { get; private set; }
        public VersionStage Stage { get; private set; }
        public string StageLabel { get; private set; }
        public int StageNumber { get; private set; }
        public bool IsKnown { get; private set; }

        public static VersionValue Parse(string raw)
        {
            if (String.IsNullOrWhiteSpace(raw)) return Unknown;
            var match = Pattern.Match(raw.Trim());
            if (!match.Success) return Unknown;
            var numbers = match.Groups["numbers"].Value.Replace(',', '.').Split('.').Select(x => Int32.Parse(x.Trim(), CultureInfo.InvariantCulture)).ToList();
            var stageText = match.Groups["stage"].Value;
            var stage = stageText.Equals("rc", StringComparison.OrdinalIgnoreCase) ? VersionStage.Rc :
                String.IsNullOrWhiteSpace(stageText) ? VersionStage.Final : VersionStage.Beta;
            var stageNumberText = match.Groups["stageNumber"].Value;
            var stageNumber = String.IsNullOrEmpty(stageNumberText) ? 0 : Int32.Parse(stageNumberText, CultureInfo.InvariantCulture);
            return new VersionValue(match.Value.Trim(), numbers, stage, stageText.ToLowerInvariant(), stageNumber, true);
        }

        public VersionComparison CompareTo(VersionValue other)
        {
            if (!IsKnown || other == null || !other.IsKnown) return VersionComparison.Unknown;
            var length = Math.Max(Numbers.Count, other.Numbers.Count);
            for (var index = 0; index < length; index++)
            {
                var left = index < Numbers.Count ? Numbers[index] : 0;
                var right = index < other.Numbers.Count ? other.Numbers[index] : 0;
                if (left < right) return VersionComparison.Less;
                if (left > right) return VersionComparison.Greater;
            }
            if (Stage < other.Stage) return VersionComparison.Less;
            if (Stage > other.Stage) return VersionComparison.Greater;
            var labels = String.Compare(StageLabel, other.StageLabel, StringComparison.OrdinalIgnoreCase);
            if (labels < 0) return VersionComparison.Less;
            if (labels > 0) return VersionComparison.Greater;
            if (StageNumber < other.StageNumber) return VersionComparison.Less;
            if (StageNumber > other.StageNumber) return VersionComparison.Greater;
            return VersionComparison.Equal;
        }
    }
}
