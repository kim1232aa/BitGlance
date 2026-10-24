// SPDX-License-Identifier: AGPL-3.0-only
// Modified 2026-10-24: PreviewMouseWheel zoom, visible-range high/low labels, compact mini chart,
// three EMA overlays, classic SuperTrend, top-left indicator legend with last values.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace BitGlance {
 public sealed class SuperTrendSeries {
  public double[] Values;
  public int[] Directions; // 1=up, -1=down, 0=unset
  public SuperTrendSeries(int n){Values=new double[n];Directions=new int[n];for(int i=0;i<n;i++)Values[i]=double.NaN;}
 }
 public sealed class PriceChart:FrameworkElement {
  List<Candle> data=new List<Candle>();
  public readonly ChartViewport View=new ChartViewport();
  public event Action ViewChanged;
  public List<Candle> Data {get{return data;}set{var rows=value??new List<Candle>();View.Update(data,rows);data=rows;Changed();}}
  public bool Dark,Candlesticks,Compact,ShowEma=true,ShowSuperTrend=true;
  public int IntervalMinutes=5;
  public int Ema1=7,Ema2=20,Ema3=50,StPeriod=10;
  public double StMult=3.0;
  public string PriceFormat="N2",Asset="BTC";
  double hover=-1,dragX;
  bool dragging;
  static readonly CultureInfo inv=CultureInfo.InvariantCulture;
  static readonly Brush orange=ColorBrush(233,129,45),up=ColorBrush(31,143,105),down=ColorBrush(209,84,75);
  static readonly Brush emaBlue=ColorBrush(91,141,239),emaOrange=ColorBrush(232,160,68),emaPurple=ColorBrush(176,124,198);
  static Brush ColorBrush(byte r,byte g,byte b){var bsh=new SolidColorBrush(Color.FromRgb(r,g,b));bsh.Freeze();return bsh;}
  Brush Muted {get{return Dark?ColorBrush(150,164,154):ColorBrush(130,137,128);}}
  Brush Ink {get{return Dark?ColorBrush(239,242,236):ColorBrush(36,43,41);}}
  Brush Surface {get{return Dark?ColorBrush(28,36,32):ColorBrush(252,251,248);}}
  double ScaleWidth {get{return Candlesticks&&!Compact?68:4;}}
  double PlotWidth {get{return Math.Max(10,ActualWidth-ScaleWidth);}}
  public PriceChart(){
   ClipToBounds=true;Focusable=true;
   MouseEnter+=(s,e)=>Focus();
   MouseMove+=(s,e)=>{
    var x=e.GetPosition(this).X;
    if(dragging&&Candlesticks&&View.Slots>0){double step=PlotWidth/View.Slots;int bars=(int)((dragX-x)/step);if(bars!=0){Pan(bars);dragX-=bars*step;}hover=-1;}
    else hover=x;
    InvalidateVisual();
   };
   MouseLeave+=(s,e)=>{hover=-1;InvalidateVisual();};
   MouseLeftButtonDown+=(s,e)=>{if(!Candlesticks||data.Count==0)return;Focus();dragX=e.GetPosition(this).X;dragging=CaptureMouse();Cursor=Cursors.SizeWE;e.Handled=true;};
   MouseLeftButtonUp+=(s,e)=>{if(!dragging)return;dragging=false;ReleaseMouseCapture();Cursor=Cursors.Cross;e.Handled=true;};
   LostMouseCapture+=(s,e)=>{dragging=false;Cursor=Candlesticks?Cursors.Cross:Cursors.Arrow;};
   MouseWheel+=(s,e)=>HandleWheel(e);
   PreviewMouseWheel+=(s,e)=>HandleWheel(e);
   KeyDown+=(s,e)=>{if(!Candlesticks)return;if(e.Key==Key.Left)Pan(-10);else if(e.Key==Key.Right)Pan(10);else if(e.Key==Key.Home)Latest();else if(e.Key==Key.Add||e.Key==Key.OemPlus)Zoom(1,0.5);else if(e.Key==Key.Subtract||e.Key==Key.OemMinus)Zoom(-1,0.5);else return;e.Handled=true;};
  }
  void HandleWheel(MouseWheelEventArgs e){if(!Candlesticks)return;Zoom(e.Delta/120,(e.GetPosition(this).X-2)/PlotWidth);e.Handled=true;}
  void Changed(){InvalidateVisual();if(ViewChanged!=null)ViewChanged();}
  public void Reset(){hover=-1;dragging=false;if(IsMouseCaptured)ReleaseMouseCapture();View.Reset();Changed();}
  public void Pan(int bars){View.Pan(bars);Changed();}
  public void Zoom(int steps,double anchor){View.Zoom(steps,anchor);Changed();}
  public void Latest(){View.Latest();Changed();}
  public List<Candle> Visible {get{return Candlesticks?data.Skip(View.First).Take(View.Count).ToList():data;}}
  public bool IsOpen(Candle c){DateTime now=DateTime.UtcNow,start=MarketClient.Unix(c.Time);return start<=now&&now<start.AddMinutes(IntervalMinutes);}
  public string Describe(Candle c){return MarketClient.Unix(c.Time).ToLocalTime().ToString("MM/dd HH:mm")+(IsOpen(c)?" · 未收盘":" · 已收盘")+"   量 "+c.Volume.ToString("N2",inv)+" "+Asset+"\n开 "+c.Open.ToString(PriceFormat,inv)+"    高 "+c.High.ToString(PriceFormat,inv)+"\n低 "+c.Low.ToString(PriceFormat,inv)+"    收 "+c.Close.ToString(PriceFormat,inv)+" USDT";}
  // Indices of the visible-range high and low candles (first max High / first min Low).
  public static void VisibleHighLow(IList<Candle> rows,out int highIndex,out int lowIndex){
   highIndex=0;lowIndex=0;if(rows==null||rows.Count==0)return;
   for(int i=1;i<rows.Count;i++){if(rows[i].High>rows[highIndex].High)highIndex=i;if(rows[i].Low<rows[lowIndex].Low)lowIndex=i;}
  }
  // Classic EMA: SMA seed at period-1, then k=2/(period+1). NaN until enough history.
  public static double[] ComputeEma(IList<Candle> rows,int period){
   int n=rows==null?0:rows.Count;var values=new double[n];
   for(int i=0;i<n;i++)values[i]=double.NaN;
   if(period<2||n<period)return values;
   double sum=0;for(int i=0;i<period;i++)sum+=(double)rows[i].Close;
   double ema=sum/period;values[period-1]=ema;double k=2.0/(period+1);
   for(int i=period;i<n;i++){ema=(double)rows[i].Close*k+ema*(1-k);values[i]=ema;}
   return values;
  }
  // Classic SuperTrend (not LuxAlgo): Wilder ATR, HL2 ± mult*ATR sticky bands, flip on close cross.
  public static SuperTrendSeries ComputeSuperTrend(IList<Candle> rows,int atrPeriod,double multiplier){
   int n=rows==null?0:rows.Count;var series=new SuperTrendSeries(n);
   if(atrPeriod<2||multiplier<=0||n==0)return series;
   var tr=new double[n];
   tr[0]=(double)(rows[0].High-rows[0].Low);
   for(int i=1;i<n;i++){
    double h=(double)rows[i].High,l=(double)rows[i].Low,pc=(double)rows[i-1].Close;
    double a=h-l,b=Math.Abs(h-pc),c=Math.Abs(l-pc);
    tr[i]=a>b?(a>c?a:c):(b>c?b:c);
   }
   var atr=new double[n];for(int i=0;i<n;i++)atr[i]=double.NaN;
   if(n<atrPeriod)return series;
   double seed=0;for(int i=0;i<atrPeriod;i++)seed+=tr[i];
   atr[atrPeriod-1]=seed/atrPeriod;
   for(int i=atrPeriod;i<n;i++)atr[i]=(atr[i-1]*(atrPeriod-1)+tr[i])/atrPeriod;
   int start=atrPeriod-1;
   double prevUpper=0,prevLower=0;int prevDir=1;
   for(int i=start;i<n;i++){
    double hl2=((double)rows[i].High+(double)rows[i].Low)/2.0;
    double basicUpper=hl2+multiplier*atr[i];
    double basicLower=hl2-multiplier*atr[i];
    double finalUpper,finalLower;
    if(i==start){finalUpper=basicUpper;finalLower=basicLower;}
    else{
     double prevClose=(double)rows[i-1].Close;
     finalUpper=(basicUpper<prevUpper||prevClose>prevUpper)?basicUpper:prevUpper;
     finalLower=(basicLower>prevLower||prevClose<prevLower)?basicLower:prevLower;
    }
    int dir;
    if(i==start)dir=((double)rows[i].Close>=hl2)?1:-1;
    else if(prevDir==1)dir=((double)rows[i].Close<finalLower)?-1:1;
    else dir=((double)rows[i].Close>finalUpper)?1:-1;
    series.Directions[i]=dir;
    series.Values[i]=dir==1?finalLower:finalUpper;
    prevUpper=finalUpper;prevLower=finalLower;prevDir=dir;
   }
   return series;
  }
  public static bool TryLastValue(double[] series,out double value){
   value=double.NaN;if(series==null)return false;
   for(int i=series.Length-1;i>=0;i--){if(!double.IsNaN(series[i])){value=series[i];return true;}}
   return false;
  }
  public static bool TryLastSuperTrend(SuperTrendSeries series,out double value,out int direction){
   value=double.NaN;direction=0;if(series==null||series.Values==null)return false;
   for(int i=series.Values.Length-1;i>=0;i--){
    if(!double.IsNaN(series.Values[i])){value=series.Values[i];direction=series.Directions[i];return true;}
   }
   return false;
  }
  public static string FormatLegendValue(double value,string priceFormat){
   if(double.IsNaN(value))return "—";
   return value.ToString(string.IsNullOrEmpty(priceFormat)?"N2":priceFormat,inv);
  }
  static int ClampPeriod(int period){return period<2?2:(period>200?200:period);}
  FormattedText Label(string text,double size,Brush color){return new FormattedText(text,inv,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,color,VisualTreeHelper.GetDpi(this).PixelsPerDip);}
  protected override void OnRender(DrawingContext dc){
   base.OnRender(dc);double w=ActualWidth,h=ActualHeight;if(w<30||h<30)return;
   dc.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,w,h));
   var grid=new Pen(Dark?ColorBrush(53,65,57):ColorBrush(232,233,226),1);grid.DashStyle=DashStyles.Dash;
   var rows=Visible;double left=2,right=left+PlotWidth,top=12,bottom=h-(Candlesticks&&!Compact?39:13);
   if(bottom<=top)return;
   for(int i=0;i<3;i++)dc.DrawLine(grid,new Point(left,top+(bottom-top)*i/2),new Point(right,top+(bottom-top)*i/2));
   if(rows.Count<2)return;
   int slots=Candlesticks?Math.Max(1,View.Slots):rows.Count;
   double lo=(double)rows.Min(c=>Candlesticks?c.Low:c.Close),hi=(double)rows.Max(c=>Candlesticks?c.High:c.Close);
   // Expand range to include indicator values so lines stay on-plot.
   if(Candlesticks){
    if(ShowEma){
     ExpandRange(ComputeEma(data,ClampPeriod(Ema1)),ref lo,ref hi);
     ExpandRange(ComputeEma(data,ClampPeriod(Ema2)),ref lo,ref hi);
     ExpandRange(ComputeEma(data,ClampPeriod(Ema3)),ref lo,ref hi);
    }
    if(ShowSuperTrend){
     var st=ComputeSuperTrend(data,Preferences.ClampStPeriod(StPeriod),Preferences.ClampStMult(StMult));
     ExpandRange(st.Values,ref lo,ref hi);
    }
   }
   double pad=Math.Max((hi-lo)*0.08,Math.Max(hi*0.00005,0.000001));lo-=pad;hi+=pad;
   Func<decimal,double> y=price=>bottom-((double)price-lo)/(hi-lo)*(bottom-top);
   Func<int,double> x=i=>Candlesticks?left+(i+0.5)*PlotWidth/slots:left+PlotWidth*(rows[i].Time-rows[0].Time)/(double)(rows[rows.Count-1].Time-rows[0].Time);
   if(Candlesticks){
    double body=Math.Max(1,Math.Min(11,PlotWidth/slots*0.66));decimal maxVolume=rows.Max(c=>c.Volume);
    for(int i=0;i<rows.Count;i++){
     var c=rows[i];var color=c.Close>=c.Open?up:down;double px=x(i),a=y(c.Open),b=y(c.Close);
     dc.DrawLine(new Pen(color,1),new Point(px,y(c.High)),new Point(px,y(c.Low)));
     dc.DrawRectangle(color,null,new Rect(px-body/2,Math.Min(a,b),body,Math.Max(1,Math.Abs(a-b))));
     if(!Compact){double volume=maxVolume>0?24*(double)(c.Volume/maxVolume):0;dc.PushOpacity(0.38);dc.DrawRectangle(color,null,new Rect(px-body/2,h-3-volume,body,volume));dc.Pop();}
    }
    if(!Compact){
     for(int i=0;i<3;i++){
      double value=hi-(hi-lo)*i/2;var label=Label(value.ToString(PriceFormat,inv),9,Muted);label.MaxTextWidth=64;label.Trimming=TextTrimming.CharacterEllipsis;
      dc.DrawText(label,new Point(right+5,top+(bottom-top)*i/2-label.Height/2));
     }
     dc.DrawText(Label("量",9,Muted),new Point(right+5,h-23));
    }
    DrawVisibleExtremes(dc,rows,x,y,left,right,w);
    if(View.Following){
     var last=rows[rows.Count-1];double py=y(last.Close);var color=last.Close>=last.Open?up:down;
     var priceLine=new Pen(color,0.65){DashStyle=DashStyles.Dot};dc.DrawLine(priceLine,new Point(left,py),new Point(right,py));
     if(!Compact){
      var label=Label(last.Close.ToString(PriceFormat,inv),9,color);label.MaxTextWidth=62;label.Trimming=TextTrimming.CharacterEllipsis;
      dc.DrawRoundedRectangle(Surface,new Pen(color,0.6),new Rect(right+2,py-9,64,18),3,3);dc.DrawText(label,new Point(right+5,py-label.Height/2));
     }
    }
   }else{
    var points=rows.Select((c,i)=>new Point(x(i),y(c.Close))).ToArray();
    var path=new StreamGeometry();using(var g=path.Open()){g.BeginFigure(points[0],false,false);g.PolyLineTo(points.Skip(1).ToArray(),true,false);}path.Freeze();
    var fill=new StreamGeometry();using(var g=fill.Open()){g.BeginFigure(new Point(left,h),true,true);g.PolyLineTo(points,true,false);g.LineTo(new Point(right,h),true,false);}fill.Freeze();
    dc.DrawGeometry(new LinearGradientBrush(Color.FromArgb(52,233,129,45),Color.FromArgb(0,233,129,45),90),null,fill);dc.DrawGeometry(null,new Pen(orange,1.8),path);dc.DrawEllipse(orange,null,points[points.Length-1],3,3);
   }
   if(Candlesticks){
    double[] emaA=null,emaB=null,emaC=null;SuperTrendSeries st=null;
    if(ShowEma){emaA=ComputeEma(data,ClampPeriod(Ema1));emaB=ComputeEma(data,ClampPeriod(Ema2));emaC=ComputeEma(data,ClampPeriod(Ema3));DrawEmaLine(dc,emaA,x,y,emaBlue);DrawEmaLine(dc,emaB,x,y,emaOrange);DrawEmaLine(dc,emaC,x,y,emaPurple);}
    if(ShowSuperTrend){st=ComputeSuperTrend(data,Preferences.ClampStPeriod(StPeriod),Preferences.ClampStMult(StMult));DrawSuperTrend(dc,st,x,y);}
    if(ShowEma||ShowSuperTrend)DrawLegend(dc,left,top,emaA,emaB,emaC,st);
   }
   if(hover>=left&&hover<=right&&!dragging){
    int index;
    if(Candlesticks){int slotsH=Math.Max(1,View.Slots);index=Math.Min(rows.Count-1,Math.Max(0,(int)((hover-left)/PlotWidth*slotsH)));}
    else {long t=rows[0].Time+(long)((hover-left)/PlotWidth*(rows[rows.Count-1].Time-rows[0].Time));index=rows.Select((c,i)=>new{c,i}).OrderBy(v=>Math.Abs(v.c.Time-t)).First().i;}
    var selected=rows[index];double px=x(index),py=y(selected.Close);dc.DrawLine(new Pen(Muted,0.7),new Point(px,top),new Point(px,h-3));
    dc.DrawEllipse(Candlesticks?(selected.Close>=selected.Open?up:down):orange,new Pen(Surface,1.5),new Point(px,py),3,3);
    string text=Candlesticks?Describe(selected):MarketClient.Unix(selected.Time).ToLocalTime().ToString("MM/dd HH:mm")+"   "+selected.Close.ToString(PriceFormat,inv)+" USDT";
    var label=Label(text,10,Ink);label.MaxTextWidth=w-20;label.Trimming=TextTrimming.CharacterEllipsis;
    double width=Math.Min(w,label.Width+16),tx=Math.Max(0,Math.Min(w-width,px-width/2));
    dc.DrawRoundedRectangle(Surface,new Pen(Muted,0.5),new Rect(tx,0,width,label.Height+10),5,5);dc.DrawText(label,new Point(tx+8,5));
   }
  }
  void ExpandRange(double[] series,ref double lo,ref double hi){
   if(series==null||data.Count==0)return;
   int last=Math.Min(View.First+View.Count,series.Length);
   for(int i=View.First;i<last;i++){if(double.IsNaN(series[i]))continue;if(series[i]<lo)lo=series[i];if(series[i]>hi)hi=series[i];}
  }
  void DrawVisibleExtremes(DrawingContext dc,List<Candle> rows,Func<int,double> x,Func<decimal,double> y,double left,double right,double w){
   int hiIdx,loIdx;VisibleHighLow(rows,out hiIdx,out loIdx);
   DrawExtreme(dc,rows[hiIdx],x(hiIdx),y(rows[hiIdx].High),true,left,right,w);
   DrawExtreme(dc,rows[loIdx],x(loIdx),y(rows[loIdx].Low),false,left,right,w);
  }
  void DrawExtreme(DrawingContext dc,Candle candle,double px,double py,bool high,double left,double right,double w){
   string text=high?candle.High.ToString(PriceFormat,inv):candle.Low.ToString(PriceFormat,inv);
   var label=Label(text,9,Muted);double pad=6,lw=label.Width,lh=label.Height;
   bool placeRight=px-left<right-px;double lineLen=Math.Min(28,Math.Max(12,(placeRight?right-px:px-left)*0.35));
   var pen=new Pen(Muted,0.8);
   if(placeRight){dc.DrawLine(pen,new Point(px,py),new Point(px+lineLen,py));double tx=Math.Min(w-lw-2,px+lineLen+pad);dc.DrawText(label,new Point(tx,high?py-lh-1:py+1));}
   else{dc.DrawLine(pen,new Point(px,py),new Point(px-lineLen,py));double tx=Math.Max(2,px-lineLen-pad-lw);dc.DrawText(label,new Point(tx,high?py-lh-1:py+1));}
  }
  void DrawEmaLine(DrawingContext dc,double[] series,Func<int,double> x,Func<decimal,double> y,Brush color){
   if(series==null||series.Length==0)return;
   var points=new List<Point>();
   int last=Math.Min(View.First+View.Count,series.Length);
   for(int i=View.First;i<last;i++){
    if(double.IsNaN(series[i])){if(points.Count>1)StrokeLine(dc,points,color,1.2);points.Clear();continue;}
    points.Add(new Point(x(i-View.First),(double)y((decimal)series[i])));
   }
   if(points.Count>1)StrokeLine(dc,points,color,1.2);
  }
  void DrawSuperTrend(DrawingContext dc,SuperTrendSeries series,Func<int,double> x,Func<decimal,double> y){
   if(series==null||series.Values==null||series.Values.Length==0)return;
   var points=new List<Point>();int segDir=0;
   int last=Math.Min(View.First+View.Count,series.Values.Length);
   Action flush=()=>{if(points.Count>1)StrokeLine(dc,points,segDir>=0?up:down,1.6);points.Clear();};
   for(int i=View.First;i<last;i++){
    if(double.IsNaN(series.Values[i])||series.Directions[i]==0){flush();segDir=0;continue;}
    if(segDir!=0&&series.Directions[i]!=segDir)flush();
    segDir=series.Directions[i];
    points.Add(new Point(x(i-View.First),(double)y((decimal)series.Values[i])));
   }
   flush();
  }
  void DrawLegend(DrawingContext dc,double left,double top,double[] emaA,double[] emaB,double[] emaC,SuperTrendSeries st){
   double font=Compact?9:10;double x=left+4,y=top+2;
   Action<string,Brush> chip=(text,color)=>{
    var label=Label(text,font,color);
    dc.DrawText(label,new Point(x,y));
    x+=label.Width+10;
   };
   if(ShowEma){
    double v;string a=TryLastValue(emaA,out v)?FormatLegendValue(v,PriceFormat):"—";
    string b=TryLastValue(emaB,out v)?FormatLegendValue(v,PriceFormat):"—";
    string c=TryLastValue(emaC,out v)?FormatLegendValue(v,PriceFormat):"—";
    chip("EMA"+ClampPeriod(Ema1)+" "+a,emaBlue);
    chip("EMA"+ClampPeriod(Ema2)+" "+b,emaOrange);
    chip("EMA"+ClampPeriod(Ema3)+" "+c,emaPurple);
   }
   if(ShowSuperTrend&&st!=null){
    double v;int d;Brush stColor=up;string stText;
    if(TryLastSuperTrend(st,out v,out d)){stText="ST "+FormatLegendValue(v,PriceFormat)+(d>=0?" ↑":" ↓");stColor=d>=0?up:down;}
    else stText="ST —";
    chip(stText,stColor);
   }
  }
  static void StrokeLine(DrawingContext dc,List<Point> points,Brush color,double thickness){
   var path=new StreamGeometry();using(var g=path.Open()){g.BeginFigure(points[0],false,false);g.PolyLineTo(points.Skip(1).ToArray(),true,false);}path.Freeze();
   dc.DrawGeometry(null,new Pen(color,thickness),path);
  }
 }
}
