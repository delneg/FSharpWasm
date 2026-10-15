#include <assert.h>
#include <stddef.h>
#include <stdint.h>
#include <stdlib.h>

static size_t backing_allocations;
static size_t backing_releases;

static void *test_allocate(size_t bytes)
{
    backing_allocations++;
    return malloc(bytes);
}

static void test_release(void *address)
{
    backing_releases++;
    free(address);
}

#define malloc test_allocate
#define free test_release
#include "../../src/NetWasm.Runtime/collector/compact/tcms_allocator.c"
#undef malloc
#undef free

int main(void)
{
    size_t alignment = _Alignof(max_align_t);
    assert(tcms_allocator_allocate(16, 0) == NULL);
    assert(tcms_allocator_allocate(SIZE_MAX, alignment) == NULL);

    void *first = tcms_allocator_allocate(24, alignment);
    void *second = tcms_allocator_allocate(24, alignment);
    assert(first != NULL && second != NULL && first != second);
    assert((uintptr_t)first % alignment == 0);
    assert(backing_allocations == 1);

    tcms_allocator_release(first, 24, alignment);
    assert(tcms_allocator_allocate(24, alignment) == first);
    assert(backing_allocations == 1);

    void *large = tcms_allocator_allocate(TCMS_SMALL_LIMIT + 1, alignment);
    assert(large != NULL && backing_allocations == 2);
    tcms_allocator_release(large, TCMS_SMALL_LIMIT + 1, alignment);
    assert(backing_releases == 1);

    tcms_allocator_dispose();
    assert(backing_releases == 2);
    return 0;
}
