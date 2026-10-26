#pragma once

#include <atomic>
#include <stddef.h>
#include <stdint.h>

namespace T7Diagnostics
{
// Fixed storage; an error flood in one VM cannot consume another VM's slots.
class ScriptErrorBudget
{
public:
    static constexpr unsigned kVms = 3; // GSC, CSC, unexpected instance.
    static constexpr unsigned kSlots = 64;
    enum class Kind { Busy, First, Repeat, Overflow, Quiet };
    struct Result
    {
        Kind kind;
        unsigned vm;
        uint32_t occurrences;
    };
    struct Counts
    {
        uint32_t total = 0;
        uint32_t unique = 0;
        uint32_t overflow = 0;
    };

    Result Observe(int instance, uintptr_t position, uint64_t message, uint64_t dialog)
    {
        const unsigned vm = instance >= 0 && instance < 2 ? (unsigned)instance : 2;
        if (lock_.test_and_set(std::memory_order_acquire))
        {
            busy_[vm].fetch_add(1, std::memory_order_relaxed);
            return { Kind::Busy, vm, 0 };
        }
        SyncEpoch();
        Counts& count = counts_[vm];
        ++count.total;
        Entry* empty = nullptr;
        for (Entry& entry : entries_[vm])
        {
            if (entry.occurrences == 0)
            {
                if (!empty) empty = &entry;
                continue;
            }
            if (entry.position == position && entry.message == message && entry.dialog == dialog)
            {
                const uint32_t n = ++entry.occurrences;
                lock_.clear(std::memory_order_release);
                return { PowerOfTwo(n) ? Kind::Repeat : Kind::Quiet, vm, n };
            }
        }
        if (empty)
        {
            *empty = { position, message, dialog, 1 };
            ++count.unique;
            lock_.clear(std::memory_order_release);
            return { Kind::First, vm, 1 };
        }
        const uint32_t n = ++count.overflow;
        lock_.clear(std::memory_order_release);
        return { PowerOfTwo(n) ? Kind::Overflow : Kind::Quiet, vm, n };
    }

    bool Snapshot(Counts (&counts)[kVms], uint32_t (&busy)[kVms])
    {
        if (lock_.test_and_set(std::memory_order_acquire)) return false;
        SyncEpoch();
        for (unsigned vm = 0; vm < kVms; ++vm)
        {
            counts[vm] = counts_[vm];
            busy[vm] = busy_[vm].load(std::memory_order_relaxed);
        }
        lock_.clear(std::memory_order_release);
        return true;
    }

    // Lazy reset avoids waiting for another VM inside a native error hook.
    void NewLevel() { epoch_.fetch_add(1, std::memory_order_relaxed); }

    static uint64_t Hash(const char* text, size_t size)
    {
        uint64_t value = 14695981039346656037ull;
        for (size_t i = 0; i < size && text[i]; ++i)
            value = (value ^ (unsigned char)text[i]) * 1099511628211ull;
        return value;
    }

private:
    struct Entry
    {
        uintptr_t position = 0;
        uint64_t message = 0;
        uint64_t dialog = 0;
        uint32_t occurrences = 0;
    };
    static bool PowerOfTwo(uint32_t n) { return n != 0 && (n & (n - 1)) == 0; }
    void SyncEpoch()
    {
        const uint32_t epoch = epoch_.load(std::memory_order_relaxed);
        if (activeEpoch_ == epoch) return;
        for (unsigned vm = 0; vm < kVms; ++vm)
        {
            counts_[vm] = {};
            busy_[vm].store(0, std::memory_order_relaxed);
            for (Entry& entry : entries_[vm]) entry = {};
        }
        activeEpoch_ = epoch;
    }
    std::atomic_flag lock_ = ATOMIC_FLAG_INIT;
    std::atomic<uint32_t> epoch_{1};
    uint32_t activeEpoch_ = 1;
    std::atomic<uint32_t> busy_[kVms]{};
    Counts counts_[kVms]{};
    Entry entries_[kVms][kSlots]{};
};
}
