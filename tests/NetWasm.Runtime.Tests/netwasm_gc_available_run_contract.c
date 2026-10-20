/* SPDX-License-Identifier: MIT */
#include "../../src/NetWasm.Runtime/collector/netwasm/netwasm_gc.h"
#include "netwasm_gc_test_layouts.h"
#include <assert.h>
#include <stdlib.h>
#include <string.h>

static size_t allocations;
static size_t releases;
static uintptr_t root;

static void *allocate_memory(size_t bytes, size_t alignment)
{
    assert(alignment <= _Alignof(max_align_t));
    allocations++;
    return malloc(bytes);
}

static void release_memory(void *address, size_t bytes, size_t alignment)
{
    (void)bytes;
    (void)alignment;
    releases++;
    free(address);
}

static void trace_roots(NetWasmGc *gc, void *state)
{
    (void)state;
    /* Even a populated run cannot be consumed while collection is active. */
    assert(test_try_allocate_available(gc, 1, 32, NULL) == NULL);
    if (root != 0) netwasm_gc_mark(gc, root);
}

static void assert_miss(NetWasmGc *gc, size_t count, size_t size,
    const NetWasmGcDescriptor *descriptor)
{
    size_t objects = netwasm_gc_object_count(gc);
    uint64_t bytes = netwasm_gc_live_bytes(gc);
    uint64_t total = netwasm_gc_total_allocated_bytes(gc);
    uint64_t collections = netwasm_gc_collection_count(gc);
    size_t backing_allocations = allocations;
    assert(test_try_allocate_available(gc, count, size, descriptor) == NULL);
    assert(netwasm_gc_object_count(gc) == objects);
    assert(netwasm_gc_live_bytes(gc) == bytes);
    assert(netwasm_gc_total_allocated_bytes(gc) == total);
    assert(netwasm_gc_collection_count(gc) == collections);
    assert(allocations == backing_allocations);
}

int main(void)
{
    assert(test_try_allocate_available(NULL, 1, 32, NULL) == NULL);
    NetWasmGc *gc = NULL;
    assert(netwasm_gc_create(allocate_memory, release_memory, 1024 * 1024, &gc) == NETWASM_GC_OK);
    netwasm_gc_set_root_tracer(gc, trace_roots, NULL);
    assert_miss(gc, 1, 32, NULL);
    void *first = NULL;
    assert(test_allocate(gc, 1, 32, NULL, &first) == NETWASM_GC_OK);
    root = (uintptr_t)first;
    size_t backing_allocations = allocations;
    size_t hits = 0;
    unsigned char *next;
    while ((next = test_try_allocate_available(gc, 1, 32, NULL)) != NULL) {
        for (size_t i = 0; i < 32; i++) assert(next[i] == 0);
        memset(next, 0xa5, 32);
        hits++;
    }
    assert(hits > 0 && allocations == backing_allocations);
    assert(netwasm_gc_object_count(gc) == hits + 1);
    /* Live bytes count whole slots, which always extend past the object. */
    size_t slot = (32 + _Alignof(max_align_t)) / _Alignof(max_align_t) * _Alignof(max_align_t);
    assert(netwasm_gc_live_bytes(gc) == (hits + 1) * slot);
    assert(netwasm_gc_total_allocated_bytes(gc) == (hits + 1) * 32);
    assert(netwasm_gc_collection_count(gc) == 0);
    assert_miss(gc, 1, 32, NULL);
    void *refilled = NULL;
    assert(test_allocate(gc, 1, 32, NULL, &refilled) == NETWASM_GC_OK);
    assert(test_try_allocate_available(gc, 1, 32, NULL) != NULL);

    assert_miss(gc, SIZE_MAX, 2, NULL);
    assert_miss(gc, 1, SIZE_MAX, NULL);
    assert_miss(gc, 1, 4096, NULL);
    netwasm_gc_collect(gc);
    assert(netwasm_gc_object_count(gc) == 1);
    assert(netwasm_gc_live_bytes(gc) == slot);
    assert(netwasm_gc_contains(gc, root));
    assert_miss(gc, 1, 32, NULL);
    assert(test_allocate(gc, 1, 32, NULL, &refilled) == NETWASM_GC_OK);
    next = test_try_allocate_available(gc, 1, 32, NULL);
    assert(next != NULL);
    for (size_t i = 0; i < 32; i++) assert(next[i] == 0);
    root = 0;
    netwasm_gc_collect(gc);
    assert(netwasm_gc_object_count(gc) == 0);
    static const size_t reference_offset[] = {0};
    static const NetWasmGcDescriptor reference = {reference_offset, 1};
    void *left = NULL;
    assert(test_allocate(gc, 1, 32, &reference, &left) == NETWASM_GC_OK);
    void *right = test_try_allocate_available(gc, 1, 32, &reference);
    assert(right != NULL);
    *(uintptr_t *)left = (uintptr_t)right;
    root = (uintptr_t)left;
    netwasm_gc_collect(gc);
    assert(netwasm_gc_object_count(gc) == 2);
    assert(netwasm_gc_contains(gc, (uintptr_t)right));
    root = 0;
    netwasm_gc_collect(gc);
    assert(netwasm_gc_object_count(gc) == 0);
    netwasm_gc_destroy(gc);
    assert(allocations == releases);
    return 0;
}
