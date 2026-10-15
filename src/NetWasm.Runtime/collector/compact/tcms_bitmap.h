/* SPDX-License-Identifier: Apache-2.0. NetWasm bitmap tracing extension.
 * Ownership, mark work and pins are independent.
 * The worklist uses existing block/large metadata and never allocates at GC. */
#ifndef NETWASM_TCMS_BITMAP_H
#define NETWASM_TCMS_BITMAP_H

union TcmsLarge {
    struct {
        TcmsLarge *next, *pending_next;
        bool marked;
    } data;
    max_align_t alignment;
};

static TcmsObject *large_object(TcmsLarge *large)
{
    return (TcmsObject *)(large + 1);
}

static TcmsLarge *large_prefix(TcmsObject *object)
{
    return (TcmsLarge *)object - 1;
}

static size_t bitmap_object_bytes(const TcmsObject *object)
{
    return sizeof(TcmsObject) + (object->data.payload_bytes == 0 ? 1 : object->data.payload_bytes);
}

static size_t bitmap_slot(const TcmsBlock *block, const TcmsObject *object)
{
    return ((const unsigned char *)object - block->slots) / block->slot_bytes;
}

static size_t bitmap_words(const TcmsBlock *block)
{
    return (block->slot_count + TCMS_WORD_BITS - 1) / TCMS_WORD_BITS;
}

static size_t bitmap_population(uintptr_t word)
{
    return sizeof(uintptr_t) == 4 ? (size_t)__builtin_popcount((unsigned)word)
        : (size_t)__builtin_popcountll((unsigned long long)word);
}

static bool bitmap_object_marked(TcmsObject *object, const TcmsBlock *block)
{
    if (object == NULL) return false;
    if (block == NULL) return large_prefix(object)->data.marked;
    size_t slot = bitmap_slot(block, object);
    return (block->marked[slot / TCMS_WORD_BITS] & ((uintptr_t)1 << (slot % TCMS_WORD_BITS))) != 0;
}

static bool mark_bitmap_object(Tcms *gc, TcmsObject *object, TcmsBlock *block)
{
    if (object == NULL) return false;
    bool references = object->data.layout.reference_count != 0 && object->data.layout.elements != 0;
    size_t bytes = bitmap_object_bytes(object);
    if (block != NULL) {
        size_t slot = bitmap_slot(block, object);
        size_t word = slot / TCMS_WORD_BITS;
        uintptr_t bit = (uintptr_t)1 << (slot % TCMS_WORD_BITS);
        if ((block->marked[word] & bit) != 0) return false;
        block->marked[word] |= bit;
        if (references) {
            block->pending[word] |= bit;
            if (!block->queued) {
                block->queued = true;
                block->pending_first = word;
                block->pending_next = gc->pending_blocks;
                gc->pending_blocks = block;
            } else if (word < block->pending_first) block->pending_first = word;
        }
    } else {
        TcmsLarge *large = large_prefix(object);
        if (large->data.marked) return false;
        large->data.marked = true;
        bytes += sizeof(TcmsLarge);
        if (references) {
            large->data.pending_next = gc->pending_large;
            gc->pending_large = large;
        }
    }
    gc->marked_objects++;
    gc->marked_bytes += bytes;
    TCMS_COUNT(gc, marked_objects, 1);
    return true;
}

static void scan_bitmap_object(Tcms *gc, TcmsObject *object)
{
    TcmsLayout layout = object->data.layout;
    for (size_t index = 0; index < layout.reference_count; index++) {
        for (size_t element = 0; element < layout.elements; element++) {
            TCMS_COUNT(gc, reference_slots, 1);
            uintptr_t reference;
            memcpy(&reference, (unsigned char *)payload(object) +
                element * layout.stride + layout.offsets[index], sizeof(reference));
#ifdef TCMS_EXACT_REFERENCE_MARK
            TcmsBlock *owner;
            TcmsObject *target = resolve_reference_with_block(gc, reference, &owner);
            mark_bitmap_object(gc, target, owner);
#else
            tcms_mark(gc, reference);
#endif
        }
    }
}

static void drain_bitmap_mark(Tcms *gc)
{
    while (gc->pending_blocks != NULL || gc->pending_large != NULL) {
        TcmsBlock *block = gc->pending_blocks;
        if (block != NULL) {
            TCMS_COUNT(gc, drain_blocks, 1);
            gc->pending_blocks = block->pending_next;
            /* Remain queued while scanning: same-block references can rewind
             * pending_first without pushing this block onto its own stack. */
            size_t words = bitmap_words(block);
            while (block->pending_first < words) {
                TCMS_COUNT(gc, drain_bitmap_words, 1);
                size_t word_index = block->pending_first;
                uintptr_t word = block->pending[word_index];
                if (word == 0) { block->pending_first++; continue; }
                size_t bit = first_set_bit(word);
                block->pending[word_index] = word & (word - 1);
                size_t slot = word_index * TCMS_WORD_BITS + bit;
                scan_bitmap_object(gc, (TcmsObject *)(block->slots + slot * block->slot_bytes));
            }
            block->queued = false;
            block->pending_next = NULL;
        } else {
            TcmsLarge *large = gc->pending_large;
            TCMS_COUNT(gc, large_records, 1);
            gc->pending_large = large->data.pending_next;
            large->data.pending_next = NULL;
            scan_bitmap_object(gc, large_object(large));
        }
    }
}

static void set_bitmap_pin(TcmsObject *object, TcmsBlock *block, bool pinned)
{
    if (block == NULL) return;
    size_t slot = bitmap_slot(block, object);
    uintptr_t bit = (uintptr_t)1 << (slot % TCMS_WORD_BITS);
    if (pinned) block->pinned[slot / TCMS_WORD_BITS] |= bit;
    else block->pinned[slot / TCMS_WORD_BITS] &= ~bit;
}

static void mark_bitmap_pins(Tcms *gc)
{
    for (TcmsBlock *block = gc->blocks; block != NULL; block = block->next) {
        TCMS_COUNT(gc, pin_blocks, 1);
        size_t words = bitmap_words(block);
        for (size_t i = 0; i < words; i++) {
            TCMS_COUNT(gc, pin_bitmap_words, 1);
            uintptr_t pins = block->pinned[i];
            while (pins != 0) {
                size_t slot = i * TCMS_WORD_BITS + first_set_bit(pins);
                pins &= pins - 1;
                mark_bitmap_object(gc, (TcmsObject *)(block->slots + slot * block->slot_bytes), block);
            }
        }
    }
    for (TcmsLarge *large = gc->large_head; large != NULL; large = large->data.next) {
        TCMS_COUNT(gc, large_records, 1);
        TcmsObject *object = large_object(large);
        if (object->data.pins != 0) mark_bitmap_object(gc, object, NULL);
    }
}

static void release_bitmap_large(Tcms *gc, TcmsLarge *large)
{
    TcmsObject *object = large_object(large);
    size_t bytes = bitmap_object_bytes(object);
    unregister_pages(gc, object, bytes);
    gc->release(large, bytes + sizeof(TcmsLarge), _Alignof(TcmsLarge));
}

static void sweep_bitmap_objects(Tcms *gc)
{
    assert(gc->pending_blocks == NULL && gc->pending_large == NULL);
    for (TcmsBlock *block = gc->blocks; block != NULL; block = block->next) {
        TCMS_COUNT(gc, sweep_blocks, 1);
        size_t words = bitmap_words(block);
        size_t live = 0;
        for (size_t i = 0; i < words; i++) {
            TCMS_COUNT(gc, sweep_bitmap_words, 1);
            uintptr_t marks = block->marked[i];
            assert((marks & ~block->allocated[i]) == 0);
            assert((block->pinned[i] & ~marks) == 0 && block->pending[i] == 0);
            block->allocated[i] = marks;
            block->marked[i] = 0;
            live += bitmap_population(marks);
        }
        block->live = live;
        TCMS_COUNT(gc, occupied_slot_bytes, live * block->slot_bytes);
    }
    TcmsLarge **link = &gc->large_head;
    while (*link != NULL) {
        TCMS_COUNT(gc, large_records, 1);
        TcmsLarge *large = *link;
        if (large->data.marked) {
            large->data.marked = false;
            link = &large->data.next;
        } else {
            *link = large->data.next;
            release_bitmap_large(gc, large);
        }
    }
    TCMS_COUNT(gc, freed_objects, gc->objects - gc->marked_objects);
    gc->objects = gc->marked_objects;
    gc->bytes = gc->marked_bytes;
    gc->marked_objects = gc->marked_bytes = 0;
}

static void dispose_bitmap_objects(Tcms *gc)
{
    TcmsLarge *large = gc->large_head;
    while (large != NULL) {
        TcmsLarge *next = large->data.next;
        release_bitmap_large(gc, large);
        large = next;
    }
    gc->large_head = gc->pending_large = NULL;
    gc->pending_blocks = NULL;
    gc->bytes = gc->objects = gc->marked_bytes = gc->marked_objects = 0;
}

#endif
