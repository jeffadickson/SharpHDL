using System.Text;
using SharpHDL;

/// <summary>
/// Independent arithmetic, state, memory and stream oracles; compares both .NET
/// backends at every edge and emits the same vectors as self-checking Verilog.
/// </summary>
internal static class SimulatorConformance
{
    /// <summary>Generate four bounded, deterministic conformance fixtures.</summary>
    public static void Run(string directory)
    {
        Directory.CreateDirectory(directory);
        Accumulator(directory);
        StateMachine(directory);
        Memory(directory);
        Pipeline(directory);
        Console.WriteLine("PASS compiled/reference and independent oracles: accumulator, FSM, RAM, streaming pipeline; emitted four RTL benches");
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    /// <summary>Modular arithmetic and enable/reset priority against host integer math.</summary>
    static void Accumulator(string directory)
    {
        var m = new Module("ConformanceAccumulator");
        var input = m.Input("data", 8);
        var enable = m.Input("enable", 1);
        var count = m.Register("count", 8, 0);
        var output = m.Output("result", 8);
        m.NextWhen(count, enable, count + input);
        m.Drive(output, count);
        var h = new Harness(m, directory);
        h.Tick(true);
        int expected = 0;
        var random = new Random(1401);
        for (int i = 0; i < 240; i++)
        {
            int data = random.Next(256), en = random.Next(2);
            bool reset = i % 67 == 0;
            h.Set(input, data); h.Set(enable, en);
            expected = reset ? 0 : en == 0 ? expected : (expected + data) & 255;
            h.Tick(reset);
            Require(h.Read(output) == expected, "Accumulator independent oracle mismatch");
        }
        h.Save();
    }

    /// <summary>Four-state control FSM; the oracle uses integers, not hardware expressions.</summary>
    static void StateMachine(string directory)
    {
        var m = new Module("ConformanceFsm");
        var advance = m.Input("advance", 1);
        var state = m.Register("state", 2, 0);
        var visible = m.Output("visible", 2);
        var done = m.Output("done", 1);
        m.NextWhen(state, advance, state + Expr.Const(2, 1));
        m.Drive(visible, state);
        m.Drive(done, state.Eq(Expr.Const(2, 3)));
        var h = new Harness(m, directory);
        h.Tick(true);
        int expected = 0;
        for (int i = 0; i < 240; i++)
        {
            int enable = i % 5 == 0 ? 0 : 1;
            bool reset = i % 73 == 0;
            h.Set(advance, enable);
            expected = reset ? 0 : (expected + enable) % 4;
            h.Tick(reset);
            Require(h.Read(visible) == expected && h.Read(done) == (expected == 3 ? 1 : 0), "FSM independent oracle mismatch");
        }
        h.Save();
    }

    /// <summary>Software array defines old-data collisions, disabled reads, bounds and retained reset memory.</summary>
    static void Memory(string directory)
    {
        var m = new Module("ConformanceMemory");
        var address = m.Input("address", 4);
        var data = m.Input("data", 16);
        var write = m.Input("write", 1);
        var read = m.Input("read", 1);
        var ram = SynchronousRam.Create(m, "memory", 16, 10, address, write, data, read);
        var result = m.Output("result", 16);
        m.Drive(result, ram.ReadData);
        var h = new Harness(m, directory);
        h.Tick(true);
        int[] cells = new int[10];
        int expectedRead = 0;
        var random = new Random(1402);
        for (int i = 0; i < 320; i++)
        {
            int addr = random.Next(16), value = random.Next(65536);
            int we = random.Next(2), re = random.Next(2);
            bool reset = i % 79 == 0;
            h.Set(address, addr); h.Set(data, value); h.Set(write, we); h.Set(read, re);
            if (reset) expectedRead = 0;
            else
            {
                if (re != 0) expectedRead = addr < cells.Length ? cells[addr] : 0;
                if (we != 0 && addr < cells.Length) cells[addr] = value;
            }
            h.Tick(reset);
            Require(h.Read(result) == expectedRead, "RAM independent oracle mismatch");
        }
        h.Save();
    }

    /// <summary>Queue scoreboard checks accepted token order, arithmetic, stalls, reset and drain.</summary>
    static void Pipeline(string directory)
    {
        var m = new Module("ConformancePipeline");
        var input = HwStream<ushort>.Input(m, "input");
        var output = HwStream<ushort>.Output(m, "output");
        var expression = (input.Data + Expr.Const(16, 3)) * Expr.Const(16, 5);
        var pipeline = StructuralPipeline.Attach(m, "pipe", input, output, expression,
            new PipelineOptions { LogicLevelsPerStage = 1 });
        var h = new Harness(m, directory);
        h.Tick(true);
        var expected = new Queue<int>();
        var random = new Random(1403);
        bool offering = false;
        int offeredData = 0;
        void StepTraffic(bool generate, bool ready)
        {
            if (generate && !offering && random.Next(4) != 0)
            {
                offering = true;
                offeredData = random.Next(65536);
            }
            h.Set(input.Valid, offering ? 1 : 0);
            h.Set(input.Data, offeredData);
            h.Set(output.Ready, ready ? 1 : 0);
            // Scoreboard observes actual pre-edge accepted handshakes. An offered
            // input remains stable until accepted, including while output stalls.
            if (ready && h.Read(output.Valid) != 0)
            {
                Require(expected.Count > 0, "Unexpected pipeline output token");
                Require(h.Read(output.Data) == expected.Dequeue(), "Pipeline data/order mismatch");
            }
            if (offering && h.Read(input.Ready) != 0)
            {
                expected.Enqueue(((((offeredData + 3) & 65535) * 5) & 65535));
                offering = false;
            }
            h.Tick();
        }
        for (int i = 0; i < 240; i++)
        {
            if (i % 71 == 0)
            {
                h.Tick(true);
                expected.Clear();
                offering = false;
            }
            else StepTraffic(true, random.Next(3) != 0);
        }
        for (int i = 0; i < pipeline.Latency * 2 + 8; i++) StepTraffic(false, true);
        Require(expected.Count == 0 && !offering && h.Read(output.Valid) == 0, "Pipeline failed to drain");
        h.Save();
    }

    /// <summary>Shared two-backend runner plus RTL vector writer; never substitutes for an independent oracle.</summary>
    sealed class Harness
    {
        readonly Module module;
        readonly Simulator reference;
        readonly CompiledSequentialSimulator compiled;
        readonly Signal[] ports;
        readonly string directory;
        readonly StringBuilder bench = new();
        int edge;

        public Harness(Module module, string directory)
        {
            this.module = module;
            this.directory = directory;
            reference = module.Simulate();
            compiled = new CompiledSequentialSimulator(module);
            ports = module.Ports.ToArray();
            bench.AppendLine("`timescale 1ns/1ps\nmodule tb_conformance; reg clk=0, rst=0;");
            foreach (var port in ports)
                bench.AppendLine($"{(port.IsInput ? "reg" : "wire")} [{port.Width - 1}:0] s_{port.Name}{(port.IsInput ? "=0" : "")};");
            bench.Append($"h_{module.Name} dut(.clk(clk),.rst(rst)");
            foreach (var port in ports) bench.Append($",.s_{port.Name}(s_{port.Name})");
            bench.AppendLine("); initial begin");
        }

        public void Set(Signal port, int value)
        {
            reference.Set(port, value);
            compiled.Set(port, value);
            bench.AppendLine($"s_{port.Name}={port.Width}'d{value};");
        }

        public int Read(Signal port)
        {
            var value = reference.Read(port);
            Require(value == compiled.Read(port), module.Name + " backend mismatch: " + port.Name);
            return (int)value;
        }

        public void Tick(bool reset = false)
        {
            reference.Tick(reset);
            compiled.Tick(reset);
            bench.AppendLine($"rst={(reset ? 1 : 0)}; #5; clk=1; #5;");
            foreach (var port in ports.Where(port => port.IsOutput))
            {
                int expected = Read(port);
                bench.AppendLine($"if(s_{port.Name}!=={port.Width}'d{expected}) $fatal(1,\"{module.Name} edge {edge} {port.Name}\");");
            }
            bench.AppendLine("clk=0; #1; rst=0;");
            edge++;
        }

        public void Save()
        {
            File.WriteAllText(Path.Combine(directory, module.Name + ".v"), module.Verilog());
            bench.AppendLine($"$display(\"PASS RTL {module.Name}: {edge} edges\"); $finish; end endmodule");
            File.WriteAllText(Path.Combine(directory, module.Name + "_tb.v"), bench.ToString());
        }
    }
}
