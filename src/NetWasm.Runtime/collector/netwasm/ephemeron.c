/* NetWasmGC for NetWasm (https://www.netwasm.com)
 * Copyright (c) 2026 Zion Sati
 * SPDX-License-Identifier: Apache-2.0
 */
#include "../collector_ephemeron.h"
#include "lifetime.h"

int collector_register_ephemeron(uintptr_t *key, uintptr_t *value)
{
    return netwasm_gc_register_ephemeron(key, value);
}

void collector_unregister_ephemeron(uintptr_t *key, uintptr_t *value)
{
    netwasm_gc_unregister_ephemeron(key, value);
}
