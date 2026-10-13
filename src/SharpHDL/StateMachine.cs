using System;
using System.Collections.Generic;
using System.Linq;
namespace SharpHDL
{
    // Explicit Moore-style controller. Transition conditions may include any graph expression.
    public sealed class StateMachine
    {
        readonly Module module;readonly string[] names;
        readonly List<Tuple<int,Expr,int>> transitions=new List<Tuple<int,Expr,int>>();
        bool built;
        public Signal State { get; private set; }
        public StateMachine(Module module,string name,params string[] states)
        {
            if(states==null||states.Length<2||states.Length>256||states.Any(string.IsNullOrWhiteSpace)||states.Distinct().Count()!=states.Length)
                throw new ArgumentException("Specify 2..256 distinct nonempty state names; the first is reset.");
            this.module=module;names=(string[])states.Clone();int width=1;while((1<<width)<names.Length)width++;
            State=module.Register(name+"_state",width,0);
            State.Describe("FSM binary encoding: "+string.Join(", ",names.Select((s,i)=>i+"="+s))+"; reset=0; invalid encoding returns to 0 on next edge.");
        }
        int Index(string name){int i=Array.IndexOf(names,name);if(i<0)throw new ArgumentException("Unknown state: "+name);return i;}
        public Expr Is(string name){return State.Eq(Expr.Const(State.Width,Index(name)));}
        public StateMachine When(string from,Expr condition,string to)
        {
            if(built||module.Frozen)throw new InvalidOperationException("FSM is already built or module frozen.");
            if(condition==null||condition.Width!=1)throw new ArgumentException("Transition condition must be one bit.");
            transitions.Add(Tuple.Create(Index(from),condition,Index(to)));return this;
        }
        public void Build()
        {
            if(built)throw new InvalidOperationException("FSM already built.");
            Expr next=Expr.Mux(State.Resize(State.Width+1).Lt(Expr.Const(State.Width+1,names.Length)),State,Expr.Const(State.Width,0));
            // First registered matching transition wins. No match holds valid state.
            for(int i=transitions.Count-1;i>=0;i--)
            {var t=transitions[i];next=Expr.Mux(State.Eq(Expr.Const(State.Width,t.Item1))&t.Item2,Expr.Const(State.Width,t.Item3),next);}
            module.Next(State,next);built=true;
        }
    }
}
