/* NetWasmGC for NetWasm (https://www.netwasm.com)
 * Copyright (c) 2026 Zion Sati
 * SPDX-License-Identifier: Apache-2.0
 */
#include "../collector_finalization.h"
#include "lifetime.h"

void collector_register_finalizer(
    void *object,
    NetWasmCollectorFinalizer finalizer,
    void *context,
    NetWasmCollectorFinalizer *previous,
    void **previous_context)
{
    netwasm_gc_register_finalizer(
        (uintptr_t)object,
        finalizer,
        context,
        previous,
        previous_context);
}

bool collector_has_pending_finalizers(void)
{
    return netwasm_gc_has_pending_finalizers();
}

int collector_invoke_finalizers(void)
{
    return netwasm_gc_invoke_finalizers();
}
