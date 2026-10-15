#include <assert.h>
#include <stddef.h>
#include <stdint.h>

#include "collector/bump/bump_arena.h"
#include "collector/collector_weak_reference.h"

static int bump_allocate_calls;

void runtime_initialize_libc(void) { }
void netwasm_bump_initialize(void) { }
void *netwasm_bump_allocate(size_t size)
{
    (void)size;
    bump_allocate_calls++;
    return NULL;
}
uint64_t netwasm_bump_total_allocated_bytes(void) { return 0; }
uint64_t netwasm_bump_heap_size(void) { return 0; }
uint64_t netwasm_bump_free_bytes(void) { return 0; }

int main(void)
{
    uintptr_t slot = 0x1234;

    assert(collector_register_weak_reference(
        &slot,
        slot,
        NETWASM_WEAK_REFERENCE_BEFORE_FINALIZATION) == 1);
    assert(slot == 0x1234);
    assert(collector_register_weak_reference(
        &slot,
        slot,
        NETWASM_WEAK_REFERENCE_AFTER_FINALIZATION) == 1);
    assert(slot == 0x1234);
    assert(collector_register_short_weak_reference(&slot, slot) == 1);
    assert(slot == 0x1234);
    assert(collector_register_short_weak_reference(&slot, slot) == 1);
    assert(slot == 0x1234);
    assert(collector_register_short_weak_reference(&slot, 0) == 1);
    assert(slot == 0x1234);
    assert(collector_unregister_weak_reference(
        &slot,
        NETWASM_WEAK_REFERENCE_BEFORE_FINALIZATION) == 0);
    assert(slot == 0x1234);
    assert(collector_unregister_weak_reference(
        &slot,
        NETWASM_WEAK_REFERENCE_AFTER_FINALIZATION) == 0);
    assert(collector_unregister_weak_reference(
        &slot,
        NETWASM_WEAK_REFERENCE_AFTER_FINALIZATION) == 0);
    assert(slot == 0x1234);
    assert(bump_allocate_calls == 0);

    return 0;
}
