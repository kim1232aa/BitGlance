// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
namespace BitGlance {
 public static class Checks {
  static readonly List<string> results=new List<string>();
  static void Assert(bool good,string name){if(!good)throw new Exception(name);results.Add("PASS "+name);}
  static void Reject(Action action,string name){bool rejected=false;try{action();}catch(FormatException){rejected=true;}Assert(rejected,name);}
  public static int Run(string path){
   try {
    var now=MarketClient.Unix(1790738700000L);
    const string bitgetJson="{\"code\":\"00000\",\"data\":[{\"symbol\":\"BTCUSDT\",\"lastPr\":\"83318.50\",\"markPrice\":\"83300\",\"change24h\":\"-0.00163\",\"high24h\":\"84563.99\",\"low24h\":\"82900\",\"baseVolume\":\"12888.6405\",\"ts\":1790738700000}]}";
    const string binanceJson="{\"symbol\":\"BTCUSDT\",\"lastPrice\":\"83318.50\",\"priceChangePercent\":\"-1.201\",\"highPrice\":\"84563.99\",\"lowPrice\":\"82900\",\"volume\":\"12888.6405\",\"closeTime\":1790738700000}";
    const string okxJson="{\"code\":\"0\",\"data\":[{\"instId\":\"BTC-USDT-SWAP\",\"last\":\"83318.50\",\"open24h\":\"83454.00\",\"high24h\":\"84563.99\",\"low24h\":\"82900\",\"vol24h\":\"100\",\"volCcy24h\":\"12888.6405\",\"ts\":\"1790738700000\"}]}";
    Thread.CurrentThread.CurrentCulture=new CultureInfo("de-DE");
    var q=MarketClient.ParseBinanceTicker(binanceJson,"BTCUSDT",now);
    Assert(q.Price==83318.50m && q.Change==-1.201m && q.Unit=="USDT" && q.Source=="Binance USDT-M","binance decimal parsing is locale-independent; change already percent");
    Assert(!q.IsStale(now.AddSeconds(119)) && q.IsStale(now.AddSeconds(121)),"old server timestamp is flagged stale");
    Reject(()=>MarketClient.ParseBinanceTicker(binanceJson.Replace("BTCUSDT","ETHUSDT"),"BTCUSDT",now),"rejects binance response for a different contract");
    Reject(()=>MarketClient.ParseBinanceTicker(binanceJson.Replace("83318.50","0"),"BTCUSDT",now),"rejects zero price");
    Reject(()=>MarketClient.ParseBinanceTicker(binanceJson.Replace("83318.50","NaN"),"BTCUSDT",now),"rejects non-numeric price");
    Reject(()=>MarketClient.ParseBinanceTicker(binanceJson.Replace("82900","99999"),"BTCUSDT",now),"rejects impossible high/low range");
    Reject(()=>MarketClient.ParseBinanceTicker(binanceJson,"BTCUSDT",now.AddHours(-1)),"rejects future server timestamps");
    var qb=MarketClient.ParseTicker(bitgetJson,"BTCUSDT",now);
    Assert(qb.Price==83318.50m && qb.Change==-0.163m && qb.Source=="Bitget USDT-FUTURES","bitget ticker keeps fraction-to-percent change");
    var qo=MarketClient.ParseOkxTicker(okxJson,"BTCUSDT",now);
    Assert(qo.Price==83318.50m && qo.Volume==12888.6405m && qo.Source=="OKX USDT-SWAP","okx ticker uses volCcy24h and SWAP instId");
    Assert(Math.Abs(qo.Change-(((83318.50m-83454.00m)/83454.00m)*100m))<0.0001m,"okx change percent from last vs open24h");
    Reject(()=>MarketClient.ParseOkxTicker(okxJson.Replace("BTC-USDT-SWAP","ETH-USDT-SWAP"),"BTCUSDT",now),"rejects okx response for a different contract");
    foreach(var symbol in MarketClient.Symbols)Assert(MarketClient.ParseBinanceTicker(binanceJson.Replace("BTCUSDT",symbol),symbol,now).Symbol==symbol,"parses perpetual contract "+symbol);
    Assert(!MarketClient.IsSupported("BTCUSD")&&!MarketClient.IsSupported("DOGEUSDT"),"unsupported symbols are rejected");
    Assert(MarketClient.IsSource("binance")&&MarketClient.IsSource("okx")&&MarketClient.IsSource("bitget")&&!MarketClient.IsSource("kraken"),"three feed sources are recognized");
    Assert(MarketClient.NormalizeBase("www.example.com")=="https://www.example.com"&&MarketClient.NormalizeBase("https://a.test/")=="https://a.test","API base normalizes host and trailing slash");
    Assert(MarketClient.NormalizeBase("")==null&&MarketClient.NormalizeBase("ftp://x")==null,"empty or non-http base is rejected");
    Assert(MarketClient.DefaultBase("binance")==MarketClient.DefaultBinanceBase&&MarketClient.DefaultBase("okx")==MarketClient.DefaultOkxBase&&MarketClient.DefaultBase("bitget")==MarketClient.DefaultBitgetBase,"default bases match Binance/OKX/Bitget mirrors");
    Assert(MarketClient.ToOkxInstId("ETHUSDT")=="ETH-USDT-SWAP","okx instId maps ETHUSDT");
    var legacy=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Preferences>("{\"Provider\":1,\"Interval\":30,\"Opacity\":30}");
    Assert(legacy.Symbol=="BTCUSDT" && legacy.ShowChange && legacy.ShowUnit && legacy.Opacity==30,"v1 settings migrate with visible fields and saved opacity");
    Assert(legacy.Feed=="binance","legacy settings keep constructor default Feed binance");
    var candles=MarketClient.ParseCandles("[[2000,2,4,1,\"2.5\",10,30],[1000,1,3,1,\"2\",0,0],[2000,2,4,1,\"3\",11,33]]");
    Assert(candles.Count==2 && candles[0].Time==1000 && candles[1].Close==3,"chart sorts timestamps and removes duplicate candles");
    Assert(candles[1].Open==2&&candles[1].High==4&&candles[1].Low==1&&candles[1].Volume==11,"candles keep real OHLC and base-asset volume");
    var okxBars=MarketClient.ParseOkxCandles("[[3000,3,4,2,3,100,9.5],[1000,1,3,1,2,10,1.5],[2000,2,4,1,2.5,20,2.5]]");
    Assert(okxBars.Count==3&&okxBars[0].Time==1000&&okxBars[2].Time==3000&&okxBars[2].Volume==9.5m,"okx candles reverse to ascending and use volCcy");
    const string bars="[[1000,2,3,1,2,0,0],[2000,2,3,1,2,10,20]]";
    Reject(()=>MarketClient.ParseCandles(bars.Replace("2,3,1,2","4,3,1,2")),"rejects open above candle high");
    Reject(()=>MarketClient.ParseCandles(bars.Replace("2,3,1,2","2,3,2.5,2")),"rejects low above open or close");
    Reject(()=>MarketClient.ParseCandles(bars.Replace("10,20","-10,20")),"rejects negative candle volume");
    Reject(()=>MarketClient.ParseCandles(bars.Replace("2000","1000")),"requires two distinct candle timestamps");
    Reject(()=>MarketClient.ParseCandles(bars.Replace("2000","999999999999999")),"rejects out of range candle timestamps");
    Assert(MarketClient.ParseCandles(bars)[0].Volume==0,"flat candles with no trades remain valid");
    Assert(!legacy.Candles&&legacy.ChartInterval=="5m","old settings default to line chart and 5m candles");
    Assert(legacy.MiniChart,"old JSON without MiniChart inherits constructor default on");
    var fresh=new Preferences();
    Assert(fresh.Feed=="binance"&&string.IsNullOrEmpty(fresh.BinanceBase),"new preferences default to binance feed");
    var extreme=new List<Candle>{new Candle{High=2,Low=1},new Candle{High=5,Low=2},new Candle{High=4,Low=0.5m},new Candle{High=3,Low=1}};
    int hi,lo;PriceChart.VisibleHighLow(extreme,out hi,out lo);
    Assert(hi==1&&lo==2,"visible high/low pick first max High and first min Low");
    PriceChart.VisibleHighLow(new List<Candle>(),out hi,out lo);Assert(hi==0&&lo==0,"empty visible range keeps zero indices");
    PriceChart.VisibleHighLow(null,out hi,out lo);Assert(hi==0&&lo==0,"null visible range keeps zero indices");
    var emaSeed=new List<Candle>{new Candle{Close=22},new Candle{Close=24},new Candle{Close=26},new Candle{Close=28},new Candle{Close=30}};
    var ema3=PriceChart.ComputeEma(emaSeed,3);
    Assert(double.IsNaN(ema3[0])&&double.IsNaN(ema3[1])&&Math.Abs(ema3[2]-24)<1e-9&&Math.Abs(ema3[3]-26)<1e-9&&Math.Abs(ema3[4]-28)<1e-9,"EMA uses SMA seed then k=2/(period+1)");
    Assert(PriceChart.ComputeEma(emaSeed,10).All(double.IsNaN),"EMA stays NaN until period bars exist");
    Assert(fresh.ShowEma&&fresh.Ema1==7&&fresh.Ema2==20&&fresh.Ema3==50,"new preferences default three EMAs 7/20/50 on");
    Assert(fresh.MiniChart&&fresh.ShowSuperTrend&&fresh.StPeriod==10&&Math.Abs(fresh.StMult-3.0)<1e-9,"new preferences default MiniChart + SuperTrend 10/3 on");
    Assert(Preferences.ClampEma(1)==2&&Preferences.ClampEma(201)==200,"EMA periods clamp to 2..200");
    Assert(Preferences.ClampStPeriod(1)==2&&Preferences.ClampStPeriod(101)==100,"SuperTrend ATR period clamps to 2..100");
    Assert(Math.Abs(Preferences.ClampStMult(0.1)-0.5)<1e-9&&Math.Abs(Preferences.ClampStMult(12)-10)<1e-9,"SuperTrend multiplier clamps to 0.5..10");
    // Rising then falling series: SuperTrend stays NaN until ATR period, then flips down after a clear break.
    var stBars=new List<Candle>();
    for(int i=0;i<12;i++)stBars.Add(new Candle{Open=100+i,High=101+i,Low=99+i,Close=100.5m+i,Volume=1});
    for(int i=0;i<8;i++){decimal px=112-i*2;stBars.Add(new Candle{Open=px,High=px+1,Low=px-1,Close=px-0.5m,Volume=1});}
    var st=PriceChart.ComputeSuperTrend(stBars,10,3.0);
    Assert(st.Values.Take(9).All(double.IsNaN),"SuperTrend stays NaN until ATR period bars");
    Assert(!double.IsNaN(st.Values[9])&&st.Directions[9]!=0,"SuperTrend has value at first ATR bar");
    int lastDir=0;double lastVal;Assert(PriceChart.TryLastSuperTrend(st,out lastVal,out lastDir)&&!double.IsNaN(lastVal)&&lastDir!=0,"SuperTrend last value helper returns final direction");
    Assert(lastDir==-1,"SuperTrend flips to down after sustained decline");
    var emaLast=PriceChart.ComputeEma(emaSeed,3);double lv;Assert(PriceChart.TryLastValue(emaLast,out lv)&&Math.Abs(lv-28)<1e-9,"legend last-value helper skips leading NaN");
    Assert(PriceChart.FormatLegendValue(double.NaN,"N2")=="—"&&PriceChart.FormatLegendValue(12.5,"N2")=="12.50","legend formats NaN as dash");
    Assert(MarketClient.Intervals.Select(MarketClient.IntervalMinutes).SequenceEqual(new[]{1,5,15,60}),"four supported candle periods map to correct durations");
    var history=Enumerable.Range(0,300).Select(i=>new Candle{Time=(i+1)*60000L,Open=2,High=3,Low=1,Close=2,Volume=0}).ToList();
    var view=new ChartViewport();view.Update(new List<Candle>(),history);
    Assert(view.First==240&&view.Count==60&&view.Following,"initial candle view follows latest 60 bars");
    view.Pan(-50);long anchor=history[view.First].Time;
    var rolled=history.Skip(1).Concat(new[]{new Candle{Time=301*60000L}}).ToList();view.Update(history,rolled);
    Assert(rolled[view.First].Time==anchor&&!view.Following,"polling preserves history by timestamp when oldest bar expires");
    view.Latest();view.Update(rolled,history);Assert(view.First==240&&view.Following,"live view remains attached to newest candle on refresh");
    view.Zoom(100,1);Assert(view.Count==8&&view.First==292,"zoom in stops at 8 candles and anchors latest edge");
    view.Zoom(-100,1);Assert(view.Count==240&&view.First==60,"zoom out stops at 240 candles and anchors latest edge");
    view.Pan(-9999);Assert(view.First==0&&!view.Following,"pan clamps to oldest loaded candle");
    view.Pan(9999);Assert(view.First==180&&!view.Following&&view.Count==120,"pan may leave right padding so latest candle sits mid-plot");
    view.Latest();Assert(view.First==60&&view.Following,"Latest snaps back to live edge");
    view.Update(history,candles);Assert(view.First==0&&view.Count==2,"short histories never create invalid viewport indices");
    view.Update(candles,new List<Candle>());view.Zoom(1,0.5);view.Pan(-1);Assert(view.Count==0&&view.First==0,"empty chart tolerates navigation while loading");
    Reject(()=>MarketClient.ParseCandles("[]"),"rejects missing chart history");
    Assert(!WindowPlacement.Usable(new SavedPosition{X=-32000,Y=-32000}),"minimize sentinel is never saved");
    Assert(WindowPlacement.Usable(new SavedPosition{X=-1600,Y=60}),"negative coordinates on a left monitor are valid");
    var areas=new[]{new Rectangle(0,0,1920,1040),new Rectangle(-1920,0,1920,1080)};
    var p=WindowPlacement.Resolve(new SavedPosition{X=-1800,Y=100},new Size(450,600),areas);
    Assert(p.X==-1800 && p.Y==100,"restores window on secondary monitor");
    p=WindowPlacement.Resolve(new SavedPosition{X=9000,Y=9000},new Size(450,600),areas);
    Assert(p.X>=0 && p.X+450<=1920 && p.Y+600<=1040,"disconnected monitor position returns to visible work area");
    p=WindowPlacement.Resolve(new SavedPosition{X=1919,Y=1039},new Size(450,600),areas);
    Assert(p.X+450<1920 && p.Y+600<1040,"edge position keeps full window reachable");
    Assert(WindowPlacement.Opacity(-1)==0 && WindowPlacement.Opacity(0)==0 && WindowPlacement.Opacity(999)==100,"background opacity supports exactly 0 through 100 percent");
    GestureChecks();
    File.WriteAllLines(path,results.ToArray());return 0;
   }catch(Exception e){results.Add("FAIL "+e);File.WriteAllLines(path,results.ToArray());return 1;}
  }
  static void GestureChecks(){
   var gesture=new MiniGesture(500,4,4,2,2);var point=new System.Windows.Point(100,100);
   gesture.Down(point,100);Assert(!gesture.Move(new System.Windows.Point(102,101)),"small pointer tremor does not start a drag");
   Assert(gesture.Up(point)==MiniGestureEnd.Click,"first mouse release stays a single click");
   gesture.Down(point,200);Assert(gesture.Up(point)==MiniGestureEnd.DoubleClick,"second completed click expands within system timing");
   gesture.Down(point,300);Assert(gesture.Up(point)==MiniGestureEnd.Click,"third click is not mistaken for another double click");
   gesture.Down(point,400);gesture.Move(new System.Windows.Point(110,100));Assert(gesture.Up(new System.Windows.Point(110,100))==MiniGestureEnd.Drag,"second press can become a drag instead of expanding");
   gesture.Down(point,500);Assert(gesture.Up(point)==MiniGestureEnd.Click,"drag clears previous click candidate");
   gesture.Down(point,1100);Assert(gesture.Up(point)==MiniGestureEnd.Click,"slow pair stays two single clicks");
   var distant=new System.Windows.Point(120,100);gesture.Down(distant,1200);Assert(gesture.Up(distant)==MiniGestureEnd.Click,"spatially separate clicks never expand");
   gesture.Down(distant,1250);gesture.Cancel();Assert(gesture.Up(distant)==MiniGestureEnd.None&&!gesture.Active,"lost capture cancels without expansion");
   gesture.Down(point,1400);gesture.Move(new System.Windows.Point(120,100));gesture.Move(point);Assert(gesture.Up(point)==MiniGestureEnd.Drag,"drag remains a drag after pointer returns to start");
   for(uint i=0;i<10;i++){gesture.Down(point,2000+i*30);if(!gesture.Move(new System.Windows.Point(105,100))||gesture.Up(new System.Windows.Point(105,100))!=MiniGestureEnd.Drag)throw new Exception("Repeated drag failed");}
   Assert(!gesture.Active,"10 consecutive quick drags succeed without click-count filtering");
   for(uint i=0;i<11;i++){gesture.Down(point,4000+i*200);if(gesture.Up(point)!=MiniGestureEnd.Click)throw new Exception("Unexpected first click");gesture.Down(point,4100+i*200);if(gesture.Up(point)!=MiniGestureEnd.DoubleClick)throw new Exception("Repeated double click failed");}
   Assert(!gesture.Active,"11 consecutive double-click pairs succeed");
   gesture.Down(point,uint.MaxValue-50);gesture.Up(point);gesture.Down(point,49);Assert(gesture.Up(point)==MiniGestureEnd.DoubleClick,"input timestamps remain valid across tick-count wraparound");
  }
  public static int Feed(string path){
   try{using(var c=new MarketClient()){
    var lines=new List<string>();
    lines.Add("PASS default feed "+c.Source+" base="+c.BaseUrl);
    foreach(string symbol in MarketClient.Symbols){
     var q=c.FetchQuote(symbol,CancellationToken.None).GetAwaiter().GetResult();
     var day=c.FetchCandles(symbol,false,CancellationToken.None).GetAwaiter().GetResult();
     var week=c.FetchCandles(symbol,true,CancellationToken.None).GetAwaiter().GetResult();
     lines.Add("PASS Binance USDT perpetual "+symbol+": "+q.Price.ToString(CultureInfo.InvariantCulture)+"; market UTC="+q.MarketUtc.ToString("o")+"; source="+q.Source);
     lines.Add("PASS "+symbol+" charts: "+day.Count+" 5m candles, "+week.Count+" 1h candles");
     foreach(string period in MarketClient.Intervals){var rows=c.FetchCandles(symbol,period,300,CancellationToken.None).GetAwaiter().GetResult();long step=MarketClient.IntervalMinutes(period)*60000L;if(rows.Count<60||rows.Zip(rows.Skip(1),(a,b)=>b.Time-a.Time).Any(d=>d!=step))throw new Exception("Wrong candle period: "+symbol+" "+period);lines.Add("PASS "+symbol+" "+period+": "+rows.Count+" validated OHLCV candles; spacing "+step+" ms");}
    }
    lines.Add("Fetched UTC: "+DateTime.UtcNow.ToString("o"));File.WriteAllLines(path,lines.ToArray());
   }return 0;}catch(Exception e){File.WriteAllText(path,"FAIL "+e);return 1;}
  }
 }
}
