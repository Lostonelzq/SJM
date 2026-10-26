namespace Sjm {
    /// <summary>Normalize signed high-resolution wheel input without queuing a run of delayed swipes.</summary>
    public sealed class WheelGesture {
        int remainder, previousTick;
        public void Reset() { remainder=0; previousTick=0; }
        public string Feed(int delta,bool control,int tick) {
            if(!control) { Reset(); return null; }
            if(unchecked((uint)(tick-previousTick))>500 || (remainder>0 && delta<0) || (remainder<0 && delta>0)) remainder=0;
            previousTick=tick;
            remainder+=delta;
            if(remainder>=120) { remainder=0; return "DOWN"; }
            if(remainder<=-120) { remainder=0; return "UP"; }
            return null;
        }
    }
}
