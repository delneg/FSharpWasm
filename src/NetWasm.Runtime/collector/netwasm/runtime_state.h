/* NetWasmGC for NetWasm (https://www.netwasm.com)
 * Copyright (c) 2026 Zion Sati
 * SPDX-License-Identifier: Apache-2.0
 */
#ifndef NETWASM_GC_RUNTIME_STATE_H
#define NETWASM_GC_RUNTIME_STATE_H

#include "netwasm_gc.h"

extern NetWasmGc *netwasm_gc_runtime;
void netwasm_gc_trace_registered_roots(NetWasmGc *gc, void *state);
void netwasm_gc_lifetime_initialize(NetWasmGc *gc);

#endif
