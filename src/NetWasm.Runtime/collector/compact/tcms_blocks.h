/* Homogeneous small-object allocation candidate. The page index identifies
 * block bases; backing allocations need only ordinary object alignment. */
#ifndef NETWASM_TCMS_BLOCKS_H
#define NETWASM_TCMS_BLOCKS_H

#define TCMS_BLOCK_BYTES (64u * 1024u)
#define TCMS_BLOCK_ALIGNMENT _Alignof(TcmsObject)
#define TCMS_BLOCK_CLASSES (TCMS_INTERIOR_SCAN_BYTES / TCMS_BLOCK_ALIGNMENT)
#define TCMS_WORD_BITS (sizeof(uintptr_t) * 8)
#define TCMS_MAX_BLOCK_SLOTS (TCMS_BLOCK_BYTES / (sizeof(TcmsObject) + 1))
#define TCMS_BLOCK_WORDS ((TCMS_MAX_BLOCK_SLOTS + TCMS_WORD_BITS - 1) / TCMS_WORD_BITS)

struct TcmsBlock {
    TcmsBlock *next, *available_next;
    size_t slot_bytes, slot_count, live, search;
#ifdef TCMS_ADAPTIVE_BLOCKS
    size_t backing_bytes;
#endif
    unsigned char *cursor, *end;
    uintptr_t allocated[TCMS_BLOCK_WORDS];
#ifdef TCMS_BITMAP_MARKING
    uintptr_t marked[TCMS_BLOCK_WORDS], pending[TCMS_BLOCK_WORDS], pinned[TCMS_BLOCK_WORDS];
    TcmsBlock *pending_next;
    size_t pending_first;
    bool queued;
#endif
    _Alignas(max_align_t) unsigned char slots[];
};

struct TcmsBlockClass {
    TcmsBlock *active, *available;
    bool used;
#ifdef TCMS_ADAPTIVE_BLOCKS
    size_t next_slots;
#endif
};

static size_t block_slot_bytes(size_t bytes)
{
    size_t alignment = TCMS_BLOCK_ALIGNMENT;
#ifdef TCMS_ADAPTIVE_BLOCKS
    /* At most eight bins per doubling above the alignment-sized bins. The
     * caller has already restricted requests to the small-object cutoff. */
    if (bytes > TCMS_BLOCK_ALIGNMENT * 16) {
        unsigned exponent = 31u - (unsigned)__builtin_clz((unsigned)(bytes - 1));
        alignment = (size_t)1 << (exponent - 3);
    }
#endif
    return (bytes + alignment - 1) & ~(alignment - 1);
}

static size_t block_backing_bytes(const TcmsBlock *block)
{
#ifdef TCMS_ADAPTIVE_BLOCKS
    return block->backing_bytes;
#else
    (void)block;
    return TCMS_BLOCK_BYTES;
#endif
}

#ifdef TCMS_ADAPTIVE_BLOCKS
static size_t initial_block_slots(size_t slot_bytes)
{
    /* Strictly longer than a page preserves the ownership index's maximum
     * of two disjoint block/large extents intersecting any one page. */
    size_t page_bytes = (size_t)1 << TCMS_PAGE_SHIFT;
    return sizeof(TcmsBlock) > page_bytes ? 1 :
        (page_bytes - sizeof(TcmsBlock)) / slot_bytes + 1;
}

static size_t next_block_slots(size_t slot_bytes, size_t slots)
{
    size_t maximum = (TCMS_BLOCK_BYTES - sizeof(TcmsBlock)) / slot_bytes;
    size_t increase = slots / 2;
    if (increase == 0) increase = 1;
    return slots >= maximum - increase ? maximum : slots + increase;
}
#endif

static bool block_bit(const TcmsBlock *block, size_t slot)
{
    return (block->allocated[slot / TCMS_WORD_BITS] &
        ((uintptr_t)1 << (slot % TCMS_WORD_BITS))) != 0;
}

static void block_set_bit(TcmsBlock *block, size_t slot, bool set)
{
    uintptr_t mask = (uintptr_t)1 << (slot % TCMS_WORD_BITS);
    if (set) block->allocated[slot / TCMS_WORD_BITS] |= mask;
    else block->allocated[slot / TCMS_WORD_BITS] &= ~mask;
}

static TcmsObject *resolve_block(TcmsBlock *block, uintptr_t address, bool endpoint)
{
    uintptr_t first = (uintptr_t)block->slots + sizeof(TcmsObject);
    if (block->slot_bytes == 0 || address < first) return NULL;
    size_t slot = (size_t)((address - first) / block->slot_bytes);
    if (slot >= block->slot_count || !block_bit(block, slot)) return NULL;
    unsigned char *header = block->slots + slot * block->slot_bytes;
    /* Allocated bits also reserve runs. Their unused tails have no objects. */
    if (block->cursor != NULL && header >= block->cursor && header < block->end) return NULL;
    TcmsObject *object = (TcmsObject *)header;
    return contains_address(object, address, endpoint) ? object : NULL;
}

#ifndef TCMS_BITMAP_MARKING
static TcmsBlock *block_for_object(const Tcms *gc, const TcmsObject *object)
{
    uintptr_t address = (uintptr_t)object;
    TcmsPage *page = find_page(gc, (address >> TCMS_PAGE_SHIFT) + 1);
    if (page == NULL) return NULL;
    for (size_t i = 0; i < 2; i++) {
        if ((page->block_flags & (1u << i)) == 0) continue;
        TcmsBlock *block = (TcmsBlock *)page->large[i];
        if (address >= (uintptr_t)block->slots &&
            address < (uintptr_t)block->slots + block->slot_count * block->slot_bytes)
            return block;
    }
    return NULL;
}
#endif

static bool block_has_run(const Tcms *gc, size_t bytes)
{
    if (gc->block_classes == NULL) return false;
    TcmsBlock *block = gc->block_classes[block_slot_bytes(bytes) / TCMS_BLOCK_ALIGNMENT - 1].active;
    return block != NULL && block->cursor != block->end;
}

static bool block_collection_due(const Tcms *gc)
{
    return gc->bytes >= gc->threshold || gc->reserved_bytes >= gc->threshold - gc->bytes;
}

static size_t first_set_bit(uintptr_t word)
{
    return sizeof(uintptr_t) == 4 ? (size_t)__builtin_ctz((unsigned)word)
        : (size_t)__builtin_ctzll((unsigned long long)word);
}

static size_t block_find_free(TcmsBlock *block)
{
    size_t slot = block->search;
    while (slot < block->slot_count) {
        size_t word_index = slot / TCMS_WORD_BITS;
        uintptr_t available = ~block->allocated[word_index] &
            (UINTPTR_MAX << (slot % TCMS_WORD_BITS));
        if (available != 0) {
            size_t found = word_index * TCMS_WORD_BITS + first_set_bit(available);
            return found < block->slot_count ? found : block->slot_count;
        }
        slot = (word_index + 1) * TCMS_WORD_BITS;
    }
    return block->slot_count;
}

static TcmsResult new_block(Tcms *gc, size_t slot_bytes, TcmsBlock **result)
{
    TcmsBlock *block = gc->empty_blocks;
    size_t bytes = TCMS_BLOCK_BYTES;
#ifdef TCMS_ADAPTIVE_BLOCKS
    TcmsBlockClass *size_class = &gc->block_classes[slot_bytes / TCMS_BLOCK_ALIGNMENT - 1];
    if (block != NULL && block_backing_bytes(block) - sizeof(*block) < slot_bytes)
        block = NULL;
#endif
    TcmsBlock *next;
    if (block != NULL) {
        bytes = block_backing_bytes(block);
        gc->empty_blocks = block->available_next;
        gc->empty_block_count--;
        next = block->next;
    } else {
#ifdef TCMS_ADAPTIVE_BLOCKS
        size_t slots = size_class->next_slots == 0 ? initial_block_slots(slot_bytes) : size_class->next_slots;
        bytes = sizeof(TcmsBlock) + slots * slot_bytes;
#endif
        if (gc->block_bytes > SIZE_MAX - bytes) return TCMS_SIZE_OVERFLOW;
        block = gc->allocate(bytes, _Alignof(TcmsBlock));
        if (block == NULL) return TCMS_OUT_OF_MEMORY;
        TcmsResult status = register_page_range(gc, (TcmsObject *)block, bytes, true);
        if (status != TCMS_OK) {
            gc->release(block, bytes, _Alignof(TcmsBlock));
            return status;
        }
        next = gc->blocks;
        gc->blocks = block;
        gc->block_bytes += bytes;
        TCMS_MAX(gc, peak_block_bytes, gc->block_bytes);
    }
    memset(block, 0, sizeof(*block));
    block->next = next;
    block->slot_bytes = slot_bytes;
    block->slot_count = (bytes - sizeof(*block)) / slot_bytes;
#ifdef TCMS_ADAPTIVE_BLOCKS
    block->backing_bytes = bytes;
    size_class->next_slots = next_block_slots(slot_bytes, block->slot_count);
#endif
    *result = block;
    return TCMS_OK;
}

static TcmsResult block_allocate(Tcms *gc, size_t bytes, TcmsObject **result)
{
    size_t slot_bytes = block_slot_bytes(bytes);
    if (gc->block_classes == NULL) {
        size_t class_bytes = TCMS_BLOCK_CLASSES * sizeof(TcmsBlockClass);
        gc->block_classes = gc->allocate(class_bytes, _Alignof(TcmsBlockClass));
        if (gc->block_classes == NULL) return TCMS_OUT_OF_MEMORY;
        memset(gc->block_classes, 0, class_bytes);
    }
    TcmsBlockClass *size_class = &gc->block_classes[slot_bytes / TCMS_BLOCK_ALIGNMENT - 1];
    TcmsBlock *block = size_class->active;
    if (block == NULL || block->cursor == block->end) {
        size_t first = 0;
        block = size_class->available;
        while (block != NULL) {
            first = block_find_free(block);
            if (first != block->slot_count) break;
            size_class->available = block = block->available_next;
        }
        if (block == NULL) {
            TcmsResult status = new_block(gc, slot_bytes, &block);
            if (status != TCMS_OK) return status;
            size_class->available = block;
            first = 0;
        }
        size_t classes = gc->active_classes + (size_class->used ? 0 : 1);
        size_t budget = gc->minimum_threshold / (2 * classes);
        if (budget > 4096) budget = 4096;
        size_t slots = budget / slot_bytes;
        if (slots == 0) slots = 1;
        size_t end = first;
        while (end < block->slot_count && end - first < slots && !block_bit(block, end)) end++;
        size_t run_bytes = (end - first) * slot_bytes;
        if (gc->reserved_bytes > SIZE_MAX - run_bytes) return TCMS_SIZE_OVERFLOW;
        block->cursor = block->slots + first * slot_bytes;
        block->end = block->slots + end * slot_bytes;
        memset(block->cursor, 0, run_bytes);
        TCMS_COUNT(gc, run_zeroed_bytes, run_bytes);
        for (size_t slot = first; slot < end; slot++) block_set_bit(block, slot, true);
        block->search = end;
        gc->reserved_bytes += run_bytes;
        size_class->active = block;
        if (!size_class->used) { size_class->used = true; gc->active_classes++; }
    }
    *result = (TcmsObject *)block->cursor;
    block->cursor += slot_bytes;
    block->live++;
    gc->reserved_bytes -= slot_bytes;
    return TCMS_OK;
}

static void retire_block_runs(Tcms *gc)
{
    if (gc->block_classes == NULL) return;
    for (size_t i = 0; i < TCMS_BLOCK_CLASSES; i++) {
        TcmsBlock *block = gc->block_classes[i].active;
        if (block == NULL) continue;
        if (block->cursor != NULL) {
            size_t first = (size_t)(block->cursor - block->slots) / block->slot_bytes;
            size_t end = (size_t)(block->end - block->slots) / block->slot_bytes;
            for (size_t slot = first; slot < end; slot++) block_set_bit(block, slot, false);
            gc->reserved_bytes -= (end - first) * block->slot_bytes;
            block->cursor = block->end = NULL;
        }
        gc->block_classes[i].active = NULL;
    }
    assert(gc->reserved_bytes == 0);
}

#ifndef TCMS_BITMAP_MARKING
static void release_block_object(Tcms *gc, TcmsObject *object)
{
    TcmsBlock *block = block_for_object(gc, object);
    assert(block != NULL && block->live != 0);
    size_t slot = ((unsigned char *)object - block->slots) / block->slot_bytes;
    assert(block_bit(block, slot));
    block_set_bit(block, slot, false);
    block->live--;
}
#endif

static void finish_block_sweep(Tcms *gc)
{
    if (gc->block_classes == NULL) return;
    for (size_t i = 0; i < TCMS_BLOCK_CLASSES; i++) {
        gc->block_classes[i].available = NULL;
#ifdef TCMS_ADAPTIVE_BLOCKS
        gc->block_classes[i].next_slots = 0;
#endif
    }
    gc->empty_blocks = NULL;
    gc->empty_block_count = 0;
    TcmsBlock **link = &gc->blocks;
    while (*link != NULL) {
        TcmsBlock *block = *link;
        TCMS_COUNT(gc, allocation_blocks, 1);
        block->available_next = NULL;
        block->search = 0;
        if (block->live == 0) {
            if (gc->empty_block_count != 0) {
                size_t bytes = block_backing_bytes(block);
                *link = block->next;
                unregister_pages(gc, (TcmsObject *)block, bytes);
                gc->release(block, bytes, _Alignof(TcmsBlock));
                gc->block_bytes -= bytes;
                continue;
            }
            block->slot_bytes = block->slot_count = 0;
            block->available_next = gc->empty_blocks;
            gc->empty_blocks = block;
            gc->empty_block_count++;
        } else {
            TcmsBlockClass *size_class = &gc->block_classes[block->slot_bytes / TCMS_BLOCK_ALIGNMENT - 1];
#ifdef TCMS_ADAPTIVE_BLOCKS
            size_t slots = next_block_slots(block->slot_bytes, block->slot_count);
            if (slots > size_class->next_slots) size_class->next_slots = slots;
#endif
            if (block->live < block->slot_count) {
                block->available_next = size_class->available;
                size_class->available = block;
            }
        }
        link = &block->next;
    }
}

static void dispose_blocks(Tcms *gc)
{
    TcmsBlock *block = gc->blocks;
    while (block != NULL) {
        TcmsBlock *next = block->next;
        size_t bytes = block_backing_bytes(block);
        unregister_pages(gc, (TcmsObject *)block, bytes);
        gc->release(block, bytes, _Alignof(TcmsBlock));
        block = next;
    }
    if (gc->block_classes != NULL)
        gc->release(gc->block_classes, TCMS_BLOCK_CLASSES * sizeof(TcmsBlockClass), _Alignof(TcmsBlockClass));
    gc->blocks = gc->empty_blocks = NULL;
    gc->block_classes = NULL;
    gc->active_classes = gc->reserved_bytes = gc->block_bytes = gc->empty_block_count = 0;
}

#endif
