namespace G.PcHealthCheck;
// Geometry only: no telemetry, severity, action or worker semantics.
internal static class ReadableLayout
{
 internal static (int Columns,int Rows,int Height) Metrics(int width,float scale,int count=7)
 {
  scale=Math.Max(1,scale);count=Math.Max(1,count);
  int columns=Math.Clamp(width/(int)Math.Ceiling(165*scale),1,count);
  int rows=(count+columns-1)/columns;
  return(columns,rows,(int)Math.Ceiling(112*scale)*rows);
 }
 internal static int Split(int height,float scale,double ratio=.62)
 {
  int upper=(int)Math.Ceiling(90*Math.Max(1,scale)),lower=(int)Math.Ceiling(80*Math.Max(1,scale));
  if(height<upper+lower)return Math.Max(0,height/2);
  return Math.Clamp((int)Math.Round(height*Math.Clamp(ratio,.51,.80)),upper,height-lower);
 }
}
