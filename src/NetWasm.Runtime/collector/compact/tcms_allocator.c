#include "tcms_allocator.h"
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>
#include <stdlib.h>

/* Managed allocations are overwhelmingly small and are released in GC
 * batches. Keep them in 64-KiB slabs and recycle exact aligned size classes;
 * libc remains the backing allocator for slabs and uncommon large blocks. */
#define TCMS_SLAB_BYTES (64u * 1024u)
#define TCMS_SMALL_LIMIT (4u * 1024u)
#define TCMS_ALIGNMENT _Alignof(max_align_t)
#define TCMS_SIZE_CLASSES (TCMS_SMALL_LIMIT / TCMS_ALIGNMENT)

typedef struct TcmsFreeNode TcmsFreeNode;
struct TcmsFreeNode { TcmsFreeNode *next; };

typedef struct TcmsSlab TcmsSlab;
struct TcmsSlab {
    TcmsSlab *next;
    size_t used;
    max_align_t alignment;
    unsigned char data[];
};

static TcmsSlab *tcms_slabs;
static TcmsSlab *tcms_current_slab;
static TcmsFreeNode *tcms_free_lists[TCMS_SIZE_CLASSES];

static bool valid_alignment(size_t alignment)
{
    return alignment != 0 && alignment <= TCMS_ALIGNMENT &&
        (alignment & (alignment - 1)) == 0;
}

static size_t allocation_size(size_t bytes)
{
    size_t minimum = bytes < sizeof(TcmsFreeNode) ? sizeof(TcmsFreeNode) : bytes;
    if (minimum > SIZE_MAX - (TCMS_ALIGNMENT - 1)) return 0;
    return (minimum + TCMS_ALIGNMENT - 1) & ~(TCMS_ALIGNMENT - 1);
}

void *tcms_allocator_allocate(size_t bytes, size_t alignment)
{
    if (!valid_alignment(alignment)) return NULL;
    size_t rounded = allocation_size(bytes);
    if (rounded == 0) return NULL;
    if (rounded > TCMS_SMALL_LIMIT) return malloc(bytes == 0 ? 1 : bytes);

    size_t size_class = rounded / TCMS_ALIGNMENT - 1;
    TcmsFreeNode *free_node = tcms_free_lists[size_class];
    if (free_node != NULL) {
        tcms_free_lists[size_class] = free_node->next;
        return free_node;
    }

    if (tcms_current_slab == NULL ||
        rounded > TCMS_SLAB_BYTES - tcms_current_slab->used) {
        if (sizeof(TcmsSlab) > SIZE_MAX - TCMS_SLAB_BYTES) return NULL;
        TcmsSlab *slab = malloc(sizeof(TcmsSlab) + TCMS_SLAB_BYTES);
        if (slab == NULL) return NULL;
        slab->next = tcms_slabs;
        slab->used = 0;
        tcms_slabs = slab;
        tcms_current_slab = slab;
    }

    void *result = tcms_current_slab->data + tcms_current_slab->used;
    tcms_current_slab->used += rounded;
    return result;
}

void tcms_allocator_release(void *address, size_t bytes, size_t alignment)
{
    if (address == NULL) return;
    size_t rounded = valid_alignment(alignment) ? allocation_size(bytes) : 0;
    if (rounded == 0 || rounded > TCMS_SMALL_LIMIT) {
        free(address);
        return;
    }
    size_t size_class = rounded / TCMS_ALIGNMENT - 1;
    TcmsFreeNode *free_node = address;
    free_node->next = tcms_free_lists[size_class];
    tcms_free_lists[size_class] = free_node;
}

void tcms_allocator_dispose(void)
{
    TcmsSlab *slab = tcms_slabs;
    while (slab != NULL) {
        TcmsSlab *next = slab->next;
        free(slab);
        slab = next;
    }
    tcms_slabs = NULL;
    tcms_current_slab = NULL;
    for (size_t index = 0; index < TCMS_SIZE_CLASSES; index++)
        tcms_free_lists[index] = NULL;
}
