#include "../script_error_budget.hpp"
#include <assert.h>
#include <stdio.h>
#include <thread>
#include <vector>

using Budget = T7Diagnostics::ScriptErrorBudget;

int main()
{
    Budget budget;
    unsigned details = 0;
    for (unsigned n = 1; n <= 1000000; ++n)
    {
        const auto r = budget.Observe(0, 0x1234, 10, 20);
        if (r.kind == Budget::Kind::First || r.kind == Budget::Kind::Repeat) ++details;
    }
    assert(details == 20);
    assert(budget.Observe(0, 0x5678, 10, 20).kind == Budget::Kind::First);
    assert(budget.Observe(1, 0x1234, 10, 20).kind == Budget::Kind::First);
    assert(budget.Observe(0, 0x1234, 11, 20).kind == Budget::Kind::First);
    assert(budget.Observe(0, 0x1234, 10, 21).kind == Budget::Kind::First);
    for (unsigned n = 0; n < 1000; ++n) budget.Observe(0, 0x10000 + n, 1, 2);
    assert(budget.Observe(1, 0x5678, 1, 2).kind == Budget::Kind::First);
    assert(budget.Observe(-1, 0x5678, 1, 2).kind == Budget::Kind::First);
    Budget::Counts counts[Budget::kVms];
    uint32_t busy[Budget::kVms];
    assert(budget.Snapshot(counts, busy));
    assert(counts[0].unique == Budget::kSlots);
    assert(counts[0].overflow == 940);
    assert(counts[1].unique == 2 && counts[2].unique == 1);
    budget.NewLevel();
    assert(budget.Observe(0, 0x1234, 10, 20).kind == Budget::Kind::First);
    assert(budget.Snapshot(counts, busy));
    assert(counts[0].total == 1 && counts[0].unique == 1 && counts[0].overflow == 0);
    assert(counts[1].total == 0 && counts[2].total == 0);

    Budget concurrent;
    std::vector<std::thread> threads;
    for (unsigned t = 0; t < 4; ++t)
        threads.emplace_back([&concurrent, t] {
            for (unsigned n = 0; n < 10000; ++n) concurrent.Observe(t % 2, 0xCAFE, 11, 22);
        });
    for (auto& thread : threads) thread.join();
    assert(concurrent.Snapshot(counts, busy));
    for (unsigned vm = 0; vm < 2; ++vm)
    {
        assert(counts[vm].total + busy[vm] == 20000);
        assert(counts[vm].unique == 1);
    }
    assert(Budget::Hash("one", 3) != Budget::Hash("two", 3));
    assert(Budget::Hash("same", 4) == Budget::Hash("same", 4));
    static_assert(sizeof(Budget) < 8192, "Diagnostic storage must remain bounded");
    puts("PASS: repeat flood, later distinct errors, VM isolation, overflow, reset, concurrent accounting");
}
