/* Internal page ownership candidate. Included after TcmsObject's definition.
 * Metadata only: allocation alignment, payload bounds and managed ABI do not
 * change. Sparse indexing works with native addresses and both Wasm widths. */
#ifndef NETWASM_TCMS_OWNERSHIP_H
#define NETWASM_TCMS_OWNERSHIP_H

#include <assert.h>

#define TCMS_PAGE_SHIFT 12
#define TCMS_PAGE_DELETED UINTPTR_MAX

struct TcmsPage {
    uintptr_t key; /* page number + 1; 0 is empty, UINTPTR_MAX is deleted */
    size_t small_count;
    TcmsObject *large[2];
#ifdef TCMS_BLOCK_ALLOCATION
    unsigned block_flags; /* each bit distinguishes a block from a large object */
#endif
};

static size_t page_hash(uintptr_t key)
{
    key ^= key >> (sizeof(key) * 4);
    key *= (uintptr_t)0x7feb352du;
    key ^= key >> 15;
    return (size_t)key;
}

static TcmsPage *find_page(const Tcms *gc, uintptr_t key)
{
    if (gc->page_capacity == 0) return NULL;
    size_t mask = gc->page_capacity - 1;
    size_t index = page_hash(key) & mask;
    for (;;) {
        TCMS_COUNT(gc, page_probes, 1);
        TcmsPage *entry = &gc->pages[index];
        if (entry->key == key) return entry;
        if (entry->key == 0) return NULL;
        index = (index + 1) & mask;
    }
}

#ifdef TCMS_PAGE_CACHE
static TcmsPage *find_page_cached(Tcms *gc, uintptr_t key)
{
    if (gc->cached_page_key == key && gc->cached_page != NULL) {
        TCMS_COUNT(gc, page_cache_hits, 1);
        return gc->cached_page;
    }
    TcmsPage *page = find_page(gc, key);
    if (page != NULL) {
        gc->cached_page_key = key;
        gc->cached_page = page;
    }
    return page;
}

static void invalidate_page_cache(Tcms *gc)
{
    gc->cached_page_key = 0;
    gc->cached_page = NULL;
}
#endif

static TcmsPage *insert_page(Tcms *gc, uintptr_t key)
{
    size_t mask = gc->page_capacity - 1;
    size_t index = page_hash(key) & mask;
    TcmsPage *deleted = NULL;
    for (;;) {
        TcmsPage *entry = &gc->pages[index];
        if (entry->key == key) return entry;
        if (entry->key == TCMS_PAGE_DELETED && deleted == NULL) deleted = entry;
        if (entry->key == 0) {
            if (deleted != NULL) entry = deleted;
            else gc->page_used++;
            *entry = (TcmsPage){.key = key};
            gc->page_count++;
            return entry;
        }
        index = (index + 1) & mask;
    }
}

/* Reserve an upper bound before touching entries, so failure never publishes
 * half of an object's ownership. Rehash tombstones without growing when live
 * occupancy still fits. Neither collection nor lookup allocates metadata. */
static TcmsResult reserve_pages(Tcms *gc, size_t additional)
{
    if (additional > SIZE_MAX - gc->page_count) return TCMS_SIZE_OVERFLOW;
    size_t required = gc->page_count + additional;
    size_t capacity = gc->page_capacity;
    if (capacity != 0 && additional <= capacity - capacity / 4 - gc->page_used)
        return TCMS_OK;
    if (capacity == 0) capacity = 8;
    while (required > capacity - capacity / 4) {
        if (capacity > SIZE_MAX / 2) return TCMS_SIZE_OVERFLOW;
        capacity *= 2;
    }
    if (capacity > SIZE_MAX / sizeof(TcmsPage)) return TCMS_SIZE_OVERFLOW;
    TcmsPage *pages = gc->allocate(capacity * sizeof(*pages), _Alignof(TcmsPage));
    if (pages == NULL) return TCMS_OUT_OF_MEMORY;
    memset(pages, 0, capacity * sizeof(*pages));
    TcmsPage *previous = gc->pages;
    size_t previous_capacity = gc->page_capacity;
#ifdef TCMS_PAGE_CACHE
    invalidate_page_cache(gc);
#endif
    gc->pages = pages;
    gc->page_capacity = capacity;
    gc->page_count = gc->page_used = 0;
    for (size_t i = 0; i < previous_capacity; i++) {
        TcmsPage entry = previous[i];
        if (entry.key != 0 && entry.key != TCMS_PAGE_DELETED)
            *insert_page(gc, entry.key) = entry;
    }
    if (previous != NULL)
        gc->release(previous, previous_capacity * sizeof(*previous), _Alignof(TcmsPage));
    return TCMS_OK;
}

static TcmsResult register_page_range(Tcms *gc, TcmsObject *object, size_t bytes, bool block)
{
#ifndef TCMS_BLOCK_ALLOCATION
    (void)block;
#endif
    uintptr_t start = (uintptr_t)object;
    if (bytes > UINTPTR_MAX - start) return TCMS_SIZE_OVERFLOW;
    uintptr_t first = start >> TCMS_PAGE_SHIFT;
    uintptr_t last = (start + bytes) >> TCMS_PAGE_SHIFT;
    TcmsResult status = reserve_pages(gc, (size_t)(last - first + 1));
    if (status != TCMS_OK) return status;
    for (uintptr_t page = first; page <= last; page++) {
        TcmsPage *entry = insert_page(gc, page + 1);
        if (bytes <= TCMS_INTERIOR_SCAN_BYTES) entry->small_count++;
        else {
            /* Three disjoint extents longer than one page cannot all intersect
             * that page: the middle extent would have to fit inside it. */
            size_t slot = entry->large[0] == NULL ? 0 : 1;
            assert(entry->large[slot] == NULL);
            entry->large[slot] = object;
#ifdef TCMS_BLOCK_ALLOCATION
            if (block) entry->block_flags |= 1u << slot;
#endif
        }
    }
    return TCMS_OK;
}

static TcmsResult register_pages(Tcms *gc, TcmsObject *object, size_t bytes)
{
    return register_page_range(gc, object, bytes, false);
}

static void unregister_pages(Tcms *gc, TcmsObject *object, size_t bytes)
{
    uintptr_t first = (uintptr_t)object >> TCMS_PAGE_SHIFT;
    uintptr_t last = ((uintptr_t)object + bytes) >> TCMS_PAGE_SHIFT;
    for (uintptr_t page = first; page <= last; page++) {
        TcmsPage *entry = find_page(gc, page + 1);
        assert(entry != NULL);
        if (bytes <= TCMS_INTERIOR_SCAN_BYTES) {
            assert(entry->small_count != 0);
            entry->small_count--;
        } else {
            size_t slot = entry->large[0] == object ? 0 : 1;
            assert(entry->large[slot] == object);
            entry->large[slot] = NULL;
#ifdef TCMS_BLOCK_ALLOCATION
            entry->block_flags &= ~(1u << slot);
#endif
        }
        if (entry->small_count == 0 && entry->large[0] == NULL && entry->large[1] == NULL) {
#ifdef TCMS_PAGE_CACHE
            if (gc->cached_page == entry) invalidate_page_cache(gc);
#endif
            entry->key = TCMS_PAGE_DELETED;
            gc->page_count--;
        }
    }
}

static void dispose_pages(Tcms *gc)
{
#ifdef TCMS_PAGE_CACHE
    invalidate_page_cache(gc);
#endif
    if (gc->pages != NULL)
        gc->release(gc->pages, gc->page_capacity * sizeof(*gc->pages), _Alignof(TcmsPage));
    gc->pages = NULL;
    gc->page_capacity = gc->page_count = gc->page_used = 0;
}

#endif
