namespace G.PcHealthCheck;

internal static class BrandAssets
{
    private const string ShieldResourceName = "G.PcHealthCheck.g-shield.png";

    public static Image? LoadShield()
    {
        var assembly = typeof(BrandAssets).Assembly;
        using var stream = assembly.GetManifestResourceStream(ShieldResourceName);
        if (stream is null) return null;

        using var source = Image.FromStream(stream, useEmbeddedColorManagement: true, validateImageData: true);
        var bitmap = new Bitmap(source);
        RemoveConnectedCornerBackground(bitmap);
        return bitmap;
    }

    private static void RemoveConnectedCornerBackground(Bitmap bitmap)
    {
        if (bitmap.Width == 0 || bitmap.Height == 0) return;
        var reference = bitmap.GetPixel(0, 0);
        if (reference.A == 0) return;

        const int tolerance = 18;
        bool Matches(Color color)
            => color.A > 0
               && Math.Abs(color.R - reference.R) <= tolerance
               && Math.Abs(color.G - reference.G) <= tolerance
               && Math.Abs(color.B - reference.B) <= tolerance;

        var visited = new bool[bitmap.Width * bitmap.Height];
        var queue = new Queue<Point>();
        void Enqueue(int x, int y)
        {
            if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Height) return;
            var index = y * bitmap.Width + x;
            if (visited[index]) return;
            visited[index] = true;
            if (Matches(bitmap.GetPixel(x, y))) queue.Enqueue(new Point(x, y));
        }

        for (var x = 0; x < bitmap.Width; x++) { Enqueue(x, 0); Enqueue(x, bitmap.Height - 1); }
        for (var y = 0; y < bitmap.Height; y++) { Enqueue(0, y); Enqueue(bitmap.Width - 1, y); }

        while (queue.Count > 0)
        {
            var point = queue.Dequeue();
            var pixel = bitmap.GetPixel(point.X, point.Y);
            bitmap.SetPixel(point.X, point.Y, Color.FromArgb(0, pixel.R, pixel.G, pixel.B));
            Enqueue(point.X - 1, point.Y);
            Enqueue(point.X + 1, point.Y);
            Enqueue(point.X, point.Y - 1);
            Enqueue(point.X, point.Y + 1);
        }
    }
}
