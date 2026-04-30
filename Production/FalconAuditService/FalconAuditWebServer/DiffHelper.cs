namespace FalconAuditService;

using System.Text;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

public static class DiffHelper
{
    private const int ContextLines = 3;

    public static string? UnifiedDiff(
        string?  oldText,
        string?  newText,
        string   fileName,
        DateTime oldTime = default,
        DateTime newTime = default)
    {
        if (oldText is null || newText is null) return null;

        var diff = InlineDiffBuilder.Diff(oldText, newText);
        if (!diff.HasDifferences) return null;

        var lines = diff.Lines;
        int n     = lines.Count;

        var inHunk = new bool[n];
        for (int i = 0; i < n; i++)
        {
            if (lines[i].Type == ChangeType.Unchanged) continue;
            for (int j = Math.Max(0, i - ContextLines);
                     j < Math.Min(n, i + ContextLines + 1); j++)
                inHunk[j] = true;
        }

        var sb    = new StringBuilder();
        var oldTs = oldTime == default ? "" : $"  {oldTime:O}";
        var newTs = newTime == default ? "" : $"  {newTime:O}";
        sb.AppendLine($"--- {fileName}{oldTs} (before)");
        sb.AppendLine($"+++ {fileName}{newTs} (after)");

        int oldNo = 1, newNo = 1, i2 = 0;

        while (i2 < n)
        {
            if (!inHunk[i2])
            {
                if (lines[i2].Type != ChangeType.Inserted) oldNo++;
                if (lines[i2].Type != ChangeType.Deleted)  newNo++;
                i2++;
                continue;
            }

            int start = i2;
            while (i2 < n && inHunk[i2]) i2++;
            int end = i2;

            var hunk   = lines.GetRange(start, end - start);
            int oldCnt = hunk.Count(l => l.Type != ChangeType.Inserted);
            int newCnt = hunk.Count(l => l.Type != ChangeType.Deleted);

            sb.AppendLine($"@@ -{oldNo},{oldCnt} +{newNo},{newCnt} @@");

            foreach (var line in hunk)
            {
                char pfx = line.Type switch
                {
                    ChangeType.Inserted => '+',
                    ChangeType.Deleted  => '-',
                    _                   => ' '
                };
                sb.AppendLine($"{pfx}{line.Text}");
                if (line.Type != ChangeType.Inserted) oldNo++;
                if (line.Type != ChangeType.Deleted)  newNo++;
            }
        }

        return sb.ToString().TrimEnd();
    }
}
