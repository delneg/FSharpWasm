/* NetWasmGC for NetWasm (https://www.netwasm.com)
 * Copyright (c) 2026 Zion Sati
 * SPDX-License-Identifier: Apache-2.0
 */
#include "../collector_allocation.h"
#include "../collector_metadata.h"
#include "runtime_state.h"

#include <stdlib.h>

typedef struct {
    NetWasmGcDescriptor descriptor;
    size_t offsets[];
} RuntimeDescriptor;

void collector_mark_descriptor_word(NetWasmCollectorDescriptorWord *bitmap, size_t bit)
{
    size_t bits = sizeof(*bitmap) * 8;
    bitmap[bit / bits] |= (uintptr_t)1 << (bit % bits);
}

NetWasmCollectorDescriptor collector_create_descriptor(
    const NetWasmCollectorDescriptorWord *bitmap,
    size_t length)
{
    if ((length != 0 && bitmap == NULL) || length > SIZE_MAX / sizeof(uintptr_t)) abort();
    size_t bits = sizeof(*bitmap) * 8;
    size_t count = 0;
    for (size_t bit = 0; bit < length; bit++)
        count += (bitmap[bit / bits] >> (bit % bits)) & 1;
    if (count == 0) return 0;
    if (count > (SIZE_MAX - sizeof(RuntimeDescriptor)) / sizeof(size_t)) abort();
    RuntimeDescriptor *runtime = collector_allocate_metadata(
        sizeof(*runtime) + count * sizeof(size_t));
    if (runtime == NULL) abort();
    runtime->descriptor.offsets = runtime->offsets;
    runtime->descriptor.reference_count = count;
    size_t output = 0;
    for (size_t bit = 0; bit < length; bit++)
        if ((bitmap[bit / bits] >> (bit % bits)) & 1)
            runtime->offsets[output++] = bit * sizeof(uintptr_t);
    return (uintptr_t)runtime;
}

static __attribute__((noinline)) void *allocate_slow(
    size_t count, size_t size, NetWasmCollectorDescriptor descriptor)
{
    RuntimeDescriptor *runtime = (RuntimeDescriptor *)descriptor;
    void *result = NULL;
    NetWasmGcResult status = netwasm_gc_allocate(
        netwasm_gc_runtime,
        count,
        size,
        runtime == NULL ? NULL : &runtime->descriptor,
        &result);
    if (status == NETWASM_GC_INVALID_ARGUMENT || status == NETWASM_GC_INVALID_LAYOUT) abort();
    return status == NETWASM_GC_OK ? result : NULL;
}

static void *allocate(size_t count, size_t size, NetWasmCollectorDescriptor descriptor)
{
    RuntimeDescriptor *runtime = (RuntimeDescriptor *)descriptor;
    void *result = netwasm_gc_try_allocate_available(netwasm_gc_runtime, count, size,
        runtime == NULL ? NULL : &runtime->descriptor);
    return result != NULL ? result : allocate_slow(count, size, descriptor);
}

void *collector_allocate_exact(size_t size, NetWasmCollectorDescriptor descriptor)
{
    return allocate(1, size, descriptor);
}

void *collector_allocate_exact_zeroed(
    size_t count,
    size_t size,
    NetWasmCollectorDescriptor descriptor)
{
    return allocate(count, size, descriptor);
}

void *collector_allocate_atomic(size_t size)
{
    return allocate(1, size, 0);
}

bool collector_atomic_allocation_is_zeroed(void) { return true; }
