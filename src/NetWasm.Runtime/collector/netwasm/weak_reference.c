/* NetWasmGC for NetWasm (https://www.netwasm.com)
 * Copyright (c) 2026 Zion Sati
 * SPDX-License-Identifier: Apache-2.0
 */
#include "../collector_weak_reference.h"
#include "lifetime.h"

int collector_register_weak_reference(
    uintptr_t *slot,
    uintptr_t target,
    NetWasmWeakReferenceLifetime lifetime)
{
    return netwasm_gc_register_weak(slot, target, lifetime);
}

int collector_register_short_weak_reference(uintptr_t *slot, uintptr_t target)
{
    return netwasm_gc_register_weak(
        slot,
        target,
        NETWASM_WEAK_REFERENCE_BEFORE_FINALIZATION);
}

int collector_unregister_weak_reference(
    uintptr_t *slot,
    NetWasmWeakReferenceLifetime lifetime)
{
    return netwasm_gc_unregister_weak(slot, lifetime);
}
