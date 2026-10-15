/* NetWasmGC for NetWasm (https://www.netwasm.com)
 * Copyright (c) 2026 Zion Sati
 * SPDX-License-Identifier: Apache-2.0
 */
#include "../collector_collection.h"
#include "runtime_state.h"

void collector_collect(void)
{
    netwasm_gc_collect(netwasm_gc_runtime);
}

uint64_t collector_collection_count(void)
{
    return netwasm_gc_collection_count(netwasm_gc_runtime);
}

uint64_t collector_total_allocated_bytes(void)
{
    return netwasm_gc_total_allocated_bytes(netwasm_gc_runtime);
}

uint64_t collector_heap_size(void)
{
    return netwasm_gc_live_bytes(netwasm_gc_runtime);
}

uint64_t collector_free_bytes(void)
{
    return 0;
}

bool collector_try_read_total_pause_milliseconds(uint64_t *value)
{
    (void)value;
    return false;
}
