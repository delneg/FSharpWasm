#include "collector_lifecycle.h"
#include "tcms_runtime_state.h"
#include "compact/tcms_allocator.h"
#include "../runtime_libc_initializer.h"
#include <stdlib.h>

Tcms netwasm_tcms;
uint64_t netwasm_tcms_collections;
uint64_t netwasm_tcms_allocated_bytes;

#define NETWASM_TCMS_MINIMUM_COLLECTION_BUDGET (1024u * 1024u)

static void *allocate_memory(size_t bytes, size_t alignment)
{
    runtime_initialize_libc();
    return tcms_allocator_allocate(bytes, alignment);
}
static void release_memory(void *address, size_t bytes, size_t alignment)
{
    tcms_allocator_release(address, bytes, alignment);
}

void collector_initialize(void)
{
    if (tcms_initialize(
            &netwasm_tcms,
            allocate_memory,
            release_memory,
            NETWASM_TCMS_MINIMUM_COLLECTION_BUDGET) != TCMS_OK) abort();
    netwasm_tcms.trace_roots = netwasm_tcms_trace_roots;
}
/* These Boehm configuration verbs are ABI-compatible no-ops. The candidate
 * always resolves known managed byrefs, never scans stacks, invokes callbacks
 * on demand, and uses the reviewed non-ordered finalization phases. */
void collector_enable_interior_pointers(void) { }
void collector_disable_stack_scanning(void) { }
void collector_enable_on_demand_finalization(void) { }
void collector_enable_java_finalization_order(void) { }
