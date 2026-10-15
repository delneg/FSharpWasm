#include <assert.h>
#include <stdlib.h>
#include "tcms_contract_implementation.h"
#include "../../src/NetWasm.Runtime/collector/compact/tcms_lifetime.h"

typedef union {
    struct { size_t bytes, alignment; } data;
    max_align_t alignment;
} Allocation;

static size_t allocations, releases;
static bool forbid_allocation;
static const size_t offsets[] = {0};
static const TcmsLayout references = {offsets, 1, sizeof(uintptr_t), 1};

static void *allocate_memory(size_t bytes, size_t alignment)
{
    assert(!forbid_allocation && alignment <= _Alignof(max_align_t));
    Allocation *allocation = malloc(sizeof(*allocation) + bytes);
    assert(allocation != NULL);
    allocation->data.bytes = bytes;
    allocation->data.alignment = alignment;
    allocations++;
    return allocation + 1;
}

static void release_memory(void *address, size_t bytes, size_t alignment)
{
    Allocation *allocation = (Allocation *)address - 1;
    assert(allocation->data.bytes == bytes && allocation->data.alignment == alignment);
    releases++;
    free(allocation);
}

static Tcms create(void)
{
    Tcms gc;
    assert(tcms_initialize(&gc, allocate_memory, release_memory, SIZE_MAX) == TCMS_OK);
    return gc;
}

static uintptr_t object(Tcms *gc, size_t bytes, TcmsLayout layout)
{
    void *value = NULL;
    assert(tcms_allocate(gc, bytes, layout, NULL, 0, &value) == TCMS_OK);
    return (uintptr_t)value;
}

static void store(uintptr_t owner, uintptr_t value)
{
    memcpy((void *)owner, &value, sizeof(value));
}

static void verify_heap(Tcms *gc)
{
    size_t objects = 0, bytes = 0;
    assert(gc->pending_blocks == NULL && gc->pending_large == NULL);
    assert(gc->marked_bytes == 0 && gc->marked_objects == 0 && gc->reserved_bytes == 0);
    for (TcmsBlock *block = gc->blocks; block != NULL; block = block->next) {
        size_t live = 0;
        assert(!block->queued);
        for (size_t i = 0; i < bitmap_words(block); i++)
            assert(block->marked[i] == 0 && block->pending[i] == 0);
        for (size_t slot = 0; slot < block->slot_count; slot++) {
            uintptr_t mask = (uintptr_t)1 << (slot % TCMS_WORD_BITS);
            bool pinned = (block->pinned[slot / TCMS_WORD_BITS] & mask) != 0;
            if (!block_bit(block, slot)) { assert(!pinned); continue; }
            TcmsObject *header = (TcmsObject *)(block->slots + slot * block->slot_bytes);
            assert((header->data.pins != 0) == pinned);
            assert(bitmap_object_bytes(header) <= block->slot_bytes);
            bytes += bitmap_object_bytes(header);
            live++;
        }
        assert(live == block->live);
        objects += live;
    }
    for (TcmsLarge *large = gc->large_head; large != NULL; large = large->data.next) {
        assert(!large->data.marked && large->data.pending_next == NULL);
        size_t extent = bitmap_object_bytes(large_object(large));
        assert(extent > TCMS_INTERIOR_SCAN_BYTES);
        bytes += extent + sizeof(TcmsLarge);
        objects++;
    }
    assert(objects == gc->objects && bytes == gc->bytes);
}

static void word_sweep_and_dead_headers(void)
{
    Tcms gc = create();
    enum { count = sizeof(uintptr_t) * 32 + 7 };
    uintptr_t values[count], roots[count];
    size_t root_count = 0, expected_bytes = 0;
    for (size_t i = 0; i < count; i++) {
        size_t size = 1 + i % 3 * 16;
        values[i] = object(&gc, size, (TcmsLayout){0});
        memset((void *)values[i], 0xa5, size);
        if (i % 7 == 0) {
            roots[root_count++] = values[i] + size;
            expected_bytes += sizeof(TcmsObject) + size;
        } else {
            /* Prove sweep does not read dead small-object metadata. */
            memset((TcmsObject *)values[i] - 1, 0xa5, sizeof(TcmsObject));
        }
    }
    forbid_allocation = true;
    tcms_collect(&gc, roots, root_count);
    forbid_allocation = false;
    assert(gc.objects == root_count && gc.bytes == expected_bytes);
    verify_heap(&gc);
    for (size_t i = 0; i < count; i++) {
        assert(tcms_contains(&gc, values[i]) == (i % 7 == 0));
        if (i % 7 == 0) assert(*(unsigned char *)values[i] == 0xa5);
    }
    for (size_t i = 0; i < count; i++) {
        size_t size = 1 + i % 3 * 16;
        uintptr_t fresh = object(&gc, size, (TcmsLayout){0});
        for (size_t j = 0; j < size; j++) assert(*(unsigned char *)(fresh + j) == 0);
    }
    tcms_collect(&gc, NULL, 0);
    verify_heap(&gc);
    assert(gc.objects == 0 && gc.empty_block_count == 1);
    tcms_destroy(&gc);
}

static void mixed_worklist_and_counted_pins(void)
{
    Tcms gc = create();
    uintptr_t head = 0;
    for (size_t i = 0; i < 6000; i++) {
        size_t size = i % 101 == 0 ? 8192 : sizeof(uintptr_t) + i % 3 * 48;
        uintptr_t next = object(&gc, size, references);
        store(next, head);
        head = next;
    }
    assert(tcms_pin(&gc, head) == TCMS_OK && tcms_pin(&gc, head + 1) == TCMS_OK);
    forbid_allocation = true;
    for (size_t i = 0; i < 4; i++) {
        tcms_collect(&gc, NULL, 0);
        assert(gc.objects == 6000);
        verify_heap(&gc);
    }
    assert(tcms_unpin(&gc, head) == TCMS_OK);
    tcms_collect(&gc, NULL, 0);
    assert(gc.objects == 6000);
    assert(tcms_unpin(&gc, head) == TCMS_OK);
    tcms_collect(&gc, NULL, 0);
    forbid_allocation = false;
    assert(gc.objects == 0 && gc.bytes == 0);
    verify_heap(&gc);
    tcms_destroy(&gc);
}

typedef struct { Tcms *gc; unsigned calls; uintptr_t resurrected; } FinalizerState;
static void finalize(void *value, void *raw_state)
{
    FinalizerState *state = raw_state;
    state->calls++;
    state->resurrected = (uintptr_t)value;
    tcms_collect(state->gc, NULL, 0);
    assert(state->gc->objects == 3 && tcms_contains(state->gc, (uintptr_t)value));
    verify_heap(state->gc);
}

static void mixed_lifetime_completion(void)
{
    Tcms gc = create();
    FinalizerState state = {.gc = &gc};
    uintptr_t key = object(&gc, 8192, (TcmsLayout){0});
    uintptr_t value = object(&gc, 4096, references);
    uintptr_t child = object(&gc, 32, (TcmsLayout){0});
    store(value, child);
    uintptr_t key_slot = key, value_slot = value, short_key = key, long_value = value;
    assert(tcms_register_finalizer(&gc, key, finalize, &state, NULL, NULL) == TCMS_OK);
    assert(tcms_register_ephemeron(&gc, &key_slot, &value_slot) == TCMS_OK);
    assert(tcms_register_weak(&gc, &short_key, key, TCMS_WEAK_SHORT) == TCMS_OK);
    assert(tcms_register_weak(&gc, &long_value, value, TCMS_WEAK_LONG) == TCMS_OK);
    forbid_allocation = true;
    tcms_collect(&gc, NULL, 0);
    assert(gc.objects == 3 && short_key == 0 && long_value == value);
    verify_heap(&gc);
    assert(tcms_invoke_finalizers(&gc) == 1 && state.calls == 1 && state.resurrected == key);
    tcms_collect(&gc, &state.resurrected, 1);
    assert(gc.objects == 3 && long_value == value);
    verify_heap(&gc);
    tcms_collect(&gc, NULL, 0);
    forbid_allocation = false;
    assert(gc.objects == 0 && key_slot == 0 && value_slot == 0 && long_value == 0);
    verify_heap(&gc);
    tcms_destroy(&gc);
}

static void explicit_mark_and_large_pin(void)
{
    Tcms gc = create();
    uintptr_t small = object(&gc, 0, (TcmsLayout){0});
    uintptr_t large = object(&gc, 8192, references);
    store(large, small);
    assert(tcms_mark(&gc, large) && !tcms_mark(&gc, large));
    tcms_drain_mark(&gc);
    assert(tcms_is_marked(&gc, small) && tcms_is_marked(&gc, large));
    tcms_collect(&gc, NULL, 0);
    assert(gc.objects == 2);
    verify_heap(&gc);
    assert(tcms_pin(&gc, large + 8192) == TCMS_OK);
    tcms_collect(&gc, NULL, 0);
    assert(gc.objects == 2);
    assert(tcms_unpin(&gc, large) == TCMS_OK);
    tcms_collect(&gc, NULL, 0);
    assert(gc.objects == 0);
    verify_heap(&gc);
    tcms_destroy(&gc);
}

typedef struct { uintptr_t value; size_t collections; } MutatorRoot;
static void trace_mutator_root(Tcms *gc, void *state)
{
    MutatorRoot *root = state;
    root->collections++;
    tcms_mark(gc, root->value);
}

static void collect_before_every_publication(void)
{
    Tcms gc;
    assert(tcms_initialize(&gc, allocate_memory, release_memory, 0) == TCMS_OK);
    MutatorRoot root = {0};
    gc.trace_roots = trace_mutator_root;
    gc.root_state = &root;
    for (size_t i = 0; i < 300; i++) {
        /* A zero minimum budget reserves exactly one slot per refill. */
        gc.threshold = 0;
        uintptr_t next = object(&gc, i % 31 == 0 ? 8192 : sizeof(uintptr_t), references);
        assert(root.collections == i + 1);
        store(next, root.value);
        root.value = next;
        assert(gc.objects == i + 1);
        verify_heap(&gc);
    }
    root.value = 0;
    tcms_collect(&gc, NULL, 0);
    assert(gc.objects == 0 && root.collections == 301);
    verify_heap(&gc);
    tcms_destroy(&gc);
}

int main(void)
{
    word_sweep_and_dead_headers();
    mixed_worklist_and_counted_pins();
    mixed_lifetime_completion();
    explicit_mark_and_large_pin();
    collect_before_every_publication();
    assert(allocations == releases);
    return 0;
}
