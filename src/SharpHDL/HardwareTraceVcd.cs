using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SharpHDL
{
    /// <summary>Exports post-edge debugger watch samples as a basic IEEE VCD trace.</summary>
    public static class HardwareTraceVcd
    {
        public static string Export(IReadOnlyList<HardwareTraceSample> samples, IReadOnlyDictionary<string, int> widths)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (widths == null) throw new ArgumentNullException(nameof(widths));
            var names = widths.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var ids = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < names.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(names[i]) || names[i].Any(c => !(char.IsLetterOrDigit(c) || c == '_')))
                    throw new ArgumentException("VCD watch names must be simple identifiers.");
                int width = widths[names[i]];
                if (width < 1 || width > 4096) throw new ArgumentOutOfRangeException(nameof(widths));
                ids[names[i]] = "v" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            var writer = new StringBuilder();
            writer.AppendLine("$version SharpHDL watch trace $end");
            writer.AppendLine("$timescale 1ns $end");
            writer.AppendLine("$scope module sharp_debug $end");
            foreach (var name in names)
                writer.AppendLine("$var wire " + widths[name] + " " + ids[name] + " " + name + " $end");
            writer.AppendLine("$upscope $end");
            writer.AppendLine("$enddefinitions $end");
            long lastCycle = -1;
            foreach (var sample in samples.OrderBy(x => x.Cycle))
            {
                if (!ids.TryGetValue(sample.Name, out var id))
                    throw new ArgumentException("Trace contains an undeclared watch: " + sample.Name);
                if (sample.Cycle < 0) throw new ArgumentOutOfRangeException(nameof(samples));
                int width = widths[sample.Name];
                if (sample.Value < 0 || sample.Value > Expr.Mask(width))
                    throw new ArgumentOutOfRangeException(nameof(samples), "Trace value exceeds watch width.");
                if (sample.Cycle != lastCycle)
                {
                    writer.Append('#').Append(sample.Cycle).AppendLine();
                    lastCycle = sample.Cycle;
                }
                var bits = new char[width];
                var value = sample.Value;
                for (int i = width - 1; i >= 0; i--)
                {
                    bits[i] = value.IsEven ? '0' : '1';
                    value >>= 1;
                }
                writer.Append('b').Append(bits).Append(' ').Append(id).AppendLine();
            }
            return writer.ToString();
        }
    }
}
