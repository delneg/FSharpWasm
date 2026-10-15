/* NetWasmGC for NetWasm (https://www.netwasm.com)
 * Copyright (c) 2026 Zion Sati
 * SPDX-License-Identifier: Apache-2.0
 */
#ifndef NETWASM_GC_LIFETIME_H
#define NETWASM_GC_LIFETIME_H

#include "../collector_finalization.h"
#include "../collector_weak_reference.h"
#include "netwasm_gc.h"

void netwasm_gc_lifetime_initialize(NetWasmGc *gc);

void netwasm_gc_register_finalizer(
    uintptr_t object,
    NetWasmCollectorFinalizer finalizer,
    void *context,
    NetWasmCollectorFinalizer *previous,
    void **previous_context);
bool netwasm_gc_has_pending_finalizers(void);
int netwasm_gc_invoke_finalizers(void);

int netwasm_gc_register_weak(
    uintptr_t *slot,
    uintptr_t target,
    NetWasmWeakReferenceLifetime lifetime);
int netwasm_gc_unregister_weak(
    uintptr_t *slot,
    NetWasmWeakReferenceLifetime lifetime);

int netwasm_gc_register_ephemeron(uintptr_t *key, uintptr_t *value);
void netwasm_gc_unregister_ephemeron(uintptr_t *key, uintptr_t *value);

#endif
