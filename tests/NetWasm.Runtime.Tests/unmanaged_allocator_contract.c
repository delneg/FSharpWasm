#include <assert.h>

#include "unmanaged_allocator.h"

static unsigned int libc_initializations;

void runtime_initialize_libc(void)
{
    ++libc_initializations;
}

int main(void)
{
    /* Unmanaged storage comes from libc, initialized before first use. */
    void *allocation = netwasm_unmanaged_allocate(32);
    assert(allocation != 0);
    assert(libc_initializations == 1);
    netwasm_unmanaged_free(allocation);
    return 0;
}
