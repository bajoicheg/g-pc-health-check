using System.ComponentModel;
using System.Reflection;

namespace G.PcHealthCheck;

internal static class HumanSizePresentationSelfTest
{
    public static int Run()
    {
        try
        {
            var temp = new TempPreviewSnapshot
            {
                State = ReviewCollectionState.Complete,
                CandidateBytes = 1_572_864,
                CandidateFiles = 2,
                LargestFiles =
                [
                    new() { Path = @"C:\Synthetic\one.bin", Bytes = 1_572_864, LastWriteTime = DateTime.Now },
                    new() { Path = @"C:\Synthetic\two.bin", Bytes = 2_097_152, LastWriteTime = DateTime.Now }
                ]
            };
            var tempHtml = ReviewReport.Html(temp);
            Require(tempHtml.Contains("Размер, MB", StringComparison.Ordinal), "Temp HTML still labels file sizes as bytes.");
            Require(tempHtml.Contains("1,5 MB", StringComparison.Ordinal) || tempHtml.Contains("1.5 MB", StringComparison.Ordinal), "Temp HTML does not present file size in MB.");
            using (var form = new ReadOnlyReviewForm(true, 3))
            {
                form.DisplaySnapshot(temp);
                var grid = (DataGridView)typeof(ReadOnlyReviewForm).GetField("_grid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                Require(grid.Columns["Bytes"].HeaderText == "Размер, MB", "Temp grid still labels size as bytes.");
                Require(grid.Columns["Bytes"].ValueType == typeof(double), "Temp grid MB value is not numeric.");
                grid.Sort(grid.Columns["Bytes"], ListSortDirection.Descending);
                Require(Math.Abs(Convert.ToDouble(grid.Rows[0].Cells["Bytes"].Value) - 2d) < 0.0001, "Temp grid lost numeric size sorting after MB presentation.");
            }

            var folder = new FolderUsageSnapshot
            {
                Root = @"C:\Synthetic",
                Outcome = "Completed",
                Folders =
                [
                    new() { Path = @"C:\Synthetic", ParentIndex = -1, Bytes = 3_145_728m, OwnBytes = 1_048_576m, Files = 3, OwnFiles = 1, Incomplete = false },
                    new() { Path = @"C:\Synthetic\Child", ParentIndex = 0, Bytes = 1_572_864m, OwnBytes = 524_288m, Files = 2, OwnFiles = 1, Incomplete = false }
                ],
                LargestFiles = [new(@"C:\Synthetic\Child\large.bin", 1_048_576, DateTimeOffset.Now)]
            };
            var storageHtml = StorageReviewReport.Html(folder);
            Require(storageHtml.Contains("Всего, MB", StringComparison.Ordinal), "Folder HTML still labels total sizes as bytes.");
            Require(storageHtml.Contains("Собственные, MB", StringComparison.Ordinal), "Folder HTML still labels own sizes as bytes.");
            Require(storageHtml.Contains("Размер, MB", StringComparison.Ordinal), "Largest-file HTML still labels size as bytes.");
            Require(!storageHtml.Contains("Всего байт", StringComparison.Ordinal) && !storageHtml.Contains("<th>Байт</th>", StringComparison.Ordinal), "Raw-byte headers remain in folder HTML.");
            using (var form = new StorageReviewForm(true))
            {
                form.DisplaySnapshot(folder);
                var grid = (DataGridView)typeof(StorageReviewForm).GetField("_grid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                Require(grid.Columns["Bytes"].HeaderText == "Всего, MB", "Folder grid still labels total size as bytes.");
                Require(grid.Columns["Bytes"].ValueType == typeof(double), "Folder grid MB value is not numeric.");
                Require(Math.Abs(Convert.ToDouble(grid.Rows[0].Cells["Bytes"].Value) - 3d) < 0.0001, "Folder grid did not convert bytes to MB.");
            }

            Console.WriteLine("Human-size presentation self-test passed: 12/12.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Human-size presentation self-test failed: " + ex.GetBaseException().Message);
            return 235;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
