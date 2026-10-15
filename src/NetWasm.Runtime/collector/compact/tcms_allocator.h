#ifndef NETWASM_TCMS_ALLOCATOR_H
#define NETWASM_TCMS_ALLOCATOR_H

#include <stddef.h>

void *tcms_allocator_allocate(size_t bytes, size_t alignment);
void tcms_allocator_release(void *address, size_t bytes, size_t alignment);
void tcms_allocator_dispose(void);

#endif
