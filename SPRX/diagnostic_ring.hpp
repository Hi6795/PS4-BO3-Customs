#pragma once

#include <atomic>
#include <cstddef>
#include <cstdint>

// A busy diagnostic recorder drops a sample instead of delaying native work.
template<class Record, size_t Capacity>
class DiagnosticRing
{
    static_assert(Capacity > 0);
    std::atomic<bool> busy_{false};
    std::atomic<uint64_t> generation_{0};
    uint64_t activeGeneration_ = 0;
    uint64_t count_ = 0;
    Record records_[Capacity] = {};

    struct Attempt
    {
        std::atomic<bool>& busy;
        bool acquired;
        explicit Attempt(std::atomic<bool>& value)
            : busy(value), acquired(!busy.exchange(true, std::memory_order_acquire)) {}
        ~Attempt() { if (acquired) busy.store(false, std::memory_order_release); }
    };

    void BeginGeneration()
    {
        const uint64_t generation = generation_.load(std::memory_order_acquire);
        if (activeGeneration_ != generation)
        {
            activeGeneration_ = generation;
            count_ = 0;
        }
    }

public:
    bool TryPush(const Record& record)
    {
        Attempt lock(busy_);
        if (!lock.acquired)
            return false;
        BeginGeneration();
        records_[count_ % Capacity] = record;
        ++count_;
        return true;
    }

    // Newest record first; the caller owns the snapshot after this returns.
    bool TrySnapshot(Record (&out)[Capacity], uint64_t& count)
    {
        count = 0;
        Attempt lock(busy_);
        if (!lock.acquired)
            return false;
        BeginGeneration();
        const size_t kept = count_ < Capacity ? (size_t)count_ : Capacity;
        for (size_t back = 0; back < kept; ++back)
            out[back] = records_[(count_ - 1 - back) % Capacity];
        if (activeGeneration_ == generation_.load(std::memory_order_acquire))
            count = count_;
        return true;
    }

    // Level changes must not wait for a stalled diagnostic writer either.
    void Reset() { generation_.fetch_add(1, std::memory_order_release); }
};
