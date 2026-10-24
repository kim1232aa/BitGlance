// SPDX-License-Identifier: AGPL-3.0-only
// Modified 2026-10-24: allow right padding so the latest candle can sit mid-plot.
using System;
using System.Collections.Generic;

namespace BitGlance {
 // Keeps a time anchor when the API rolls its fixed-size history forward.
 // Slots is the fixed plot width in bars; Count is how many real candles are drawn.
 // First may advance past "live" so empty slots appear on the right (latest mid-screen).
 public sealed class ChartViewport {
  int total,first,requested=60;
  public int First {get{return first;}}
  public int Slots {get{return total==0?0:requested;}}
  public int Count {get{return Math.Min(Slots,Math.Max(0,total-first));}}
  public bool Following {get;private set;}
  public ChartViewport(){Following=true;}
  int LiveFirst(){return Math.Max(0,total-requested);}
  int MaxFirst(){return Math.Max(0,total-Math.Max(1,requested/2));}
  int Clamp(int value){return Math.Max(0,Math.Min(MaxFirst(),value));}
  public void Update(IList<Candle> oldData,IList<Candle> newData){
   long anchor=oldData.Count>first?oldData[first].Time:0;total=newData.Count;
   if(Following||oldData.Count==0){first=Clamp(LiveFirst());Following=true;return;}
   int index=0;while(index<total&&newData[index].Time<anchor)index++;first=Clamp(index);Following=first==LiveFirst();
  }
  public void Reset(){requested=60;Latest();}
  public void Latest(){first=Clamp(LiveFirst());Following=true;}
  public void Pan(int bars){first=Clamp(first+bars);Following=first==LiveFirst();}
  public void Zoom(int steps,double anchor){
   if(total==0||steps==0)return;
   anchor=Math.Max(0,Math.Min(1,anchor));double at=first+anchor*(Math.Max(1,Slots)-1);
   requested=(int)Math.Max(20,Math.Min(160,Math.Round(requested*Math.Pow(1.2,-Math.Max(-20,Math.Min(20,steps))))));
   first=Clamp((int)Math.Round(at-anchor*(Math.Max(1,Slots)-1)));Following=first==LiveFirst();
  }
 }
}
