//! Runs a WASI command component like `wasmtime run <component> <args>`, then
//! reports the peak aggregate linear-memory capacity on stderr as
//! `NETWASM-MEMORY|<bytes>`.

use std::{env, path::PathBuf, process::ExitCode};

use netwasm_memory_host::CapacityLimiter;
use wasmtime::{
    Config, Engine, Error, Result, Store,
    component::{Component, Linker, ResourceTable},
    error::Context,
};
use wasmtime_wasi::p2::bindings::sync::Command;
use wasmtime_wasi::{I32Exit, WasiCtx, WasiCtxView, WasiView};

struct HostState {
    table: ResourceTable,
    wasi: WasiCtx,
    limiter: CapacityLimiter,
}

impl WasiView for HostState {
    fn ctx(&mut self) -> WasiCtxView<'_> {
        WasiCtxView {
            ctx: &mut self.wasi,
            table: &mut self.table,
        }
    }
}

/// Returns the guest's exit status: zero when `run` succeeds or the guest exits
/// with zero, otherwise the guest's own nonzero status.
fn run() -> Result<i32> {
    let mut arguments = env::args().skip(1);
    let component_path = PathBuf::from(
        arguments
            .next()
            .ok_or_else(|| Error::msg("usage: netwasm-memory-host <component> [arguments...]"))?,
    );
    let mut configuration = Config::new();
    configuration.wasm_component_model(true);
    configuration.wasm_exceptions(true);
    let engine = Engine::new(&configuration)?;
    let component = Component::from_file(&engine, &component_path)
        .with_context(|| format!("failed to load {}", component_path.display()))?;
    let mut linker = Linker::new(&engine);
    wasmtime_wasi::p2::add_to_linker_sync(&mut linker)?;

    let mut wasi = WasiCtx::builder();
    wasi.inherit_stdout().inherit_stderr();
    wasi.arg(component_path.to_string_lossy());
    for argument in arguments {
        wasi.arg(argument);
    }
    let mut store = Store::new(
        &engine,
        HostState {
            table: ResourceTable::new(),
            wasi: wasi.build(),
            limiter: CapacityLimiter::default(),
        },
    );
    store.limiter(|state| &mut state.limiter);
    let command = Command::instantiate(&mut store, &component, &linker)?;
    let status = match command.wasi_cli_run().call_run(&mut store) {
        Ok(Ok(())) => 0,
        Ok(Err(())) => 1,
        Err(error) => match error.downcast_ref::<I32Exit>() {
            Some(exit) => exit.0,
            None => return Err(error),
        },
    };
    eprintln!("NETWASM-MEMORY|{}", store.data().limiter.total_bytes());
    Ok(status)
}

fn main() -> ExitCode {
    match run() {
        Ok(0) => ExitCode::SUCCESS,
        Ok(status) => ExitCode::from(u8::try_from(status).unwrap_or(1).max(1)),
        Err(error) => {
            eprintln!("{error:?}");
            ExitCode::from(2)
        }
    }
}
