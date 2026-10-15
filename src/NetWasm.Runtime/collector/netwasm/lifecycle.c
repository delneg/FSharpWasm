/* NetWasmGC for NetWasm (https://www.netwasm.com)
 * Copyright (c) 2026 Zion Sati
 * SPDX-License-Identifier: Apache-2.0
 */
#include "../collector_lifecycle.h"
#include "../../runtime_libc_initializer.h"
#include "runtime_state.h"

#include <stdlib.h>

#define NETWASM_GC_MINIMUM_COLLECTION_BUDGET (4u * 1024u * 1024u)

NetWasmGc *netwasm_gc_runtime;

static void *allocate_memory(size_t bytes, size_t alignment)
{
    (void)alignment;
    runtime_initialize_libc();
    return malloc(bytes == 0 ? 1 : bytes);
}

static void release_memory(void *address, size_t bytes, size_t alignment)
{
    (void)bytes;
    (void)alignment;
    free(address);
}

void collector_initialize(void)
{
    if (netwasm_gc_runtime != NULL) return;
    if (netwasm_gc_create(
            allocate_memory,
            release_memory,
            NETWASM_GC_MINIMUM_COLLECTION_BUDGET,
            &netwasm_gc_runtime) != NETWASM_GC_OK) abort();
    netwasm_gc_set_root_tracer(netwasm_gc_runtime, netwasm_gc_trace_registered_roots, NULL);
    netwasm_gc_lifetime_initialize(netwasm_gc_runtime);
}

void collector_enable_interior_pointers(void) { }
void collector_disable_stack_scanning(void) { }
void collector_enable_on_demand_finalization(void) { }
void collector_enable_java_finalization_order(void) { }
