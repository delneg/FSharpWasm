#ifndef NETWASM_TCMS_DIAGNOSTICS_H
#define NETWASM_TCMS_DIAGNOSTICS_H

/* Maintainer-only measurement. No storage, branches or clock calls are emitted
 * unless the entire diagnostic build opts in. Counters perturb hot paths;
 * uninstrumented A/B runs, not these counters, establish a speedup. */
#ifdef TCMS_DIAGNOSTICS
#include <stdint.h>

typedef struct {
    uint64_t (*clock_nanoseconds)(void);
    uint64_t allocations, payload_bytes, allocation_bytes, payload_zeroed_bytes;
    uint64_t run_zeroed_bytes;
    uint64_t table_zeroed_bytes, table_insertions, hash_probes;
    uint64_t page_probes, page_cache_hits;
    uint64_t resolve_calls, large_candidates;
    uint64_t collections, marked_objects, freed_objects;
    uint64_t root_slots, reference_slots;
    uint64_t drain_blocks, drain_bitmap_words;
    uint64_t pin_blocks, pin_bitmap_words;
    uint64_t sweep_blocks, sweep_bitmap_words;
    uint64_t allocation_blocks, large_records, lifetime_records;
    uint64_t occupied_slot_bytes;
    uint64_t peak_block_bytes;
    uint64_t roots_nanoseconds, drain_nanoseconds, complete_mark_nanoseconds;
    uint64_t sweep_nanoseconds, rebuild_nanoseconds;
} TcmsDiagnostics;

#define TCMS_COUNT(gc, member, value) do { \
    if ((gc)->diagnostics != NULL) \
        (gc)->diagnostics->member += (uint64_t)(value); \
} while (0)
#define TCMS_MAX(gc, member, value) do { \
    if ((gc)->diagnostics != NULL && \
        (gc)->diagnostics->member < (uint64_t)(value)) \
        (gc)->diagnostics->member = (uint64_t)(value); \
} while (0)
#define TCMS_CLOCK(gc) ((gc)->diagnostics != NULL && \
    (gc)->diagnostics->clock_nanoseconds != NULL ? \
    (gc)->diagnostics->clock_nanoseconds() : 0)
#define TCMS_TIME_START(gc, name) uint64_t name = TCMS_CLOCK(gc)
#define TCMS_TIME_END(gc, member, name) TCMS_COUNT(gc, member, TCMS_CLOCK(gc) - (name))
#else
#define TCMS_COUNT(gc, member, value) ((void)0)
#define TCMS_MAX(gc, member, value) ((void)0)
#define TCMS_TIME_START(gc, name) ((void)0)
#define TCMS_TIME_END(gc, member, name) ((void)0)
#endif

#endif
