#include "../mod_load_context.hpp"

#include <atomic>
#include <cassert>
#include <cstdint>
#include <thread>
#include <vector>

static void TestNestedLoads()
{
    ModLoadContexts<2> contexts;
    assert(contexts.Current(10) == 0);

    {
        ModLoadContexts<2>::Scope outer(contexts, 10, 0xA1);
        assert(outer.Valid());
        assert(contexts.Current(10) == 0xA1);

        {
            ModLoadContexts<2>::Scope inner(contexts, 10, 0xA2);
            assert(inner.Valid());
            assert(contexts.Current(10) == 0xA2);
        }

        assert(contexts.Current(10) == 0xA1);
    }

    assert(contexts.Current(10) == 0);
}

static void TestCapacityIsolation()
{
    ModLoadContexts<2> contexts;
    ModLoadContexts<2>::Scope first(contexts, 1, 101);
    ModLoadContexts<2>::Scope second(contexts, 2, 202);
    ModLoadContexts<2>::Scope full(contexts, 3, 303);

    assert(first.Valid() && second.Valid() && !full.Valid());
    assert(contexts.Current(1) == 101);
    assert(contexts.Current(2) == 202);
    assert(contexts.Current(3) == 0);
    assert(contexts.Current(0) == 0);
}

static void TestConcurrentLoads()
{
    constexpr int workers = 12;
    constexpr int rounds = 10000;
    ModLoadContexts<16> contexts;
    std::atomic<int> ready{0};
    std::atomic<int> failures{0};
    std::vector<std::thread> threads;

    for (int id = 1; id <= workers; ++id)
    {
        threads.emplace_back([&, id]
        {
            const uintptr_t zone = static_cast<uintptr_t>(id) * 100;
            ModLoadContexts<16>::Scope outer(contexts, id, zone);
            if (!outer.Valid())
            {
                ++failures;
                return;
            }

            ++ready;
            while (ready.load() < workers)
                std::this_thread::yield();

            for (int round = 0; round < rounds; ++round)
            {
                if (contexts.Current(id) != zone)
                    ++failures;

                if ((round & 7) == 0)
                {
                    ModLoadContexts<16>::Scope inner(contexts, id, zone + 1);
                    if (!inner.Valid() || contexts.Current(id) != zone + 1)
                        ++failures;
                }

                if (contexts.Current(id) != zone)
                    ++failures;
            }
        });
    }

    for (std::thread& thread : threads)
        thread.join();

    assert(failures == 0);

    for (int id = 1; id <= workers; ++id)
        assert(contexts.Current(id) == 0);
}

int main()
{
    TestNestedLoads();
    TestCapacityIsolation();
    TestConcurrentLoads();
}
