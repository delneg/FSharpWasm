/* SPDX-License-Identifier: MIT */
#include "collector_allocation.h"
#include "collector_collection.h"
#include "collector_ephemeron.h"
#include "collector_finalization.h"
#include "collector_identity_hash.h"
#include "collector_lifecycle.h"
#include "collector_metadata.h"
#include "collector_pinning.h"
#include "collector_reference_store.h"
#include "collector_roots.h"
#include "collector_weak_reference.h"
#include "netwasm/runtime_state.h"

#include <assert.h>
#include <string.h>

void runtime_initialize_libc(void) { }

static uintptr_t frame_root;
static size_t finalized;

static void enumerate_roots(void)
{
    collector_trace_root_range(&frame_root, &frame_root + 1);
}

static void finalize(void *object, void *context)
{
    assert(context == &finalized);
    assert(netwasm_gc_contains(netwasm_gc_runtime, (uintptr_t)object));
    finalized++;
    collector_collect();
    assert(netwasm_gc_contains(netwasm_gc_runtime, (uintptr_t)object));
}

static void allocation_and_roots(void)
{
    uintptr_t unrooted = (uintptr_t)collector_allocate_atomic(32);
    assert(unrooted != 0);
    collector_collect();
    assert(!netwasm_gc_contains(netwasm_gc_runtime, unrooted));

    collector_set_root_enumerator(enumerate_roots);
    frame_root = (uintptr_t)collector_allocate_atomic(32);
    collector_collect();
    assert(netwasm_gc_contains(netwasm_gc_runtime, frame_root));
    frame_root = 0;
    collector_collect();
    assert(netwasm_gc_object_count(netwasm_gc_runtime) == 0);

    uintptr_t roots[2] = {
        (uintptr_t)collector_allocate_atomic(8),
        (uintptr_t)collector_allocate_atomic(8),
    };
    collector_register_roots(roots, sizeof(roots));
    collector_register_roots(roots, sizeof(roots));
    collector_collect();
    assert(netwasm_gc_object_count(netwasm_gc_runtime) == 2);
    collector_unregister_roots(roots, sizeof(roots));
    collector_collect();
    assert(netwasm_gc_object_count(netwasm_gc_runtime) == 2);
    collector_unregister_roots(roots, sizeof(roots));
    collector_collect();
    assert(netwasm_gc_object_count(netwasm_gc_runtime) == 0);
}

static void descriptors_and_ranges(void)
{
    NetWasmCollectorDescriptorWord bitmap = 0;
    collector_mark_descriptor_word(&bitmap, 0);
    NetWasmCollectorDescriptor descriptor = collector_create_descriptor(&bitmap, 2);
    uintptr_t *parent = collector_allocate_exact(2 * sizeof(uintptr_t), descriptor);
    uintptr_t reference = (uintptr_t)collector_allocate_atomic(8);
    uintptr_t integer = (uintptr_t)collector_allocate_atomic(8);
    parent[0] = reference;
    parent[1] = integer;
    frame_root = (uintptr_t)parent;
    collector_collect();
    assert(netwasm_gc_contains(netwasm_gc_runtime, reference));
    assert(!netwasm_gc_contains(netwasm_gc_runtime, integer));
    frame_root = 0;
    collector_collect();
    collector_release_metadata((void *)descriptor);

    descriptor = collector_create_descriptor(&bitmap, 1);
    uintptr_t *array = collector_allocate_exact_zeroed(3, sizeof(uintptr_t), descriptor);
    for (size_t index = 0; index < 3; index++)
        array[index] = (uintptr_t)collector_allocate_atomic(8);
    uintptr_t endpoint = (uintptr_t)(array + 3);
    collector_register_root_range(&endpoint, &endpoint + 1);
    collector_collect();
    assert(netwasm_gc_object_count(netwasm_gc_runtime) == 4);
    collector_unregister_root_range(&endpoint, &endpoint + 1);
    collector_collect();
    assert(netwasm_gc_object_count(netwasm_gc_runtime) == 0);
    collector_release_metadata((void *)descriptor);
}

static void lifetime_contract(void)
{
    uintptr_t key = (uintptr_t)collector_allocate_atomic(8);
    uintptr_t value = (uintptr_t)collector_allocate_atomic(8);
    uintptr_t short_slot = key;
    uintptr_t long_slot = key;
    collector_register_finalizer((void *)key, finalize, &finalized, NULL, NULL);
    assert(collector_register_short_weak_reference(&short_slot, key));
    assert(collector_register_weak_reference(
        &long_slot,
        key,
        NETWASM_WEAK_REFERENCE_AFTER_FINALIZATION));
    assert(collector_register_ephemeron(&key, &value));
    collector_collect();
    assert(short_slot == 0);
    assert(long_slot == key);
    assert(netwasm_gc_contains(netwasm_gc_runtime, value));
    assert(collector_has_pending_finalizers());
    assert(collector_invoke_finalizers() == 1);
    assert(finalized == 1);
    assert(!collector_has_pending_finalizers());
    collector_collect();
    assert(key == 0 && value == 0 && long_slot == 0);
    assert(netwasm_gc_object_count(netwasm_gc_runtime) == 0);
}

static void stores_hash_and_metrics(void)
{
    uintptr_t source[3] = {1, 2, 3};
    uintptr_t destination[3] = {0};
    collector_move_value_range(0, destination, source, 3, sizeof(uintptr_t), 0);
    assert(memcmp(source, destination, sizeof(source)) == 0);
    collector_store_reference(0, &destination[1], 7);
    assert(destination[1] == 7);
    collector_clear_value_range(0, destination, 3, sizeof(uintptr_t), 0);
    assert(destination[0] == 0 && destination[1] == 0 && destination[2] == 0);
    assert(collector_identity_hash(1234) == collector_identity_hash(1234));
    assert(collector_pinned_reference_count() == 0);
    collector_pin_reference(&destination[0]);
    assert(collector_pinned_reference_count() == 1);
    collector_unpin_reference(&destination[0]);
    assert(collector_pinned_reference_count() == 0);
    assert(collector_total_allocated_bytes() != 0);
    assert(collector_collection_count() != 0);
    uint64_t pause = 123;
    assert(!collector_try_read_total_pause_milliseconds(&pause) && pause == 123);
}

static void atomic_allocations_are_zeroed_after_reclamation(void)
{
    assert(collector_atomic_allocation_is_zeroed());
    static const size_t sizes[] = {0, 1, 32, 4096, 8192};
    frame_root = 0;
    for (size_t iteration = 0; iteration < 3; iteration++) {
        for (size_t index = 0; index < sizeof(sizes) / sizeof(sizes[0]); index++) {
            size_t size = sizes[index];
            unsigned char *object = collector_allocate_atomic(size);
            assert(object != NULL);
            for (size_t offset = 0; offset < size; offset++) assert(object[offset] == 0);
            memset(object, 0xa5, size);
            collector_collect();
            assert(netwasm_gc_object_count(netwasm_gc_runtime) == 0);
        }
    }
}

int main(void)
{
    collector_initialize();
    collector_enable_interior_pointers();
    collector_disable_stack_scanning();
    collector_enable_on_demand_finalization();
    collector_enable_java_finalization_order();
    allocation_and_roots();
    descriptors_and_ranges();
    lifetime_contract();
    stores_hash_and_metrics();
    atomic_allocations_are_zeroed_after_reclamation();
    collector_clear_roots();
    netwasm_gc_destroy(netwasm_gc_runtime);
    return 0;
}
