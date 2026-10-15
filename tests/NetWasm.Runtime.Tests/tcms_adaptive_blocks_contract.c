#include <assert.h>
#include <stdlib.h>
#include "tcms_contract_implementation.h"

#ifndef TCMS_ADAPTIVE_BLOCKS
#error This contract requires adaptive blocks.
#endif

typedef union {
    struct { size_t bytes, alignment; } data;
    max_align_t alignment;
} Allocation;
static size_t allocations, releases, fail_after = SIZE_MAX;

static void *allocate_memory(size_t bytes, size_t alignment)
{
    assert(alignment <= _Alignof(max_align_t));
    if (fail_after == 0) return NULL;
    if (fail_after != SIZE_MAX) fail_after--;
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

static void *allocate_object(Tcms *gc, size_t bytes)
{
    void *object = NULL;
    assert(tcms_allocate(gc, bytes, (TcmsLayout){0}, NULL, 0, &object) == TCMS_OK);
    for (size_t i = 0; i < bytes; i++) assert(((unsigned char *)object)[i] == 0);
    return object;
}

static TcmsBlock *owner(Tcms *gc, void *object)
{
    TcmsBlock *block;
    assert(resolve_with_block(gc, (uintptr_t)object, false, &block) == (TcmsObject *)object - 1);
    return block;
}

static void rounding_and_boundaries(void)
{
    size_t previous = 0;
    for (size_t bytes = sizeof(TcmsObject) + 1; bytes <= TCMS_INTERIOR_SCAN_BYTES; bytes++) {
        size_t rounded = block_slot_bytes(bytes);
        assert(rounded >= bytes && rounded >= previous && rounded <= TCMS_INTERIOR_SCAN_BYTES);
        assert(rounded % TCMS_BLOCK_ALIGNMENT == 0);
        if (bytes <= TCMS_BLOCK_ALIGNMENT * 16)
            assert(rounded - bytes < TCMS_BLOCK_ALIGNMENT);
        else assert(rounded - bytes < (bytes + 7) / 8);
        size_t initial = initial_block_slots(rounded);
        size_t extent = sizeof(TcmsBlock) + initial * rounded;
        assert(initial != 0 && extent > ((size_t)1 << TCMS_PAGE_SHIFT) && extent <= TCMS_BLOCK_BYTES);
        assert(extent - rounded <= ((size_t)1 << TCMS_PAGE_SHIFT));
        size_t maximum = (TCMS_BLOCK_BYTES - sizeof(TcmsBlock)) / rounded;
        assert(next_block_slots(rounded, maximum) == maximum);
        for (size_t slots = initial; slots < maximum;) {
            size_t next = next_block_slots(rounded, slots);
            assert(next > slots && next <= maximum);
            slots = next;
        }
        previous = rounded;
    }
    const size_t sizes[] = {0, 1, 127, 128, 129, 255, 256, 257, 511, 1023, 2047,
        TCMS_INTERIOR_SCAN_BYTES - sizeof(TcmsObject), 4095, 4096, 8192};
    Tcms gc = create();
    for (size_t i = 0; i < sizeof(sizes) / sizeof(sizes[0]); i++) {
        size_t bytes = sizes[i];
        void *object = allocate_object(&gc, bytes);
        TcmsBlock *block = owner(&gc, object);
        bool small = sizeof(TcmsObject) + (bytes == 0 ? 1 : bytes) <= TCMS_INTERIOR_SCAN_BYTES;
        assert((block != NULL) == small);
        if (bytes != 0) {
            assert(tcms_contains(&gc, (uintptr_t)object + bytes - 1));
            assert(!tcms_contains(&gc, (uintptr_t)object + bytes));
            assert(tcms_pin(&gc, (uintptr_t)object + bytes) == TCMS_OK);
            assert(tcms_unpin(&gc, (uintptr_t)object + bytes) == TCMS_OK);
        }
    }
    tcms_destroy(&gc);
}

static void growth_and_hint_reset(void)
{
    Tcms gc = create();
    void *first = allocate_object(&gc, 16);
    TcmsBlock *a = owner(&gc, first);
    size_t stride = a->slot_bytes, first_slots = a->slot_count;
    assert(first_slots == initial_block_slots(stride));
    assert(block_backing_bytes(a) == sizeof(TcmsBlock) + first_slots * stride);
    for (size_t i = 1; i < first_slots; i++)
        assert(owner(&gc, allocate_object(&gc, 16)) == a);
    void *second = allocate_object(&gc, 16);
    TcmsBlock *b = owner(&gc, second);
    assert(b != a && b->slot_count == next_block_slots(stride, first_slots));
    assert(gc.block_bytes == block_backing_bytes(a) + block_backing_bytes(b));
    void *temporary = allocate_object(&gc, 512);
    TcmsBlock *temporary_block = owner(&gc, temporary);
    size_t temporary_class = temporary_block->slot_bytes / TCMS_BLOCK_ALIGNMENT - 1;
    assert(gc.block_classes[temporary_class].next_slots != 0);
    uintptr_t roots[] = {(uintptr_t)first, (uintptr_t)second};
    tcms_collect(&gc, roots, 2);
    assert(gc.objects == 2 && gc.reserved_bytes == 0);
    assert(gc.block_classes[temporary_class].next_slots == 0);
    size_t size_class = stride / TCMS_BLOCK_ALIGNMENT - 1;
    assert(gc.block_classes[size_class].next_slots == next_block_slots(stride, b->slot_count));
    tcms_collect(&gc, NULL, 0);
    assert(gc.block_classes[size_class].next_slots == 0 && gc.empty_block_count == 1);
    tcms_destroy(&gc);
}

static void cached_block_fit_and_failure(void)
{
    Tcms gc = create();
    void *first = allocate_object(&gc, 1);
    TcmsBlock *small_block = owner(&gc, first);
    size_t small_backing = block_backing_bytes(small_block);
    assert(small_backing - sizeof(TcmsBlock) < TCMS_INTERIOR_SCAN_BYTES);
    tcms_collect(&gc, NULL, 0);
    assert(gc.empty_blocks == small_block && gc.objects == 0);
    size_t payload_bytes = TCMS_INTERIOR_SCAN_BYTES - sizeof(TcmsObject);
    void *result = (void *)(uintptr_t)1;
    fail_after = 0;
    assert(tcms_allocate(&gc, payload_bytes, (TcmsLayout){0}, NULL, 0, &result) == TCMS_OUT_OF_MEMORY);
    assert(result == (void *)(uintptr_t)1 && gc.objects == 0 && gc.bytes == 0);
    assert(gc.empty_blocks == small_block && gc.block_bytes == small_backing);
    assert(gc.block_classes[TCMS_BLOCK_CLASSES - 1].next_slots == 0);
    fail_after = SIZE_MAX;
    result = allocate_object(&gc, payload_bytes);
    TcmsBlock *large_class = owner(&gc, result);
    assert(large_class != small_block && gc.empty_blocks == small_block);
    size_t larger_backing = block_backing_bytes(large_class);
    assert(gc.block_bytes == small_backing + larger_backing);
    memset(result, 0xa5, payload_bytes);
    tcms_collect(&gc, NULL, 0);
    assert(gc.empty_blocks == large_class && gc.empty_block_count == 1);
    assert(gc.block_bytes == larger_backing && gc.blocks == large_class && large_class->next == NULL);
    size_t allocated_before = allocations;
    result = allocate_object(&gc, 1);
    assert(owner(&gc, result) == large_class && allocations == allocated_before);
    assert(gc.block_bytes == larger_backing && gc.empty_blocks == NULL);
    tcms_destroy(&gc);
}

int main(void)
{
    rounding_and_boundaries();
    growth_and_hint_reset();
    cached_block_fit_and_failure();
    assert(allocations == releases);
    return 0;
}
