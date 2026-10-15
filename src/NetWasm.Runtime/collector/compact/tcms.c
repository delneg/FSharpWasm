/* Copyright (c) The AssemblyScript Authors.
 * SPDX-License-Identifier: Apache-2.0
 * Modified by Zion Sati: C port of std/assembly/rt/tcms.ts at
 * b6bda05c29c9eb7d07d7f6cb2703508483b30493. See NOTICE for adaptations. */
#include "tcms.h"
#include <string.h>

union TcmsObject {
    struct {
#ifndef TCMS_BITMAP_MARKING
        TcmsObject *next, *previous;
#endif
        size_t payload_bytes;
#ifndef TCMS_BITMAP_MARKING
        size_t allocation_bytes;
#endif
        TcmsLayout layout;
        size_t pins;
#ifndef TCMS_BITMAP_MARKING
        bool colour;
#endif
    } data;
    max_align_t alignment;
};

#define TCMS_INTERIOR_SCAN_BYTES (4u * 1024u)

#ifdef TCMS_PAGE_OWNERSHIP
#include "tcms_ownership.h"
#endif

static void *payload(TcmsObject *object) { return object + 1; }

#ifndef TCMS_BITMAP_MARKING
static void append(TcmsList *list, TcmsObject *object)
{
    object->data.next = NULL;
    object->data.previous = list->last;
    if (list->last != NULL) list->last->data.next = object;
    else list->first = object;
    list->last = object;
}

static void unlink(TcmsList *list, TcmsObject *object)
{
    TcmsObject *next = object->data.next, *previous = object->data.previous;
    if (previous != NULL) previous->data.next = next;
    else list->first = next;
    if (next != NULL) next->data.previous = previous;
    else list->last = previous;
}
#endif

static bool contains_address(
    const TcmsObject *object, uintptr_t address, bool include_endpoint)
{
    uintptr_t start = (uintptr_t)payload((TcmsObject *)object);
    if (address < start) return false;
    uintptr_t distance = address - start;
    return distance == 0 || distance < object->data.payload_bytes ||
        (include_endpoint && distance == object->data.payload_bytes);
}

#ifdef TCMS_BLOCK_ALLOCATION
#include "tcms_blocks.h"
#else
static size_t object_hash(const TcmsObject *object)
{
    uintptr_t value = (uintptr_t)object / _Alignof(TcmsObject);
    value ^= value >> (sizeof(value) * 4);
    value *= (uintptr_t)0x7feb352du;
    value ^= value >> 15;
    return (size_t)value;
}

static TcmsObject *lookup_object(const Tcms *gc, uintptr_t address)
{
    if (gc->object_table_capacity == 0) return NULL;
    TcmsObject *candidate = (TcmsObject *)address;
    size_t mask = gc->object_table_capacity - 1;
    size_t index = object_hash(candidate) & mask;
    for (;;) {
        TCMS_COUNT(gc, hash_probes, 1);
        TcmsObject *object = gc->object_table[index];
        if (object == NULL || object == candidate) return object;
        index = (index + 1) & mask;
    }
}

static void insert_object(Tcms *gc, TcmsObject *object)
{
    size_t mask = gc->object_table_capacity - 1;
    size_t index = object_hash(object) & mask;
    while (gc->object_table[index] != NULL) {
        index = (index + 1) & mask;
    }
    gc->object_table[index] = object;
    TCMS_COUNT(gc, table_insertions, 1);
}

static TcmsResult grow_object_table(Tcms *gc)
{
    size_t required = gc->objects + 1;
    if (required != 0 && gc->object_table_capacity != 0 &&
        required <= gc->object_table_capacity - gc->object_table_capacity / 4)
        return TCMS_OK;
    if (gc->object_table_capacity > SIZE_MAX / (2 * sizeof(TcmsObject *)))
        return TCMS_SIZE_OVERFLOW;
    size_t capacity = gc->object_table_capacity == 0
        ? 8 : gc->object_table_capacity * 2;
    TcmsObject **table = gc->allocate(
        capacity * sizeof(*table), _Alignof(TcmsObject *));
    if (table == NULL) return TCMS_OUT_OF_MEMORY;
    memset(table, 0, capacity * sizeof(*table));
    TCMS_COUNT(gc, table_zeroed_bytes, capacity * sizeof(*table));
    TcmsObject **previous = gc->object_table;
    size_t previous_capacity = gc->object_table_capacity;
    gc->object_table = table;
    gc->object_table_capacity = capacity;
    for (size_t index = 0; index < previous_capacity; index++)
        if (previous[index] != NULL) insert_object(gc, previous[index]);
    if (previous != NULL)
        gc->release(previous, previous_capacity * sizeof(*previous),
            _Alignof(TcmsObject *));
    return TCMS_OK;
}
#endif

#ifndef TCMS_PAGE_OWNERSHIP
static TcmsResult grow_large_objects(Tcms *gc)
{
    if (gc->large_object_count < gc->large_object_capacity) return TCMS_OK;
    if (gc->large_object_capacity > SIZE_MAX / (2 * sizeof(TcmsObject *)))
        return TCMS_SIZE_OVERFLOW;
    size_t capacity = gc->large_object_capacity == 0
        ? 4 : gc->large_object_capacity * 2;
    TcmsObject **objects = gc->allocate(
        capacity * sizeof(*objects), _Alignof(TcmsObject *));
    if (objects == NULL) return TCMS_OUT_OF_MEMORY;
    if (gc->large_objects != NULL) {
        memcpy(objects, gc->large_objects,
            gc->large_object_count * sizeof(*objects));
        gc->release(gc->large_objects,
            gc->large_object_capacity * sizeof(*objects),
            _Alignof(TcmsObject *));
    }
    gc->large_objects = objects;
    gc->large_object_capacity = capacity;
    return TCMS_OK;
}
#endif

#ifdef TCMS_BITMAP_MARKING
/* The owner is consumed immediately. Inlining lets LLVM eliminate its
 * out-parameter stack slot in marking and weak-reference queries. */
static inline __attribute__((always_inline)) TcmsObject *resolve_with_block(const Tcms *gc, uintptr_t address,
    bool include_endpoint, TcmsBlock **owner)
#else
static TcmsObject *resolve(const Tcms *gc, uintptr_t address, bool include_endpoint)
#endif
{
#ifdef TCMS_BITMAP_MARKING
    *owner = NULL;
#endif
    TCMS_COUNT(gc, resolve_calls, 1);
    if (address < sizeof(TcmsObject)) return NULL;
#ifdef TCMS_BLOCK_ALLOCATION
    uintptr_t page_key = (address >> TCMS_PAGE_SHIFT) + 1;
#ifdef TCMS_PAGE_CACHE
    TcmsPage *page = gc->collecting
        ? find_page_cached((Tcms *)gc, page_key)
        : find_page(gc, page_key);
#else
    TcmsPage *page = find_page(gc, page_key);
#endif
    if (page == NULL) return NULL;
    for (size_t i = 0; i < 2; i++) {
        TcmsObject *object = page->large[i];
        if (object == NULL) continue;
        if ((page->block_flags & (1u << i)) != 0) {
            TcmsBlock *block = (TcmsBlock *)object;
            object = resolve_block(block, address, include_endpoint);
            if (object != NULL) {
#ifdef TCMS_BITMAP_MARKING
                *owner = block;
#endif
                return object;
            }
        } else {
            TCMS_COUNT(gc, large_candidates, 1);
            if (contains_address(object, address, include_endpoint)) return object;
        }
    }
#else
    uintptr_t candidate = (address - sizeof(TcmsObject)) &
        ~((uintptr_t)_Alignof(TcmsObject) - 1);
#ifdef TCMS_PAGE_OWNERSHIP
    TcmsObject *exact = lookup_object(gc, candidate);
    if (exact != NULL && contains_address(exact, address, include_endpoint)) return exact;
    TcmsPage *page = find_page(gc, (address >> TCMS_PAGE_SHIFT) + 1);
    if (page == NULL) return NULL;
    for (size_t i = 0; i < 2; i++) {
        TcmsObject *object = page->large[i];
        if (object == NULL) continue;
        TCMS_COUNT(gc, large_candidates, 1);
        if (contains_address(object, address, include_endpoint)) return object;
    }
    if (page->small_count == 0) return NULL;
#endif
    uintptr_t limit = candidate > TCMS_INTERIOR_SCAN_BYTES
        ? candidate - TCMS_INTERIOR_SCAN_BYTES : 0;
    for (;;) {
        TcmsObject *object = lookup_object(gc, candidate);
        if (object != NULL && contains_address(object, address, include_endpoint))
            return object;
        if (candidate <= limit || candidate < _Alignof(TcmsObject)) break;
        candidate -= _Alignof(TcmsObject);
    }
#ifndef TCMS_PAGE_OWNERSHIP
    for (size_t index = 0; index < gc->large_object_count; index++) {
        TCMS_COUNT(gc, large_candidates, 1);
        TcmsObject *object = gc->large_objects[index];
        if (contains_address(object, address, include_endpoint)) return object;
    }
#endif
#endif
    return NULL;
}

#ifdef TCMS_BITMAP_MARKING
static TcmsObject *resolve(const Tcms *gc, uintptr_t address, bool include_endpoint)
{
    TcmsBlock *owner;
    return resolve_with_block(gc, address, include_endpoint, &owner);
}

#ifdef TCMS_EXACT_REFERENCE_MARK
/* Managed reference fields normally contain exact payload addresses. Resolve
 * that common case without payload-bound calculations, then retain the general
 * interior/endpoint path for supported unsafe and packed-reference values. */
static inline __attribute__((always_inline)) TcmsObject *resolve_reference_with_block(
    Tcms *gc, uintptr_t address, TcmsBlock **owner)
{
    *owner = NULL;
    TCMS_COUNT(gc, resolve_calls, 1);
    if (address < sizeof(TcmsObject)) return NULL;
    uintptr_t page_key = (address >> TCMS_PAGE_SHIFT) + 1;
#ifdef TCMS_PAGE_CACHE
    TcmsPage *page = find_page_cached(gc, page_key);
#else
    TcmsPage *page = find_page(gc, page_key);
#endif
    if (page == NULL) return NULL;
    uintptr_t header = address - sizeof(TcmsObject);
    for (size_t i = 0; i < 2; i++) {
        TcmsObject *object = page->large[i];
        if (object == NULL) continue;
        if ((page->block_flags & (1u << i)) != 0) {
            TcmsBlock *block = (TcmsBlock *)object;
            uintptr_t start = (uintptr_t)block->slots;
            if (header < start) continue;
            size_t distance = (size_t)(header - start);
            size_t slot = distance / block->slot_bytes;
            if (slot >= block->slot_count || distance != slot * block->slot_bytes ||
                !block_bit(block, slot)) continue;
            unsigned char *candidate = block->slots + slot * block->slot_bytes;
            if (block->cursor != NULL && candidate >= block->cursor && candidate < block->end)
                continue;
            *owner = block;
            return (TcmsObject *)candidate;
        }
        TCMS_COUNT(gc, large_candidates, 1);
        if ((uintptr_t)payload(object) == address) return object;
    }
    /* Interior references in managed slots remain a supported precise-root
     * shape. Reuse this page and fall back only when the exact check misses. */
    for (size_t i = 0; i < 2; i++) {
        TcmsObject *object = page->large[i];
        if (object == NULL) continue;
        if ((page->block_flags & (1u << i)) != 0) {
            TcmsBlock *block = (TcmsBlock *)object;
            object = resolve_block(block, address, true);
            if (object != NULL) {
                *owner = block;
                return object;
            }
        } else {
            TCMS_COUNT(gc, large_candidates, 1);
            if (contains_address(object, address, true)) return object;
        }
    }
    return NULL;
}
#endif

#include "tcms_bitmap.h"
#else
static bool mark_object(Tcms *gc, TcmsObject *object)
{
    if (object != NULL && object->data.colour == gc->white) {
        TCMS_COUNT(gc, marked_objects, 1);
        object->data.colour = !gc->white;
        unlink(&gc->from_space, object);
        append(&gc->to_space, object);
        return true;
    }
    return false;
}
#endif

bool tcms_mark(Tcms *gc, uintptr_t address)
{
#ifdef TCMS_BITMAP_MARKING
    TcmsBlock *block;
    TcmsObject *object = resolve_with_block(gc, address, true, &block);
    return mark_bitmap_object(gc, object, block);
#else
    return mark_object(gc, resolve(gc, address, true));
#endif
}

bool tcms_is_marked(const Tcms *gc, uintptr_t address)
{
#ifdef TCMS_BITMAP_MARKING
    TcmsBlock *block;
    TcmsObject *object = resolve_with_block(gc, address, true, &block);
    return bitmap_object_marked(object, block);
#else
    TcmsObject *object = resolve(gc, address, true);
    return object != NULL && object->data.colour != gc->white;
#endif
}

#ifndef TCMS_BITMAP_MARKING
static void release_white(Tcms *gc)
{
    TcmsObject *object = gc->from_space.first;
    while (object != NULL) {
        TcmsObject *next = object->data.next;
        size_t bytes = object->data.allocation_bytes;
        gc->bytes -= bytes;
        gc->objects--;
        TCMS_COUNT(gc, freed_objects, 1);
#ifdef TCMS_BLOCK_ALLOCATION
        if (bytes <= TCMS_INTERIOR_SCAN_BYTES) {
            release_block_object(gc, object);
        } else {
            unregister_pages(gc, object, bytes);
            gc->release(object, bytes, _Alignof(TcmsObject));
        }
#else
#ifdef TCMS_PAGE_OWNERSHIP
        unregister_pages(gc, object, bytes);
#endif
        gc->release(object, bytes, _Alignof(TcmsObject));
#endif
        object = next;
    }
    gc->from_space = (TcmsList){0};
}
#endif

TcmsResult tcms_initialize(Tcms *gc, TcmsAllocate allocate, TcmsRelease release, size_t threshold)
{
    if (gc == NULL || allocate == NULL || release == NULL) return TCMS_INVALID_ARGUMENT;
    *gc = (Tcms){.allocate = allocate, .release = release,
        .threshold = threshold, .minimum_threshold = threshold};
    return TCMS_OK;
}

static TcmsResult validate_layout(size_t bytes, TcmsLayout layout)
{
    if (layout.reference_count == 0) return TCMS_OK;
    if (layout.offsets == NULL || layout.stride == 0 ||
        layout.elements > SIZE_MAX / layout.stride ||
        layout.elements * layout.stride > bytes) return TCMS_INVALID_LAYOUT;
    for (size_t index = 0; index < layout.reference_count; index++) {
        size_t offset = layout.offsets[index];
        if (offset > SIZE_MAX - sizeof(uintptr_t) ||
            offset + sizeof(uintptr_t) > layout.stride) return TCMS_INVALID_LAYOUT;
    }
    return TCMS_OK;
}

TcmsResult tcms_allocate(Tcms *gc, size_t bytes, TcmsLayout layout,
    const uintptr_t *roots, size_t root_count, void **result)
{
    if (gc == NULL || result == NULL || (root_count != 0 && roots == NULL))
        return TCMS_INVALID_ARGUMENT;
    TcmsResult status = validate_layout(bytes, layout);
    if (status != TCMS_OK) return status;
    size_t payload_bytes = bytes == 0 ? 1 : bytes;
    if (payload_bytes > SIZE_MAX - sizeof(TcmsObject)) return TCMS_SIZE_OVERFLOW;
    size_t allocation_bytes = sizeof(TcmsObject) + payload_bytes;
    size_t backing_bytes = allocation_bytes;
#ifdef TCMS_BITMAP_MARKING
    if (allocation_bytes > TCMS_INTERIOR_SCAN_BYTES) {
        if (allocation_bytes > SIZE_MAX - sizeof(TcmsLarge)) return TCMS_SIZE_OVERFLOW;
        backing_bytes += sizeof(TcmsLarge);
    }
#endif
#ifdef TCMS_BLOCK_ALLOCATION
    bool small = allocation_bytes <= TCMS_INTERIOR_SCAN_BYTES;
    if ((!small || !block_has_run(gc, allocation_bytes)) && block_collection_due(gc))
        tcms_collect(gc, roots, root_count);
#else
    if (gc->bytes >= gc->threshold) tcms_collect(gc, roots, root_count);
#endif
    if (gc->bytes > SIZE_MAX - backing_bytes || gc->objects == SIZE_MAX)
        return TCMS_SIZE_OVERFLOW;
#ifndef TCMS_BLOCK_ALLOCATION
    status = grow_object_table(gc);
    if (status != TCMS_OK) return status;
#endif
#ifndef TCMS_PAGE_OWNERSHIP
    bool large = allocation_bytes > TCMS_INTERIOR_SCAN_BYTES;
    if (large) {
        status = grow_large_objects(gc);
        if (status != TCMS_OK) return status;
    }
#endif
    TcmsObject *object;
#ifdef TCMS_BLOCK_ALLOCATION
    if (small) {
        status = block_allocate(gc, allocation_bytes, &object);
        if (status != TCMS_OK) return status;
    } else {
#endif
    void *backing = gc->allocate(backing_bytes, _Alignof(TcmsObject));
    if (backing == NULL) return TCMS_OUT_OF_MEMORY;
#ifdef TCMS_BITMAP_MARKING
    object = large_object(backing);
#else
    object = backing;
#endif
#ifdef TCMS_PAGE_OWNERSHIP
    status = register_pages(gc, object, allocation_bytes);
    if (status != TCMS_OK) {
        gc->release(backing, backing_bytes, _Alignof(TcmsObject));
        return status;
    }
#endif
#ifdef TCMS_BITMAP_MARKING
    TcmsLarge *large = backing;
    *large = (TcmsLarge){.data.next = gc->large_head};
    gc->large_head = large;
#endif
#ifdef TCMS_BLOCK_ALLOCATION
    }
#endif
    object->data.payload_bytes = bytes;
#ifndef TCMS_BITMAP_MARKING
    object->data.allocation_bytes = allocation_bytes;
#endif
    object->data.layout = layout;
    object->data.pins = 0;
#ifndef TCMS_BITMAP_MARKING
    object->data.colour = gc->white;
#endif
#ifdef TCMS_BLOCK_ALLOCATION
    if (!small) {
#endif
        memset(payload(object), 0, payload_bytes);
        TCMS_COUNT(gc, payload_zeroed_bytes, payload_bytes);
#ifdef TCMS_BLOCK_ALLOCATION
    }
#endif
#ifndef TCMS_BITMAP_MARKING
    append(&gc->from_space, object);
#endif
#ifndef TCMS_BLOCK_ALLOCATION
    insert_object(gc, object);
#endif
#ifndef TCMS_PAGE_OWNERSHIP
    if (large) gc->large_objects[gc->large_object_count++] = object;
#endif
    gc->bytes += backing_bytes;
    gc->objects++;
    TCMS_COUNT(gc, allocations, 1);
    TCMS_COUNT(gc, payload_bytes, bytes);
    TCMS_COUNT(gc, allocation_bytes, backing_bytes);
    *result = payload(object);
    return TCMS_OK;
}

void tcms_collect(Tcms *gc, const uintptr_t *roots, size_t root_count)
{
    TCMS_COUNT(gc, collections, 1);
#ifdef TCMS_BLOCK_ALLOCATION
    TCMS_MAX(gc, peak_block_bytes, gc->block_bytes);
#endif
    TCMS_TIME_START(gc, roots_started);
    gc->collecting = true;
#ifndef TCMS_BITMAP_MARKING
    gc->scan_cursor = NULL;
#endif
#ifdef TCMS_BLOCK_ALLOCATION
    retire_block_runs(gc);
#endif
    TCMS_COUNT(gc, root_slots, root_count);
    for (size_t index = 0; index < root_count; index++) tcms_mark(gc, roots[index]);
    if (gc->trace_roots != NULL) gc->trace_roots(gc, gc->root_state);
#ifdef TCMS_BITMAP_MARKING
    mark_bitmap_pins(gc);
#else
    TcmsObject *pinned = gc->from_space.first;
    while (pinned != NULL) {
        TcmsObject *next = pinned->data.next;
        if (pinned->data.pins != 0) mark_object(gc, pinned);
        pinned = next;
    }
#endif
    TCMS_TIME_END(gc, roots_nanoseconds, roots_started);
    TCMS_TIME_START(gc, drain_started);
    tcms_drain_mark(gc);
    TCMS_TIME_END(gc, drain_nanoseconds, drain_started);
    TCMS_TIME_START(gc, complete_mark_started);
    if (gc->hooks != NULL) gc->hooks->complete_mark(gc);
    TCMS_TIME_END(gc, complete_mark_nanoseconds, complete_mark_started);
    TCMS_TIME_START(gc, sweep_started);
#ifdef TCMS_BITMAP_MARKING
    sweep_bitmap_objects(gc);
#else
    release_white(gc);
#endif
#ifdef TCMS_BLOCK_ALLOCATION
    finish_block_sweep(gc);
#endif
    TCMS_TIME_END(gc, sweep_nanoseconds, sweep_started);
    TCMS_TIME_START(gc, rebuild_started);
#ifndef TCMS_BLOCK_ALLOCATION
    if (gc->object_table != NULL)
        memset(gc->object_table, 0,
            gc->object_table_capacity * sizeof(*gc->object_table));
    TCMS_COUNT(gc, table_zeroed_bytes,
        gc->object_table_capacity * sizeof(*gc->object_table));
    gc->large_object_count = 0;
    for (TcmsObject *object = gc->to_space.first;
        object != NULL; object = object->data.next) {
        insert_object(gc, object);
#ifndef TCMS_PAGE_OWNERSHIP
        if (object->data.allocation_bytes > TCMS_INTERIOR_SCAN_BYTES)
            gc->large_objects[gc->large_object_count++] = object;
#endif
    }
#endif
#ifndef TCMS_BITMAP_MARKING
    gc->from_space = gc->to_space;
    gc->to_space = (TcmsList){0};
    gc->white = !gc->white;
    gc->scan_cursor = NULL;
#endif
    gc->collecting = false;
    gc->threshold = gc->bytes > (SIZE_MAX - gc->minimum_threshold) / 2
        ? SIZE_MAX : gc->bytes * 2 + gc->minimum_threshold;
    TCMS_TIME_END(gc, rebuild_nanoseconds, rebuild_started);
}

void tcms_drain_mark(Tcms *gc)
{
#ifdef TCMS_BITMAP_MARKING
    drain_bitmap_mark(gc);
#else
    TcmsObject *object = gc->scan_cursor == NULL ? gc->to_space.first : gc->scan_cursor->data.next;
    while (object != NULL) {
        TcmsLayout layout = object->data.layout;
        for (size_t index = 0; index < layout.reference_count; index++) {
            for (size_t element = 0; element < layout.elements; element++) {
                TCMS_COUNT(gc, reference_slots, 1);
                uintptr_t reference;
                /* Packed reference fields need not be pointer-aligned. */
                memcpy(&reference, (unsigned char *)payload(object) +
                    element * layout.stride + layout.offsets[index], sizeof(reference));
                tcms_mark(gc, reference);
            }
        }
        /* Visiting appends new live objects. Read next after tracing members. */
        gc->scan_cursor = object;
        object = object->data.next;
    }
#endif
}

bool tcms_contains(const Tcms *gc, uintptr_t address)
{
    return resolve(gc, address, false) != NULL;
}

uintptr_t tcms_base(const Tcms *gc, uintptr_t address)
{
    TcmsObject *object = resolve(gc, address, false);
    return object == NULL ? 0 : (uintptr_t)payload(object);
}

TcmsResult tcms_pin(Tcms *gc, uintptr_t address)
{
#ifdef TCMS_BITMAP_MARKING
    TcmsBlock *block;
    TcmsObject *object = resolve_with_block(gc, address, true, &block);
#else
    TcmsObject *object = resolve(gc, address, true);
#endif
    if (object == NULL) return TCMS_UNKNOWN_OBJECT;
    if (object->data.pins == SIZE_MAX) return TCMS_PIN_OVERFLOW;
    object->data.pins++;
#ifdef TCMS_BITMAP_MARKING
    set_bitmap_pin(object, block, true);
#endif
    return TCMS_OK;
}

TcmsResult tcms_unpin(Tcms *gc, uintptr_t address)
{
#ifdef TCMS_BITMAP_MARKING
    TcmsBlock *block;
    TcmsObject *object = resolve_with_block(gc, address, true, &block);
#else
    TcmsObject *object = resolve(gc, address, true);
#endif
    if (object == NULL) return TCMS_UNKNOWN_OBJECT;
    if (object->data.pins == 0) return TCMS_NOT_PINNED;
    object->data.pins--;
#ifdef TCMS_BITMAP_MARKING
    if (object->data.pins == 0) set_bitmap_pin(object, block, false);
#endif
    return TCMS_OK;
}

void tcms_destroy(Tcms *gc)
{
    if (gc->hooks != NULL) gc->hooks->dispose(gc);
#ifdef TCMS_BLOCK_ALLOCATION
    retire_block_runs(gc);
#endif
#ifdef TCMS_BITMAP_MARKING
    dispose_bitmap_objects(gc);
#else
    release_white(gc);
#endif
#ifdef TCMS_BLOCK_ALLOCATION
    dispose_blocks(gc);
#endif
    if (gc->object_table != NULL)
        gc->release(gc->object_table,
            gc->object_table_capacity * sizeof(*gc->object_table),
            _Alignof(TcmsObject *));
    if (gc->large_objects != NULL)
        gc->release(gc->large_objects,
            gc->large_object_capacity * sizeof(*gc->large_objects),
            _Alignof(TcmsObject *));
    gc->object_table = NULL;
    gc->large_objects = NULL;
    gc->object_table_capacity = 0;
    gc->large_object_count = 0;
    gc->large_object_capacity = 0;
#ifdef TCMS_PAGE_OWNERSHIP
    dispose_pages(gc);
#endif
}
