/* NetWasmGC for NetWasm (https://www.netwasm.com)
 * Copyright (c) 2026 Zion Sati
 * SPDX-License-Identifier: Apache-2.0
 */
#ifndef NETWASM_GC_H
#define NETWASM_GC_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

typedef struct NetWasmGc NetWasmGc;

typedef struct {
    const size_t *offsets;
    size_t reference_count;
} NetWasmGcDescriptor;

typedef void *(*NetWasmGcAllocate)(size_t bytes, size_t alignment);
typedef void (*NetWasmGcRelease)(void *address, size_t bytes, size_t alignment);
typedef void (*NetWasmGcTraceRoots)(NetWasmGc *gc, void *state);
typedef void (*NetWasmGcCompleteMark)(NetWasmGc *gc, void *state);

typedef enum {
    NETWASM_GC_OK,
    NETWASM_GC_INVALID_ARGUMENT,
    NETWASM_GC_INVALID_LAYOUT,
    NETWASM_GC_SIZE_OVERFLOW,
    NETWASM_GC_OUT_OF_MEMORY,
    NETWASM_GC_UNKNOWN_OBJECT,
} NetWasmGcResult;

NetWasmGcResult netwasm_gc_create(
    NetWasmGcAllocate allocate,
    NetWasmGcRelease release,
    size_t minimum_collection_budget,
    NetWasmGc **result);

void netwasm_gc_set_root_tracer(
    NetWasmGc *gc,
    NetWasmGcTraceRoots trace_roots,
    void *state);

void netwasm_gc_set_mark_completion(
    NetWasmGc *gc,
    NetWasmGcCompleteMark complete_mark,
    void *state);

/* Returns NULL without changing state when no precharged run is available. */
void *netwasm_gc_try_allocate_available(
    NetWasmGc *gc,
    size_t element_count,
    size_t element_size,
    const NetWasmGcDescriptor *descriptor);

NetWasmGcResult netwasm_gc_allocate(
    NetWasmGc *gc,
    size_t element_count,
    size_t element_size,
    const NetWasmGcDescriptor *descriptor,
    void **result);

void netwasm_gc_collect(NetWasmGc *gc);
bool netwasm_gc_mark(NetWasmGc *gc, uintptr_t address);
void netwasm_gc_drain(NetWasmGc *gc);
bool netwasm_gc_is_marked(const NetWasmGc *gc, uintptr_t address);
uintptr_t netwasm_gc_base(const NetWasmGc *gc, uintptr_t address);
bool netwasm_gc_contains(const NetWasmGc *gc, uintptr_t address);
bool netwasm_gc_is_collecting(const NetWasmGc *gc);

uint64_t netwasm_gc_collection_count(const NetWasmGc *gc);
uint64_t netwasm_gc_total_allocated_bytes(const NetWasmGc *gc);
uint64_t netwasm_gc_live_bytes(const NetWasmGc *gc);
uint64_t netwasm_gc_reserved_bytes(const NetWasmGc *gc);
size_t netwasm_gc_object_count(const NetWasmGc *gc);

void netwasm_gc_destroy(NetWasmGc *gc);

#endif
