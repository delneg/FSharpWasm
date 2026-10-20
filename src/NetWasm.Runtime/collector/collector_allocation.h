#ifndef NETWASM_COLLECTOR_ALLOCATION_H
#define NETWASM_COLLECTOR_ALLOCATION_H

#include <stddef.h>
#include <stdint.h>
#include <stdbool.h>

/* A collected object's layout, read from its type id: references at
 * fixed_offsets within its first fixed_bytes, then element_count elements of
 * element_size bytes with references at element_offsets. Collectors keep no
 * per-object layout. */
typedef struct {
    const size_t *fixed_offsets;
    size_t fixed_reference_count;
    size_t fixed_bytes;
    const size_t *element_offsets;
    size_t element_reference_count;
    size_t element_size;
    size_t element_count;
} NetWasmCollectorLayout;

/* Implemented by the runtime. Valid once the object's type id, lengths and
 * data pointer are stored; it does not allocate. */
void collector_object_layout(const void *object, NetWasmCollectorLayout *layout);

/* Returns zeroed storage. The collector traces it through
 * collector_object_layout only when contains_references is true. */
void *collector_allocate_object(size_t bytes, bool contains_references);

#endif
