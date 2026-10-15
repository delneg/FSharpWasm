#include <assert.h>
#include <stdlib.h>
#include "../../src/NetWasm.Runtime/collector/compact/tcms.c"

static size_t successful_allocations, releases, fail_after = SIZE_MAX;

static void *allocate_memory(size_t bytes, size_t alignment)
{
    assert(alignment <= _Alignof(max_align_t));
    if (fail_after == 0) return NULL;
    if (fail_after != SIZE_MAX) fail_after--;
    void *result = malloc(bytes);
    if (result != NULL) successful_allocations++;
    return result;
}

static void release_memory(void *address, size_t bytes, size_t alignment)
{
    (void)bytes;
    (void)alignment;
    releases++;
    free(address);
}

static Tcms create(void)
{
    Tcms gc;
    assert(tcms_initialize(&gc, allocate_memory, release_memory, SIZE_MAX) == TCMS_OK);
    return gc;
}

static void shared_pages_and_bounds(void)
{
    /* Deliberately unaligned to the page, but aligned for object headers.
     * Consecutive large extents share pages and each preceding endpoint is
     * the following header, not the following payload. */
    _Alignas(4096) static unsigned char memory[16384];
    const size_t extent = 4112;
    Tcms gc = create();
    TcmsObject *objects[3];
    for (size_t i = 0; i < 3; i++) {
        objects[i] = (TcmsObject *)(memory + 16 + i * extent);
        objects[i]->data.payload_bytes = extent - sizeof(TcmsObject);
        assert(register_pages(&gc, objects[i], extent) == TCMS_OK);
    }
    for (size_t i = 0; i < 3; i++) {
        uintptr_t start = (uintptr_t)payload(objects[i]);
        uintptr_t end = (uintptr_t)objects[i] + extent;
        assert(resolve(&gc, start, false) == objects[i]);
        assert(resolve(&gc, end - 1, false) == objects[i]);
        assert(resolve(&gc, end, true) == objects[i]);
        assert(resolve(&gc, end, false) == NULL);
        assert(resolve(&gc, start - 1, true) == NULL);
    }
    assert(resolve(&gc, (uintptr_t)&gc, true) == NULL);
    for (size_t i = 0; i < 3; i++) {
        unregister_pages(&gc, objects[i], extent);
        assert(resolve(&gc, (uintptr_t)payload(objects[i]), true) == NULL);
    }
    assert(gc.page_count == 0);
    tcms_destroy(&gc);
}

static void failure_is_atomic(void)
{
    Tcms gc = create();
    /* Object table and object succeed, page metadata fails. */
    fail_after = 2;
    void *result = (void *)(uintptr_t)1;
    assert(tcms_allocate(&gc, 8192, (TcmsLayout){0}, NULL, 0, &result) == TCMS_OUT_OF_MEMORY);
    assert(result == (void *)(uintptr_t)1);
    assert(gc.objects == 0 && gc.bytes == 0 && gc.page_count == 0);
    fail_after = SIZE_MAX;
    assert(tcms_allocate(&gc, 8192, (TcmsLayout){0}, NULL, 0, &result) == TCMS_OK);
    assert(tcms_contains(&gc, (uintptr_t)result + 8191));
    assert(!tcms_contains(&gc, (uintptr_t)result + 8192));
    tcms_destroy(&gc);
    assert(gc.page_count == 0 && gc.pages == NULL);
}

static void collection_and_reuse(void)
{
    Tcms gc = create();
    const size_t sizes[] = {0, 1, 16, 63, 4000, 4096, 4097, 8192, 16384};
    uintptr_t roots[sizeof(sizes) / sizeof(sizes[0])];
    size_t count = sizeof(roots) / sizeof(roots[0]);
    for (size_t round = 0; round < 30; round++) {
        for (size_t i = 0; i < count; i++) {
            void *object;
            assert(tcms_allocate(&gc, sizes[i], (TcmsLayout){0}, roots, i, &object) == TCMS_OK);
            roots[i] = (uintptr_t)object + sizes[i];
            assert(tcms_contains(&gc, (uintptr_t)object));
            assert(!tcms_contains(&gc, roots[i] + 1));
        }
        tcms_collect(&gc, roots, count);
        assert(gc.objects == count);
        for (size_t i = 0; i < count; i++) {
            assert(tcms_base(&gc, roots[i] - sizes[i]) == roots[i] - sizes[i]);
            assert(tcms_base(&gc, roots[i]) == (sizes[i] == 0 ? roots[i] : 0));
            assert(tcms_pin(&gc, roots[i]) == TCMS_OK);
            assert(tcms_unpin(&gc, roots[i]) == TCMS_OK);
        }
        tcms_collect(&gc, NULL, 0);
        assert(gc.objects == 0 && gc.page_count == 0);
        for (size_t i = 0; i < count; i++) assert(!tcms_contains(&gc, roots[i]));
    }
    tcms_destroy(&gc);
}

static void collision_and_tombstone_reuse(void)
{
    Tcms gc = create();
    assert(reserve_pages(&gc, 6) == TCMS_OK && gc.page_capacity == 8);
    uintptr_t keys[7];
    size_t found = 0;
    for (uintptr_t key = 1; found < 7; key++)
        if ((page_hash(key) & 7) == 0) keys[found++] = key;
    for (size_t i = 0; i < 6; i++) insert_page(&gc, keys[i])->small_count = i + 1;
    assert(gc.page_count == 6 && gc.page_used == 6);
    TcmsPage *deleted = find_page(&gc, keys[0]);
    deleted->key = TCMS_PAGE_DELETED;
    deleted->small_count = 0;
    gc.page_count--;
    assert(find_page(&gc, keys[0]) == NULL);
    for (size_t i = 1; i < 6; i++) assert(find_page(&gc, keys[i])->small_count == i + 1);
    assert(insert_page(&gc, keys[6]) == deleted);
    assert(gc.page_count == 6 && gc.page_used == 6);
    deleted->key = TCMS_PAGE_DELETED;
    gc.page_count--;
    assert(reserve_pages(&gc, 1) == TCMS_OK);
    assert(gc.page_capacity == 8 && gc.page_count == 5 && gc.page_used == 5);
    for (size_t i = 1; i < 6; i++) assert(find_page(&gc, keys[i])->small_count == i + 1);
    assert(reserve_pages(&gc, SIZE_MAX) == TCMS_SIZE_OVERFLOW);
    fail_after = 0;
    assert(reserve_pages(&gc, 100) == TCMS_OUT_OF_MEMORY);
    assert(gc.page_capacity == 8 && gc.page_count == 5 && gc.page_used == 5);
    for (size_t i = 1; i < 6; i++) assert(find_page(&gc, keys[i])->small_count == i + 1);
    fail_after = SIZE_MAX;
    dispose_pages(&gc);
}

int main(void)
{
    shared_pages_and_bounds();
    failure_is_atomic();
    collection_and_reuse();
    collision_and_tombstone_reuse();
    assert(successful_allocations == releases);
    return 0;
}
