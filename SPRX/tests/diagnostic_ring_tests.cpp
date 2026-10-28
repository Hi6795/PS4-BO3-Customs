#include "../diagnostic_ring.hpp"
#include <cassert>
#include <cstdio>
#include <thread>
#include <vector>

struct Record
{
    uint64_t a=0, b=0, c=0, d=0;
};

static Record Make(uint64_t n) { return {n,n^0xBADF00D,n+9,~n}; }
static void Check(const Record& r)
{
    assert(r.b==(r.a^0xBADF00D) && r.c==r.a+9 && r.d==~r.a);
}

static std::atomic<bool> hold{false}, entered{false}, resume{false};
struct HeldRecord
{
    int value=0;
    HeldRecord& operator=(const HeldRecord& other)
    {
        if(hold.load())
        {
            entered=true;
            while(!resume.load()) std::this_thread::yield();
        }
        value=other.value;
        return *this;
    }
};

int main()
{
    DiagnosticRing<Record,64> ring;
    Record snapshot[64]; uint64_t count=99;
    assert(ring.TrySnapshot(snapshot,count) && count==0);
    for(uint64_t i=0;i<100;++i) assert(ring.TryPush(Make(i)));
    assert(ring.TrySnapshot(snapshot,count) && count==100);
    for(uint64_t i=0;i<64;++i) { Check(snapshot[i]); assert(snapshot[i].a==99-i); }
    assert(ring.TryPush(Make(100)) && snapshot[0].a==99);
    ring.Reset(); assert(ring.TrySnapshot(snapshot,count) && count==0);

    std::atomic<uint64_t> serial{0}, accepted{0};
    std::atomic<int> running{4};
    std::vector<std::thread> writers;
    for(int t=0;t<4;++t) writers.emplace_back([&] {
        for(int i=0;i<10000;++i)
            if(ring.TryPush(Make(serial.fetch_add(1)))) ++accepted;
        --running;
    });
    std::thread reader([&] {
        Record view[64]; uint64_t seen;
        do {
            if(ring.TrySnapshot(view,seen))
                for(uint64_t i=0;i<seen && i<64;++i) Check(view[i]);
        } while(running.load());
    });
    for(auto& writer:writers) writer.join();
    reader.join();
    assert(accepted>0 && ring.TrySnapshot(snapshot,count) && count==accepted);

    // A paused diagnostic copy cannot block capture, snapshots or level reset.
    DiagnosticRing<HeldRecord,2> busy;
    hold=true;
    std::thread paused([&] { assert(busy.TryPush(HeldRecord{7})); });
    while(!entered.load()) std::this_thread::yield();
    HeldRecord view[2];
    assert(!busy.TryPush(HeldRecord{8}));
    assert(!busy.TrySnapshot(view,count) && count==0);
    busy.Reset(); // Must return while the writer is still deliberately paused.
    hold=false; resume=true;
    paused.join();
    assert(busy.TrySnapshot(view,count) && count==0);
    assert(busy.TryPush(HeldRecord{9}) && busy.TrySnapshot(view,count) && count==1 && view[0].value==9);
    std::puts("PASS: coherent concurrent diagnostic snapshots, wrap/reset, stable copies and nonblocking busy paths");
}
