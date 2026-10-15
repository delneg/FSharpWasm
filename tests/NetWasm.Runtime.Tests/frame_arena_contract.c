#include <assert.h>
#include <stdbool.h>

/* Include the production implementation so owner state and exact root
 * enumeration are tested directly. Unused runtime entry points are discarded
 * by the contract link, not replaced by test implementations. */
#define NETWASM_RUNTIME_PACK
#define NETWASM_UNMANAGED_ALLOCATOR_BACKEND "unmanaged_allocator_libc.h"
#pragma GCC diagnostic push
#pragma GCC diagnostic ignored "-Wattributes"
#include "../../src/NetWasm.Runtime/native_runtime.c"
#pragma GCC diagnostic pop

static bool fail_metadata;
static size_t metadata_allocations, metadata_releases, observed_roots;
static uintptr_t observed_sum;

void *collector_allocate_metadata(size_t size)
{
    if (fail_metadata || size > 1024 * 1024) return NULL;
    void *allocation = calloc(1, size + sizeof(void *) + 31);
    assert(allocation != NULL);
    /* Exercise the actual minimum Wasm alignment even on native allocators
       that happen to provide sixteen bytes. Preserve the true free address. */
    uintptr_t address = ((uintptr_t)allocation + sizeof(void *) + 15) & ~(uintptr_t)15;
    address += 8;
    void *result = (void *)address;
    ((void **)result)[-1] = allocation;
    metadata_allocations++;
    return result;
}

void collector_release_metadata(void *address)
{
    metadata_releases++;
    free(((void **)address)[-1]);
}

void collector_trace_root_range(void *start, void *end)
{
    for (netwasm_reference_t *slot = start; slot < (netwasm_reference_t *)end; slot++) {
        if (*slot == 0) continue;
        observed_roots++;
        observed_sum += *slot;
    }
}

static void verify_roots(size_t count, uintptr_t sum)
{
    observed_roots = 0;
    observed_sum = 0;
    enumerate_managed_roots();
    assert(observed_roots == count && observed_sum == sum);
}

static size_t root_slot_count(void)
{
    size_t count = 0;
    uintptr_t end = root_frame_arena.cursor;
    for (FrameArenaChunk *chunk = root_frame_arena.current; chunk != NULL; chunk = chunk->previous) {
        count += (end - (uintptr_t)frame_arena_bytes(chunk)) / sizeof(netwasm_reference_t);
        if (chunk->previous != NULL) end = chunk->previous->saved_cursor;
    }
    return count;
}

static void release_arena(FrameArena *arena)
{
    FrameArenaChunk *chunk = arena->current;
    while (chunk != NULL) {
        FrameArenaChunk *next = chunk->next;
        collector_release_metadata(chunk);
        chunk = next;
    }
    arena->current = NULL;
    arena->cursor = arena->limit = arena->begin = 0;
}

static void root_frames(void)
{
    fail_metadata = true;
    assert(root_frame_enter(1) == 0 && root_slot_count() == 0 && root_frame_arena.current == NULL);
    fail_metadata = false;
    netwasm_address_t frames[96];
    uintptr_t sum = 0;
    for (size_t i = 0; i < 96; i++) {
        frames[i] = root_frame_enter(17);
        assert(frames[i] != 0);
        assert(frames[i] >= root_frame_arena.begin && frames[i] < root_frame_arena.cursor);
        assert(root_slot_count() == (i + 1) * 17);
        netwasm_reference_t *slots = (void *)(uintptr_t)frames[i];
        for (size_t j = 0; j < 17; j++) slots[j] = i * 17 + j + 1;
        sum += (i * 17 + 1 + i * 17 + 17) * 17 / 2;
        verify_roots((i + 1) * 17, sum);
    }
    FrameArenaChunk *current = root_frame_arena.current;
    uintptr_t cursor = root_frame_arena.cursor, limit = root_frame_arena.limit;
    uintptr_t begin = root_frame_arena.begin;
    u32 depth = root_slot_count();
    fail_metadata = true;
    u32 beyond_available = (u32)((limit - cursor) / sizeof(netwasm_reference_t)) + 1;
    assert(root_frame_enter(beyond_available) == 0);
    assert(root_frame_arena.current == current && root_frame_arena.cursor == cursor &&
        root_frame_arena.limit == limit && root_frame_arena.begin == begin && root_slot_count() == depth);
    fail_metadata = false;
    for (size_t i = 96; i-- != 0;) {
        root_frame_leave(frames[i]);
        sum -= (i * 17 + 1 + i * 17 + 17) * 17 / 2;
        verify_roots(i * 17, sum);
    }
    assert(root_slot_count() == 0 && root_frame_arena.cursor == (uintptr_t)frame_arena_bytes(root_frame_arena.current));
    size_t allocations_before = metadata_allocations;
    for (size_t i = 0; i < 96; i++) {
        assert(root_frame_enter(17) == frames[i]);
        assert(frames[i] >= root_frame_arena.begin && frames[i] < root_frame_arena.cursor);
    }
    assert(metadata_allocations == allocations_before);
    for (size_t i = 96; i-- != 0;) root_frame_leave(frames[i]);
    netwasm_address_t empty = root_frame_enter(0);
    assert(empty != 0 && root_slot_count() == 0);
    verify_roots(0, 0);
    root_frame_leave(empty);
    assert(root_frame_enter(UINT32_MAX) == 0 && root_slot_count() == 0);
    release_arena(&root_frame_arena);
}

static void root_chunk_transitions(void)
{
    /* Both collecting and Bump runtimes own distinct storage for nested frames. */
    netwasm_address_t outer = root_frame_enter(65);
    assert(outer != 0);
    netwasm_reference_t *slots = (void *)(uintptr_t)outer;
    for (size_t i = 0; i < 65; i++) slots[i] = i + 1;
    FrameArenaChunk *first = root_frame_arena.current;
    assert(first->capacity == ROOT_ARENA_INITIAL_CAPACITY);
    uintptr_t first_cursor = root_frame_arena.cursor;

    u32 inner_slots = (u32)(first->capacity / sizeof(netwasm_reference_t)) + 1;
    netwasm_address_t inner = root_frame_enter(inner_slots);
    assert(inner != 0);
    memset((void *)(uintptr_t)inner, 0, inner_slots * sizeof(netwasm_reference_t));
    FrameArenaChunk *cached = root_frame_arena.current;
    assert(cached != first && cached->previous == first && first->saved_cursor == first_cursor);
    u32 depth = root_slot_count();
    uintptr_t cursor = root_frame_arena.cursor, begin = root_frame_arena.begin;
    uintptr_t limit = root_frame_arena.limit, saved_cursor = cached->saved_cursor;
    fail_metadata = true;
    assert(root_frame_enter(65535) == 0);
    assert(root_frame_arena.current == cached && root_frame_arena.cursor == cursor &&
        root_frame_arena.begin == begin && root_frame_arena.limit == limit && root_slot_count() == depth &&
        cached->saved_cursor == saved_cursor && cached->previous == first);
    fail_metadata = false;
    root_frame_leave(inner);
    assert(root_frame_arena.current == first && root_frame_arena.cursor == first_cursor &&
        root_frame_arena.begin == (uintptr_t)frame_arena_bytes(first));
    verify_roots(65, 65 * 66 / 2);

    /* Insert a larger chunk, then reuse and reparent its cached successor. */
    u32 large_slots = inner_slots + 1024;
    netwasm_address_t large = root_frame_enter(large_slots);
    assert(large != 0);
    memset((void *)(uintptr_t)large, 0, large_slots * sizeof(netwasm_reference_t));
    FrameArenaChunk *inserted = root_frame_arena.current;
    assert(inserted != cached && inserted->next == cached && inserted->previous == first);
    uintptr_t large_cursor = root_frame_arena.cursor;
    size_t allocations_before = metadata_allocations;
    netwasm_address_t reused = root_frame_enter(65);
    assert(reused != 0 && root_frame_arena.current == cached && cached->previous == inserted &&
        metadata_allocations == allocations_before);
    memset((void *)(uintptr_t)reused, 0, 65 * sizeof(netwasm_reference_t));
    verify_roots(65, 65 * 66 / 2);
    root_frame_leave(reused);
    assert(root_frame_arena.current == inserted && root_frame_arena.cursor == large_cursor);
    root_frame_leave(large);
    assert(root_frame_arena.current == first && root_frame_arena.cursor == first_cursor);
    verify_roots(65, 65 * 66 / 2);
    root_frame_leave(outer);
    assert(root_slot_count() == 0 && root_frame_arena.cursor == root_frame_arena.begin);
    release_arena(&root_frame_arena);
}

static void root_frame_capacity_boundary(void)
{
    /* Use more than the Bump scratch capacity to exercise the shared arena. */
    netwasm_address_t outer = root_frame_enter(65);
    assert(outer != 0);
    memset((void *)(uintptr_t)outer, 0, 65 * sizeof(netwasm_reference_t));
    ((netwasm_reference_t *)(uintptr_t)outer)[0] = 17;
    uintptr_t cursor = root_frame_arena.cursor;
    FrameArenaChunk *chunk = root_frame_arena.current;
    size_t available = root_frame_arena.limit - cursor;
    u32 slots = (u32)(available / sizeof(netwasm_reference_t));
    assert(slots > 64);
    size_t before = metadata_allocations;
    netwasm_address_t filled = root_frame_enter(slots);
    assert(filled != 0 && root_frame_arena.cursor == root_frame_arena.limit &&
        root_frame_arena.current == chunk && metadata_allocations == before);
    memset((void *)(uintptr_t)filled, 0, slots * sizeof(netwasm_reference_t));
    ((netwasm_reference_t *)(uintptr_t)filled)[slots - 1] = 23;
    verify_roots(2, 40);

    u32 depth = root_slot_count();
    netwasm_address_t empty = root_frame_enter(0);
    assert(empty != 0 && root_slot_count() == depth);
    fail_metadata = true;
    assert(root_frame_enter(65) == 0 && root_slot_count() == depth &&
        root_frame_arena.current == chunk && root_frame_arena.cursor == root_frame_arena.limit);
    fail_metadata = false;
    netwasm_address_t next = root_frame_enter(65);
    assert(next != 0 && root_frame_arena.current != chunk);
    memset((void *)(uintptr_t)next, 0, 65 * sizeof(netwasm_reference_t));
    verify_roots(2, 40);
    root_frame_leave(next);
    assert(root_frame_arena.current == chunk && root_frame_arena.cursor == root_frame_arena.limit);
    root_frame_leave(empty);
    root_frame_leave(filled);
    assert(root_frame_arena.cursor == cursor);
    verify_roots(1, 17);
    root_frame_leave(outer);
    assert(root_slot_count() == 0);
    release_arena(&root_frame_arena);
}

static void value_frames(void)
{
    fail_metadata = true;
    assert(value_frame_enter(1) == 0 && value_frame == NULL && value_frame_arena.current == NULL);
    fail_metadata = false;
    netwasm_address_t outer = value_frame_enter(32);
    assert(outer != 0 && outer % 16 == 0);
    memset((void *)(uintptr_t)outer, 0x5a, 32);
    FrameArenaChunk *first = value_frame_arena.current;
    netwasm_address_t middle = value_frame_enter(8192);
    assert(middle != 0 && middle % 16 == 0 && value_frame->chunk != first);
    memset((void *)(uintptr_t)middle, 0xa5, 8192);
    assert(value_frame_enter(8192) != 0);
    assert(value_frame->chunk == value_frame_arena.current);
    value_frame_leave(middle); /* An exception may unwind several value frames. */
    assert(value_frame->bytes == (unsigned char *)(uintptr_t)outer);
    size_t allocations_before = metadata_allocations;
    assert(value_frame_enter(8192) == middle && metadata_allocations == allocations_before);
    for (size_t i = 0; i < 32; i++) assert(((unsigned char *)(uintptr_t)outer)[i] == 0x5a);
    value_frame_leave(middle);
    value_frame_leave(outer);
    assert(value_frame == NULL && value_frame_arena.current == first &&
        value_frame_arena.cursor == (uintptr_t)frame_arena_bytes(first));
    assert(value_frame_enter(UINT32_MAX) == 0 && value_frame == NULL);
    release_arena(&value_frame_arena);
}

int main(void)
{
    root_frames();
    netwasm_address_t empty = root_frame_enter(0);
    assert(empty != 0 && root_slot_count() == 0);
    verify_roots(0, 0);
    root_frame_leave(empty);
    release_arena(&root_frame_arena);
    root_chunk_transitions();
    root_frame_capacity_boundary();
    value_frames();
    assert(metadata_allocations == metadata_releases);
    return 0;
}
