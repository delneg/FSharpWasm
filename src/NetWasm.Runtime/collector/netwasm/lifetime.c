/* NetWasmGC for NetWasm (https://www.netwasm.com)
 * Copyright (c) 2026 Zion Sati
 * SPDX-License-Identifier: Apache-2.0
 */
#include "lifetime.h"

#include "../collector_metadata.h"
#include "runtime_state.h"

#include <limits.h>
#include <stdlib.h>

typedef struct FinalizerEntry FinalizerEntry;
struct FinalizerEntry {
    FinalizerEntry *next;
    uintptr_t object;
    NetWasmCollectorFinalizer finalizer;
    void *context;
    bool queued;
    bool running;
    bool batch;
};

typedef struct WeakEntry WeakEntry;
struct WeakEntry {
    WeakEntry *next;
    uintptr_t *slot;
    uintptr_t target;
    NetWasmWeakReferenceLifetime lifetime;
};

typedef struct EphemeronEntry EphemeronEntry;
struct EphemeronEntry {
    EphemeronEntry *next;
    uintptr_t *key;
    uintptr_t *value;
};

typedef struct {
    NetWasmGc *gc;
    FinalizerEntry *finalizers;
    WeakEntry *weak;
    EphemeronEntry *ephemerons;
    bool invoking;
} LifetimeState;

static LifetimeState lifetime;

static bool slot_is_available(uintptr_t *slot)
{
    uintptr_t owner = netwasm_gc_base(lifetime.gc, (uintptr_t)slot);
    return owner == 0 || netwasm_gc_is_marked(lifetime.gc, owner);
}

static void drain_ephemerons(void)
{
    bool changed;
    do {
        changed = false;
        for (EphemeronEntry *entry = lifetime.ephemerons; entry != NULL; entry = entry->next) {
            if (!slot_is_available(entry->key) || !slot_is_available(entry->value)) continue;
            if (*entry->key != 0 && netwasm_gc_is_marked(lifetime.gc, *entry->key))
                changed = netwasm_gc_mark(lifetime.gc, *entry->value) || changed;
        }
        netwasm_gc_drain(lifetime.gc);
    } while (changed);
}

static void process_weak(NetWasmWeakReferenceLifetime phase)
{
    WeakEntry **link = &lifetime.weak;
    while (*link != NULL) {
        WeakEntry *entry = *link;
        bool owner_available = slot_is_available(entry->slot);
        bool discard_owner = phase == NETWASM_WEAK_REFERENCE_AFTER_FINALIZATION && !owner_available;
        bool clear_target = entry->lifetime == phase &&
            !netwasm_gc_is_marked(lifetime.gc, entry->target);
        if (!discard_owner && !clear_target) {
            link = &entry->next;
            continue;
        }
        if (!discard_owner) *entry->slot = 0;
        *link = entry->next;
        collector_release_metadata(entry);
    }
}

static void finish_mark(NetWasmGc *gc, void *state)
{
    (void)state;
    for (FinalizerEntry *entry = lifetime.finalizers; entry != NULL; entry = entry->next)
        if (entry->queued || entry->running) netwasm_gc_mark(gc, entry->object);
    netwasm_gc_drain(gc);
    drain_ephemerons();
    process_weak(NETWASM_WEAK_REFERENCE_BEFORE_FINALIZATION);

    for (FinalizerEntry *entry = lifetime.finalizers; entry != NULL; entry = entry->next)
        if (!entry->queued && !entry->running && entry->finalizer != NULL &&
            !netwasm_gc_is_marked(gc, entry->object)) entry->queued = true;
    for (FinalizerEntry *entry = lifetime.finalizers; entry != NULL; entry = entry->next)
        if (entry->queued) netwasm_gc_mark(gc, entry->object);
    netwasm_gc_drain(gc);
    drain_ephemerons();
    process_weak(NETWASM_WEAK_REFERENCE_AFTER_FINALIZATION);

    EphemeronEntry **link = &lifetime.ephemerons;
    while (*link != NULL) {
        EphemeronEntry *entry = *link;
        bool available = slot_is_available(entry->key) && slot_is_available(entry->value);
        if (available && netwasm_gc_is_marked(gc, *entry->key)) {
            link = &entry->next;
            continue;
        }
        if (available) {
            *entry->key = 0;
            *entry->value = 0;
        }
        *link = entry->next;
        collector_release_metadata(entry);
    }
}

void netwasm_gc_lifetime_initialize(NetWasmGc *gc)
{
    if (lifetime.gc != NULL && lifetime.gc != gc) abort();
    lifetime.gc = gc;
    netwasm_gc_set_mark_completion(gc, finish_mark, NULL);
}

void netwasm_gc_register_finalizer(
    uintptr_t object,
    NetWasmCollectorFinalizer finalizer,
    void *context,
    NetWasmCollectorFinalizer *previous,
    void **previous_context)
{
    if (object == 0 || netwasm_gc_base(lifetime.gc, object) != object) abort();
    FinalizerEntry **link = &lifetime.finalizers;
    while (*link != NULL && (*link)->object != object) link = &(*link)->next;
    FinalizerEntry *entry = *link;
    if (entry == NULL && finalizer != NULL) {
        entry = collector_allocate_metadata(sizeof(*entry));
        if (entry == NULL) abort();
        entry->object = object;
        *link = entry;
    }
    if (previous != NULL) *previous = entry == NULL ? NULL : entry->finalizer;
    if (previous_context != NULL) *previous_context = entry == NULL ? NULL : entry->context;
    if (entry == NULL) return;
    entry->finalizer = finalizer;
    entry->context = context;
    if (finalizer == NULL && !entry->running) {
        *link = entry->next;
        collector_release_metadata(entry);
    }
}

bool netwasm_gc_has_pending_finalizers(void)
{
    for (FinalizerEntry *entry = lifetime.finalizers; entry != NULL; entry = entry->next)
        if (entry->queued) return true;
    return false;
}

int netwasm_gc_invoke_finalizers(void)
{
    if (lifetime.invoking || netwasm_gc_is_collecting(lifetime.gc)) return 0;
    lifetime.invoking = true;
    for (FinalizerEntry *entry = lifetime.finalizers; entry != NULL; entry = entry->next)
        entry->batch = entry->queued;
    size_t invoked = 0;
    for (;;) {
        FinalizerEntry *entry = lifetime.finalizers;
        while (entry != NULL && !entry->batch) entry = entry->next;
        if (entry == NULL) break;
        NetWasmCollectorFinalizer callback = entry->finalizer;
        void *context = entry->context;
        entry->batch = false;
        entry->queued = false;
        entry->running = true;
        entry->finalizer = NULL;
        entry->context = NULL;
        if (callback != NULL) callback((void *)entry->object, context);
        entry->running = false;
        if (invoked != SIZE_MAX) invoked++;
        if (entry->finalizer != NULL) continue;
        FinalizerEntry **link = &lifetime.finalizers;
        while (*link != NULL && *link != entry) link = &(*link)->next;
        if (*link == entry) {
            *link = entry->next;
            collector_release_metadata(entry);
        }
    }
    lifetime.invoking = false;
    return invoked > INT_MAX ? INT_MAX : (int)invoked;
}

int netwasm_gc_register_weak(
    uintptr_t *slot,
    uintptr_t target,
    NetWasmWeakReferenceLifetime weak_lifetime)
{
    if (slot == NULL || weak_lifetime > NETWASM_WEAK_REFERENCE_AFTER_FINALIZATION) return 0;
    WeakEntry **link = &lifetime.weak;
    while (*link != NULL && ((*link)->slot != slot || (*link)->lifetime != weak_lifetime))
        link = &(*link)->next;
    if (target == 0) {
        if (*link != NULL) {
            WeakEntry *entry = *link;
            *link = entry->next;
            collector_release_metadata(entry);
        }
        return 1;
    }
    if (netwasm_gc_base(lifetime.gc, target) == 0) return 0;
    if (*link == NULL) {
        *link = collector_allocate_metadata(sizeof(**link));
        if (*link == NULL) return 0;
        (*link)->slot = slot;
        (*link)->lifetime = weak_lifetime;
    }
    (*link)->target = target;
    *slot = target;
    return 1;
}

int netwasm_gc_unregister_weak(
    uintptr_t *slot,
    NetWasmWeakReferenceLifetime weak_lifetime)
{
    WeakEntry **link = &lifetime.weak;
    while (*link != NULL && ((*link)->slot != slot || (*link)->lifetime != weak_lifetime))
        link = &(*link)->next;
    if (*link == NULL) return 0;
    WeakEntry *entry = *link;
    *link = entry->next;
    collector_release_metadata(entry);
    return 1;
}

int netwasm_gc_register_ephemeron(uintptr_t *key, uintptr_t *value)
{
    if (key == NULL || value == NULL) return 0;
    for (EphemeronEntry *entry = lifetime.ephemerons; entry != NULL; entry = entry->next)
        if (entry->key == key && entry->value == value) return 1;
    EphemeronEntry *entry = collector_allocate_metadata(sizeof(*entry));
    if (entry == NULL) return 0;
    entry->key = key;
    entry->value = value;
    entry->next = lifetime.ephemerons;
    lifetime.ephemerons = entry;
    return 1;
}

void netwasm_gc_unregister_ephemeron(uintptr_t *key, uintptr_t *value)
{
    EphemeronEntry **link = &lifetime.ephemerons;
    while (*link != NULL && ((*link)->key != key || (*link)->value != value))
        link = &(*link)->next;
    if (*link == NULL) return;
    EphemeronEntry *entry = *link;
    *link = entry->next;
    collector_release_metadata(entry);
}
