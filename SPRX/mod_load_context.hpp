#pragma once

#include <atomic>
#include <cstddef>
#include <cstdint>

// Engine workers can load different mod zones concurrently. A shared saved
// zone/thread pair is not a stack and can attribute assets to another mod.
// These slots keep the active zone per worker without using dynamic PS4 TLS.
template <std::size_t Capacity = 64>
class ModLoadContexts
{
    struct Slot
    {
        std::atomic<uint64_t> owner{0};
        std::atomic<uintptr_t> value{0};
    };

    Slot slots_[Capacity]{};

    Slot* Claim(uint64_t thread, bool& first)
    {
        if (!thread)
            return nullptr;

        for (Slot& slot : slots_)
        {
            if (slot.owner.load(std::memory_order_acquire) == thread)
                return &slot;
        }

        for (Slot& slot : slots_)
        {
            uint64_t empty = 0;

            if (slot.owner.compare_exchange_strong(empty, thread, std::memory_order_acq_rel))
            {
                first = true;
                return &slot;
            }
        }

        return nullptr;
    }

public:
    ModLoadContexts() = default;
    ModLoadContexts(const ModLoadContexts&) = delete;
    ModLoadContexts& operator=(const ModLoadContexts&) = delete;

    class Scope
    {
        Slot* slot_ = nullptr;
        uintptr_t previous_ = 0;
        bool first_ = false;

    public:
        Scope(ModLoadContexts& contexts, uint64_t thread, uintptr_t value)
            : slot_(contexts.Claim(thread, first_))
        {
            if (slot_)
                previous_ = slot_->value.exchange(value, std::memory_order_acq_rel);
        }

        ~Scope()
        {
            if (!slot_)
                return;

            slot_->value.store(previous_, std::memory_order_release);

            if (first_)
                slot_->owner.store(0, std::memory_order_release);
        }

        Scope(const Scope&) = delete;
        Scope& operator=(const Scope&) = delete;

        bool Valid() const { return slot_ != nullptr; }
    };

    uintptr_t Current(uint64_t thread) const
    {
        if (!thread)
            return 0;

        for (const Slot& slot : slots_)
        {
            if (slot.owner.load(std::memory_order_acquire) == thread)
                return slot.value.load(std::memory_order_acquire);
        }

        return 0;
    }
};
