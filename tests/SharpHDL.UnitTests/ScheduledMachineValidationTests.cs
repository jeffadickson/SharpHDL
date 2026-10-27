using SharpHDL.HLS;
using SharpHDL.Simulation;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Compiler-boundary regressions for scheduled design ownership and lifetime.</summary>
public sealed class ScheduledMachineValidationTests
{
    /// <summary>Every public expression sink rejects nested foreign input/register leaves.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Foreign_expression_leaves_are_rejected(bool register)
    {
        var owner = new ScheduledMachineBuilder("Owner");
        var foreign = new ScheduledMachineBuilder("Foreign");
        var input = owner.Input("input", 8);
        var leaf = register ? foreign.Register("foreign", 8).Value : foreign.Input("foreign", 8);
        var expression = input.Add(leaf).Not();
        var target = owner.Register("target", 8);
        var memory = owner.Memory("ram", 8, 256);
        var state = owner.State("only");
        var guard = leaf.Equal(owner.Constant(8, 0));
        Assert.Throws<ArgumentException>(() => owner.Output("invalid", expression));
        Assert.Throws<ArgumentException>(() => state.Assign(target, expression));
        Assert.Throws<ArgumentException>(() => state.Transition("only", guard));
        Assert.Throws<ArgumentException>(() => state.Read(memory, expression, target, owner.Constant(1, 1)));
        Assert.Throws<ArgumentException>(() => state.Write(memory, input, expression, owner.Constant(1, 1)));
    }

    /// <summary>A completed lowering freezes retained state handles as well as the builder.</summary>
    [Fact]
    public void Lowering_freezes_every_state_mutation()
    {
        var builder = new ScheduledMachineBuilder("Frozen");
        var value = builder.Input("input", 8);
        var address = builder.Input("address", 1);
        var target = builder.Register("target", 8);
        var ram = builder.Memory("ram", 8, 2);
        var resource = builder.Resource("alu");
        var state = builder.State("only");
        builder.Output("result", target.Value);
        builder.Lower();
        Assert.Throws<InvalidOperationException>(() => state.Assign(target, value));
        Assert.Throws<InvalidOperationException>(() => state.Transition("only"));
        Assert.Throws<InvalidOperationException>(() => state.Read(ram, address, target, builder.Constant(1, 1)));
        Assert.Throws<InvalidOperationException>(() => state.Write(ram, address, value, builder.Constant(1, 1)));
        Assert.Throws<InvalidOperationException>(() => state.Use(resource));
        Assert.Throws<InvalidOperationException>(() => builder.Lower());
    }

    /// <summary>Validation failure leaves the model repairable rather than permanently frozen.</summary>
    [Fact]
    public void Unknown_transition_can_be_repaired_after_failed_lowering()
    {
        var builder = new ScheduledMachineBuilder("Repair");
        builder.State("start").Transition("finish");
        Assert.Throws<InvalidOperationException>(() => builder.Lower());
        builder.State("finish");
        var design = builder.Lower();
        var machine = new HardwareGraphMachine(design.Graph, design.Inputs, design.Outputs,
            design.NextState, design.ResetValues, design.Memories);
        machine.Tick();
        Assert.Equal(1, (int)machine.ReadRegister("scheduler_state"));
    }

    /// <summary>Compiler-generated state names cannot collide with user boundaries.</summary>
    [Fact]
    public void Scheduler_state_name_is_reserved()
    {
        var builder = new ScheduledMachineBuilder("Reserved");
        Assert.Throws<ArgumentException>(() => builder.Input("scheduler_state", 8));
        Assert.Throws<ArgumentException>(() => builder.Register("scheduler_state", 8));
        Assert.Throws<ArgumentException>(() => builder.Memory("scheduler_state", 8, 2));
        Assert.Throws<ArgumentException>(() => builder.Output("scheduler_state", builder.Constant(1, 0)));
    }

    /// <summary>Portable constants and shared local DAGs preserve modulo-width arithmetic.</summary>
    [Fact]
    public void Local_shared_dag_matches_exhaustive_byte_reference()
    {
        var builder = new ScheduledMachineBuilder("Local");
        var other = new ScheduledMachineBuilder("Constants");
        var input = builder.Input("input", 8);
        var sum = input.Add(other.Constant(8, 17));
        builder.State("only");
        builder.Output("result", sum.Multiply(sum));
        var design = builder.Lower();
        var machine = new HardwareGraphMachine(design.Graph, design.Inputs, design.Outputs,
            design.NextState, design.ResetValues, design.Memories);
        for (int i = 0; i < 256; i++)
        {
            machine.SetInput("input", i);
            Assert.Equal(((i + 17) * (i + 17)) & 255, (int)machine.ReadOutput("result"));
        }
    }
    /// <summary>Malformed declarations must not consume names needed by corrected declarations.</summary>
    [Fact]
    public void Invalid_declarations_are_transactional()
    {
        var builder = new ScheduledMachineBuilder("Transactional");
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Input("input", 0));
        var input = builder.Input("input", 8);
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Register("register", 8, 256));
        builder.Register("register", 8, 255);
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Memory("memory", 8, 0));
        builder.Memory("memory", 8, 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Resource("resource", 0));
        builder.Resource("resource", 2);
        Assert.Throws<ArgumentNullException>(() => builder.Output("output", null!));
        builder.Output("output", input);
        builder.State("only");
        builder.Lower();
    }

    /// <summary>FsCheck fuzzes malformed widths and verifies the same name remains reusable.</summary>
    [FsCheck.Xunit.Property(MaxTest = 500, Replay = "1145655947,296144285")]
    public void Invalid_width_fuzz_preserves_builder(int width)
    {
        if (width is >= 1 and <= 4096) return;
        var builder = new ScheduledMachineBuilder("Widths");
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Input("input", width));
        builder.Input("input", 8);
        builder.State("only");
        builder.Lower();
    }

    /// <summary>FsCheck generates mixed local/foreign expression DAGs and shrinks any failure.</summary>
    [FsCheck.Xunit.Property(MaxTest = 500, Replay = "1145655947,296144285")]
    public void Nested_foreign_expression_fuzz(int[] operations)
    {
        var builder = new ScheduledMachineBuilder("Local");
        var foreign = new ScheduledMachineBuilder("Foreign");
        var local = builder.Input("local", 8);
        var value = foreign.Input("foreign", 8);
        // Bound expression size while retaining all dependency shapes in the generated prefix.
        foreach (int operation in operations.Take(128))
            value = (operation & 3) switch
            {
                0 => value.Add(local),
                1 => local.Subtract(value),
                2 => value.Not(),
                _ => ScheduledValue.Select(builder.Constant(1, 1), local, value)
            };
        Assert.Throws<ArgumentException>(() => builder.Output("result", value));
        builder.Output("result", local);
        builder.State("only");
        builder.Lower();
    }
}
