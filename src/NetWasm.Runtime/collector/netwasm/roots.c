/* NetWasmGC for NetWasm (https://www.netwasm.com)
 * Copyright (c) 2026 Zion Sati
 * SPDX-License-Identifier: Apache-2.0
 */
#include "../collector_metadata.h"
#include "../collector_roots.h"
#include "runtime_state.h"

#include <stdlib.h>
#include <string.h>

typedef struct RootRegistration RootRegistration;
struct RootRegistration {
    RootRegistration *next;
    void *start;
    size_t bytes;
    size_t count;
};

static RootRegistration *registrations;
static NetWasmCollectorRootEnumerator enumerate_roots;

void collector_register_roots(void *start, size_t size)
{
    if (start == NULL || size % sizeof(uintptr_t) != 0) abort();
    for (RootRegistration *entry = registrations; entry != NULL; entry = entry->next) {
        if (entry->start != start || entry->bytes != size) continue;
        if (entry->count == SIZE_MAX) abort();
        entry->count++;
        return;
    }
    RootRegistration *entry = collector_allocate_metadata(sizeof(*entry));
    if (entry == NULL) abort();
    *entry = (RootRegistration){
        .next = registrations,
        .start = start,
        .bytes = size,
        .count = 1,
    };
    registrations = entry;
}

void collector_unregister_roots(void *start, size_t size)
{
    RootRegistration **link = &registrations;
    while (*link != NULL && ((*link)->start != start || (*link)->bytes != size))
        link = &(*link)->next;
    if (*link == NULL) return;
    RootRegistration *entry = *link;
    if (--entry->count != 0) return;
    *link = entry->next;
    collector_release_metadata(entry);
}

void collector_register_root_range(void *start, void *end)
{
    if ((uintptr_t)end < (uintptr_t)start) abort();
    collector_register_roots(start, (uintptr_t)end - (uintptr_t)start);
}

void collector_unregister_root_range(void *start, void *end)
{
    if ((uintptr_t)end < (uintptr_t)start) abort();
    collector_unregister_roots(start, (uintptr_t)end - (uintptr_t)start);
}

void collector_set_root_enumerator(NetWasmCollectorRootEnumerator enumerator)
{
    if (enumerator == NULL || enumerate_roots != NULL) abort();
    enumerate_roots = enumerator;
}

void collector_trace_root_range(void *start, void *end)
{
    uintptr_t first = (uintptr_t)start;
    uintptr_t limit = (uintptr_t)end;
    if (!netwasm_gc_is_collecting(netwasm_gc_runtime) || limit < first ||
        (limit - first) % sizeof(uintptr_t) != 0) abort();
    for (uintptr_t slot = first; slot < limit; slot += sizeof(uintptr_t)) {
        uintptr_t reference;
        memcpy(&reference, (void *)slot, sizeof(reference));
        netwasm_gc_mark(netwasm_gc_runtime, reference);
    }
}

void netwasm_gc_trace_registered_roots(NetWasmGc *gc, void *state)
{
    (void)gc;
    (void)state;
    for (RootRegistration *entry = registrations; entry != NULL; entry = entry->next)
        collector_trace_root_range(entry->start, (unsigned char *)entry->start + entry->bytes);
    if (enumerate_roots != NULL) enumerate_roots();
}

void collector_clear_roots(void)
{
    while (registrations != NULL) {
        RootRegistration *entry = registrations;
        registrations = entry->next;
        collector_release_metadata(entry);
    }
}
