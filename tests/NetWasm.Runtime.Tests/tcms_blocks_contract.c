#include <assert.h>
#include <stdlib.h>
#include "tcms_contract_implementation.h"

static size_t allocations, releases, fail_after = SIZE_MAX;
static void *allocate_memory(size_t bytes, size_t alignment)
{
    assert(alignment <= _Alignof(max_align_t));
    if (fail_after == 0) return NULL;
    if (fail_after != SIZE_MAX) fail_after--;
    void *address = malloc(bytes);
    if (address != NULL) allocations++;
    return address;
}
static void release_memory(void *address, size_t bytes, size_t alignment)
{
    (void)bytes;
    (void)alignment;
    releases++;
    free(address);
}
static Tcms create(size_t threshold)
{
    Tcms gc;
    assert(tcms_initialize(&gc, allocate_memory, release_memory, threshold) == TCMS_OK);
    return gc;
}

static TcmsBlock *object_block(const Tcms *gc, TcmsObject *object)
{
#ifdef TCMS_BITMAP_MARKING
    TcmsBlock *block;
    assert(resolve_with_block(gc, (uintptr_t)(object + 1), false, &block) == object);
    return block;
#else
    return block_for_object(gc, object);
#endif
}

static void tails_bounds_and_reuse(void)
{
    Tcms gc = create(SIZE_MAX);
    void *object;
    assert(tcms_allocate(&gc, 16, (TcmsLayout){0}, NULL, 0, &object) == TCMS_OK);
    TcmsBlock *block = object_block(&gc, (TcmsObject *)object - 1);
    assert(block != NULL && block->live == 1 && gc.block_bytes == block_backing_bytes(block));
    assert(block->cursor < block->end && gc.reserved_bytes != 0);
    uintptr_t unused = (uintptr_t)block->cursor + sizeof(TcmsObject);
    assert(!tcms_contains(&gc, unused) && !tcms_mark(&gc, unused));
    assert(tcms_pin(&gc, unused) == TCMS_UNKNOWN_OBJECT);
    assert(tcms_contains(&gc, (uintptr_t)object + 15));
    assert(!tcms_contains(&gc, (uintptr_t)object + 16));
    assert(!tcms_contains(&gc, (uintptr_t)object - 1));
    assert(tcms_pin(&gc, (uintptr_t)object + 16) == TCMS_OK);
    assert(tcms_unpin(&gc, (uintptr_t)object + 16) == TCMS_OK);
    memset(object, 0xa5, 16);
    uintptr_t root = (uintptr_t)object + 16;
    tcms_collect(&gc, &root, 1);
    assert(gc.objects == 1 && gc.reserved_bytes == 0);
    for (size_t i = 0; i < 16; i++) assert(((unsigned char *)object)[i] == 0xa5);
    assert(gc.object_table == NULL && gc.object_table_capacity == 0);
    tcms_collect(&gc, NULL, 0);
    assert(gc.objects == 0 && gc.empty_block_count == 1);
    assert(!tcms_contains(&gc, (uintptr_t)object));
    assert(tcms_allocate(&gc, 256, (TcmsLayout){0}, NULL, 0, &object) == TCMS_OK);
    assert(object_block(&gc, (TcmsObject *)object - 1) == block);
    assert(gc.empty_block_count == 0 && block->live == 1);
    for (size_t i = 0; i < 256; i++) assert(((unsigned char *)object)[i] == 0);
    tcms_destroy(&gc);
    assert(gc.blocks == NULL && gc.block_classes == NULL && gc.pages == NULL);
    assert(gc.block_bytes == 0 && gc.reserved_bytes == 0);
}

static void mixed_classes_and_collection(void)
{
    const size_t sizes[] = {0, 1, 16, 63, 200, 1000, 4000, 4096, 8192};
    uintptr_t roots[256];
    Tcms gc = create(64 * 1024);
    for (size_t round = 0; round < 12; round++) {
        for (size_t i = 0; i < 256; i++) {
            size_t bytes = sizes[i % (sizeof(sizes) / sizeof(sizes[0]))];
            void *object;
            assert(tcms_allocate(&gc, bytes, (TcmsLayout){0}, roots, i, &object) == TCMS_OK);
            roots[i] = (uintptr_t)object;
            for (size_t j = 0; j < bytes; j++) assert(((unsigned char *)object)[j] == 0);
            if (bytes != 0) memset(object, (int)(i % 251 + 1), bytes);
            if (bytes >= 4096) assert(object_block(&gc, (TcmsObject *)object - 1) == NULL);
        }
        tcms_collect(&gc, roots, 256);
        assert(gc.objects == 256 && gc.reserved_bytes == 0);
        for (size_t i = 0; i < 256; i++) {
            size_t bytes = sizes[i % (sizeof(sizes) / sizeof(sizes[0]))];
            assert(tcms_base(&gc, roots[i]) == roots[i]);
            for (size_t j = 0; j < bytes; j++)
                assert(((unsigned char *)roots[i])[j] == (unsigned char)(i % 251 + 1));
        }
        tcms_collect(&gc, NULL, 0);
        assert(gc.objects == 0 && gc.bytes == 0 && gc.reserved_bytes == 0);
        assert(gc.empty_block_count == 1 && gc.block_bytes == block_backing_bytes(gc.empty_blocks));
    }
    tcms_destroy(&gc);
}

static void failed_metadata(void)
{
    for (size_t allowed = 0; allowed < 5; allowed++) {
        Tcms gc = create(SIZE_MAX);
        fail_after = allowed;
        void *object = (void *)(uintptr_t)1;
        TcmsResult status = tcms_allocate(&gc, 16, (TcmsLayout){0}, NULL, 0, &object);
        assert(status == (allowed < 3 ? TCMS_OUT_OF_MEMORY : TCMS_OK));
        if (status != TCMS_OK) {
            assert(object == (void *)(uintptr_t)1 && gc.objects == 0 && gc.bytes == 0);
            assert(gc.reserved_bytes == 0);
        } else {
            assert(gc.objects == 1 && tcms_contains(&gc, (uintptr_t)object));
        }
        fail_after = SIZE_MAX;
        tcms_destroy(&gc);
        assert(allocations == releases);
    }
}

int main(void)
{
    tails_bounds_and_reuse();
    mixed_classes_and_collection();
    failed_metadata();
    assert(allocations == releases);
    return 0;
}
