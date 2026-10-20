use netwasm_memory_host::CapacityLimiter;
use wasmtime::{Engine, Instance, Module, ResourceLimiter, Store};

const PAGE: usize = 65536;

/// Encodes a module that exports memories `m0`, `m1`, ... with the given
/// (minimum, maximum) page limits; every value must stay below 128.
fn module(memories: &[(u8, Option<u8>)]) -> Vec<u8> {
    let mut limits = vec![memories.len() as u8];
    let mut exports = vec![memories.len() as u8];
    for (index, (minimum, maximum)) in memories.iter().enumerate() {
        match maximum {
            Some(maximum) => limits.extend([1, *minimum, *maximum]),
            None => limits.extend([0, *minimum]),
        }
        exports.extend([2, b'm', b'0' + index as u8, 2, index as u8]);
    }
    let mut bytes = b"\0asm\x01\0\0\0".to_vec();
    for (id, section) in [(5, limits), (7, exports)] {
        bytes.extend([id, section.len() as u8]);
        bytes.extend(section);
    }
    bytes
}

fn instantiate(memories: &[(u8, Option<u8>)]) -> (Store<CapacityLimiter>, Instance) {
    let engine = Engine::default();
    let module = Module::new(&engine, module(memories)).unwrap();
    let mut store = Store::new(&engine, CapacityLimiter::default());
    store.limiter(|limiter| limiter as &mut dyn ResourceLimiter);
    let instance = Instance::new(&mut store, &module, &[]).unwrap();
    (store, instance)
}

fn data_size(store: &mut Store<CapacityLimiter>, instance: &Instance, name: &str) -> usize {
    instance.get_memory(&mut *store, name).unwrap().data_size(&*store)
}

#[test]
fn counts_initial_pages_and_every_growth() {
    let (mut store, instance) = instantiate(&[(2, None)]);
    assert_eq!(store.data().total_bytes(), 2 * PAGE);
    let memory = instance.get_memory(&mut store, "m0").unwrap();
    memory.grow(&mut store, 3).unwrap();
    memory.grow(&mut store, 1).unwrap();
    assert_eq!(store.data().total_bytes(), data_size(&mut store, &instance, "m0"));
    assert_eq!(store.data().total_bytes(), 6 * PAGE);
}

#[test]
fn a_memory_that_starts_empty_still_counts_its_growth() {
    let (mut store, instance) = instantiate(&[(0, None)]);
    assert_eq!(store.data().total_bytes(), 0);
    instance.get_memory(&mut store, "m0").unwrap().grow(&mut store, 4).unwrap();
    assert_eq!(store.data().total_bytes(), 4 * PAGE);
}

#[test]
fn adds_the_capacity_of_every_memory() {
    let (mut store, instance) = instantiate(&[(1, None), (3, None)]);
    instance.get_memory(&mut store, "m1").unwrap().grow(&mut store, 2).unwrap();
    let expected = data_size(&mut store, &instance, "m0") + data_size(&mut store, &instance, "m1");
    assert_eq!(store.data().total_bytes(), expected);
    assert_eq!(expected, 6 * PAGE);
}

#[test]
fn a_rejected_growth_leaves_the_total_unchanged() {
    let (mut store, instance) = instantiate(&[(1, Some(2))]);
    let memory = instance.get_memory(&mut store, "m0").unwrap();
    assert!(memory.grow(&mut store, 5).is_err());
    assert_eq!(store.data().total_bytes(), data_size(&mut store, &instance, "m0"));
    assert_eq!(store.data().total_bytes(), PAGE);
}
