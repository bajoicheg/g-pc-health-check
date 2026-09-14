namespace G.PcHealthCheck;

public sealed partial class MainForm
{
    // MainForm's plain panels include the custom-painted rounded cards. Their border
    // depends on ClientRectangle, so every size change must invalidate the full panel
    // instead of only the newly exposed strip; otherwise the old right edge can remain.
    private sealed class Panel : System.Windows.Forms.Panel
    {
        public Panel()
        {
            ResizeRedraw = true;
        }
    }
}
