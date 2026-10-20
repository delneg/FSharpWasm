//! Measures the aggregate linear-memory capacity a Wasm instance reaches.

use wasmtime::{Error, ResourceLimiter, Result};

/// Follows every linear memory's size through creation and growth. Wasm linear
/// memory never shrinks, so the total after a run is the peak aggregate capacity
/// of all memories in the store: not live heap and not host RSS.
#[derive(Default)]
pub struct CapacityLimiter {
    total: usize,
    pending: usize,
}

impl CapacityLimiter {
    pub fn total_bytes(&self) -> usize {
        self.total
    }
}

impl ResourceLimiter for CapacityLimiter {
    fn memory_growing(&mut self, current: usize, desired: usize, _maximum: Option<usize>) -> Result<bool> {
        // A failed growth reports itself next, so the change waits there to be undone.
        self.pending = desired - current;
        self.total += self.pending;
        Ok(true)
    }

    fn memory_grow_failed(&mut self, _error: Error) -> Result<()> {
        self.total -= self.pending;
        self.pending = 0;
        Ok(())
    }

    fn table_growing(&mut self, _current: usize, _desired: usize, _maximum: Option<usize>) -> Result<bool> {
        Ok(true)
    }
}
