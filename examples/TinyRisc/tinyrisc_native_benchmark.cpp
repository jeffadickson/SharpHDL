// Matched, clock-only Verilator benchmark. No wall-clock startup/compilation
// time is included. Keep the workload contract aligned with TinyRiscBenchmark.cs.
#include "VBenchmarkTinyRisc.h"
#include "verilated.h"
#include <array>
#include <chrono>
#include <cstdint>
#include <iomanip>
#include <iostream>
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>

namespace {
constexpr int warmup_edges = 100;
constexpr int reset_period = 128;
constexpr int qualification_edges = 256;
constexpr std::array<const char*, 19> names = {
    "tx_valid", "tx_data", "rx_ready", "pc", "ir", "phase", "halted", "fault",
    "retired", "last_pc", "leds", "r0", "r1", "r2", "r3", "r4", "r5", "r6", "r7"
};
using State = std::array<std::uint64_t, 19>;

// One sampled rising edge followed by combinational settling to clock-low.
void tick(VBenchmarkTinyRisc& model, bool reset) {
    model.rst = reset;
    model.clk = 0; model.eval();
    model.clk = 1; model.eval();
    model.clk = 0; model.eval();
}

void prepare(VBenchmarkTinyRisc& model) {
    model.s_run = 1; model.s_step = 0; model.s_switches = 0;
    model.s_rx_valid = 0; model.s_rx_data = 0; model.s_tx_ready = 1;
    tick(model, true);
    for (int i = 0; i < warmup_edges; ++i) tick(model, false);
}

State read_state(const VBenchmarkTinyRisc& m) {
    return {m.s_tx_valid, m.s_tx_data, m.s_rx_ready, m.s_pc, m.s_ir, m.s_phase,
        m.s_halted, m.s_fault, m.s_retired, m.s_last_pc, m.s_leds,
        m.s_r0, m.s_r1, m.s_r2, m.s_r3, m.s_r4, m.s_r5, m.s_r6, m.s_r7};
}

int parse(const char* text, int maximum) {
    std::size_t consumed = 0;
    int value = std::stoi(text, &consumed);
    if (consumed != std::string(text).size() || value < 1 || value > maximum)
        throw std::invalid_argument("Benchmark argument out of bounds");
    return value;
}
} // namespace

int main(int argc, char** argv) {
    try {
        if (argc != 3) throw std::invalid_argument("usage: native-benchmark edges trials");
        int edges = parse(argv[1], 10000000);
        int trials = parse(argv[2], 15);
        if (trials < 3) throw std::invalid_argument("Use at least three timing trials");
        auto context = std::make_unique<VerilatedContext>();
        context->commandArgs(argc, argv);
        auto model = std::make_unique<VBenchmarkTinyRisc>(context.get());
        prepare(*model);
        std::uint64_t trace_hash = UINT64_C(14695981039346656037);
        for (int edge = 0; edge < qualification_edges; ++edge) {
            tick(*model, edge % reset_period == 0);
            auto state = read_state(*model);
            if (state[7] != 0) throw std::runtime_error("CPU fault during qualification");
            for (auto value : state)
                trace_hash = (trace_hash ^ value) * UINT64_C(1099511628211);
        }
        std::vector<double> seconds;
        State final_state{};
        for (int trial = 0; trial < trials; ++trial) {
            prepare(*model);
            auto start = std::chrono::steady_clock::now();
            for (int edge = 0; edge < edges; ++edge)
                tick(*model, edge % reset_period == 0);
            double elapsed = std::chrono::duration<double>(std::chrono::steady_clock::now() - start).count();
            if (!(elapsed > 0)) throw std::runtime_error("Timing resolution insufficient");
            seconds.push_back(elapsed);
            auto state = read_state(*model);
            if (state[7] != 0 || (trial > 0 && state != final_state))
                throw std::runtime_error("CPU state mismatch across repetitions");
            final_state = state;
        }
        model->final();
        std::cout << std::setprecision(17)
            << "{\"schema_version\":1,\"workload\":\"tinyrisc-hello-reset128-v1\","
            << "\"edges_per_trial\":" << edges << ",\"trials\":" << trials
            << ",\"warmup_edges\":" << warmup_edges << ",\"reset_period\":" << reset_period
            << ",\"qualification_edges\":" << qualification_edges
            << ",\"qualification_hash\":\"" << std::hex << std::setw(16) << std::setfill('0')
            << trace_hash << std::dec << "\",\"seconds\":[";
        for (std::size_t i = 0; i < seconds.size(); ++i)
            std::cout << (i ? "," : "") << seconds[i];
        std::cout << "],\"final_state\":{";
        for (std::size_t i = 0; i < names.size(); ++i)
            std::cout << (i ? "," : "") << "\"" << names[i] << "\":" << final_state[i];
        std::cout << "}}\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "Benchmark failure: " << error.what() << '\n';
        return 1;
    }
}
