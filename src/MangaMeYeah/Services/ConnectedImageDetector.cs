using System.Buffers;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using MangaMeYeah.Models;

namespace MangaMeYeah.Services;

public sealed record ConnectedImageGroupResult(
    int ImageCount,
    PageDisplayMode SuggestedMode,
    SpreadDirection SuggestedDirection,
    string Description,
    IReadOnlyList<int>? PermutedSlotIndices = null);

public static partial class ConnectedImageDetector
{
    // Matches explicit split suffixes like: foo_1, foo_2, foo-a, foo-b, foo(1), foo_part1, foo_L, foo_R
    [GeneratedRegex(@"^(?<stem>.+?)[_\-\s\(]+(?<part>[0-9]{1,2}|[a-dA-D]|left|right|top|bottom|upper|lower|part\s*[1-4])\)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SplitSuffixRegex();

    public static ConnectedImageGroupResult? DetectConnectedGroup(
        IReadOnlyList<string> files,
        int startIndex,
        PageCacheEngine cacheEngine,
        ViewerSettings settings)
    {
        // Requires preload enabled with at least 4 images forward as specified
        if (!settings.PreloadEnabled || settings.PreloadForwardCount < 4)
        {
            return null;
        }

        int remaining = files.Count - startIndex;
        if (remaining < 2)
        {
            return null;
        }

        int maxCheck = Math.Min(4, remaining);

        // Gather dimensions for up to 4 images
        var dims = new (int Width, int Height)[maxCheck];
        for (int i = 0; i < maxCheck; i++)
        {
            dims[i] = cacheEngine.GetImageDimensionsFast(files[startIndex + i]);
            if (dims[i].Width <= 1 || dims[i].Height <= 1)
            {
                maxCheck = i;
                break;
            }
        }

        if (maxCheck < 2) return null;

        // Check 4-image configurations first (Grid2x2, Horizontal4, Vertical4), then 3-image, then 2-image
        for (int count = maxCheck; count >= 2; count--)
        {
            bool explicitStemMatch = CheckAllShareExplicitSplitStem(files, startIndex, count);
            if (!explicitStemMatch)
            {
                // Only consider non-explicit suffix if filenames share a long common stem (> 4 chars)
                // and end in a strict consecutive numeric index
                if (!CheckAllShareConsecutiveNumericStem(files, startIndex, count))
                {
                    continue;
                }
            }

            bool sameHeight = true;
            bool sameWidth = true;
            for (int i = 1; i < count; i++)
            {
                if (Math.Abs(dims[i].Height - dims[0].Height) > 2) sameHeight = false;
                if (Math.Abs(dims[i].Width - dims[0].Width) > 2) sameWidth = false;
            }

            if (!sameHeight && !sameWidth)
            {
                continue;
            }

            // Load bitmaps from cache at fast analysis thumbnail resolution (160x160)
            var bitmaps = new BitmapSource[count];
            for (int i = 0; i < count; i++)
            {
                var entry = cacheEngine.GetOrLoadPage(files[startIndex + i], 160, 160, settings, countColors: false);
                bitmaps[i] = entry.Bitmap;
            }

            // 1. Check 2x2 Grid when count == 4 and all 4 have matching dimensions
            // Tests all 4! = 24 permutations to automatically restore shuffled (trick) uploads
            if (count == 4 && sameHeight && sameWidth)
            {
                if (TryDetectGrid2x2Permutations(bitmaps, explicitStemMatch, out var gridDir, out var bestPermutation))
                {
                    bool isNatural = bestPermutation[0] == 0 && bestPermutation[1] == 1 &&
                                     bestPermutation[2] == 2 && bestPermutation[3] == 3;
                    string desc = isNatural
                        ? "縦横2×2連結画像 (4枚)"
                        : "縦横2×2連結画像 (4枚・シャッフル配置自動復元)";

                    return new ConnectedImageGroupResult(
                        4,
                        PageDisplayMode.Grid2x2,
                        gridDir,
                        desc,
                        bestPermutation);
                }
            }

            // 2. Check Horizontal connection (count = 4, 3, 2)
            if (sameHeight)
            {
                if (TryDetectHorizontalChain(bitmaps, explicitStemMatch, out var horizDir))
                {
                    PageDisplayMode mode = count switch
                    {
                        4 => PageDisplayMode.Horizontal4,
                        3 => PageDisplayMode.Horizontal3,
                        _ => PageDisplayMode.TwoPage
                    };
                    string dirLabel = horizDir == SpreadDirection.RightToLeft ? "右開き" : "左開き";
                    return new ConnectedImageGroupResult(
                        count,
                        mode,
                        horizDir,
                        $"横{count}枚連結画像 ({dirLabel})");
                }
            }

            // 3. Check Vertical connection (count = 4, 3, 2)
            if (sameWidth)
            {
                if (TryDetectVerticalChain(bitmaps, explicitStemMatch, out var vertDir))
                {
                    PageDisplayMode mode = count switch
                    {
                        4 => PageDisplayMode.Vertical4,
                        3 => PageDisplayMode.Vertical3,
                        _ => PageDisplayMode.Vertical2
                    };
                    return new ConnectedImageGroupResult(
                        count,
                        mode,
                        vertDir,
                        $"縦{count}枚連結画像");
                }
            }
        }

        return null;
    }

    private static bool HasExplicitSplitStemMatch(string fileA, string fileB)
    {
        string nameA = Path.GetFileNameWithoutExtension(fileA);
        string nameB = Path.GetFileNameWithoutExtension(fileB);
        var mA = SplitSuffixRegex().Match(nameA);
        var mB = SplitSuffixRegex().Match(nameB);
        if (!mA.Success || !mB.Success) return false;
        return string.Equals(mA.Groups["stem"].Value, mB.Groups["stem"].Value, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CheckAllShareExplicitSplitStem(IReadOnlyList<string> files, int startIndex, int count)
    {
        string firstName = Path.GetFileNameWithoutExtension(files[startIndex]);
        var firstMatch = SplitSuffixRegex().Match(firstName);
        if (!firstMatch.Success) return false;

        string stem = firstMatch.Groups["stem"].Value;
        if (stem.Length < 1) return false;

        for (int i = 1; i < count; i++)
        {
            string name = Path.GetFileNameWithoutExtension(files[startIndex + i]);
            var m = SplitSuffixRegex().Match(name);
            if (!m.Success || !string.Equals(m.Groups["stem"].Value, stem, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }

    private static bool CheckAllShareCommonPrefix(IReadOnlyList<string> files, int startIndex, int count)
    {
        string first = Path.GetFileNameWithoutExtension(files[startIndex]);
        if (first.Length < 2) return false;

        int commonLen = first.Length;
        for (int i = 1; i < count; i++)
        {
            string cur = Path.GetFileNameWithoutExtension(files[startIndex + i]);
            int max = Math.Min(commonLen, cur.Length);
            int k = 0;
            while (k < max && char.ToUpperInvariant(first[k]) == char.ToUpperInvariant(cur[k]))
            {
                k++;
            }
            commonLen = k;
        }

        // At least 2 common leading characters in filename
        return commonLen >= 2;
    }

    private static bool CheckAllShareConsecutiveNumericStem(IReadOnlyList<string> files, int startIndex, int count)
    {
        string first = Path.GetFileNameWithoutExtension(files[startIndex]);
        var mFirst = Regex.Match(first, @"^(?<stem>.*?)(?<num>\d{1,4})$");
        if (!mFirst.Success) return false;

        string stem = mFirst.Groups["stem"].Value;
        if (!int.TryParse(mFirst.Groups["num"].Value, out int startNum)) return false;

        for (int i = 1; i < count; i++)
        {
            string cur = Path.GetFileNameWithoutExtension(files[startIndex + i]);
            var m = Regex.Match(cur, @"^(?<stem>.*?)(?<num>\d{1,4})$");
            if (!m.Success || !string.Equals(m.Groups["stem"].Value, stem, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (!int.TryParse(m.Groups["num"].Value, out int curNum) || curNum != startNum + i)
            {
                return false;
            }
        }
        return true;
    }

    private static readonly int[][] Permutations4 =
    [
        [0, 1, 2, 3], [0, 1, 3, 2], [0, 2, 1, 3], [0, 2, 3, 1], [0, 3, 1, 2], [0, 3, 2, 1],
        [1, 0, 2, 3], [1, 0, 3, 2], [1, 2, 0, 3], [1, 2, 3, 0], [1, 3, 0, 2], [1, 3, 2, 0],
        [2, 0, 1, 3], [2, 0, 3, 1], [2, 1, 0, 3], [2, 1, 3, 0], [2, 3, 0, 1], [2, 3, 1, 0],
        [3, 0, 1, 2], [3, 0, 2, 1], [3, 1, 0, 2], [3, 1, 2, 0], [3, 2, 0, 1], [3, 2, 1, 0]
    ];

    private static bool TryDetectHorizontalChain(BitmapSource[] bitmaps, bool explicitStemMatch, out SpreadDirection direction)
    {
        direction = SpreadDirection.LeftToRight;
        int count = bitmaps.Length;

        double totalMaeLtr = 0;
        double totalMaeRtl = 0;
        double minEdgeStdDev = double.MaxValue;

        for (int i = 0; i < count - 1; i++)
        {
            var leftRightA = ExtractVerticalEdges(bitmaps[i]);
            var leftRightB = ExtractVerticalEdges(bitmaps[i + 1]);
            if (leftRightA == null || leftRightB == null) return false;

            double maeLtr = ComputeEdgeMae(leftRightA.Value.RightEdge, leftRightB.Value.LeftEdge);
            double maeRtl = ComputeEdgeMae(leftRightA.Value.LeftEdge, leftRightB.Value.RightEdge);
            double stdA = Math.Max(ComputeEdgeStdDev(leftRightA.Value.RightEdge), ComputeEdgeStdDev(leftRightA.Value.LeftEdge));
            double stdB = Math.Max(ComputeEdgeStdDev(leftRightB.Value.RightEdge), ComputeEdgeStdDev(leftRightB.Value.LeftEdge));

            totalMaeLtr += maeLtr;
            totalMaeRtl += maeRtl;
            minEdgeStdDev = Math.Min(minEdgeStdDev, Math.Min(stdA, stdB));
        }

        double avgMaeLtr = totalMaeLtr / (count - 1);
        double avgMaeRtl = totalMaeRtl / (count - 1);
        double bestMae = Math.Min(avgMaeLtr, avgMaeRtl);
        direction = avgMaeRtl < avgMaeLtr ? SpreadDirection.RightToLeft : SpreadDirection.LeftToRight;

        // 誤検出防止: 同一プロンプトのAI連作（空や白背景）を弾くため閾値を厳格化
        // 実際の分割画像は境界MAEが極小(通常<12)。色合いが似ているだけの別絵(25〜40)を確実に除外。
        if (explicitStemMatch && bestMae < 14.0 && minEdgeStdDev > 10.0)
        {
            return true;
        }

        return minEdgeStdDev > 14.0 && bestMae < 8.0;
    }

    private static bool TryDetectVerticalChain(BitmapSource[] bitmaps, bool explicitStemMatch, out SpreadDirection direction)
    {
        direction = SpreadDirection.LeftToRight;
        int count = bitmaps.Length;

        double totalMaeTopToBottom = 0;
        double totalMaeBottomToTop = 0;
        double minEdgeStdDev = double.MaxValue;

        for (int i = 0; i < count - 1; i++)
        {
            var edgesA = ExtractHorizontalEdges(bitmaps[i]);
            var edgesB = ExtractHorizontalEdges(bitmaps[i + 1]);
            if (edgesA == null || edgesB == null) return false;

            double maeTB = ComputeEdgeMae(edgesA.Value.BottomEdge, edgesB.Value.TopEdge);
            double maeBT = ComputeEdgeMae(edgesA.Value.TopEdge, edgesB.Value.BottomEdge);
            double stdA = Math.Max(ComputeEdgeStdDev(edgesA.Value.BottomEdge), ComputeEdgeStdDev(edgesA.Value.TopEdge));
            double stdB = Math.Max(ComputeEdgeStdDev(edgesB.Value.BottomEdge), ComputeEdgeStdDev(edgesB.Value.TopEdge));

            totalMaeTopToBottom += maeTB;
            totalMaeBottomToTop += maeBT;
            minEdgeStdDev = Math.Min(minEdgeStdDev, Math.Min(stdA, stdB));
        }

        double avgTB = totalMaeTopToBottom / (count - 1);
        double avgBT = totalMaeBottomToTop / (count - 1);
        double bestMae = Math.Min(avgTB, avgBT);
        direction = avgBT < avgTB ? SpreadDirection.RightToLeft : SpreadDirection.LeftToRight;

        if (explicitStemMatch && bestMae < 14.0 && minEdgeStdDev > 10.0)
        {
            return true;
        }

        return minEdgeStdDev > 14.0 && bestMae < 8.0;
    }

    private static bool TryDetectGrid2x2Permutations(
        BitmapSource[] bitmaps,
        bool explicitStemMatch,
        out SpreadDirection direction,
        out IReadOnlyList<int> bestPermutation)
    {
        direction = SpreadDirection.LeftToRight;
        bestPermutation = [0, 1, 2, 3];
        if (bitmaps.Length < 4) return false;

        var vEdges = new (byte[] LeftEdge, byte[] RightEdge)?[4];
        var hEdges = new (byte[] TopEdge, byte[] BottomEdge)?[4];

        for (int i = 0; i < 4; i++)
        {
            vEdges[i] = ExtractVerticalEdges(bitmaps[i]);
            hEdges[i] = ExtractHorizontalEdges(bitmaps[i]);
            if (vEdges[i] == null || hEdges[i] == null) return false;
        }

        double minStdDev = double.MaxValue;
        for (int i = 0; i < 4; i++)
        {
            minStdDev = Math.Min(minStdDev, ComputeEdgeStdDev(vEdges[i]!.Value.LeftEdge));
            minStdDev = Math.Min(minStdDev, ComputeEdgeStdDev(vEdges[i]!.Value.RightEdge));
            minStdDev = Math.Min(minStdDev, ComputeEdgeStdDev(hEdges[i]!.Value.TopEdge));
            minStdDev = Math.Min(minStdDev, ComputeEdgeStdDev(hEdges[i]!.Value.BottomEdge));
        }

        double bestMae = double.MaxValue;
        double secondBestMae = double.MaxValue;
        int[]? bestPerm = null;

        // 全 24 通りの配置 (TL, TR, BL, BR) を探索し、十字の継ぎ目4箇所が最も連続する順列を判定
        foreach (var p in Permutations4)
        {
            int tl = p[0], tr = p[1], bl = p[2], br = p[3];

            double sTop = ComputeEdgeMae(vEdges[tl]!.Value.RightEdge, vEdges[tr]!.Value.LeftEdge);
            double sBot = ComputeEdgeMae(vEdges[bl]!.Value.RightEdge, vEdges[br]!.Value.LeftEdge);
            double sLeft = ComputeEdgeMae(hEdges[tl]!.Value.BottomEdge, hEdges[bl]!.Value.TopEdge);
            double sRight = ComputeEdgeMae(hEdges[tr]!.Value.BottomEdge, hEdges[br]!.Value.TopEdge);

            double score = (sTop + sBot + sLeft + sRight) * 0.25;

            if (score < bestMae)
            {
                secondBestMae = bestMae;
                bestMae = score;
                bestPerm = p;
            }
            else if (score < secondBestMae)
            {
                secondBestMae = score;
            }
        }

        if (bestPerm == null) return false;

        bool isNaturalOrder = bestPerm[0] == 0 && bestPerm[1] == 1 && bestPerm[2] == 2 && bestPerm[3] == 3;

        // 確信度判定: 正解順列は4箇所の継ぎ目が同時に合致するためMAEが突出して低くなる
        bool passSeam = explicitStemMatch ? bestMae < 17.5 : bestMae < 12.0;
        bool passConfidence = isNaturalOrder || (secondBestMae - bestMae >= 1.0);
        bool passTexture = minStdDev > 10.0;

        if (passSeam && passConfidence && passTexture)
        {
            bestPermutation = bestPerm;
            return true;
        }

        return false;
    }

    private static (byte[] LeftEdge, byte[] RightEdge)? ExtractVerticalEdges(BitmapSource bmp)
    {
        int w = bmp.PixelWidth;
        int h = bmp.PixelHeight;
        int bpp = (bmp.Format.BitsPerPixel + 7) / 8;
        if (w < 4 || h < 4 || bpp < 1) return null;

        int stride = (w * bpp + 3) & ~3;
        int bufLen = stride * h;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(bufLen);
        try
        {
            bmp.CopyPixels(buffer, stride, 0);
            int samples = Math.Min(h, 128);
            byte[] left = new byte[samples * 3];
            byte[] right = new byte[samples * 3];

            for (int s = 0; s < samples; s++)
            {
                int y = (s * (h - 1)) / Math.Max(1, samples - 1);
                int rowOff = y * stride;
                int rightOff = rowOff + (w - 1) * bpp;

                if (bpp >= 3)
                {
                    left[s * 3] = buffer[rowOff];
                    left[s * 3 + 1] = buffer[rowOff + 1];
                    left[s * 3 + 2] = buffer[rowOff + 2];

                    right[s * 3] = buffer[rightOff];
                    right[s * 3 + 1] = buffer[rightOff + 1];
                    right[s * 3 + 2] = buffer[rightOff + 2];
                }
                else
                {
                    left[s * 3] = left[s * 3 + 1] = left[s * 3 + 2] = buffer[rowOff];
                    right[s * 3] = right[s * 3 + 1] = right[s * 3 + 2] = buffer[rightOff];
                }
            }
            return (left, right);
        }
        catch
        {
            return null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static (byte[] TopEdge, byte[] BottomEdge)? ExtractHorizontalEdges(BitmapSource bmp)
    {
        int w = bmp.PixelWidth;
        int h = bmp.PixelHeight;
        int bpp = (bmp.Format.BitsPerPixel + 7) / 8;
        if (w < 4 || h < 4 || bpp < 1) return null;

        int stride = (w * bpp + 3) & ~3;
        int bufLen = stride * h;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(bufLen);
        try
        {
            bmp.CopyPixels(buffer, stride, 0);
            int samples = Math.Min(w, 128);
            byte[] top = new byte[samples * 3];
            byte[] bottom = new byte[samples * 3];
            int bottomRowOff = (h - 1) * stride;

            for (int s = 0; s < samples; s++)
            {
                int x = (s * (w - 1)) / Math.Max(1, samples - 1);
                int topOff = x * bpp;
                int botOff = bottomRowOff + x * bpp;

                if (bpp >= 3)
                {
                    top[s * 3] = buffer[topOff];
                    top[s * 3 + 1] = buffer[topOff + 1];
                    top[s * 3 + 2] = buffer[topOff + 2];

                    bottom[s * 3] = buffer[botOff];
                    bottom[s * 3 + 1] = buffer[botOff + 1];
                    bottom[s * 3 + 2] = buffer[botOff + 2];
                }
                else
                {
                    top[s * 3] = top[s * 3 + 1] = top[s * 3 + 2] = buffer[topOff];
                    bottom[s * 3] = bottom[s * 3 + 1] = bottom[s * 3 + 2] = buffer[botOff];
                }
            }
            return (top, bottom);
        }
        catch
        {
            return null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static double ComputeEdgeMae(byte[] edgeA, byte[] edgeB)
    {
        int len = Math.Min(edgeA.Length, edgeB.Length);
        if (len == 0) return 255.0;
        long sum = 0;
        for (int i = 0; i < len; i++)
        {
            sum += Math.Abs(edgeA[i] - edgeB[i]);
        }
        return (double)sum / len;
    }

    private static double ComputeEdgeStdDev(byte[] edge)
    {
        if (edge.Length == 0) return 0;
        double sum = 0;
        double sumSq = 0;
        for (int i = 0; i < edge.Length; i++)
        {
            double v = edge[i];
            sum += v;
            sumSq += v * v;
        }
        double mean = sum / edge.Length;
        double variance = Math.Max(0, (sumSq / edge.Length) - (mean * mean));
        return Math.Sqrt(variance);
    }
}
