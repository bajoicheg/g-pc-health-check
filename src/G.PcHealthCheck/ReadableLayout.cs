namespace G.PcHealthCheck;
// Presentation geometry and RAM display band only; no telemetry, scoring or action semantics.
internal static class ReadableLayout
{
 internal static (int Columns,int Rows,int Height) Metrics(int width,float scale,int count=7)
 {
  scale=Math.Max(1,scale);count=Math.Max(1,count);
  int columns=Math.Clamp(width/(int)Math.Ceiling(165*scale),1,count);
  int rows=(count+columns-1)/columns;
  return(columns,rows,(int)Math.Ceiling(112*scale)*rows);
 }
 internal static (int Width,int Height) WindowSize(int width,int height,int availableWidth,int availableHeight)
 {
  return (Math.Clamp(width,1,Math.Max(1,availableWidth)),Math.Clamp(height,1,Math.Max(1,availableHeight)));
 }
 internal static string MemoryBand(double? available,double critical,double warning)
 {
  if(available is not double value || !double.IsFinite(value))return "UNKNOWN";
  return value<=critical?"CRIT":value<=warning?"WARN":"OK";
 }
 internal static int Split(int height,float scale,double ratio=.62)
 {
  int upper=(int)Math.Ceiling(90*Math.Max(1,scale)),lower=(int)Math.Ceiling(80*Math.Max(1,scale));
  if(height<upper+lower)return Math.Max(0,height/2);
  return Math.Clamp((int)Math.Round(height*Math.Clamp(ratio,.51,.80)),upper,height-lower);
 }
}
