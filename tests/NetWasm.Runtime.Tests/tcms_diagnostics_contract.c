#include "tcms.h"
#include <assert.h>
#include <stdlib.h>

static uint64_t ticks;
static const size_t reference_offsets[] = {0};
static const TcmsLayout references = {
    reference_offsets, 1, sizeof(uintptr_t), 1
};
static uint64_t clock_nanoseconds(void) { return ticks += 10; }
static void *allocate_memory(size_t bytes, size_t alignment)
{
    (void)alignment;
    return malloc(bytes);
}
static void release_memory(void *address, size_t bytes, size_t alignment)
{
    (void)bytes;
    (void)alignment;
    free(address);
}

int main(void)
{
    Tcms gc;
    assert(tcms_initialize(&gc, allocate_memory, release_memory, SIZE_MAX) == TCMS_OK);
    assert(gc.diagnostics == NULL);
    TcmsDiagnostics counters = {.clock_nanoseconds = clock_nanoseconds};
    gc.diagnostics = &counters;

    void *first, *second;
    assert(tcms_allocate(&gc, 16, (TcmsLayout){0}, NULL, 0, &first) == TCMS_OK);
    assert(tcms_allocate(&gc, 8, (TcmsLayout){0}, NULL, 0, &second) == TCMS_OK);
    assert(counters.allocations == 2 && counters.payload_bytes == 24);
    assert(counters.allocation_bytes == gc.bytes && counters.allocation_bytes > 24);
#ifdef TCMS_BLOCK_ALLOCATION
    assert(counters.payload_zeroed_bytes == 0 && counters.run_zeroed_bytes >= 24);
    assert(counters.table_insertions == 0 && counters.table_zeroed_bytes == 0);
#else
    assert(counters.payload_zeroed_bytes == 24);
    assert(counters.table_insertions == 2 && counters.table_zeroed_bytes > 0);
#endif
    assert(counters.collections == 0 && ticks == 0);
    for (size_t i = 0; i < 16; i++) assert(((unsigned char *)first)[i] == 0);
    for (size_t i = 0; i < 8; i++) assert(((unsigned char *)second)[i] == 0);

    uintptr_t root = (uintptr_t)first;
    size_t table_bytes = gc.object_table_capacity * sizeof(TcmsObject *);
    tcms_collect(&gc, &root, 1);
    assert(gc.objects == 1 && counters.collections == 1);
    assert(counters.marked_objects == 1 && counters.freed_objects == 1);
    assert(counters.resolve_calls == 1 && counters.root_slots == 1);
    assert(counters.reference_slots == 0 && counters.lifetime_records == 0);
#ifdef TCMS_BLOCK_ALLOCATION
    assert(counters.hash_probes == 0 && counters.table_insertions == 0 && table_bytes == 0);
    assert(counters.pin_blocks > 0 && counters.pin_bitmap_words > 0);
    assert(counters.sweep_blocks > 0 && counters.sweep_bitmap_words > 0);
    assert(counters.allocation_blocks > 0 && counters.large_records == 0);
    assert(counters.occupied_slot_bytes >= gc.bytes);
#else
    assert(counters.hash_probes >= 1);
    assert(counters.table_insertions == 3 && counters.table_zeroed_bytes == 2 * table_bytes);
#endif
    assert(counters.roots_nanoseconds == 10 && counters.drain_nanoseconds == 10);
    assert(counters.complete_mark_nanoseconds == 10 && counters.sweep_nanoseconds == 10);
    assert(counters.rebuild_nanoseconds == 10 && ticks == 100);
    assert(tcms_contains(&gc, root));

    gc.threshold = SIZE_MAX;
    void *holder;
    assert(tcms_allocate(&gc, sizeof(uintptr_t), references, NULL, 0, &holder) == TCMS_OK);
    *(uintptr_t *)holder = root;
    counters = (TcmsDiagnostics){0};
    root = (uintptr_t)holder;
    tcms_collect(&gc, &root, 1);
    assert(gc.objects == 2 && counters.marked_objects == 2);
    assert(counters.root_slots == 1 && counters.reference_slots == 1);
#ifdef TCMS_BLOCK_ALLOCATION
    assert(counters.drain_blocks > 0 && counters.drain_bitmap_words > 0);
#else
    assert(counters.drain_blocks == 0 && counters.drain_bitmap_words == 0);
#endif

    void *large;
    assert(tcms_allocate(&gc, 8192, (TcmsLayout){0}, NULL, 0, &large) == TCMS_OK);
    counters = (TcmsDiagnostics){0};
    root = (uintptr_t)large + 8191;
    tcms_collect(&gc, &root, 1);
    assert(gc.objects == 1 && counters.collections == 1);
    assert(counters.large_candidates == 1);
#ifdef TCMS_BLOCK_ALLOCATION
    assert(counters.hash_probes == 0);
#elif defined(TCMS_PAGE_OWNERSHIP)
    assert(counters.hash_probes >= 1);
#else
    assert(counters.hash_probes > 1);
#endif
    assert(counters.marked_objects == 1 && counters.freed_objects == 2);
    assert(counters.root_slots == 1 && counters.reference_slots == 0);
#ifdef TCMS_BLOCK_ALLOCATION
    assert(counters.large_records >= 2);
#else
    assert(counters.large_records == 0);
#endif
    assert(counters.roots_nanoseconds == 0 && counters.rebuild_nanoseconds == 0);

#ifdef TCMS_PAGE_CACHE
    uintptr_t cache_roots[16];
    for (size_t index = 0; index < 16; index++) {
        void *value;
        assert(tcms_allocate(&gc, 16, (TcmsLayout){0}, NULL, 0, &value) == TCMS_OK);
        cache_roots[index] = (uintptr_t)value;
    }
    counters = (TcmsDiagnostics){0};
    tcms_collect(&gc, cache_roots, 16);
    assert(gc.objects == 16 && counters.resolve_calls == 16);
    assert(counters.page_probes > 0 && counters.page_cache_hits > 0);
#endif

    gc.diagnostics = NULL;
    tcms_collect(&gc, NULL, 0);
    assert(gc.objects == 0 && counters.collections == 1);
    tcms_destroy(&gc);
    return 0;
}
