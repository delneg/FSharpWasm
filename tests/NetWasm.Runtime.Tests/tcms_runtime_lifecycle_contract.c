#include "collector/compact/tcms.h"
#include <assert.h>
#include <stdlib.h>

extern Tcms netwasm_tcms;
void collector_initialize(void);

void runtime_initialize_libc(void) { }

void *tcms_allocator_allocate(size_t bytes, size_t alignment)
{
    assert(alignment <= _Alignof(max_align_t));
    return malloc(bytes);
}

void tcms_allocator_release(void *address, size_t bytes, size_t alignment)
{
    (void)bytes;
    (void)alignment;
    free(address);
}

void netwasm_tcms_trace_roots(Tcms *gc, void *state)
{
    (void)gc;
    (void)state;
}

int main(void)
{
    collector_initialize();
    assert(netwasm_tcms.minimum_threshold == 1024u * 1024u);
    assert(netwasm_tcms.threshold == netwasm_tcms.minimum_threshold);
    assert(netwasm_tcms.trace_roots == netwasm_tcms_trace_roots);
    tcms_destroy(&netwasm_tcms);
    return 0;
}
