using System.Reflection;

namespace G.PcHealthCheck;

internal static class MainCardResizeRedrawSelfTest
{
    public static int Run()
    {
        try
        {
            var cardFactory = typeof(MainForm).GetMethod("Card", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("MainForm.Card factory is missing.");
            using var card = cardFactory.Invoke(null, null) as Panel
                ?? throw new InvalidOperationException("MainForm.Card did not return a Panel.");

            var getStyle = typeof(Control).GetMethod(
                "GetStyle",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(ControlStyles) },
                modifiers: null)
                ?? throw new InvalidOperationException("Control.GetStyle is unavailable.");

            var resizeRedraw = (bool)(getStyle.Invoke(card, new object[] { ControlStyles.ResizeRedraw }) ?? false);
            if (!resizeRedraw)
                throw new InvalidOperationException("Main dashboard rounded card does not request a full redraw when its size changes.");

            Console.WriteLine("Main card resize redraw self-test passed: 1/1.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Main card resize redraw self-test failed: " + ex.GetBaseException().Message);
            return 239;
        }
    }
}
